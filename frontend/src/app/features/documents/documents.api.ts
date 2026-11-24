import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import type { AssignmentStatus } from '../assignments/assignments.api';
import type { PropertyType } from '../properties/properties.api';
import { Paged } from '../users/users.api';

export type DocumentType =
  | 'SaleDeed' | 'AgreementForSale' | 'IndexII' | 'SevenTwelve' | 'PropertyCard' | 'NaOrder' | 'ApprovedPlan' | 'BuildingPermission'
  | 'OccupancyCertificate' | 'TaxBill' | 'ElectricityBill' | 'ShareCertificate' | 'AllotmentLetter' | 'TitleReport'
  | 'EncumbranceCertificate' | 'Noc' | 'Other';
export type Availability = 'OriginalSeen' | 'CopyOnly' | 'NotProvided';
export type ExpiryState = 'Valid' | 'Expiring' | 'Expired';

export const DOC_TYPES: { value: DocumentType; label: string; icon: string; hint?: string }[] = [
  { value: 'SaleDeed', label: 'Sale deed', icon: 'fa-file-signature', hint: 'Conveyance / title deed' },
  { value: 'AgreementForSale', label: 'Agreement for sale', icon: 'fa-handshake' },
  { value: 'IndexII', label: 'Index II', icon: 'fa-list-ol', hint: 'Registration extract' },
  { value: 'SevenTwelve', label: '7/12 extract', icon: 'fa-scroll', hint: 'Land record' },
  { value: 'PropertyCard', label: 'Property card', icon: 'fa-id-card' },
  { value: 'NaOrder', label: 'NA order', icon: 'fa-stamp', hint: 'Non-agricultural permission' },
  { value: 'ApprovedPlan', label: 'Approved plan', icon: 'fa-ruler-combined' },
  { value: 'BuildingPermission', label: 'Building permission', icon: 'fa-helmet-safety' },
  { value: 'OccupancyCertificate', label: 'Occupancy / BU certificate', icon: 'fa-award' },
  { value: 'TaxBill', label: 'Property tax bill', icon: 'fa-receipt' },
  { value: 'ElectricityBill', label: 'Electricity bill', icon: 'fa-bolt' },
  { value: 'ShareCertificate', label: 'Share certificate', icon: 'fa-certificate', hint: 'Society' },
  { value: 'AllotmentLetter', label: 'Allotment letter', icon: 'fa-envelope-open-text' },
  { value: 'TitleReport', label: 'Title search report', icon: 'fa-magnifying-glass' },
  { value: 'EncumbranceCertificate', label: 'Encumbrance certificate', icon: 'fa-link-slash' },
  { value: 'Noc', label: 'NOC', icon: 'fa-file-circle-check' },
  { value: 'Other', label: 'Other document', icon: 'fa-file' },
];
export const docMeta = (t: DocumentType) => DOC_TYPES.find((x) => x.value === t) ?? DOC_TYPES[DOC_TYPES.length - 1];

export const AVAILABILITY: { value: Availability; label: string; badge: string }[] = [
  { value: 'OriginalSeen', label: 'Original seen', badge: 'b-ok' },
  { value: 'CopyOnly', label: 'Copy only', badge: 'b-info' },
  { value: 'NotProvided', label: 'Not provided', badge: 'b-danger' },
];
export const availabilityMeta = (a: Availability) => AVAILABILITY.find((x) => x.value === a) ?? AVAILABILITY[1];
export const EXPIRY_BADGE: Record<ExpiryState, string> = { Valid: 'b-ok', Expiring: 'b-warn', Expired: 'b-danger' };

export interface DocumentFile { id: string; version: number; fileName: string; contentType: string; sizeBytes: number; uploadedAt: string; uploadedByName: string }

export interface CaseDocument {
  id: string; assignmentId: string; type: DocumentType; label: string; title: string | null; availability: Availability;
  number: string | null; issuedOn: string | null; issuedBy: string | null; validUntil: string | null; expiryState: ExpiryState | null;
  notes: string | null; files: DocumentFile[]; copiedFromCase: string | null; createdAt: string;
}

export interface ChecklistItem { type: DocumentType; label: string; recorded: boolean; notProvided: boolean }

export interface CaseDocuments {
  assignmentId: string; number: string; status: AssignmentStatus; bankShortCode: string; bankColor: string; applicantName: string;
  propertySummary: string; propertyType: PropertyType | null; documents: CaseDocument[]; checklist: ChecklistItem[];
  earlier: { document: CaseDocument; caseNumber: string }[]; canEdit: boolean; readOnlyReason: string | null;
}

export interface SaveDocument {
  type: DocumentType; title: string | null; availability: Availability; number: string | null; issuedOn: string | null;
  issuedBy: string | null; validUntil: string | null; notes: string | null;
}

export interface DocumentListItem { document: CaseDocument; caseNumber: string; bankShortCode: string; bankColor: string; applicantName: string }
export interface DocumentPage {
  page: Paged<DocumentListItem>;
  summary: { total: number; withFile: number; notProvided: number; expiring: number; expired: number };
}
export interface DocumentQuery { search?: string; type?: DocumentType | ''; expiry?: 'Expiring' | 'Expired' | ''; mine?: boolean; page?: number; pageSize?: number }

/** A document's display name: its own title, or the type's name. */
export const docName = (d: Pick<CaseDocument, 'title' | 'label'>) => d.title || d.label;

/** 2026-04-01 → 1 Apr 2026 */
export function day(iso: string | null): string {
  return iso ? new Date(iso + 'T00:00:00').toLocaleDateString('en-IN', { day: 'numeric', month: 'short', year: 'numeric' }) : '';
}

export function size(bytes: number): string {
  return bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;
}

@Injectable({ providedIn: 'root' })
export class DocumentsApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/documents`;

  list(q: DocumentQuery): Observable<DocumentPage> {
    let params = new HttpParams();
    for (const [k, v] of Object.entries(q)) if (v !== undefined && v !== null && v !== '' && v !== false) params = params.set(k, String(v));
    return this.http.get<DocumentPage>(this.base, { params });
  }
  case(assignmentId: string): Observable<CaseDocuments> {
    return this.http.get<CaseDocuments>(`${this.base}/cases/${assignmentId}`);
  }
  create(assignmentId: string, body: SaveDocument): Observable<CaseDocument> {
    return this.http.post<CaseDocument>(`${this.base}/cases/${assignmentId}`, body);
  }
  update(id: string, body: SaveDocument): Observable<CaseDocument> {
    return this.http.put<CaseDocument>(`${this.base}/${id}`, body);
  }
  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
  upload(id: string, file: File): Observable<CaseDocument> {
    const f = new FormData();
    f.append('file', file);
    return this.http.post<CaseDocument>(`${this.base}/${id}/files`, f);
  }
  reuse(assignmentId: string, documentId: string): Observable<CaseDocument> {
    return this.http.post<CaseDocument>(`${this.base}/cases/${assignmentId}/reuse/${documentId}`, {});
  }
  file(fileId: string): Observable<Blob> {
    return this.http.get(`${this.base}/files/${fileId}`, { responseType: 'blob' });
  }
  zip(assignmentId: string): Observable<Blob> {
    return this.http.get(`${this.base}/cases/${assignmentId}/zip`, { responseType: 'blob' });
  }
}
