import { Routes } from '@angular/router';
import { adminGuard } from './core/auth';
import { Forbidden } from './pages/forbidden';
import { Soon } from './pages/soon';
import { Shell } from './shell/shell';

export const routes: Routes = [
  { path: 'interdit', component: Forbidden },
  {
    path: '',
    component: Shell,
    canActivate: [adminGuard],
    children: [
      { path: '', component: Soon, data: { title: 'Vue d’ensemble' } },
      { path: 'missions', component: Soon, data: { title: 'Missions' } },
      { path: 'audit', component: Soon, data: { title: 'Audit' } },
      { path: 'agents', component: Soon, data: { title: 'Agents' } },
      { path: 'modeles', component: Soon, data: { title: 'Modèles' } },
    ],
  },
  { path: '**', redirectTo: '' },
];
