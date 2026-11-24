import { Permission } from '../core/auth/auth.models';

export interface NavItem {
  path: string;
  label: string;
  icon: string;
  permission: string;
  /** Phase in which the module is built; shown on placeholder pages until then. */
  phase: number;
  /** Built and usable (no longer a placeholder). */
  ready?: boolean;
  summary: string;
}

export interface NavGroup {
  label: string;
  items: NavItem[];
}

/** Same groups and order as the prototype's sidebar. */
export const NAVIGATION: NavGroup[] = [
  {
    label: 'Overview',
    items: [
      { path: 'dashboard', label: 'Dashboard', icon: 'fa-gauge-high', permission: Permission.dashboardView, phase: 4, summary: 'KPIs, status and bank-wise charts, revenue trend, today’s tasks.' },
      { path: 'calendar', label: 'Calendar', icon: 'fa-calendar-days', permission: Permission.assignmentsView, phase: 4, summary: 'Site visit scheduling, reminders, month/week/day views.' },
    ],
  },
  {
    label: 'Operations',
    items: [
      { path: 'assignments', label: 'Assignments', icon: 'fa-clipboard-list', permission: Permission.assignmentsView, phase: 1, ready: true, summary: 'Valuation cases through the 7-stage workflow, list and Kanban views.' },
      { path: 'inspection', label: 'Site Inspection', icon: 'fa-location-crosshairs', permission: Permission.inspectionsView, phase: 2, summary: 'Checklist, observations and GPS capture, built for phones.' },
      { path: 'valuation', label: 'Valuation Engine', icon: 'fa-calculator', permission: Permission.valuationView, phase: 2, summary: 'Land and floor-wise building valuation, depreciation, final values.' },
      { path: 'reports', label: 'Reports', icon: 'fa-file-contract', permission: Permission.reportsView, phase: 3, summary: 'Five report types from templates, review, signature, PDF/Word.' },
      { path: 'photos', label: 'Photos', icon: 'fa-images', permission: Permission.inspectionsView, phase: 2, summary: 'Geo-tagged site photos by category.' },
      { path: 'documents', label: 'Documents', icon: 'fa-folder-tree', permission: Permission.assignmentsView, phase: 2, summary: 'Property documents with versions and expiry alerts.' },
    ],
  },
  {
    label: 'Masters',
    items: [
      { path: 'clients', label: 'Clients', icon: 'fa-users', permission: Permission.clientsView, phase: 1, ready: true, summary: 'Individuals, builders, bank branches and advocates.' },
      { path: 'banks', label: 'Banks', icon: 'fa-building-columns', permission: Permission.banksView, phase: 1, ready: true, summary: 'Banks, branches, fee rates, valuation rules and questionnaires.' },
      { path: 'properties', label: 'Properties', icon: 'fa-map-location-dot', permission: Permission.propertiesView, phase: 1, ready: true, summary: 'Property register and history across assignments.' },
      { path: 'rates', label: 'Rate Master', icon: 'fa-table-cells', permission: Permission.ratesView, phase: 2, ready: true, summary: 'Location, circle, construction rates and depreciation tables.' },
      { path: 'gis', label: 'GIS Map', icon: 'fa-map', permission: Permission.propertiesView, phase: 5, summary: 'Property map, clustering and nearby comparables.' },
    ],
  },
  {
    label: 'Finance & Admin',
    items: [
      { path: 'billing', label: 'Billing', icon: 'fa-file-invoice-dollar', permission: Permission.billingView, phase: 4, summary: 'GST invoices, payments and outstanding report.' },
      { path: 'users', label: 'User Management', icon: 'fa-user-shield', permission: Permission.usersView, phase: 1, ready: true, summary: 'Users, roles and the role × module permission matrix.' },
      { path: 'settings', label: 'Settings', icon: 'fa-gear', permission: Permission.settingsView, phase: 1, ready: true, summary: 'Firm profile, branding, numbering, email and SMS.' },
    ],
  },
];
