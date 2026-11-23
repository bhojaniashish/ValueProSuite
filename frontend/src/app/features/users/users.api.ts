import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export type UserStatus = 'Active' | 'OnLeave' | 'Inactive';

export const USER_STATUSES: { value: UserStatus; label: string; badge: string }[] = [
  { value: 'Active', label: 'Active', badge: 'b-ok' },
  { value: 'OnLeave', label: 'On Leave', badge: 'b-warn' },
  { value: 'Inactive', label: 'Inactive', badge: 'b-gray' },
];

/** Badge colours per role, as in the prototype. */
export const ROLE_BADGE: Record<string, string> = {
  Admin: 'b-danger',
  'Senior Valuer': 'b-brand',
  'Junior Valuer': 'b-info',
  Architect: 'b-purple',
  'Office Staff': 'b-gray',
  Reviewer: 'b-gold',
  'Bank User': 'b-teal',
};

export const ROLES = Object.keys(ROLE_BADGE);

export interface UserListItem {
  id: string;
  fullName: string;
  email: string;
  phone: string | null;
  registrationNo: string | null;
  role: string;
  status: UserStatus;
  createdAt: string;
  lastLoginAt: string | null;
}

export interface UserQuery {
  search?: string;
  role?: string;
  status?: UserStatus | '';
  sort?: string;
  desc?: boolean;
  page: number;
  pageSize: number;
}

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface UserSummary {
  total: number;
  active: number;
  valuers: number;
  reviewers: number;
}

export interface SaveUserRequest {
  fullName: string;
  email: string;
  phone: string | null;
  registrationNo: string | null;
  role: string;
  status: UserStatus;
}

export interface TemporaryPasswordResponse {
  user: UserListItem;
  temporaryPassword: string;
}

export interface PermissionAction {
  key: string;
  label: string;
}

export interface PermissionModule {
  key: string;
  label: string;
  actions: PermissionAction[];
}

export interface PermissionMatrix {
  roles: string[];
  modules: PermissionModule[];
  grants: Record<string, string[]>;
  lockedRoles: string[];
}

@Injectable({ providedIn: 'root' })
export class UsersApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api`;

  list(q: UserQuery): Observable<Paged<UserListItem>> {
    let params = new HttpParams().set('page', q.page).set('pageSize', q.pageSize);
    if (q.search) params = params.set('search', q.search);
    if (q.role) params = params.set('role', q.role);
    if (q.status) params = params.set('status', q.status);
    if (q.sort) params = params.set('sort', q.sort).set('desc', !!q.desc);
    return this.http.get<Paged<UserListItem>>(`${this.base}/users`, { params });
  }

  summary(): Observable<UserSummary> {
    return this.http.get<UserSummary>(`${this.base}/users/summary`);
  }

  create(body: SaveUserRequest): Observable<TemporaryPasswordResponse> {
    return this.http.post<TemporaryPasswordResponse>(`${this.base}/users`, body);
  }

  update(id: string, body: SaveUserRequest): Observable<UserListItem> {
    return this.http.put<UserListItem>(`${this.base}/users/${id}`, body);
  }

  resetPassword(id: string): Observable<TemporaryPasswordResponse> {
    return this.http.post<TemporaryPasswordResponse>(`${this.base}/users/${id}/reset-password`, {});
  }

  matrix(): Observable<PermissionMatrix> {
    return this.http.get<PermissionMatrix>(`${this.base}/roles/permissions`);
  }

  updateRole(role: string, permissions: string[]): Observable<PermissionMatrix> {
    return this.http.put<PermissionMatrix>(`${this.base}/roles/${encodeURIComponent(role)}/permissions`, { permissions });
  }
}
