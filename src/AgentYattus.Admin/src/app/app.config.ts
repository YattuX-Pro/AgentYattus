import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding, withViewTransitions } from '@angular/router';
import { routes } from './app.routes';
import { AuthService, authInterceptor } from './core/auth';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withInterceptors([authInterceptor])),
    // Rien ne s'affiche avant la connexion : l'application attend le retour de Keycloak.
    provideAppInitializer(() => inject(AuthService).init()),
    // Chaque changement de page passe par une transition de vue (fondu et léger glissement, styles.css).
    provideRouter(routes, withComponentInputBinding(), withViewTransitions({ skipInitialTransition: true })),
  ],
};
