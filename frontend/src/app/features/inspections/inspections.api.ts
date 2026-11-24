import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import type { AssignmentStatus } from '../assignments/assignments.api';
import type { AnswerType } from '../banks/banks.api';
import type { PropertyType } from '../properties/properties.api';

export type InspectionStatus = 'Draft' | 'Submitted';
export type PhotoTag =
  | 'Front' | 'Rear' | 'Side' | 'Interior' | 'Kitchen' | 'Bathroom' | 'Road' | 'Neighbourhood'
  | 'NameBoard' | 'Selfie' | 'Deviation' | 'Document' | 'Other';

export type SectionKey = 'visit' | 'location' | 'boundaries' | 'occupancy' | 'building' | 'measurements' | 'approvals' | 'bank' | 'photos' | 'remarks';

export interface Opt { value: string; label: string }
const o = (...labels: string[]): Opt[] => labels.map((l) => ({ value: l, label: l }));

// ---------------- choices (what valuers usually pick; most also allow a note) ----------------

export const RELATIONS = o('Owner', 'Applicant', 'Tenant', 'Family member', 'Builder / Developer', 'Caretaker', 'Neighbour', 'Other');
export const IDENTIFIED_BY = o('Owner / applicant showed it', 'Name board / door number', 'Boundaries match documents', 'Neighbours confirmed', 'Society office / security', 'Local enquiry');
export const ACCESS: Opt[] = [
  { value: 'Full', label: 'Full inspection' },
  { value: 'OutsideOnly', label: 'Outside only' },
  { value: 'NoEntry', label: 'No entry' },
];

export const LOCALITY_TYPES = o('Urban', 'Semi-urban', 'Rural');
export const LOCALITY_CLASSES = o('High income', 'Middle income', 'Low income', 'Commercial', 'Industrial');
export const DEVELOPMENT = o('Fully developed', 'Developing', 'Undeveloped');
export const ROAD_TYPES = o('RCC / Concrete', 'Asphalt', 'WBM', 'Kutcha');
export const FACING = o('North', 'North-East', 'East', 'South-East', 'South', 'South-West', 'West', 'North-West');
export const CIVIC = o('Municipal water', 'Borewell', 'Underground drainage', 'Septic tank', 'Street lights', 'Public transport', 'School nearby', 'Hospital nearby', 'Market nearby');
export const NEGATIVES = o('High-tension line', 'Nala / open drain', 'Cemetery / crematorium', 'Railway track adjacent', 'Flood-prone / low-lying', 'Slum nearby', 'Industrial pollution', 'No proper road access', 'Road widening / acquisition');

export const OCCUPANCY: Opt[] = [
  { value: 'Owner', label: 'Owner' },
  { value: 'Tenant', label: 'Tenant' },
  { value: 'PartlyRented', label: 'Partly rented' },
  { value: 'Vacant', label: 'Vacant' },
  { value: 'UnderConstruction', label: 'Under construction' },
];

export const QUALITY = o('Good', 'Average', 'Poor');
export const CONDITION = o('Excellent', 'Good', 'Average', 'Poor', 'Dilapidated');
export const PARKING = o('Covered', 'Open', 'Stilt', 'None');
export const BUILDING_AMENITIES = o('Lift', 'Power backup', 'Security', 'CCTV', 'Fire fighting', 'Intercom', 'Club house', 'Garden', 'Swimming pool', 'Gym', 'Rainwater harvesting', 'Solar');
export const DEFECTS = o('Cracks in walls', 'Seepage / dampness', 'Exposed reinforcement', 'Settlement', 'Roof leakage', 'Poor maintenance');

