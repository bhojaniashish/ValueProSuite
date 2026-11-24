import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import type { AssignmentListItem } from '../assignments/assignments.api';
import { Paged } from '../users/users.api';

export type PropertyType =
  | 'ResidentialFlat' | 'IndependentHouse' | 'CommercialShop' | 'OfficeSpace' | 'IndustrialShed'
  | 'AgriculturalLand' | 'OpenPlot' | 'Bungalow' | 'RowHouse' | 'Warehouse';

export type PropertyUsage = 'SelfOccupied' | 'Rented' | 'Vacant' | 'UnderConstruction';

export const PROPERTY_TYPES: { value: PropertyType; label: string; icon: string; land: boolean; built: boolean }[] = [
  { value: 'ResidentialFlat', label: 'Residential Flat', icon: 'fa-building', land: false, built: true },
  { value: 'IndependentHouse', label: 'Independent House', icon: 'fa-house', land: true, built: true },
  { value: 'Bungalow', label: 'Bungalow', icon: 'fa-house-chimney', land: true, built: true },
  { value: 'RowHouse', label: 'Row House', icon: 'fa-house-flag', land: true, built: true },
  { value: 'CommercialShop', label: 'Commercial Shop', icon: 'fa-shop', land: false, built: true },
  { value: 'OfficeSpace', label: 'Office Space', icon: 'fa-briefcase', land: false, built: true },
  { value: 'IndustrialShed', label: 'Industrial Shed', icon: 'fa-industry', land: true, built: true },
  { value: 'Warehouse', label: 'Warehouse', icon: 'fa-warehouse', land: true, built: true },
  { value: 'OpenPlot', label: 'Open Plot', icon: 'fa-vector-square', land: true, built: false },
  { value: 'AgriculturalLand', label: 'Agricultural Land', icon: 'fa-seedling', land: true, built: false },
];

export const PROPERTY_USAGES: { value: PropertyUsage; label: string }[] = [
  { value: 'SelfOccupied', label: 'Self-occupied' },
  { value: 'Rented', label: 'Rented' },
  { value: 'Vacant', label: 'Vacant' },
  { value: 'UnderConstruction', label: 'Under construction' },
];

export const propertyType = (t: PropertyType) => PROPERTY_TYPES.find((x) => x.value === t) ?? PROPERTY_TYPES[0];
export const usageLabel = (u: PropertyUsage | null) => (u ? (PROPERTY_USAGES.find((x) => x.value === u)?.label ?? u) : null);

export interface Property {
  id: string;
  code: string;
  type: PropertyType;
  ownerName: string;
  ownerClientId: string | null;
  address: string;
  city: string | null;
  state: string | null;
  pincode: string | null;
  surveyNo: string | null;
  ctsNo: string | null;
  plotNo: string | null;
  landArea: number | null;
  builtUpArea: number | null;
  zone: string | null;
  usage: PropertyUsage | null;
  latitude: number | null;
  longitude: number | null;
  remarks: string | null;
  createdAt: string;
}

export type SaveProperty = Omit<Property, 'id' | 'code' | 'createdAt'>;

export interface PropertyDetail {
  property: Property;
  assignments: AssignmentListItem[];
}

export interface PropertyQuery {
  search?: string;
  type?: PropertyType | '';
  city?: string;
  zone?: string;
  sort?: string;
  desc?: boolean;
  page: number;
  pageSize: number;
}

export interface PropertyFilters {
  cities: string[];
  zones: string[];
  total: number;
}

/** "1,200 sq.ft" with Indian digit grouping. */
export function area(v: number | null): string {
  return v == null ? '—' : `${v.toLocaleString('en-IN', { maximumFractionDigits: 2 })} sq.ft`;
}

export function mapsUrl(p: Pick<Property, 'latitude' | 'longitude' | 'address' | 'city'>): string {
  const q = p.latitude != null && p.longitude != null ? `${p.latitude},${p.longitude}` : [p.address, p.city].filter(Boolean).join(', ');
  return `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(q)}`;
}

@Injectable({ providedIn: 'root' })
export class PropertiesApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/properties`;

  list(q: PropertyQuery): Observable<Paged<Property>> {
    let params = new HttpParams().set('page', q.page).set('pageSize', q.pageSize);
    if (q.search) params = params.set('search', q.search);
    if (q.type) params = params.set('type', q.type);
    if (q.city) params = params.set('city', q.city);
    if (q.zone) params = params.set('zone', q.zone);
    if (q.sort) params = params.set('sort', q.sort).set('desc', !!q.desc);
    return this.http.get<Paged<Property>>(this.base, { params });
  }
  filters(): Observable<PropertyFilters> {
    return this.http.get<PropertyFilters>(`${this.base}/filters`);
  }
  get(id: string): Observable<PropertyDetail> {
    return this.http.get<PropertyDetail>(`${this.base}/${id}`);
  }
  create(body: SaveProperty): Observable<Property> {
    return this.http.post<Property>(this.base, body);
  }
  update(id: string, body: SaveProperty): Observable<Property> {
    return this.http.put<Property>(`${this.base}/${id}`, body);
  }
  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
}
