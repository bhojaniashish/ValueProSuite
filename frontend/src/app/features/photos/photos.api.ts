import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import type { AssignmentStatus } from '../assignments/assignments.api';
import { Paged } from '../users/users.api';

export type PhotoTag =
  | 'Front' | 'Rear' | 'Side' | 'Interior' | 'Kitchen' | 'Bathroom' | 'Road' | 'Neighbourhood'
  | 'NameBoard' | 'Selfie' | 'Deviation' | 'Document' | 'Other';
export type PhotoSource = 'Site' | 'Upload';

export const PHOTO_TAGS: { value: PhotoTag; label: string; icon: string }[] = [
  { value: 'Front', label: 'Front elevation', icon: 'fa-building' },
  { value: 'Road', label: 'Approach road', icon: 'fa-road' },
  { value: 'Rear', label: 'Rear elevation', icon: 'fa-building-circle-arrow-right' },
  { value: 'Side', label: 'Side elevation', icon: 'fa-building-flag' },
  { value: 'Interior', label: 'Interior', icon: 'fa-couch' },
  { value: 'Kitchen', label: 'Kitchen', icon: 'fa-kitchen-set' },
  { value: 'Bathroom', label: 'Bathroom', icon: 'fa-toilet' },
  { value: 'Neighbourhood', label: 'Neighbourhood', icon: 'fa-tree-city' },
  { value: 'NameBoard', label: 'Name board / meter', icon: 'fa-signs-post' },
  { value: 'Selfie', label: 'Valuer at site', icon: 'fa-user-check' },
  { value: 'Deviation', label: 'Deviation', icon: 'fa-triangle-exclamation' },
  { value: 'Document', label: 'Document', icon: 'fa-file-lines' },
  { value: 'Other', label: 'Other', icon: 'fa-image' },
];
/** Photos every inspection needs (mirrors InspectionRules.RequiredPhotos). */
export const REQUIRED_PHOTOS: PhotoTag[] = ['Front', 'Road'];
export const tagMeta = (t: PhotoTag) => PHOTO_TAGS.find((x) => x.value === t) ?? PHOTO_TAGS[PHOTO_TAGS.length - 1];

/** Further than this from the property's saved location is flagged (mirrors InspectionRules.FarFromPropertyMetres). */
export const FAR_METRES = 300;

export interface PhotoItem {
  id: string; assignmentId: string; clientKey: string; tag: PhotoTag; caption: string | null; width: number | null; height: number | null;
  sizeBytes: number; latitude: number | null; longitude: number | null; distanceFromPropertyMetres: number | null; takenAt: string;
  takenByName: string; source: PhotoSource; stamped: boolean; includeInReport: boolean; sortOrder: number;
}

export interface PhotoMeta {
  clientKey: string; tag: PhotoTag; caption: string | null; latitude: number | null; longitude: number | null;
  takenAt: string; width: number; height: number; source: PhotoSource; stamped: boolean;
}

export interface GalleryItem { photo: PhotoItem; number: string; bankShortCode: string; bankColor: string; applicantName: string }
export interface GalleryPage { page: Paged<GalleryItem>; summary: { total: number; attention: number; byTag: Partial<Record<PhotoTag, number>> } }
export interface GalleryQuery { search?: string; tag?: PhotoTag | ''; mine?: boolean; attention?: boolean; from?: string; to?: string; page?: number; pageSize?: number }

export interface CasePhotos {
  assignmentId: string; number: string; status: AssignmentStatus; bankShortCode: string; bankColor: string; applicantName: string;
  propertySummary: string; propertyLatitude: number | null; propertyLongitude: number | null; inspectionSubmitted: boolean;
  photos: PhotoItem[]; canEdit: boolean; canDelete: boolean; readOnlyReason: string | null;
}

/** A site photo with no location, or one taken far from the property. */
export function needsAttention(p: Pick<PhotoItem, 'source' | 'latitude' | 'distanceFromPropertyMetres'>): string | null {
  if (p.distanceFromPropertyMetres != null && p.distanceFromPropertyMetres > FAR_METRES) return `${metres(p.distanceFromPropertyMetres)} from the property`;
  if (p.source === 'Site' && p.latitude == null) return 'No location';
  return null;
}

export const metres = (m: number) => (m >= 1000 ? `${(m / 1000).toFixed(1)} km` : `${m} m`);

@Injectable({ providedIn: 'root' })
export class PhotosApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/photos`;

  gallery(q: GalleryQuery): Observable<GalleryPage> {
    let params = new HttpParams();
    for (const [k, v] of Object.entries(q)) if (v !== undefined && v !== null && v !== '' && v !== false) params = params.set(k, String(v));
    return this.http.get<GalleryPage>(this.base, { params });
  }
  case(assignmentId: string): Observable<CasePhotos> {
    return this.http.get<CasePhotos>(`${this.base}/cases/${assignmentId}`);
  }
  upload(assignmentId: string, meta: PhotoMeta, image: Blob, thumb: Blob): Observable<PhotoItem> {
    const f = new FormData();
    f.append('file', image, `${meta.clientKey}.jpg`);
    f.append('thumb', thumb, `${meta.clientKey}-thumb.jpg`);
    for (const [k, v] of Object.entries(meta)) if (v != null) f.append(k, String(v));
    return this.http.post<PhotoItem>(`${this.base}/cases/${assignmentId}`, f);
  }
  reorder(assignmentId: string, photoIds: string[]): Observable<PhotoItem[]> {
    return this.http.put<PhotoItem[]>(`${this.base}/cases/${assignmentId}/order`, { photoIds });
  }
  update(id: string, tag: PhotoTag, caption: string | null, includeInReport: boolean): Observable<PhotoItem> {
    return this.http.put<PhotoItem>(`${this.base}/${id}`, { tag, caption, includeInReport });
  }
  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
  blob(id: string, thumb: boolean): Observable<Blob> {
    return this.http.get(`${this.base}/${id}/file`, { params: { thumb }, responseType: 'blob' });
  }
  zip(assignmentId: string, reportOnly: boolean): Observable<Blob> {
    return this.http.get(`${this.base}/cases/${assignmentId}/zip`, { params: { report: reportOnly }, responseType: 'blob' });
  }
}

/** Saves a downloaded blob with a file name. */
export function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  Object.assign(document.createElement('a'), { href: url, download: fileName }).click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