/** Typical specifications, offered as suggestions (the valuer can type anything). */
export const SPEC_SUGGESTIONS: Record<string, string[]> = {
  foundation: ['RCC footing', 'Pile foundation', 'Stone masonry', 'Raft'],
  walls: ['Brick masonry', 'AAC block', 'Concrete block', 'Stone masonry'],
  roof: ['RCC slab', 'GI / metal sheet', 'Mangalore tiles', 'AC sheet'],
  flooring: ['Vitrified tiles', 'Marble', 'Granite', 'Ceramic tiles', 'Kota stone', 'IPS / cement'],
  doorsWindows: ['Wooden doors, aluminium windows', 'Wooden doors, UPVC windows', 'Flush doors, MS windows'],
  kitchen: ['Granite platform with sink', 'Modular kitchen', 'Basic platform'],
  toilets: ['Branded CP & sanitary fittings', 'Standard fittings', 'Basic fittings'],
  electrical: ['Concealed copper wiring', 'Open wiring'],
  plumbing: ['Concealed CPVC / GI', 'Open GI pipes'],
};

export const PLAN_SEEN: Opt[] = [
  { value: 'Yes', label: 'Yes, seen' },
  { value: 'No', label: 'Not shown' },
  { value: 'NotAvailable', label: 'Not available' },
];
export const DEVIATION = o('None', 'Minor', 'Major');
export const OC: Opt[] = [
  { value: 'Yes', label: 'Available' },
  { value: 'No', label: 'Not obtained' },
  { value: 'NotSeen', label: 'Not seen' },
  { value: 'NotApplicable', label: 'Not applicable' },
];
export const MARKETABILITY = o('Good', 'Average', 'Poor');
export const YES_NO: Opt[] = [{ value: 'Yes', label: 'Yes' }, { value: 'No', label: 'No' }];

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
export const REQUIRED_PHOTOS: PhotoTag[] = ['Front', 'Road'];
export const tagMeta = (t: PhotoTag) => PHOTO_TAGS.find((x) => x.value === t) ?? PHOTO_TAGS[PHOTO_TAGS.length - 1];

// ---------------- data ----------------

export interface Boundary { asPerDocuments: string | null; atSite: string | null; matches: boolean | null }
export interface FloorArea { name: string | null; asPerDocuments: number | null; measured: number | null }

export interface InspectionData {
  visit: {
    personMet: string | null; personRelation: string | null; personPhone: string | null; identifiedBy: string[];
    addressMatches: boolean | null; addressAtSite: string | null; access: string | null; accessNote: string | null;
  };
  location: {
    localityType: string | null; localityClass: string | null; development: string | null; roadWidthFt: number | null; roadType: string | null;
    facing: string | null; cornerPlot: boolean | null; landmark: string | null; stationKm: number | null; busStopKm: number | null;
    cityCentreKm: number | null; amenities: string[]; negatives: string[]; note: string | null;
  };
  boundaries: { north: Boundary; south: Boundary; east: Boundary; west: Boundary; encroachment: boolean | null; note: string | null };
  occupancy: { occupiedBy: string | null; tenantName: string | null; monthlyRent: number | null; occupiedSinceYears: number | null; note: string | null };
  building: {
    structureType: string | null; ageYears: number | null; totalFloors: number | null; unitFloor: string | null; quality: string | null;
    condition: string | null; completionPercent: number | null; foundation: string | null; walls: string | null; roof: string | null;
    flooring: string | null; doorsWindows: string | null; kitchen: string | null; toilets: string | null; electrical: string | null;
    plumbing: string | null; lift: boolean | null; parking: string | null; parkingSlots: number | null; amenities: string[];
    defects: string[]; note: string | null;
  };
  measurements: {
    unit: 'SqFt' | 'SqM'; plotLengthFt: number | null; plotBreadthFt: number | null; landAsPerDocuments: number | null;
    landMeasured: number | null; carpetAsPerDocuments: number | null; carpetMeasured: number | null; builtUpAsPerDocuments: number | null;
    builtUpMeasured: number | null; superBuiltUpAsPerDocuments: number | null; floors: FloorArea[]; note: string | null;
  };
  approvals: {
    planSeen: string | null; planAuthority: string | null; planNumber: string | null; deviation: string | null;
    deviationNote: string | null; occupancyCertificate: string | null; note: string | null;
  };
  bankAnswers: { question: string; answer: string | null }[];
  remarks: { marketability: string | null; text: string | null };
}

