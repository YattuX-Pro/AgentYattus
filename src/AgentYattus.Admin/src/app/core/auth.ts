import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { InMemoryWebStorage, User, UserManager, WebStorageStateStore } from 'oidc-client-ts';
import { tap } from 'rxjs';

/** Le rôle qui ouvre l'espace admin (ADR 0008). L'API le vérifie de son côté, route par route. */
export const ADMIN_ROLE = 'llm-admin';

export const AUTH_SETTINGS = {
  authority: 'http://localhost:8380/realms/agent-yattus',
  client_id: 'agent-yattus-admin',
  scope: 'openid profile',
};

/** Les rôles du realm, lus dans le jeton d'accès (« realm_access.roles »). */
export function realmRoles(accessToken: string | undefined): string[] {
  try {
    const payload = accessToken?.split('.')[1];
    if (!payload) return [];
    const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/'));
    const roles = JSON.parse(json)?.realm_access?.roles;
    return Array.isArray(roles) ? roles.filter((r): r is string => typeof r === 'string') : [];
  } catch {
    return [];
  }
}

/**
 * Connexion Keycloak : code d'autorisation + PKCE (oidc-client-ts).
 * Les jetons restent en mémoire : jamais dans localStorage, lisible par tout script de la page.
 * Recharger la page repasse par Keycloak, qui reconnecte sans saisie tant que sa session dure.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly manager = new UserManager({
    ...AUTH_SETTINGS,
    redirect_uri: `${location.origin}/`,
    post_logout_redirect_uri: `${location.origin}/`,
    userStore: new WebStorageStateStore({ store: new InMemoryWebStorage() }),
    // L'état de l'aller-retour (vérificateur PKCE) doit survivre à la redirection, le temps d'un onglet.
    stateStore: new WebStorageStateStore({ store: sessionStorage }),
    automaticSilentRenew: true,
  });

  private readonly user = signal<User | null>(null);
  readonly name = computed(() => this.user()?.profile.preferred_username ?? '');
  readonly roles = computed(() => realmRoles(this.user()?.access_token));
  readonly isAdmin = computed(() => this.roles().includes(ADMIN_ROLE));
  get accessToken() {
    return this.user()?.access_token;
  }

  constructor() {
    this.manager.events.addUserLoaded((u) => this.user.set(u));
    this.manager.events.addUserUnloaded(() => this.user.set(null));
  }

  /** Au démarrage : termine le retour de Keycloak, ou y part. Résout seulement une fois connecté. */
  async init(): Promise<void> {
    const params = new URLSearchParams(location.search);
    if (params.has('code') && params.has('state')) {
      const user = await this.manager.signinRedirectCallback();
      this.user.set(user);
      const target = typeof user.state === 'string' && user.state.startsWith('/') ? user.state : '/';
      history.replaceState(null, '', target);
      return;
    }
    await this.login();
    return new Promise(() => {}); // la page part chez Keycloak
  }

  login() {
    // Revenir sur la page demandée ; seulement un chemin local, jamais une adresse extérieure.
    return this.manager.signinRedirect({ state: location.pathname + location.hash });
  }

  logout() {
    return this.manager.signoutRedirect();
  }
}

/** Ajoute le jeton aux seuls appels à notre API ; un 401 renvoie vers Keycloak. */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith('/api/')) return next(request);
  const auth = inject(AuthService);
  const token = auth.accessToken;
  return next(token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request).pipe(
    tap({
      error: (e: unknown) => {
        if (e instanceof HttpErrorResponse && e.status === 401) void auth.login();
      },
    }),
  );
};

export const adminGuard: CanActivateFn = () =>
  inject(AuthService).isAdmin() || inject(Router).parseUrl('/interdit');

