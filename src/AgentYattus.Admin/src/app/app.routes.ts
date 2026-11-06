import { Component, inject } from '@angular/core';
import { Routes } from '@angular/router';
import { AuthService, adminGuard } from './core/auth';
import { Forbidden } from './pages/forbidden';

@Component({ template: `<p class="p-6">Connecté : {{ auth.name() }}</p>` })
class Welcome {
  protected readonly auth = inject(AuthService);
}

export const routes: Routes = [
  { path: 'interdit', component: Forbidden },
  { path: '', component: Welcome, canActivate: [adminGuard] },
  { path: '**', redirectTo: '' },
];