const b = (): Boundary => ({ asPerDocuments: null, atSite: null, matches: null });

/** The server leaves out empty fields; this fills every field so the form can bind to it. */
export function normalize(d: Partial<InspectionData> | null | undefined): InspectionData {
  const x = (d ?? {}) as Partial<InspectionData>;
  const base: InspectionData = {
    visit: { personMet: null, personRelation: null, personPhone: null, identifiedBy: [], addressMatches: null, addressAtSite: null, access: null, accessNote: null },
    location: {
      localityType: null, localityClass: null, development: null, roadWidthFt: null, roadType: null, facing: null, cornerPlot: null,
      landmark: null, stationKm: null, busStopKm: null, cityCentreKm: null, amenities: [], negatives: [], note: null,
    },
    boundaries: { north: b(), south: b(), east: b(), west: b(), encroachment: null, note: null },
    occupancy: { occupiedBy: null, tenantName: null, monthlyRent: null, occupiedSinceYears: null, note: null },
    building: {
      structureType: null, ageYears: null, totalFloors: null, unitFloor: null, quality: null, condition: null, completionPercent: null,
      foundation: null, walls: null, roof: null, flooring: null, doorsWindows: null, kitchen: null, toilets: null, electrical: null,
      plumbing: null, lift: null, parking: null, parkingSlots: null, amenities: [], defects: [], note: null,
    },
    measurements: {
      unit: 'SqFt', plotLengthFt: null, plotBreadthFt: null, landAsPerDocuments: null, landMeasured: null, carpetAsPerDocuments: null,
      carpetMeasured: null, builtUpAsPerDocuments: null, builtUpMeasured: null, superBuiltUpAsPerDocuments: null, floors: [], note: null,
    },
    approvals: { planSeen: null, planAuthority: null, planNumber: null, deviation: null, deviationNote: null, occupancyCertificate: null, note: null },
    bankAnswers: [],
    remarks: { marketability: null, text: null },
  };
  const merge = <T extends object>(a: T, over: Partial<T> | undefined): T => ({ ...a, ...(over ?? {}) });
  return {
    visit: merge(base.visit, x.visit),
    location: merge(base.location, x.location),
    boundaries: {
      ...merge(base.boundaries, x.boundaries),
      north: merge(b(), x.boundaries?.north), south: merge(b(), x.boundaries?.south),
      east: merge(b(), x.boundaries?.east), west: merge(b(), x.boundaries?.west),
    },
    occupancy: merge(base.occupancy, x.occupancy),
    building: merge(base.building, x.building),
    measurements: { ...merge(base.measurements, x.measurements), floors: (x.measurements?.floors ?? []).map((f) => merge<FloorArea>({ name: null, asPerDocuments: null, measured: null }, f)) },
    approvals: merge(base.approvals, x.approvals),
    bankAnswers: (x.bankAnswers ?? []).map((a) => ({ question: a.question, answer: a.answer ?? null })),
    remarks: merge(base.remarks, x.remarks),
  };
}

export interface MissingItem { section: SectionKey; message: string }

export interface InspectionCase {
  assignmentId: string; number: string; status: AssignmentStatus; bankName: string; bankShortCode: string; bankColor: string;
  branchName: string | null; applicantName: string; applicantPhone: string | null; propertySummary: string; propertyId: string | null;
  propertyType: PropertyType | null; propertyAddress: string | null; propertyLatitude: number | null; propertyLongitude: number | null;
  propertyLandArea: number | null; propertyBuiltUpArea: number | null; siteVisitAt: string | null; dueDate: string; valuerName: string;
}

export interface PhotoItem {
  id: string; clientKey: string; tag: PhotoTag; caption: string | null; width: number | null; height: number | null; sizeBytes: number;
  latitude: number | null; longitude: number | null; takenAt: string; takenByName: string;
}

