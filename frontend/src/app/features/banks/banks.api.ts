import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export type RateBasis = 'MarketRates' | 'CircleRates' | 'BankRates';
export type DepreciationMethod = 'StraightLine' | 'WrittenDownValue';
export type AnswerType = 'YesNo' | 'Text' | 'Number' | 'Choice';

export const RATE_BASES: { value: RateBasis; label: string }[] = [
  { value: 'MarketRates', label: 'Market Rates' },
  { value: 'CircleRates', label: 'Circle / Jantri Rates' },
  { value: 'BankRates', label: 'Bank Rates' },
];

export const DEPRECIATION_METHODS: { value: DepreciationMethod; label: string }[] = [
  { value: 'StraightLine', label: 'Straight Line' },
  { value: 'WrittenDownValue', label: 'Written Down Value' },
];

export const ANSWER_TYPES: { value: AnswerType; label: string; icon: string }[] = [
  { value: 'YesNo', label: 'Yes / No', icon: 'fa-toggle-on' },
  { value: 'Text', label: 'Text', icon: 'fa-font' },
  { value: 'Number', label: 'Number', icon: 'fa-hashtag' },
  { value: 'Choice', label: 'Choice', icon: 'fa-list-ul' },
];

/** Common banks for the quick pick in "Add Bank" (name, code, IFSC prefix, badge colour). */
export const COMMON_BANKS: { name: string; code: string; ifsc: string; color: string }[] = [
  { name: 'State Bank of India', code: 'SBI', ifsc: 'SBIN', color: '#2453A6' },
  { name: 'HDFC Bank', code: 'HDFC', ifsc: 'HDFC', color: '#E32219' },
  { name: 'ICICI Bank', code: 'ICICI', ifsc: 'ICIC', color: '#F47216' },
  { name: 'Axis Bank', code: 'AXIS', ifsc: 'UTIB', color: '#97144D' },
  { name: 'Bank of Baroda', code: 'BOB', ifsc: 'BARB', color: '#F15A22' },
  { name: 'Punjab National Bank', code: 'PNB', ifsc: 'PUNB', color: '#A20E37' },
  { name: 'Union Bank of India', code: 'UBI', ifsc: 'UBIN', color: '#D4202E' },
  { name: 'Canara Bank', code: 'CANARA', ifsc: 'CNRB', color: '#00599D' },
  { name: 'Kotak Mahindra Bank', code: 'KOTAK', ifsc: 'KKBK', color: '#ED1C24' },
  { name: 'Bank of India', code: 'BOI', ifsc: 'BKID', color: '#F37021' },
  { name: 'IDBI Bank', code: 'IDBI', ifsc: 'IBKL', color: '#00836C' },
  { name: 'Indian Bank', code: 'INDIAN', ifsc: 'IDIB', color: '#1D4F91' },
  { name: 'Central Bank of India', code: 'CBI', ifsc: 'CBIN', color: '#D71920' },
  { name: 'IndusInd Bank', code: 'INDUS', ifsc: 'INDB', color: '#8B1D41' },
  { name: 'Yes Bank', code: 'YES', ifsc: 'YESB', color: '#00518F' },
  { name: 'Federal Bank', code: 'FEDERAL', ifsc: 'FDRL', color: '#C79100' },
];

export interface BankRules {
  rateBasis: RateBasis;
  distressPercent: number;
  realizablePercent: number;
  depreciation: DepreciationMethod;
}

export interface Bank {
  id: string;
  name: string;
  shortCode: string;
  brandColor: string;
  ifscPrefix: string | null;
  reportFormatId: string | null;
  /** Null = the firm's default format. */
  reportFormatName: string | null;
  panelCode: string | null;
  isActive: boolean;
  rules: BankRules;
  branches: number;
  activeBranches: number;
  questions: number;
}

export type SaveBank = Omit<Bank, 'id' | 'branches' | 'activeBranches' | 'questions' | 'reportFormatName'>;

export interface Question {
  text: string;
  answerType: AnswerType;
  options: string[];
  required: boolean;
}

export interface BankDetail {
  bank: Bank;
  questions: Question[];
}

export interface Branch {
  id: string;
  bankId: string;
  name: string;
  ifsc: string | null;
  city: string | null;
  state: string | null;
  address: string | null;
  contactPerson: string | null;
  phone: string | null;
  email: string | null;
  isActive: boolean;
}

export type SaveBranch = Omit<Branch, 'id' | 'bankId'>;

export const rateBasisLabel = (v: RateBasis) => RATE_BASES.find((x) => x.value === v)?.label ?? v;
export const depreciationLabel = (v: DepreciationMethod) => DEPRECIATION_METHODS.find((x) => x.value === v)?.label ?? v;

@Injectable({ providedIn: 'root' })
export class BanksApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/banks`;

  list(search?: string): Observable<Bank[]> {
    const params = search ? new HttpParams().set('search', search) : undefined;
    return this.http.get<Bank[]>(this.base, { params });
  }
  get(id: string): Observable<BankDetail> {
    return this.http.get<BankDetail>(`${this.base}/${id}`);
  }
  create(body: SaveBank): Observable<Bank> {
    return this.http.post<Bank>(this.base, body);
  }
  update(id: string, body: SaveBank): Observable<Bank> {
    return this.http.put<Bank>(`${this.base}/${id}`, body);
  }
  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
  updateRules(banks: { bankId: string; rules: BankRules }[]): Observable<Bank[]> {
    return this.http.put<Bank[]>(`${this.base}/rules`, { banks });
  }
  updateQuestionnaire(id: string, questions: Question[]): Observable<Question[]> {
    return this.http.put<Question[]>(`${this.base}/${id}/questionnaire`, { questions });
  }
  branches(id: string, search?: string): Observable<Branch[]> {
    const params = search ? new HttpParams().set('search', search) : undefined;
    return this.http.get<Branch[]>(`${this.base}/${id}/branches`, { params });
  }
  createBranch(id: string, body: SaveBranch): Observable<Branch> {
    return this.http.post<Branch>(`${this.base}/${id}/branches`, body);
  }
  updateBranch(id: string, branchId: string, body: SaveBranch): Observable<Branch> {
    return this.http.put<Branch>(`${this.base}/${id}/branches/${branchId}`, body);
  }
  deleteBranch(id: string, branchId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}/branches/${branchId}`);
  }
}
