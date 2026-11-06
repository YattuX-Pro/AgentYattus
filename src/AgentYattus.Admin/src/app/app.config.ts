import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { AuthService, authInterceptor } from './core/auth';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withInterceptors([authInterceptor])),
    // Rien ne s'affiche avant la connexion : l'application attend le retour de Keycloak.
    provideAppInitializer(() => inject(AuthService).init()),
    provideRouter(routes, withComponentInputBinding()),
  ],
};
