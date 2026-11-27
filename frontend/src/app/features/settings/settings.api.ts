import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface CompanySettings {
  name: string;
  registeredValuerNo: string | null;
  ibbiRegistration: string | null;
  contactEmail: string | null;
  phone: string | null;
  address: string | null;
  state: string | null;
}

export interface BrandingSettings {
  primaryColor: string;
  accentColor: string;
  logoUrl: string | null;
  watermarkUrl: string | null;
}

export type SmsProvider = 'None' | 'Msg91' | 'Twilio' | 'TextLocal';

export interface CommunicationSettings {
  smtpHost: string | null;
  smtpPort: number;
  smtpUseSsl: boolean;
  smtpUsername: string | null;
  hasSmtpPassword: boolean;
  fromEmail: string | null;
  fromName: string | null;
  smsProvider: SmsProvider;
  smsSenderId: string | null;
  hasSmsApiKey: boolean;
}

/** null = keep the stored secret, '' = clear it, text = replace it. */
export interface UpdateCommunication extends Omit<CommunicationSettings, 'hasSmtpPassword' | 'hasSmsApiKey'> {
  smtpPassword: string | null;
  smsApiKey: string | null;
}

export interface GstSettings {
  gstin: string | null;
  defaultGstRate: number;
  stateCode: string | null;
  placeOfSupply: string | null;
  invoicePrefix: string;
  assignmentPrefix: string;
  /** Service code on invoice lines (997222 = real estate appraisal). */
  sacCode: string;
  paymentDueDays: number;
  /** Bank account / UPI printed on invoices. */
  paymentDetails: string | null;
  invoiceTerms: string | null;
}

export interface NotificationSetting {
  event: string;
  label: string;
  description: string;
  email: boolean;
  sms: boolean;
  inApp: boolean;
}

export interface Settings {
  company: CompanySettings;
  branding: BrandingSettings;
  communication: CommunicationSettings;
  gst: GstSettings;
  notifications: NotificationSetting[];
}

/** GST state codes (first two digits of a GSTIN). */
export const INDIAN_STATES: { code: string; name: string }[] = [
  { code: '01', name: 'Jammu and Kashmir' }, { code: '02', name: 'Himachal Pradesh' }, { code: '03', name: 'Punjab' },
  { code: '04', name: 'Chandigarh' }, { code: '05', name: 'Uttarakhand' }, { code: '06', name: 'Haryana' },
  { code: '07', name: 'Delhi' }, { code: '08', name: 'Rajasthan' }, { code: '09', name: 'Uttar Pradesh' },
  { code: '10', name: 'Bihar' }, { code: '11', name: 'Sikkim' }, { code: '12', name: 'Arunachal Pradesh' },
  { code: '13', name: 'Nagaland' }, { code: '14', name: 'Manipur' }, { code: '15', name: 'Mizoram' },
  { code: '16', name: 'Tripura' }, { code: '17', name: 'Meghalaya' }, { code: '18', name: 'Assam' },
  { code: '19', name: 'West Bengal' }, { code: '20', name: 'Jharkhand' }, { code: '21', name: 'Odisha' },
  { code: '22', name: 'Chhattisgarh' }, { code: '23', name: 'Madhya Pradesh' }, { code: '24', name: 'Gujarat' },
  { code: '26', name: 'Dadra and Nagar Haveli and Daman and Diu' }, { code: '27', name: 'Maharashtra' },
  { code: '29', name: 'Karnataka' }, { code: '30', name: 'Goa' }, { code: '31', name: 'Lakshadweep' },
  { code: '32', name: 'Kerala' }, { code: '33', name: 'Tamil Nadu' }, { code: '34', name: 'Puducherry' },
  { code: '35', name: 'Andaman and Nicobar Islands' }, { code: '36', name: 'Telangana' }, { code: '37', name: 'Andhra Pradesh' },
  { code: '38', name: 'Ladakh' },
];

@Injectable({ providedIn: 'root' })
export class SettingsApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/settings`;

  get(): Observable<Settings> {
    return this.http.get<Settings>(this.base);
  }
  updateCompany(body: CompanySettings): Observable<CompanySettings> {
    return this.http.put<CompanySettings>(`${this.base}/company`, body);
  }
  updateBranding(primaryColor: string, accentColor: string): Observable<BrandingSettings> {
    return this.http.put<BrandingSettings>(`${this.base}/branding`, { primaryColor, accentColor });
  }
  uploadImage(image: 'logo' | 'watermark', file: File): Observable<BrandingSettings> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<BrandingSettings>(`${this.base}/branding/${image}`, form);
  }
  removeImage(image: 'logo' | 'watermark'): Observable<BrandingSettings> {
    return this.http.delete<BrandingSettings>(`${this.base}/branding/${image}`);
  }
  updateCommunication(body: UpdateCommunication): Observable<CommunicationSettings> {
    return this.http.put<CommunicationSettings>(`${this.base}/communication`, body);
  }
  testEmail(): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.base}/communication/test-email`, {});
  }
  updateGst(body: GstSettings): Observable<GstSettings> {
    return this.http.put<GstSettings>(`${this.base}/gst`, body);
  }
  updateNotifications(preferences: { event: string; email: boolean; sms: boolean; inApp: boolean }[]): Observable<NotificationSetting[]> {
    return this.http.put<NotificationSetting[]>(`${this.base}/notifications`, { preferences });
  }
}
