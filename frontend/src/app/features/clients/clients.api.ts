import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Paged } from '../users/users.api';

export type ClientType = 'Individual' | 'Builder' | 'BankBranch' | 'Advocate';

export const CLIENT_TYPES: { value: ClientType; label: string; plural: string; badge: string; icon: string; tone: string }[] = [
  { value: 'Individual', label: 'Individual', plural: 'Individual clients', badge: 'b-brand', icon: 'fa-user', tone: 'b-brand' },
  { value: 'Builder', label: 'Builder', plural: 'Builders', badge: 'b-purple', icon: 'fa-helmet-safety', tone: 'b-purple' },
  { value: 'BankBranch', label: 'Bank Branch', plural: 'Bank branches', badge: 'b-teal', icon: 'fa-building-columns', tone: 'b-teal' },
  { value: 'Advocate', label: 'Advocate', plural: 'Advocates', badge: 'b-gold', icon: 'fa-scale-balanced', tone: 'b-gold' },
];

/** Display info for a type; falls back to Individual so an unexpected value can't break a page. */
export const clientType = (t: ClientType) => CLIENT_TYPES.find((x) => x.value === t) ?? CLIENT_TYPES[0];

export interface Client {
  id: string;
  code: string;
  type: ClientType;
  name: string;
  mobile: string;
  email: string | null;
  gstin: string | null;
  pan: string | null;
  address: string | null;
  city: string | null;
  state: string | null;
  pincode: string | null;
  remarks: string | null;
  createdAt: string;
}

export type SaveClient = Omit<Client, 'id' | 'code' | 'createdAt'>;

export interface ClientQuery {
  search?: string;
  type?: ClientType | '';
  state?: string;
  sort?: string;
  desc?: boolean;
  page: number;
  pageSize: number;
}

export interface ClientSummary {
  total: number;
  individual: number;
  builder: number;
  bankBranch: number;
  advocate: number;
  states: string[];
}

export interface ImportRow {
  row: number;
  client: SaveClient | null;
  errors: string[];
  isValid: boolean;
}

export interface ImportPreview {
  rows: ImportRow[];
  valid: number;
  invalid: number;
}

export interface ImportResult {
  imported: number;
  rejected: ImportRow[];
}

@Injectable({ providedIn: 'root' })
export class ClientsApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/clients`;

  list(q: ClientQuery): Observable<Paged<Client>> {
    let params = new HttpParams().set('page', q.page).set('pageSize', q.pageSize);
    if (q.search) params = params.set('search', q.search);
    if (q.type) params = params.set('type', q.type);
    if (q.state) params = params.set('state', q.state);
    if (q.sort) params = params.set('sort', q.sort).set('desc', !!q.desc);
    return this.http.get<Paged<Client>>(this.base, { params });
  }

  summary(): Observable<ClientSummary> {
    return this.http.get<ClientSummary>(`${this.base}/summary`);
  }

  create(body: SaveClient): Observable<Client> {
    return this.http.post<Client>(this.base, body);
  }

  update(id: string, body: SaveClient): Observable<Client> {
    return this.http.put<Client>(`${this.base}/${id}`, body);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  template(): Observable<Blob> {
    return this.http.get(`${this.base}/import/template`, { responseType: 'blob' });
  }

  previewImport(file: File): Observable<ImportPreview> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<ImportPreview>(`${this.base}/import/preview`, form);
  }

  import(clients: SaveClient[]): Observable<ImportResult> {
    return this.http.post<ImportResult>(`${this.base}/import`, { clients });
  }
}
