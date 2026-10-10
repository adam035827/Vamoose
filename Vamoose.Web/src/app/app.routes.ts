import { Routes } from '@angular/router';
import { Home } from './pages/home/home';
import { authGuard, guestGuard } from '@services/auth.guard';

export const routes: Routes = [
  { path: 'login', canActivate: [guestGuard], loadComponent: () => import('./pages/login/login').then((m) => m.Login) },
  { path: '', canActivate: [authGuard], component: Home },
  {
    path: 'trips/new',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/trip-new/trip-new').then((m) => m.TripNew),
  },
  {
    path: 'trips/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/trip/trip').then((m) => m.TripPage),
  },
  { path: '**', redirectTo: '' },
];
