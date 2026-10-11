import { Routes } from '@angular/router';
import { Home } from './pages/home/home';
import { authGuard } from '@services/auth.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', loadComponent: () => import('./pages/welcome/welcome').then((m) => m.Welcome) },
  { path: 'home', component: Home },
  { path: 't/:token', loadComponent: () => import('./pages/magic-link/magic-link').then((m) => m.MagicLink) },
  {
    path: 'trip/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/trip/trip').then((m) => m.TripPage),
  },
  { path: 'dev/outbox', loadComponent: () => import('./pages/dev-outbox/dev-outbox').then((m) => m.DevOutbox) },
  { path: '**', redirectTo: '' },
];
