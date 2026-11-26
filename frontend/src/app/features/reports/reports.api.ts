import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import type { AssignmentStatus } from '../assignments/assignments.api';

export type ReportKind = 'Draft' | 'Final';
export type ReportStage = 'draft' | 'review' | 'done';

export interface ReportCheck { key: 'inspection' | 'valuation' | 'documents' | 'photos' | 'signature'; label: string; ok: boolean; blocking: boolean; detail: string | null }
export interface ReportFileItem {
  id: string; number: number; kind: ReportKind; sizeBytes: number; generatedAt: string; generatedByName: string;
  fairMarketValue: number; signedByName: string | null; approvedByName: string | null;
}
export interface ReportFields {
  reportDate: string | null; valuationDate: string | null; opinion: string | null; declaration: string;
  declarationIsStandard: boolean; place: string | null; photosPerPage: number;
}
export interface ReportDetail {
  assignmentId: string; number: string; status: AssignmentStatus; bankShortCode: string; bankColor: string; applicantName: string;
  propertySummary: string; valuerName: string; reviewerName: string | null; fairMarketValue: number | null;
  checks: ReportCheck[]; fields: ReportFields; files: ReportFileItem[];
  canEdit: boolean; canGenerate: boolean; generateBlockedReason: string | null; canApprove: boolean; approveBlockedReason: string | null;
  readOnlyReason: string | null;
  caseType: 'Valuation' | 'StageInspection' | 'Reinspection'; recommendedRelease: number | null;
}
export interface ReportListItem {
  assignmentId: string; number: string; status: AssignmentStatus; bankShortCode: string; bankColor: string; applicantName: string;
  propertySummary: string; valuerName: string; reviewerName: string | null; dueDate: string; fairMarketValue: number | null;
  drafts: number; lastGeneratedAt: string | null;
}
export interface SaveReport {
  reportDate: string | null; valuationDate: string | null; opinion: string | null; declaration: string | null; place: string | null; photosPerPage: number;
}

/** Where each readiness check can be fixed. */
export const CHECK_LINKS: Record<ReportCheck['key'], (id: string) => string> = {
  inspection: (id) => `/inspection/${id}`,
  valuation: (id) => `/valuation/${id}`,
  documents: (id) => `/documents/${id}`,
  photos: (id) => `/photos/${id}`,
  signature: () => '/account/signature',
};

@Injectable({ providedIn: 'root' })
export class ReportsApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/reports`;

  list(stage: ReportStage, mine: boolean, search = ''): Observable<ReportListItem[]> {
    return this.http.get<ReportListItem[]>(this.base, { params: new HttpParams().set('stage', stage).set('mine', mine).set('search', search) });
  }
  get(id: string): Observable<ReportDetail> {
    return this.http.get<ReportDetail>(`${this.base}/${id}`);
  }
  save(id: string, body: SaveReport): Observable<ReportDetail> {
    return this.http.put<ReportDetail>(`${this.base}/${id}`, body);
  }
  draft(id: string): Observable<ReportDetail> {
    return this.http.post<ReportDetail>(`${this.base}/${id}/draft`, {});
  }
  approve(id: string): Observable<ReportDetail> {
    return this.http.post<ReportDetail>(`${this.base}/${id}/approve`, {});
  }
  pdf(fileId: string): Observable<Blob> {
    return this.http.get(`${this.base}/files/${fileId}`, { responseType: 'blob' });
  }
}
