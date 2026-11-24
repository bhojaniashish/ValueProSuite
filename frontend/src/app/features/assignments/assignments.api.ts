import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import type { ClientType } from '../clients/clients.api';
import type { PropertyType } from '../properties/properties.api';
import { Paged } from '../users/users.api';

export type AssignmentStatus =
  | 'New' | 'SiteVisitScheduled' | 'InspectionDone' | 'DraftPrepared' | 'QualityReview' | 'Submitted' | 'InvoiceGenerated' | 'Cancelled';

export type Priority = 'Low' | 'Medium' | 'High';

/** Labels, badge colours and icons from the prototype's STATUS_META. */
export const STATUS_META: Record<AssignmentStatus, { label: string; badge: string; icon: string }> = {
  New: { label: 'New Assignment', badge: 'b-gray', icon: 'fa-folder-plus' },
  SiteVisitScheduled: { label: 'Site Visit Scheduled', badge: 'b-info', icon: 'fa-calendar-check' },
  InspectionDone: { label: 'Inspection Done', badge: 'b-teal', icon: 'fa-clipboard-check' },
  DraftPrepared: { label: 'Draft Prepared', badge: 'b-purple', icon: 'fa-file-pen' },
  QualityReview: { label: 'Quality Review', badge: 'b-warn', icon: 'fa-magnifying-glass-chart' },
  Submitted: { label: 'Submitted', badge: 'b-ok', icon: 'fa-paper-plane' },
  InvoiceGenerated: { label: 'Invoice Generated', badge: 'b-gold', icon: 'fa-file-invoice-dollar' },
  Cancelled: { label: 'Cancelled', badge: 'b-danger', icon: 'fa-ban' },
};

export const STAGES: AssignmentStatus[] = ['New', 'SiteVisitScheduled', 'InspectionDone', 'DraftPrepared', 'QualityReview', 'Submitted', 'InvoiceGenerated'];

export const PRIORITIES: { value: Priority; label: string; badge: string }[] = [
  { value: 'High', label: 'High', badge: 'b-danger' },
  { value: 'Medium', label: 'Medium', badge: 'b-warn' },
  { value: 'Low', label: 'Low', badge: 'b-gray' },
];

export const priorityBadge = (p: Priority) => PRIORITIES.find((x) => x.value === p)?.badge ?? 'b-gray';
export const statusMeta = (s: AssignmentStatus) => STATUS_META[s] ?? STATUS_META.New;

export const PURPOSES = ['Home loan', 'Loan against property', 'Top-up loan', 'Balance transfer', 'Project finance', 'Mortgage / OD', 'Purchase / sale', 'Capital gains', 'Court / legal'];

export interface AssignmentListItem {
  id: string;
  number: string;
  status: AssignmentStatus;
  priority: Priority;
  bankId: string;
  bankShortCode: string;
  bankName: string;
  bankColor: string;
  branchId: string | null;
  branchName: string | null;
  applicantName: string;
  clientId: string | null;
  propertySummary: string;
  propertyId: string | null;
  valuerId: string;
  valuerName: string;
  dueDate: string;
  fee: number;
  isOverdue: boolean;
  createdAt: string;
}

export interface AssignmentEvent {
  at: string;
  byName: string;
  fromStatus: AssignmentStatus | null;
  toStatus: AssignmentStatus;
  title: string;
  note: string | null;
}

export interface AllowedMoves {
  next: AssignmentStatus | null;
  canMoveNext: boolean;
  nextBlockedReason: string | null;
  back: AssignmentStatus | null;
  canCancel: boolean;
  canReopen: boolean;
  canEdit: boolean;
  canDelete: boolean;
}

export interface AssignmentDetail {
  assignment: AssignmentListItem;
  bankReference: string | null;
  purpose: string | null;
  applicantPhone: string | null;
  reviewerId: string | null;
  reviewerName: string | null;
  siteVisitAt: string | null;
  remarks: string | null;
  events: AssignmentEvent[];
  moves: AllowedMoves;
}

