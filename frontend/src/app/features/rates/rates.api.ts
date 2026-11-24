import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import type { DepreciationMethod } from '../banks/banks.api';
import { Paged } from '../users/users.api';

export type RateKind = 'Location' | 'Circle' | 'Construction';
export type RateZone = 'Residential' | 'Commercial' | 'MixedUse' | 'Industrial' | 'Agricultural';
export type CircleScheme = 'Jantri' | 'ReadyReckoner' | 'GuidelineValue' | 'CircleRate' | 'DlcRate';
export type RateUnit = 'SqFt' | 'SqM' | 'RunningFt';

export const ZONES: { value: RateZone; label: string; badge: string }[] = [
  { value: 'Residential', label: 'Residential', badge: 'b-brand' },
  { value: 'Commercial', label: 'Commercial', badge: 'b-purple' },
  { value: 'MixedUse', label: 'Mixed Use', badge: 'b-teal' },
  { value: 'Industrial', label: 'Industrial', badge: 'b-gray' },
  { value: 'Agricultural', label: 'Agricultural', badge: 'b-ok' },
];

export const SCHEMES: { value: CircleScheme; label: string; hint: string }[] = [
  { value: 'Jantri', label: 'Jantri', hint: 'Gujarat' },
  { value: 'ReadyReckoner', label: 'Ready Reckoner', hint: 'Maharashtra' },
  { value: 'GuidelineValue', label: 'Guideline Value', hint: 'Karnataka, Tamil Nadu, MP…' },
  { value: 'CircleRate', label: 'Circle Rate', hint: 'Delhi, UP, Haryana…' },
  { value: 'DlcRate', label: 'DLC Rate', hint: 'Rajasthan' },
];

export const UNITS: { value: RateUnit; label: string }[] = [
  { value: 'SqFt', label: 'per sq.ft' },
  { value: 'SqM', label: 'per sq.m' },
  { value: 'RunningFt', label: 'per r.ft' },
];

/** Which scheme a state usually uses (pre-selected when adding a circle rate). */
export const SCHEME_BY_STATE: Record<string, CircleScheme> = {
  Gujarat: 'Jantri',
  Maharashtra: 'ReadyReckoner',
  Rajasthan: 'DlcRate',
  Karnataka: 'GuidelineValue',
  'Tamil Nadu': 'GuidelineValue',
  'Madhya Pradesh': 'GuidelineValue',
  Kerala: 'GuidelineValue',
};

export const zoneMeta = (z: RateZone) => ZONES.find((x) => x.value === z) ?? ZONES[0];
export const schemeLabel = (s: CircleScheme | null) => (s ? (SCHEMES.find((x) => x.value === s)?.label ?? s) : '');
export const unitLabel = (u: RateUnit) => UNITS.find((x) => x.value === u)?.label ?? u;

export interface LocationRate {
  id: string; locality: string; city: string; state: string | null; zone: RateZone; minRate: number; maxRate: number;
  effectiveFrom: string; source: string | null; isCurrent: boolean; circleRatePerSqM: number | null; circleScheme: CircleScheme | null;
}
export interface CircleRate {
  id: string; state: string; scheme: CircleScheme; city: string; locality: string; residential: number | null;
  commercial: number | null; openLand: number | null; effectiveFrom: string; reference: string | null; isCurrent: boolean;
}
export interface ConstructionRate {
  id: string; constructionType: string; unit: RateUnit; cpwdRate: number | null; pwdRate: number | null; bankRate: number | null;
  effectiveFrom: string; remarks: string | null; isCurrent: boolean;
}
export interface DepreciationRule {
  id: string; structureType: string; totalLifeYears: number; salvagePercent: number; method: DepreciationMethod; remarks: string | null;
  bands: { label: string; fromYears: number; toYears: number; percent: number }[];
}

export type SaveLocation = Omit<LocationRate, 'id' | 'isCurrent' | 'circleRatePerSqM' | 'circleScheme'>;
export type SaveCircle = Omit<CircleRate, 'id' | 'isCurrent'>;
export type SaveConstruction = Omit<ConstructionRate, 'id' | 'isCurrent'>;
export type SaveDepreciation = Omit<DepreciationRule, 'id' | 'bands'>;

