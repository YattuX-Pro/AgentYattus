import { Component, computed, inject, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideExternalLink, lucideRefreshCw, lucideSearch } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { AUTH_SETTINGS } from '../../core/auth';
import { AdminApi, ORG_ROLE, User, errorMessage } from '../../core/api';
import { when } from '../../core/format';
import { PageHeader } from '../../shell/page-header';

const WEEK_MS = 7 * 86_400_000;

@Component({
  selector: 'app-users',
  imports: [PageHeader, NgIcon, HlmButtonImports, HlmBadgeImports, HlmTableImports, HlmSkeletonImports],
  providers: [provideIcons({ lucideExternalLink, lucideRefreshCw, lucideSearch })],
  template: `
    <app-page-header title="Utilisateurs" description="Les personnes qui se sont connectées au moins une fois. Les comptes se créent dans Keycloak.">
      <a hlmBtn variant="outline" [href]="console" target="_blank" rel="noopener">Console Keycloak <ng-icon name="lucideExternalLink" /></a>
      <button hlmBtn variant="outline" (click)="load()"><ng-icon name="lucideRefreshCw" /> Actualiser</button>
    </app-page-header>

    <div class="grid grid-cols-1 gap-2.5">
      <section class="bg-card grid grid-cols-2 rounded-xl border sm:grid-cols-4">
        @for (k of kpis(); track k.label) {
          <div class="-ml-px -mt-px flex flex-col gap-1.5 border-t border-l px-3.5 py-3">
            <span class="text-muted-foreground text-[10.5px] font-medium tracking-wider uppercase">{{ k.label }}</span>
            <span class="num text-2xl leading-none font-semibold" [class.text-warning]="k.warn">{{ k.value }}</span>
            <span class="text-muted-foreground text-xs">{{ k.hint }}</span>
          </div>
        }
      </section>

      <section class="bg-card rounded-xl border">
        <div class="flex items-center gap-2 border-b px-3.5 py-2.5">
          <h2 class="font-semibold">Annuaire</h2>
          <span class="flex-1"></span>
          <label class="bg-muted text-muted-foreground flex h-8 w-56 max-w-full items-center gap-2 rounded-md border px-2.5">
            <ng-icon name="lucideSearch" />
            <input class="text-foreground min-w-0 flex-1 bg-transparent outline-none" placeholder="Nom ou identifiant" aria-label="Filtrer l'annuaire" [value]="query()" (input)="query.set($any($event.target).value)" />
          </label>
        </div>
        <div hlmTableContainer>
          <table hlmTable>
            <thead hlmTHead>
              <tr hlmTr>
                <th hlmTh>Personne</th>
                <th hlmTh>Instance</th>
                <th hlmTh>Organisations</th>
                <th hlmTh>Première visite</th>
                <th hlmTh>Dernière visite</th>
              </tr>
            </thead>
            <tbody hlmTBody>
              @if (!users()) {
                @for (i of [1, 2, 3]; track i) {
                  <tr hlmTr><td hlmTd colspan="5"><hlm-skeleton class="h-5 w-full" /></td></tr>
                }
              }
              @for (u of shown(); track u.id) {
                <tr hlmTr>
                  <td hlmTd>
                    <div class="flex items-center gap-2.5">
                      <span class="bg-muted grid size-8 shrink-0 place-items-center rounded-full border text-[11px] font-semibold">{{ (u.displayName ?? u.username).slice(0, 2).toUpperCase() }}</span>
                      <div>
                        <div class="font-medium">{{ u.displayName ?? u.username }}</div>
                        <div class="text-muted-foreground font-mono text-xs">{{ u.username }}</div>
                      </div>
                    </div>
                  </td>
                  <td hlmTd>
                    @if (u.isInstanceAdmin) {
                      <span hlmBadge variant="success">Administrateur</span>
                    } @else {
                      <span class="text-muted-foreground">Utilisateur</span>
                    }
                  </td>
                  <td hlmTd class="whitespace-normal">
                    <div class="flex flex-wrap gap-1">
                      @for (o of u.organizations; track o.organizationId) {
                        <span hlmBadge variant="outline">{{ o.name }} · {{ ORG_ROLE[o.role] }}</span>
                      } @empty {
                        <span class="text-muted-foreground">—</span>
                      }
                    </div>
                  </td>
                  <td hlmTd class="text-muted-foreground num">{{ when(u.firstSeenAt) }}</td>
                  <td hlmTd class="text-muted-foreground num">{{ when(u.lastSeenAt) }}</td>
                </tr>
              } @empty {
                @if (users()) {
                  <tr hlmTr><td hlmTd colspan="5" class="text-muted-foreground py-8 text-center">Personne ne correspond.</td></tr>
                }
              }
            </tbody>
          </table>
        </div>
      </section>
    </div>
  `,
})
export class Users {
  private readonly api = inject(AdminApi);

  protected readonly users = signal<User[] | null>(null);
  protected readonly query = signal('');
  protected readonly shown = computed(() => {
    const q = this.query().trim().toLowerCase();
    return (this.users() ?? []).filter((u) => !q || `${u.username} ${u.displayName ?? ''}`.toLowerCase().includes(q));
  });
  protected readonly kpis = computed(() => {
    const users = this.users() ?? [];
    const now = Date.now();
    const lone = users.filter((u) => !u.organizations.length).length;
    return [
      { label: 'Comptes', value: users.length, hint: 'connectés au moins une fois', warn: false },
      { label: 'Administrateurs', value: users.filter((u) => u.isInstanceAdmin).length, hint: 'de l’instance', warn: false },
      { label: 'Actifs, 7 jours', value: users.filter((u) => now - Date.parse(u.lastSeenAt) < WEEK_MS).length, hint: 'dernière visite', warn: false },
      { label: 'Sans organisation', value: lone, hint: 'espace personnel seulement', warn: lone > 0 },
    ];
  });
  /** La console d'administration du realm, pour créer un compte. */
  protected readonly console = AUTH_SETTINGS.authority.replace('/realms/', '/admin/master/console/#/') + '/users';
  protected readonly ORG_ROLE = ORG_ROLE;
  protected readonly when = when;

  constructor() {
    void this.load();
  }

  protected async load() {
    try {
      this.users.set(await this.api.users());
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }
}