export interface BankQuestion { text: string; answerType: AnswerType; options: string[]; required: boolean }

export interface InspectionDetail {
  case: InspectionCase; started: boolean; status: InspectionStatus; startedAt: string | null; startedByName: string | null;
  latitude: number | null; longitude: number | null; accuracyMetres: number | null; locatedAt: string | null;
  distanceFromPropertyMetres: number | null; farFromProperty: boolean; submittedAt: string | null; submittedByName: string | null;
  version: number; data: InspectionData; photos: PhotoItem[]; bankQuestions: BankQuestion[]; structureTypes: string[];
  landOnly: boolean; missing: MissingItem[]; canEdit: boolean; readOnlyReason: string | null;
}

export interface GpsFix { latitude: number; longitude: number; accuracyMetres: number | null; at: string | null }

export interface SaveResult { version: number; missing: MissingItem[]; distanceFromPropertyMetres: number | null; farFromProperty: boolean; savedAt: string }

export interface InspectionListItem {
  assignmentId: string; number: string; assignmentStatus: AssignmentStatus; bankShortCode: string; bankColor: string; applicantName: string;
  propertySummary: string; siteVisitAt: string | null; dueDate: string; valuerName: string; status: InspectionStatus | null;
  updatedAt: string | null; photoCount: number;
}

export interface PhotoMeta {
  clientKey: string; tag: PhotoTag; caption: string | null; latitude: number | null; longitude: number | null;
  takenAt: string; width: number; height: number;
}

/** Straight-line distance in metres, for instant feedback before the server confirms. */
export function distanceMetres(lat1: number, lng1: number, lat2: number, lng2: number): number {
  const r = (d: number) => (d * Math.PI) / 180;
  const h = Math.sin(r(lat2 - lat1) / 2) ** 2 + Math.cos(r(lat1)) * Math.cos(r(lat2)) * Math.sin(r(lng2 - lng1) / 2) ** 2;
  return Math.round(2 * 6_371_000 * Math.asin(Math.min(1, Math.sqrt(h))));
}

export const metres = (m: number) => (m >= 1000 ? `${(m / 1000).toFixed(1)} km` : `${m} m`);

@Injectable({ providedIn: 'root' })
export class InspectionsApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/inspections`;

  list(done: boolean, mine: boolean, search = ''): Observable<InspectionListItem[]> {
    const params = new HttpParams().set('done', done).set('mine', mine).set('search', search);
    return this.http.get<InspectionListItem[]>(this.base, { params });
  }
  get(assignmentId: string): Observable<InspectionDetail> {
    return this.http.get<InspectionDetail>(`${this.base}/${assignmentId}`);
  }
  save(assignmentId: string, version: number, data: InspectionData, location: GpsFix | null): Observable<SaveResult> {
    return this.http.put<SaveResult>(`${this.base}/${assignmentId}`, { version, data, location });
  }
  submit(assignmentId: string, version: number): Observable<InspectionDetail> {
    return this.http.post<InspectionDetail>(`${this.base}/${assignmentId}/submit`, { version });
  }
  upload(assignmentId: string, meta: PhotoMeta, image: Blob, thumb: Blob): Observable<PhotoItem> {
    const f = new FormData();
    f.append('file', image, `${meta.clientKey}.jpg`);
    f.append('thumb', thumb, `${meta.clientKey}-thumb.jpg`);
    for (const [k, v] of Object.entries(meta)) if (v != null) f.append(k, String(v));
    return this.http.post<PhotoItem>(`${this.base}/${assignmentId}/photos`, f);
  }
  updatePhoto(id: string, tag: PhotoTag, caption: string | null): Observable<PhotoItem> {
    return this.http.put<PhotoItem>(`${this.base}/photos/${id}`, { tag, caption });
  }
  deletePhoto(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/photos/${id}`);
  }
  photoBlob(id: string, thumb: boolean): Observable<Blob> {
    return this.http.get(`${this.base}/photos/${id}/file`, { params: { thumb }, responseType: 'blob' });
  }
}
