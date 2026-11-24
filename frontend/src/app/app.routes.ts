import { Routes } from '@angular/router';
import { authGuard, guestGuard, passwordChangedGuard, permissionGuard } from './core/auth/auth.guards';
import { Permission } from './core/auth/auth.models';
import { NAVIGATION } from './layout/navigation';

/** Modules that are built; every other sidebar module shows a placeholder until its phase. */
const BUILT: Record<string, Routes[number]['loadComponent']> = {
  users: () => import('./features/users/users-page').then((m) => m.UsersPage),
  settings: () => import('./features/settings/settings-page').then((m) => m.SettingsPage),
  clients: () => import('./features/clients/clients-page').then((m) => m.ClientsPage),
  banks: () => import('./features/banks/banks-page').then((m) => m.BanksPage),
  properties: () => import('./features/properties/properties-page').then((m) => m.PropertiesPage),
  assignments: () => import('./features/assignments/assignments-page').then((m) => m.AssignmentsPage),
  rates: () => import('./features/rates/rates-page').then((m) => m.RatesPage),
  inspection: () => import('./features/inspections/inspections-page').then((m) => m.InspectionsPage),
  photos: () => import('./features/photos/photos-page').then((m) => m.PhotosPage),
  documents: () => import('./features/documents/documents-page').then((m) => m.DocumentsPage),
  valuation: () => import('./features/valuations/valuations-page').then((m) => m.ValuationsPage),
};

const moduleRoutes: Routes = NAVIGATION.flatMap((g) => g.items)
  .filter((i) => i.path !== 'dashboard')
  .map((i) => ({
    path: i.path,
    title: `${i.label} · ValuPro`,
    canActivate: [permissionGuard],
    data: { permission: i.permission, path: i.path },
    loadComponent: BUILT[i.path] ?? (() => import('./shared/module-placeholder').then((m) => m.ModulePlaceholder)),
  }));

export const routes: Routes = [
  {
    path: 'login',
    title: 'Sign in · ValuPro',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/login').then((m) => m.Login),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/shell').then((m) => m.Shell),
    children: [
      {
        path: 'change-password',
        title: 'Change password · ValuPro',
        loadComponent: () => import('./features/account/change-password').then((m) => m.ChangePassword),
      },
      {
        path: '',
        canActivateChild: [passwordChangedGuard],
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
          {
            path: 'dashboard',
            title: 'Dashboard · ValuPro',
            loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard),
          },
          {
            path: 'banks/:id',
            title: 'Bank · ValuPro',
            canActivate: [permissionGuard],
            data: { permission: Permission.banksView },
            loadComponent: () => import('./features/banks/bank-detail-page').then((m) => m.BankDetailPage),
          },
          {
            path: 'properties/:id',
            title: 'Property · ValuPro',
            canActivate: [permissionGuard],
            data: { permission: Permission.propertiesView },
            loadComponent: () => import('./features/properties/property-detail-page').then((m) => m.PropertyDetailPage),
          },
          {
            path: 'assignments/:id',
            title: 'Assignment · ValuPro',
            canActivate: [permissionGuard],
            data: { permission: Permission.assignmentsView },
            loadComponent: () => import('./features/assignments/assignment-detail-page').then((m) => m.AssignmentDetailPage),
          },
          {
            path: 'inspection/:id',
            title: 'Site Inspection · ValuPro',
            canActivate: [permissionGuard],
            data: { permission: Permission.inspectionsView },
            loadComponent: () => import('./features/inspections/inspection-page').then((m) => m.InspectionPage),
          },
          {
            path: 'photos/:id',
            title: 'Photos · ValuPro',
            canActivate: [permissionGuard],
            data: { permission: Permission.inspectionsView },
            loadComponent: () => import('./features/photos/case-photos-page').then((m) => m.CasePhotosPage),
          },
          {
            path: 'documents/:id',
            title: 'Documents · ValuPro',
            canActivate: [permissionGuard],
            data: { permission: Permission.assignmentsView },
            loadComponent: () => import('./features/documents/case-documents-page').then((m) => m.CaseDocumentsPage),
          },
          {
            path: 'valuation/:id',
            title: 'Valuation · ValuPro',
            canActivate: [permissionGuard],
            data: { permission: Permission.valuationView },
            loadComponent: () => import('./features/valuations/valuation-page').then((m) => m.ValuationPage),
          },
          ...moduleRoutes,
          {
            path: 'forbidden',
            title: 'No access · ValuPro',
            loadComponent: () => import('./shared/forbidden').then((m) => m.Forbidden),
          },
        ],
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
