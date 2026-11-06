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
      { path: '', loadComponent: () => import('./pages/soon').then((m) => m.Soon), data: { title: 'Vue d’ensemble' } },
      { path: 'missions', loadComponent: () => import('./pages/soon').then((m) => m.Soon), data: { title: 'Missions' } },
      { path: 'audit', loadComponent: () => import('./pages/soon').then((m) => m.Soon), data: { title: 'Audit' } },
      { path: 'agents', loadComponent: () => import('./pages/agents/agents').then((m) => m.Agents) },
      { path: 'modeles', loadComponent: () => import('./pages/models/models').then((m) => m.Models) },
    ],
  },
  { path: '**', redirectTo: '' },
];