export interface AssignmentQuery {
  search?: string;
  status?: AssignmentStatus | '';
  bankId?: string;
  priority?: Priority | '';
  valuerId?: string;
  clientId?: string;
  propertyId?: string;
  mine?: boolean;
  overdue?: boolean;
  openOnly?: boolean;
  sort?: string;
  desc?: boolean;
  page: number;
  pageSize: number;
}

export interface Pipeline {
  stages: { status: AssignmentStatus; count: number }[];
  open: number;
  overdue: number;
  dueThisWeek: number;
  cancelled: number;
}

export interface SaveAssignment {
  bankId: string;
  branchId: string | null;
  bankReference: string | null;
  purpose: string | null;
  applicantName: string;
  clientId: string | null;
  applicantPhone: string | null;
  propertySummary: string;
  propertyId: string | null;
  valuerId: string;
  reviewerId: string | null;
  priority: Priority;
  dueDate: string;
  siteVisitAt: string | null;
  fee: number;
  remarks: string | null;
}

export interface Lookups {
  banks: { id: string; name: string; shortCode: string; brandColor: string; branches: { id: string; name: string; city: string | null }[] }[];
  valuers: { id: string; name: string; role: string }[];
  reviewers: { id: string; name: string; role: string }[];
  numberPreview: string;
}

export interface ClientOption { id: string; code: string; name: string; mobile: string; type: ClientType; city: string | null }
export interface PropertyOption { id: string; code: string; summary: string; ownerName: string; type: PropertyType }

/** "12 Jun" or "12 Jun 2027" when not this year. */
export function shortDate(iso: string): string {
  const d = new Date(iso.length === 10 ? iso + 'T00:00:00' : iso);
  const sameYear = d.getFullYear() === new Date().getFullYear();
  return d.toLocaleDateString('en-IN', { day: 'numeric', month: 'short', ...(sameYear ? {} : { year: 'numeric' }) });
}

export function rupees(v: number): string {
  return '₹' + v.toLocaleString('en-IN', { maximumFractionDigits: 0 });
}

@Injectable({ providedIn: 'root' })
export class AssignmentsApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/assignments`;

  list(q: AssignmentQuery): Observable<Paged<AssignmentListItem>> {
    let params = new HttpParams().set('page', q.page).set('pageSize', q.pageSize);
    for (const key of ['search', 'status', 'bankId', 'priority', 'valuerId', 'clientId', 'propertyId', 'sort'] as const) {
      if (q[key]) params = params.set(key, q[key] as string);
    }
    if (q.sort) params = params.set('desc', !!q.desc);
    if (q.mine) params = params.set('mine', true);
    if (q.overdue) params = params.set('overdue', true);
    if (q.openOnly) params = params.set('openOnly', true);
    return this.http.get<Paged<AssignmentListItem>>(this.base, { params });
  }
  pipeline(mine = false): Observable<Pipeline> {
    return this.http.get<Pipeline>(`${this.base}/pipeline`, { params: mine ? new HttpParams().set('mine', true) : undefined });
  }
  lookups(): Observable<Lookups> {
    return this.http.get<Lookups>(`${this.base}/lookups`);
  }
  searchClients(search: string): Observable<ClientOption[]> {
    return this.http.get<ClientOption[]>(`${this.base}/lookups/clients`, { params: new HttpParams().set('search', search) });
  }
  searchProperties(search: string): Observable<PropertyOption[]> {
    return this.http.get<PropertyOption[]>(`${this.base}/lookups/properties`, { params: new HttpParams().set('search', search) });
  }
  get(id: string): Observable<AssignmentDetail> {
    return this.http.get<AssignmentDetail>(`${this.base}/${id}`);
  }
  create(body: SaveAssignment): Observable<AssignmentDetail> {
    return this.http.post<AssignmentDetail>(this.base, body);
  }
  update(id: string, body: SaveAssignment): Observable<AssignmentDetail> {
    return this.http.put<AssignmentDetail>(`${this.base}/${id}`, body);
  }
  move(id: string, to: AssignmentStatus, note: string | null, siteVisitAt: string | null = null): Observable<AssignmentDetail> {
    return this.http.post<AssignmentDetail>(`${this.base}/${id}/move`, { to, note, siteVisitAt });
  }
  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
}
