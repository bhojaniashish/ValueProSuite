import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import type { AssignmentStatus } from '../assignments/assignments.api';

export type InvoiceStatus = 'Draft' | 'Issued' | 'Cancelled';
export type PaymentState = 'Draft' | 'Unpaid' | 'PartlyPaid' | 'Paid' | 'Overdue' | 'Cancelled';
export type BillTo = 'Bank' | 'Client' | 'Other';
export type PaymentMode = 'BankTransfer' | 'Upi' | 'Cheque' | 'Cash' | 'Other';
export type InvoiceFilter = '' | 'draft' | 'unpaid' | 'overdue' | 'paid' | 'cancelled';

export interface InvoiceListItem {
  id: string; number: string | null; status: InvoiceStatus; state: PaymentState; invoiceDate: string; dueDate: string; billToName: string;
  bankShortCode: string | null; bankColor: string | null; cases: string[]; taxable: number; gst: number; total: number; outstanding: number;
}
export interface InvoicePage { items: InvoiceListItem[]; total: number; page: number; pageSize: number }

export interface UnbilledCase {
  assignmentId: string; number: string; status: AssignmentStatus; bankId: string; bankShortCode: string; bankColor: string; branchName: string | null;
  applicantName: string; propertySummary: string; fee: number; submittedAt: string | null; draftInvoiceId: string | null;
}

export interface BillingSummary {
  financialYear: string; invoiced: number; collected: number; tds: number; outstanding: number; overdue: number; overdueCount: number;
  unbilledCount: number; unbilledFees: number; months: { month: string; invoiced: number; collected: number }[];
  byBank: { bankId: string | null; name: string; shortCode: string | null; color: string | null; invoices: number; outstanding: number; overdue: number }[];
}

export interface InvoiceLine { assignmentId: string | null; assignmentNumber: string | null; description: string; sac: string | null; amount: number }
export interface Payment {
  id: string; receivedOn: string; amount: number; tds: number; mode: PaymentMode; reference: string | null; note: string | null;
  recordedByName: string; recordedAt: string;
}
export interface BillToOption { billTo: BillTo; clientId: string | null; name: string; address: string | null; gstin: string | null; stateCode: string | null; label: string }

export interface InvoiceDetail {
  id: string; number: string | null; status: InvoiceStatus; state: PaymentState; version: number; invoiceDate: string; dueDate: string;
  bankId: string | null; bankName: string | null; billTo: BillTo; clientId: string | null; billToName: string; billToAddress: string | null;
  billToGstin: string | null; billToStateCode: string | null; placeOfSupply: string | null;
  gstRate: number; interState: boolean; taxable: number; cgst: number; sgst: number; igst: number; roundOff: number; total: number;
  totalInWords: string; received: number; tds: number; outstanding: number; notes: string | null;
  lines: InvoiceLine[]; payments: Payment[];
  issuedAt: string | null; issuedByName: string | null; cancelledAt: string | null; cancelledByName: string | null; cancelReason: string | null;
  firm: { name: string; address: string | null; gstin: string | null; stateCode: string | null; stateName: string | null; pan: string | null };
  billToOptions: BillToOption[]; canEdit: boolean; issueBlockedReason: string | null;
}

export interface SaveInvoice {
  version: number; invoiceDate: string; dueDate: string; billTo: BillTo; clientId: string | null; billToName: string; billToAddress: string | null;
  billToGstin: string | null; billToStateCode: string | null; gstRate: number;
  lines: { assignmentId: string | null; description: string; sac: string | null; amount: number }[]; notes: string | null;
}

export interface AddPayment { receivedOn: string; amount: number; tds: number; mode: PaymentMode; reference: string | null; note: string | null }

export const PAYMENT_MODES: { value: PaymentMode; label: string }[] = [
  { value: 'BankTransfer', label: 'NEFT / RTGS' },
  { value: 'Upi', label: 'UPI' },
  { value: 'Cheque', label: 'Cheque' },
  { value: 'Cash', label: 'Cash' },
  { value: 'Other', label: 'Other' },
];

export const GST_RATES = [18, 12, 5, 0];

