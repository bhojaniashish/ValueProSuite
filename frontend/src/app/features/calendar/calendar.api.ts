import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import type { AssignmentStatus } from '../assignments/assignments.api';

export type ItemKind = 'visit' | 'due' | 'invoice' | 'entry';
export type EntryKind = 'FollowUp' | 'Reminder' | 'Meeting' | 'Other';

export interface CalendarItem {
  key: string; kind: ItemKind; date: string; time: string | null; title: string; subtitle: string | null;
  assignmentId: string | null; assignmentNumber: string | null; status: AssignmentStatus | null;
  bankShortCode: string | null; bankColor: string | null; personName: string | null;
  done: boolean; overdue: boolean; canMove: boolean;
  entryId: string | null; entryKind: EntryKind | null; notes: string | null; assigneeId: string | null; invoiceId: string | null;
}

export interface CaseToSchedule {
  id: string; number: string; bankShortCode: string; bankColor: string; applicantName: string; propertySummary: string;
  valuerName: string; dueDate: string; canSchedule: boolean;
}

export interface CalendarFeed {
  from: string; to: string; today: string; mine: boolean; items: CalendarItem[]; toSchedule: CaseToSchedule[];
  people: { id: string; name: string }[]; showsInvoices: boolean;
}

export interface SaveEntry {
  title: string; kind: EntryKind; date: string; time: string | null; assignmentId: string | null; assigneeId: string | null;
  notes: string | null; done: boolean;
}

export const ENTRY_KINDS: { value: EntryKind; label: string; badge: string; icon: string }[] = [
  { value: 'FollowUp', label: 'Follow-up', badge: 'b-warn', icon: 'fa-phone' },
  { value: 'Reminder', label: 'Reminder', badge: 'b-teal', icon: 'fa-bell' },
  { value: 'Meeting', label: 'Meeting', badge: 'b-purple', icon: 'fa-people-group' },
  { value: 'Other', label: 'Other', badge: 'b-gray', icon: 'fa-thumbtack' },
];

/** The four layers on the calendar, each can be shown or hidden. */
export const LAYERS: { kind: ItemKind; label: string; badge: string; icon: string }[] = [
  { kind: 'visit', label: 'Site visits', badge: 'b-info', icon: 'fa-location-dot' },
  { kind: 'due', label: 'Report due', badge: 'b-danger', icon: 'fa-flag' },
  { kind: 'entry', label: 'Follow-ups & reminders', badge: 'b-warn', icon: 'fa-thumbtack' },
  { kind: 'invoice', label: 'Payments due', badge: 'b-gold', icon: 'fa-indian-rupee-sign' },
];

export function itemBadge(i: CalendarItem): string {
  if (i.kind === 'entry') return ENTRY_KINDS.find((k) => k.value === i.entryKind)?.badge ?? 'b-gray';
  return LAYERS.find((l) => l.kind === i.kind)?.badge ?? 'b-gray';
}

export function itemIcon(i: CalendarItem): string {
  if (i.kind === 'entry') return ENTRY_KINDS.find((k) => k.value === i.entryKind)?.icon ?? 'fa-thumbtack';
  return LAYERS.find((l) => l.kind === i.kind)?.icon ?? 'fa-circle';
}

// ---- plain yyyy-mm-dd arithmetic (no time zones involved) ----

function toUtc(iso: string): Date {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d));
}
function fromUtc(d: Date): string {
  return d.toISOString().slice(0, 10);
}
export function addDays(iso: string, n: number): string {
  const d = toUtc(iso);
  d.setUTCDate(d.getUTCDate() + n);
  return fromUtc(d);
}
export function addMonths(iso: string, n: number): string {
  const d = toUtc(iso);
  return fromUtc(new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth() + n, 1)));
}
export function monthStart(iso: string): string {
  return iso.slice(0, 8) + '01';
}
/** Monday = 0 … Sunday = 6. */
export function weekday(iso: string): number {
  return (toUtc(iso).getUTCDay() + 6) % 7;
}
export function weekStart(iso: string): string {
  return addDays(iso, -weekday(iso));
}
export function dayNumber(iso: string): number {
  return toUtc(iso).getUTCDate();
}
export function label(iso: string, opts: Intl.DateTimeFormatOptions): string {
  return toUtc(iso).toLocaleDateString('en-IN', { ...opts, timeZone: 'UTC' });
}
/** "10:30" → "10:30 am". */
export function clock(t: string | null): string {
  if (!t) return '';
  const [h, m] = t.split(':').map(Number);
  return `${((h + 11) % 12) + 1}:${String(m).padStart(2, '0')} ${h < 12 ? 'am' : 'pm'}`;
}

@Injectable({ providedIn: 'root' })
export class CalendarApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/calendar`;

  feed(from: string, to: string, mine: boolean): Observable<CalendarFeed> {
    return this.http.get<CalendarFeed>(this.base, { params: new HttpParams().set('from', from).set('to', to).set('mine', mine) });
  }
  scheduleVisit(assignmentId: string, date: string, time: string): Observable<CalendarItem> {
    return this.http.put<CalendarItem>(`${this.base}/visits/${assignmentId}`, { date, time });
  }
  createEntry(e: SaveEntry): Observable<CalendarItem> {
    return this.http.post<CalendarItem>(`${this.base}/entries`, e);
  }
  updateEntry(id: string, e: SaveEntry): Observable<CalendarItem> {
    return this.http.put<CalendarItem>(`${this.base}/entries/${id}`, e);
  }
  deleteEntry(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/entries/${id}`);
  }
}

/** Saves an entry with a new day (and optionally time), keeping everything else. */
export function entryAt(i: CalendarItem, date: string, time = i.time): SaveEntry {
  return {
    title: i.title, kind: i.entryKind ?? 'Other', date, time, assignmentId: i.assignmentId, assigneeId: i.assigneeId, notes: i.notes, done: i.done,
  };
}
