import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import type { AssignmentStatus, CaseType } from '../assignments/assignments.api';

export type AttentionReason = 'overdue' | 'today' | 'soon' | 'review';

export interface DashboardSummary {
  mine: boolean;
  today: string;
  kpis: {
    open: number; overdue: number; dueThisWeek: number; visitsToday: number; inReview: number;
    receivedThisMonth: number; completedThisMonth: number; completedLastMonth: number;
  };
  pipeline: { status: AssignmentStatus; count: number }[];
  attention: {
    id: string; number: string; status: AssignmentStatus; caseType: CaseType; bankShortCode: string; bankColor: string; applicantName: string;
    propertySummary: string; valuerName: string; dueDate: string; reason: AttentionReason; days: number;
  }[];
  visits: { id: string; number: string; at: string; bankShortCode: string; bankColor: string; applicantName: string; propertySummary: string; valuerName: string }[];
  byBank: { bankId: string; name: string; shortCode: string; color: string; open: number; overdue: number }[];
  workload: { userId: string; name: string; open: number; overdue: number; dueThisWeek: number }[];
  trend: { month: string; received: number; completed: number; fees: number }[];
  activity: { assignmentId: string; number: string; at: string; byName: string; title: string; toStatus: AssignmentStatus }[];
  money: {
    invoicedThisMonth: number; collectedThisMonth: number; outstanding: number; overdue: number; overdueInvoices: number;
    toBillCount: number; toBillFees: number;
  } | null;
}

@Injectable({ providedIn: 'root' })
export class DashboardApi {
  private readonly http = inject(HttpClient);

  get(mine: boolean): Observable<DashboardSummary> {
    return this.http.get<DashboardSummary>(`${environment.apiUrl}/api/dashboard`, { params: new HttpParams().set('mine', mine) });
  }
}
