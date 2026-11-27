import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface ReportSectionSetting { key: string; include: boolean; title: string | null }

export interface ReportLayout {
  sections: ReportSectionSetting[];
  numberSections: boolean;
  showWorking: boolean;
  showRealizable: boolean;
  showDistress: boolean;
  showGuideline: boolean;
  showInsurance: boolean;
  photoAnnexure: boolean;
}

export interface ReportFormatItem { id: string; name: string; description: string | null; isDefault: boolean; banks: string[]; updatedAt: string }

export interface SaveReportFormat {
  name: string; description: string | null; title: string | null; subtitle: string | null; addressTo: string | null;
  layout: ReportLayout; declaration: string | null; limitingConditions: string | null; opinionWording: string | null;
}

export interface ReportFormatDetail extends SaveReportFormat { id: string; isDefault: boolean; banks: string[] }

export interface SectionInfo { key: string; title: string; description: string; stageOnly: boolean }
export interface Placeholder { token: string; meaning: string }
export interface ReportFormatCatalog {
  sections: SectionInfo[]; declarationPlaceholders: Placeholder[]; opinionPlaceholders: Placeholder[];
  standardDeclaration: string; standardOpinion: string; standardTitle: string; standardAddressTo: string;
}

/** A new format starts as the standard report. */
export function blankFormat(catalog: ReportFormatCatalog, name: string): SaveReportFormat {
  return {
    name, description: null, title: null, subtitle: null, addressTo: null, declaration: null, limitingConditions: null, opinionWording: null,
    layout: {
      sections: catalog.sections.map((s) => ({ key: s.key, include: true, title: null })),
      numberSections: true, showWorking: true, showRealizable: true, showDistress: true, showGuideline: true, showInsurance: true, photoAnnexure: true,
    },
  };
}

@Injectable({ providedIn: 'root' })
export class ReportFormatsApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/report-formats`;

  catalog(): Observable<ReportFormatCatalog> {
    return this.http.get<ReportFormatCatalog>(`${this.base}/catalog`);
  }
  list(): Observable<ReportFormatItem[]> {
    return this.http.get<ReportFormatItem[]>(this.base);
  }
  get(id: string): Observable<ReportFormatDetail> {
    return this.http.get<ReportFormatDetail>(`${this.base}/${id}`);
  }
  create(body: SaveReportFormat): Observable<ReportFormatDetail> {
    return this.http.post<ReportFormatDetail>(this.base, body);
  }
  update(id: string, body: SaveReportFormat): Observable<ReportFormatDetail> {
    return this.http.put<ReportFormatDetail>(`${this.base}/${id}`, body);
  }
  duplicate(id: string): Observable<ReportFormatDetail> {
    return this.http.post<ReportFormatDetail>(`${this.base}/${id}/duplicate`, {});
  }
  makeDefault(id: string): Observable<ReportFormatDetail> {
    return this.http.post<ReportFormatDetail>(`${this.base}/${id}/default`, {});
  }
  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
  preview(body: SaveReportFormat, stage: boolean): Observable<Blob> {
    return this.http.post(`${this.base}/preview`, body, { params: new HttpParams().set('stage', stage), responseType: 'blob' });
  }
}
