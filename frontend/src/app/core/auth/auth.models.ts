export interface UserProfile {
  id: string;
  email: string;
  fullName: string;
  registrationNo: string | null;
  tenantId: string;
  tenantName: string;
  roles: string[];
  permissions: string[];
  mustChangePassword: boolean;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
  expiresAt: string;
  user: UserProfile;
}

/** Permission keys, mirroring ValuPro.Domain.Security.Permissions on the API. */
export const Permission = {
  dashboardView: 'dashboard.view',
  clientsView: 'clients.view',
  clientsManage: 'clients.manage',
  banksView: 'banks.view',
  banksManage: 'banks.manage',
  propertiesView: 'properties.view',
  assignmentsView: 'assignments.view',
  inspectionsView: 'inspections.view',
  valuationView: 'valuation.view',
  reportsView: 'reports.view',
  billingView: 'billing.view',
  ratesView: 'rates.view',
  usersView: 'users.view',
  usersManage: 'users.manage',
  settingsManage: 'settings.manage',
  settingsView: 'settings.view',
} as const;
