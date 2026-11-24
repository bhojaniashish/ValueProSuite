import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import type { AssignmentStatus } from '../assignments/assignments.api';
import type { DepreciationMethod, RateBasis } from '../banks/banks.api';
import type { PropertyType } from '../properties/properties.api';
import type { CircleScheme, RateZone } from '../rates/rates.api';

export type ValuationMethod = 'LandAndBuilding' | 'Composite';
export type AreaBasis = 'Carpet' | 'BuiltUp' | 'SuperBuiltUp';

export interface BuildingLine {
  label: string | null; areaSqFt: number; ratePerSqFt: number; ageYears: number; lifeYears: number; salvagePercent: number; method: DepreciationMethod;
}
export interface Adjustment { label: string | null; percent: number }
export interface Extra { label: string | null; amount: number }

export interface ValuationInput {
  method: ValuationMethod;
  landAreaSqFt: number; landRatePerSqFt: number; building: BuildingLine[];
  areaBasis: AreaBasis; areaSqFt: number; compositeRatePerSqFt: number; constructionRatePerSqFt: number; builtUpSqFt: number;
  adjustments: Adjustment[]; extras: Extra[];
  circleRatePerSqM: number; guidelineAreaSqFt: number;
  realizablePercent: number; distressPercent: number;
  marketRateMin: number | null; marketRateMax: number | null; documentAreaSqFt: number | null; measuredAreaSqFt: number | null;
  remarks: string | null;
}

export interface WorkingLine { label: string; working: string | null; amount: number }

export interface ValuationResult {
  landValue: number; replacementCost: number; depreciation: number; buildingValue: number; compositeValue: number;
  adjustmentAmount: number; extrasAmount: number; marketValueBeforeRounding: number; fairMarketValue: number;
  realizableValue: number; distressValue: number; guidelineValue: number; insuranceValue: number; ratePerSqFt: number;
  fairMarketValueInWords: string; working: WorkingLine[]; warnings: string[];
}

export interface LocationOption {
  id: string; locality: string; city: string; zone: RateZone; minRate: number; maxRate: number; circleResidential: number | null;
  circleCommercial: number | null; circleOpenLand: number | null; scheme: CircleScheme | null; effectiveFrom: string; suggested: boolean;
}
export interface ConstructionOption { id: string; constructionType: string; cpwdPerSqFt: number | null; pwdPerSqFt: number | null; bankPerSqFt: number | null; suggested: boolean }
export interface DepreciationOption { structureType: string; lifeYears: number; salvagePercent: number }

export interface ValuationDetail {
  case: {
    assignmentId: string; number: string; status: AssignmentStatus; bankName: string; bankShortCode: string; bankColor: string;
    applicantName: string; propertySummary: string; propertyType: PropertyType | null; city: string | null; valuerName: string;
  };
  saved: boolean; version: number; valuedAt: string | null; valuedByName: string | null;
  input: ValuationInput; result: ValuationResult;
  bank: { rateBasis: RateBasis; depreciation: DepreciationMethod; realizablePercent: number; distressPercent: number };
  inspection: {
    submitted: boolean; landDocs: number | null; landMeasured: number | null; carpetDocs: number | null; carpetMeasured: number | null;
    builtUpDocs: number | null; builtUpMeasured: number | null; superBuiltUpDocs: number | null; structureType: string | null;
    ageYears: number | null; condition: string | null; marketability: string | null;
  };
  options: { locations: LocationOption[]; construction: ConstructionOption[]; depreciation: DepreciationOption[] };
  suggestions: string[]; canEdit: boolean; readOnlyReason: string | null;
}

export interface ValuationListItem {
  assignmentId: string; number: string; status: AssignmentStatus; bankShortCode: string; bankColor: string; applicantName: string;
  propertySummary: string; valuerName: string; dueDate: string; fairMarketValue: number | null; valuedAt: string | null;
}

export const AREA_BASES: { value: AreaBasis; label: string }[] = [
  { value: 'Carpet', label: 'Carpet' },
  { value: 'BuiltUp', label: 'Built-up' },
  { value: 'SuperBuiltUp', label: 'Super built-up' },
];

/** Common adjustments valuers apply, offered as one-tap additions. */
export const ADJUSTMENT_PRESETS: Adjustment[] = [
  { label: 'Floor rise', percent: 2 },
  { label: 'Corner / park facing', percent: 5 },
  { label: 'Old building', percent: -5 },
  { label: 'Poor approach', percent: -5 },
  { label: 'Negatives nearby', percent: -10 },
];
export const EXTRA_PRESETS = ['Compound wall', 'Borewell', 'Solar plant', 'Car parking', 'Lift'];

/** ₹ with Indian grouping, whole rupees. */
export function inr(v: number | null | undefined): string {
  return v == null ? '—' : '₹' + Math.round(v).toLocaleString('en-IN');
}

/** ₹1,25,65,000 → "₹1.26 Cr"; ₹85,20,000 → "₹85.2 L" (for quick reading). */
export function short(v: number): string {
  if (v >= 1_00_00_000) return `₹${(v / 1_00_00_000).toFixed(2).replace(/\.?0+$/, '')} Cr`;
  if (v >= 1_00_000) return `₹${(v / 1_00_000).toFixed(2).replace(/\.?0+$/, '')} L`;
  return inr(v);
}

@Injectable({ providedIn: 'root' })
export class ValuationsApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/valuations`;

  list(done: boolean, mine: boolean, search = ''): Observable<ValuationListItem[]> {
    return this.http.get<ValuationListItem[]>(this.base, { params: new HttpParams().set('done', done).set('mine', mine).set('search', search) });
  }
  get(assignmentId: string): Observable<ValuationDetail> {
    return this.http.get<ValuationDetail>(`${this.base}/${assignmentId}`);
  }
  calculate(input: ValuationInput): Observable<ValuationResult> {
    return this.http.post<ValuationResult>(`${this.base}/calculate`, input);
  }
  save(assignmentId: string, version: number, input: ValuationInput): Observable<ValuationDetail> {
    return this.http.put<ValuationDetail>(`${this.base}/${assignmentId}`, { version, input });
  }
}
