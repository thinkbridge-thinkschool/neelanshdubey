import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';
import { homeRedirectGuard } from './auth/home-redirect.guard';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    canActivate: [homeRedirectGuard],
    loadComponent: () => import('./auth/login.component').then((m) => m.LoginComponent),
  },
  {
    path: 'login',
    loadComponent: () => import('./auth/login.component').then((m) => m.LoginComponent),
  },
  {
    path: 'search',
    canActivate: [authGuard],
    loadComponent: () => import('./search/search.component').then((m) => m.SearchComponent),
  },
  {
    path: 'quotes',
    canActivate: [authGuard],
    loadComponent: () => import('./quotes/quotes-list.component').then((m) => m.QuotesListComponent),
  },
  {
    path: 'quotes/new',
    canActivate: [authGuard],
    loadComponent: () => import('./quotes/create-quote.component').then((m) => m.CreateQuoteComponent),
  },
  {
    // Must come after the static 'quotes/new' above — the router matches
    // routes in order, so ':id' would otherwise swallow "new" as an id.
    path: 'quotes/:id',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./quotes/quote-detail-page.component').then((m) => m.QuoteDetailPageComponent),
  },
  { path: '**', redirectTo: '' },
];
