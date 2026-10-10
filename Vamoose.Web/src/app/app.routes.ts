import { Routes } from '@angular/router';
import { Home } from './pages/home/home';
import { authGuard } from '@services/auth.guard';

export const routes: Routes = [
  // The home page is the agent: "Where do you want to go?" (requirements 3.1, 3.3).
  { path: '', component: Home },
  { path: 't/:token', loadComponent: () => import('./pages/magic-link/magic-link').then((m) => m.MagicLink) },
  {
    path: 'trips/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/trip/trip').then((m) => m.TripPage),
  },
  { path: 'dev/outbox', loadComponent: () => import('./pages/dev-outbox/dev-outbox').then((m) => m.DevOutbox) },
  { path: '**', redirectTo: '' },
];