export interface RateFilters { cities: string[]; states: string[]; location: number; circle: number; construction: number; depreciation: number }

export interface RateImportRow { row: number; data: Record<string, unknown> | null; errors: string[]; isValid: boolean }
export interface RateImportPreview { kind: RateKind; rows: RateImportRow[]; valid: number; invalid: number }

/** ₹ with Indian digit grouping: 115000 → ₹1,15,000. */
export function inr(v: number | null | undefined): string {
  return v == null ? '—' : '₹' + Number(v).toLocaleString('en-IN', { maximumFractionDigits: 2 });
}

/** yyyy-mm-dd → 1 Apr 2026. */
export function day(iso: string): string {
  return new Date(iso + 'T00:00:00').toLocaleDateString('en-IN', { day: 'numeric', month: 'short', year: 'numeric' });
}

/** Local date → yyyy-mm-dd (no time-zone shift). */
export function isoDate(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** Saves rows as a CSV the user can open in Excel (UTF-8 with BOM so ₹ shows correctly). */
export function downloadCsv(fileName: string, header: string[], rows: (string | number | null)[][]): void {
  const esc = (v: string | number | null) => {
    const s = v == null ? '' : String(v);
    return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
  };
  const text = '﻿' + [header, ...rows].map((r) => r.map(esc).join(',')).join('\n');
  const url = URL.createObjectURL(new Blob([text], { type: 'text/csv;charset=utf-8' }));
  Object.assign(document.createElement('a'), { href: url, download: fileName }).click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

@Injectable({ providedIn: 'root' })
export class RatesApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/rates`;

  filters(): Observable<RateFilters> {
    return this.http.get<RateFilters>(`${this.base}/filters`);
  }

  /** Loads up to 500 rows; the table sorts, filters and pages them in the browser. */
  location(currentOnly: boolean): Observable<Paged<LocationRate>> {
    return this.http.get<Paged<LocationRate>>(`${this.base}/location`, { params: this.params(currentOnly) });
  }
  circle(currentOnly: boolean): Observable<Paged<CircleRate>> {
    return this.http.get<Paged<CircleRate>>(`${this.base}/circle`, { params: this.params(currentOnly) });
  }
  construction(currentOnly: boolean): Observable<Paged<ConstructionRate>> {
    return this.http.get<Paged<ConstructionRate>>(`${this.base}/construction`, { params: this.params(currentOnly) });
  }
  depreciation(): Observable<DepreciationRule[]> {
    return this.http.get<DepreciationRule[]>(`${this.base}/depreciation`);
  }
  addStandardDepreciation(): Observable<DepreciationRule[]> {
    return this.http.post<DepreciationRule[]>(`${this.base}/depreciation/standard`, {});
  }

  save(kind: 'location' | 'circle' | 'construction' | 'depreciation', id: string | null, body: unknown): Observable<unknown> {
    return id ? this.http.put(`${this.base}/${kind}/${id}`, body) : this.http.post(`${this.base}/${kind}`, body);
  }
  delete(kind: 'location' | 'circle' | 'construction' | 'depreciation', id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${kind}/${id}`);
  }

  template(kind: RateKind): Observable<Blob> {
    return this.http.get(`${this.base}/${kind}/import/template`, { responseType: 'blob' });
  }
  previewImport(kind: RateKind, file: File): Observable<RateImportPreview> {
    return this.http.post<RateImportPreview>(`${this.base}/${kind}/import/preview`, this.form(file));
  }
  import(kind: RateKind, file: File): Observable<{ imported: number; rejected: number }> {
    return this.http.post<{ imported: number; rejected: number }>(`${this.base}/${kind}/import`, this.form(file));
  }

  private params(currentOnly: boolean): HttpParams {
    return new HttpParams().set('currentOnly', currentOnly).set('pageSize', 500);
  }

  private form(file: File): FormData {
    const f = new FormData();
    f.append('file', file);
    return f;
  }
}