/** GST state codes (the first two digits of a GSTIN). */
export const GST_STATES: { code: string; name: string }[] = [
  ['01', 'Jammu and Kashmir'], ['02', 'Himachal Pradesh'], ['03', 'Punjab'], ['04', 'Chandigarh'], ['05', 'Uttarakhand'], ['06', 'Haryana'],
  ['07', 'Delhi'], ['08', 'Rajasthan'], ['09', 'Uttar Pradesh'], ['10', 'Bihar'], ['11', 'Sikkim'], ['12', 'Arunachal Pradesh'], ['13', 'Nagaland'],
  ['14', 'Manipur'], ['15', 'Mizoram'], ['16', 'Tripura'], ['17', 'Meghalaya'], ['18', 'Assam'], ['19', 'West Bengal'], ['20', 'Jharkhand'],
  ['21', 'Odisha'], ['22', 'Chhattisgarh'], ['23', 'Madhya Pradesh'], ['24', 'Gujarat'], ['26', 'Dadra and Nagar Haveli and Daman and Diu'],
  ['27', 'Maharashtra'], ['29', 'Karnataka'], ['30', 'Goa'], ['31', 'Lakshadweep'], ['32', 'Kerala'], ['33', 'Tamil Nadu'], ['34', 'Puducherry'],
  ['35', 'Andaman and Nicobar Islands'], ['36', 'Telangana'], ['37', 'Andhra Pradesh'], ['38', 'Ladakh'],
].map(([code, name]) => ({ code, name }));

export const STATE_META: Record<PaymentState, { label: string; badge: string }> = {
  Draft: { label: 'Draft', badge: 'b-gray' },
  Unpaid: { label: 'Unpaid', badge: 'b-warn' },
  PartlyPaid: { label: 'Part paid', badge: 'b-info' },
  Paid: { label: 'Paid', badge: 'b-ok' },
  Overdue: { label: 'Overdue', badge: 'b-danger' },
  Cancelled: { label: 'Cancelled', badge: 'b-gray' },
};

/** ₹12,34,567.50 with paise only when there are any. */
export function money(v: number | null | undefined): string {
  if (v == null) return '—';
  return '₹' + v.toLocaleString('en-IN', { minimumFractionDigits: Number.isInteger(v) ? 0 : 2, maximumFractionDigits: 2 });
}

export function day(iso: string): string {
  return new Date(iso + (iso.length === 10 ? 'T00:00:00' : '')).toLocaleDateString('en-IN', { day: 'numeric', month: 'short', year: 'numeric' });
}

/** Today in India as yyyy-mm-dd. */
export function today(): string {
  return new Date(Date.now() + 5.5 * 3600_000).toISOString().slice(0, 10);
}

@Injectable({ providedIn: 'root' })
export class BillingApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/billing`;

  summary(): Observable<BillingSummary> {
    return this.http.get<BillingSummary>(`${this.base}/summary`);
  }
  list(q: { search?: string; state?: InvoiceFilter; bankId?: string | null; assignmentId?: string; page?: number; pageSize?: number }): Observable<InvoicePage> {
    let p = new HttpParams();
    for (const [k, v] of Object.entries(q)) if (v != null && v !== '') p = p.set(k, String(v));
    return this.http.get<InvoicePage>(`${this.base}/invoices`, { params: p });
  }
  unbilled(bankId?: string | null): Observable<UnbilledCase[]> {
    return this.http.get<UnbilledCase[]>(`${this.base}/unbilled`, { params: bankId ? new HttpParams().set('bankId', bankId) : undefined });
  }
  create(assignmentIds: string[], billTo: BillTo | null = null): Observable<InvoiceDetail> {
    return this.http.post<InvoiceDetail>(`${this.base}/invoices`, { assignmentIds, billTo });
  }
  get(id: string): Observable<InvoiceDetail> {
    return this.http.get<InvoiceDetail>(`${this.base}/invoices/${id}`);
  }
  save(id: string, body: SaveInvoice): Observable<InvoiceDetail> {
    return this.http.put<InvoiceDetail>(`${this.base}/invoices/${id}`, body);
  }
  issue(id: string): Observable<InvoiceDetail> {
    return this.http.post<InvoiceDetail>(`${this.base}/invoices/${id}/issue`, {});
  }
  cancel(id: string, reason: string): Observable<InvoiceDetail> {
    return this.http.post<InvoiceDetail>(`${this.base}/invoices/${id}/cancel`, { reason });
  }
  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/invoices/${id}`);
  }
  addPayment(id: string, body: AddPayment): Observable<InvoiceDetail> {
    return this.http.post<InvoiceDetail>(`${this.base}/invoices/${id}/payments`, body);
  }
  removePayment(id: string, paymentId: string): Observable<InvoiceDetail> {
    return this.http.delete<InvoiceDetail>(`${this.base}/invoices/${id}/payments/${paymentId}`);
  }
  pdf(id: string, download = false): Observable<Blob> {
    return this.http.get(`${this.base}/invoices/${id}/pdf`, { params: new HttpParams().set('download', download), responseType: 'blob' });
  }
}
