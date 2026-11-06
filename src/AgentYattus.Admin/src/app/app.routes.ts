import { Routes } from '@angular/router';
import { adminGuard } from './core/auth';
import { Forbidden } from './pages/forbidden';
import { Shell } from './shell/shell';

// Chaque page est chargée à la demande : le premier affichage ne télécharge que la coque.
export const routes: Routes = [
  { path: 'interdit', component: Forbidden },
  {
    path: '',
    component: Shell,
    canActivate: [adminGuard],
    children: [
      { path: '', loadComponent: () => import('./pages/overview/overview').then((m) => m.Overview) },
      { path: 'projets', loadComponent: () => import('./pages/projects/projects').then((m) => m.Projects) },
      { path: 'missions', loadComponent: () => import('./pages/missions/missions').then((m) => m.Missions) },
      { path: 'audit', loadComponent: () => import('./pages/audit/audit').then((m) => m.Audit) },
      { path: 'agents', loadComponent: () => import('./pages/agents/agents').then((m) => m.Agents) },
      { path: 'organisations', loadComponent: () => import('./pages/organizations/organizations').then((m) => m.Organizations) },
      { path: 'utilisateurs', loadComponent: () => import('./pages/users/users').then((m) => m.Users) },
      { path: 'reglages', loadComponent: () => import('./pages/settings/settings').then((m) => m.Settings) },
      { path: 'modeles', loadComponent: () => import('./pages/models/models').then((m) => m.Models) },
    ],
  },
  { path: '**', redirectTo: '' },
];
