import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, input, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Dialog } from '../../core/ui/dialog';
import { ToastService } from '../../core/ui/toast';
import { AmountInput } from '../../shared/amount-input';
import { Choice, Multi, YesNo } from '../../shared/chips';
import { shortDate } from '../assignments/assignments.api';
import { mapsUrl, propertyType } from '../properties/properties.api';
import { BrandingService } from '../../core/branding/branding.service';
import { AuthImage } from '../photos/auth-image';
import { newKey, preparePhoto } from '../photos/photo-tools';
import { PhotoTagDialog } from '../photos/photo-tag-dialog';
import { PhotoChange, PhotoViewer, ViewerPhoto } from '../photos/photo-viewer';
import { PHOTO_TAGS, PhotoItem, PhotosApi, PhotoTag, REQUIRED_PHOTOS, tagMeta } from '../photos/photos.api';
import { Drafts, Gps } from './field-tools';
import {
  ACCESS, BUILDING_AMENITIES, CIVIC, CONDITION, DEFECTS, DEVELOPMENT, DEVIATION, distanceMetres, FACING, GpsFix, IDENTIFIED_BY,
  InspectionData, InspectionDetail, InspectionsApi, LOCALITY_CLASSES, LOCALITY_TYPES, MARKETABILITY, metres, MissingItem, NEGATIVES,
  normalize, OC, OCCUPANCY, PARKING, PLAN_SEEN, QUALITY, RELATIONS, ROAD_TYPES, SectionKey, SPEC_SUGGESTIONS, YES_NO,
} from './inspections.api';
import { PhotoQueue, QueuedPhoto } from '../photos/photo-queue';

type Step = SectionKey | 'review';
type SaveState = 'idle' | 'pending' | 'saving' | 'saved' | 'offline' | 'conflict' | 'error';

interface SectionDef { key: Step; label: string; icon: string }

const SECTIONS: SectionDef[] = [
  { key: 'visit', label: 'Visit', icon: 'fa-person-walking' },
  { key: 'location', label: 'Locality', icon: 'fa-map-location-dot' },
  { key: 'boundaries', label: 'Boundaries', icon: 'fa-draw-polygon' },
  { key: 'occupancy', label: 'Occupancy', icon: 'fa-house-user' },
  { key: 'building', label: 'Building', icon: 'fa-building' },
  { key: 'measurements', label: 'Measurements', icon: 'fa-ruler-combined' },
  { key: 'approvals', label: 'Approvals', icon: 'fa-stamp' },
  { key: 'bank', label: 'Bank questions', icon: 'fa-building-columns' },
  { key: 'photos', label: 'Photos', icon: 'fa-camera' },
  { key: 'remarks', label: 'Remarks', icon: 'fa-comment-dots' },
  { key: 'review', label: 'Review & submit', icon: 'fa-check-double' },
];

/** The photo tag a quick photo gets on each step (the valuer can change it before saving). */
const DEFAULT_TAG: Partial<Record<Step, PhotoTag>> = {
  visit: 'Front', location: 'Road', boundaries: 'Side', occupancy: 'NameBoard', building: 'Interior',
  measurements: 'Interior', approvals: 'Deviation', bank: 'Other', photos: 'Other', remarks: 'Other',
};

/** Shown in the photo grid / viewer: either on the server or still waiting on the phone. */
export interface ShownPhoto {
  key: string;
  tag: PhotoTag;
  caption: string | null;
  takenAt: string;
  latitude: number | null;
  longitude: number | null;
  server?: PhotoItem;
  queued?: QueuedPhoto;
  localUrl?: string;
}

@Component({
  selector: 'vp-inspection-page',
  imports: [FormsModule, RouterLink, Dialog, AmountInput, Choice, Multi, YesNo, AuthImage, PhotoViewer, PhotoTagDialog],
  templateUrl: './inspection-page.html',
  styleUrl: './inspection-page.scss',
})
export class InspectionPage implements OnInit {
  private readonly api = inject(InspectionsApi);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  protected readonly queue = inject(PhotoQueue);
  private readonly photosApi = inject(PhotosApi);
  private readonly branding = inject(BrandingService);
  protected readonly gps = inject(Gps);

  readonly id = input.required<string>();
  readonly s = input<string>();

  // option lists for the template
  protected readonly o = {
    RELATIONS, IDENTIFIED_BY, ACCESS, LOCALITY_TYPES, LOCALITY_CLASSES, DEVELOPMENT, ROAD_TYPES, FACING, CIVIC, NEGATIVES, OCCUPANCY,
    QUALITY, CONDITION, PARKING, BUILDING_AMENITIES, DEFECTS, PLAN_SEEN, DEVIATION, OC, MARKETABILITY, YES_NO, SPEC_SUGGESTIONS,
  };
  protected readonly photoTags = PHOTO_TAGS;
  protected readonly required = REQUIRED_PHOTOS;
  protected readonly tagMeta = tagMeta;
  protected readonly metres = metres;
  protected readonly shortDate = shortDate;
  protected readonly propertyType = propertyType;

  protected readonly detail = signal<InspectionDetail | null>(null);
  protected readonly loadError = signal<string | null>(null);
  /** The form model: plain object bound with ngModel; every change autosaves. */
  protected d: InspectionData = normalize(null);
  private version = 0;

  protected readonly step = signal<Step>('visit');
  protected readonly missing = signal<MissingItem[]>([]);
  protected readonly location = signal<GpsFix | null>(null);
  protected readonly distance = signal<number | null>(null);
  protected readonly locating = signal(false);
  protected readonly locationError = signal<string | null>(null);

  protected readonly saveState = signal<SaveState>('idle');
  protected readonly savedAt = signal<string | null>(null);
  protected readonly saveMessage = signal<string | null>(null);
  /** A copy on this phone that could not be saved because the server copy changed meanwhile. */
  protected readonly strayCopy = signal<{ savedAt: string } | null>(null);
  protected readonly offlineCopy = signal(false);

  protected readonly photos = signal<PhotoItem[]>([]);
  protected readonly viewing = signal<{ list: ViewerPhoto[]; start: number } | null>(null);
  protected readonly tagging = signal<{ files: File[]; tag: PhotoTag; fromCamera: boolean } | null>(null);
  protected readonly preparing = signal(false);
  protected readonly confirming = signal(false);
  protected readonly submitting = signal(false);
  protected readonly submitted = signal(false);

  private saveTimer?: ReturnType<typeof setTimeout>;
  private retryTimer?: ReturnType<typeof setTimeout>;
  private saving: Promise<void> | null = null;
  private again = false;
  private localUrls = new Map<string, string>();
  private pendingCameraTag: PhotoTag | null = null;

  protected readonly c = computed(() => this.detail()?.case ?? null);
  protected readonly canEdit = computed(() => this.detail()?.canEdit ?? false);

  protected readonly sections = computed(() => {
    const d = this.detail();
    return SECTIONS.filter((s) => !(s.key === 'building' && d?.landOnly) && !(s.key === 'bank' && !d?.bankQuestions.length));
  });
  protected readonly index = computed(() => this.sections().findIndex((s) => s.key === this.step()));
  protected readonly current = computed(() => this.sections()[this.index()] ?? this.sections()[0]);
  protected readonly missingBySection = computed(() => {
    const m = new Map<string, MissingItem[]>();
    for (const x of this.missing()) m.set(x.section, [...(m.get(x.section) ?? []), x]);
    return m;
  });
  /** Share of sections with nothing missing (review excluded). */
  protected readonly progress = computed(() => {
    const keys = this.sections().filter((s) => s.key !== 'review');
    const done = keys.filter((s) => !this.missingBySection().has(s.key)).length;
    return Math.round((done / Math.max(1, keys.length)) * 100);
  });

  protected readonly queued = computed(() => this.queue.items().filter((q) => q.assignmentId === this.id()));
  protected readonly shown = computed<ShownPhoto[]>(() => [
    ...this.photos().map((p) => ({ key: p.clientKey, tag: p.tag, caption: p.caption, takenAt: p.takenAt, latitude: p.latitude, longitude: p.longitude, server: p })),
    ...this.queued()
      .filter((q) => !this.photos().some((p) => p.clientKey === q.clientKey))
      .map((q) => ({ key: q.clientKey, tag: q.tag, caption: q.caption, takenAt: q.takenAt, latitude: q.latitude, longitude: q.longitude, queued: q, localUrl: this.localUrl(q) })),
  ]);
  protected readonly byTag = computed(() => {
    const m = new Map<PhotoTag, ShownPhoto[]>();
    for (const p of this.shown()) m.set(p.tag, [...(m.get(p.tag) ?? []), p]);
    return m;
  });

  constructor() {
    const sub = this.queue.uploaded.subscribe(({ assignmentId, photo }) => {
      if (assignmentId !== this.id()) return;
      this.photos.update((ps) => (ps.some((p) => p.id === photo.id) ? ps : [...ps, photo]));
      this.schedule(300); // refresh what is missing
    });
    const online = () => {
      if (this.saveState() === 'offline' || this.saveState() === 'pending') void this.save();
    };
    window.addEventListener('online', online);
    inject(DestroyRef).onDestroy(() => {
      sub.unsubscribe();
      window.removeEventListener('online', online);
      clearTimeout(this.saveTimer);
      clearTimeout(this.retryTimer);
      for (const u of this.localUrls.values()) URL.revokeObjectURL(u);
      if (this.saveState() === 'pending') void this.save();
    });
  }

  async ngOnInit(): Promise<void> {
    const id = this.id();
    const local = Drafts.get(id);
    try {
      const detail = await firstValueFrom(this.api.get(id));
      this.apply(detail);
      if (local?.dirty && detail.canEdit) {
        if (local.version === detail.version) {
          this.d = normalize(local.data);
          if (local.location) this.setLocation(local.location);
          void this.save();
        } else {
          this.strayCopy.set({ savedAt: local.savedAt });
        }
      }
    } catch (e) {
      if (e instanceof HttpErrorResponse && e.status === 0 && local?.detail) {
        // No signal: carry on from the copy on this phone.
        this.apply(local.detail);
        this.d = normalize(local.data);
        this.version = local.version;
        if (local.location) this.setLocation(local.location);
        this.offlineCopy.set(true);
        this.saveState.set(local.dirty ? 'offline' : 'idle');
      } else {
        this.loadError.set(toProblem(e).message);
        return;
      }
    }
    const s = this.s();
    if (s && this.sections().some((x) => x.key === s)) this.step.set(s as Step);
    else if (this.detail()?.status === 'Submitted') this.step.set('review');
  }

  private apply(detail: InspectionDetail): void {
    this.detail.set(detail);
    this.d = normalize(detail.data);
    for (const q of detail.bankQuestions)
      if (!this.d.bankAnswers.some((a) => a.question.toLowerCase() === q.text.toLowerCase())) this.d.bankAnswers.push({ question: q.text, answer: null });
    this.version = detail.version;
    this.missing.set(detail.missing);
    this.photos.set(detail.photos);
    this.distance.set(detail.distanceFromPropertyMetres);
    this.location.set(detail.latitude != null && detail.longitude != null
      ? { latitude: detail.latitude, longitude: detail.longitude, accuracyMetres: detail.accuracyMetres, at: detail.locatedAt }
      : null);
    this.submitted.set(detail.status === 'Submitted');
  }

  // ---------------- navigation ----------------

  protected go(step: Step): void {
    this.step.set(step);
    void this.router.navigate([], { queryParams: { s: step === 'visit' ? null : step }, replaceUrl: true });
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  protected move(delta: number): void {
    const next = this.sections()[this.index() + delta];
    if (next) this.go(next.key);
  }

  protected sectionMissing(key: Step): number {
    return this.missingBySection().get(key)?.length ?? 0;
  }

  // ---------------- saving ----------------

  /** Called on every change in the form (inputs bubble input/change events up to the form). */
  protected changed(): void {
    if (!this.canEdit()) return;
    this.keepLocal(true);
    this.saveState.set('pending');
    this.schedule(1200);
  }

  private schedule(ms: number): void {
    clearTimeout(this.saveTimer);
    this.saveTimer = setTimeout(() => void this.save(), ms);
  }

  private keepLocal(dirty: boolean): void {
    const detail = this.detail();
    Drafts.put(this.id(), {
      version: this.version, data: this.d, location: this.location(), dirty, savedAt: new Date().toISOString(),
      detail: detail ? { ...detail, data: this.d, version: this.version, photos: this.photos(), missing: this.missing() } : undefined,
    });
  }

  protected async save(): Promise<void> {
    if (!this.canEdit() || this.strayCopy()) return;
    if (this.saving) {
      this.again = true;
      return this.saving;
    }
    clearTimeout(this.saveTimer);
    clearTimeout(this.retryTimer);
    this.saving = (async () => {
      this.saveState.set('saving');
      try {
        const r = await firstValueFrom(this.api.save(this.id(), this.version, this.d, this.location()));
        this.version = r.version;
        this.missing.set(r.missing);
        this.distance.set(r.distanceFromPropertyMetres);
        this.savedAt.set(r.savedAt);
        this.saveMessage.set(null);
        this.offlineCopy.set(false);
        this.detail.update((x) => (x ? { ...x, started: true, version: r.version, farFromProperty: r.farFromProperty } : x));
        this.saveState.set('saved');
        this.keepLocal(false);
      } catch (e) {
        const err = e instanceof HttpErrorResponse ? e : null;
        if (!err || err.status === 0 || err.status >= 500) {
          this.saveState.set('offline');
          this.retryTimer = setTimeout(() => void this.save(), 20000);
        } else if (err.status === 409) {
          this.saveState.set('conflict');
          this.saveMessage.set(toProblem(e).message);
        } else {
          this.saveState.set('error');
          this.saveMessage.set(toProblem(e).message);
        }
      } finally {
        this.saving = null;
      }
    })();
    await this.saving;
    if (this.again) {
      this.again = false;
      await this.save();
    }
  }

  protected reload(): void {
    Drafts.clear(this.id());
    location.reload();
  }

  /** Keep the phone's copy over the newer server copy (it overwrites what was saved from the other device). */
  protected useStrayCopy(): void {
    const local = Drafts.get(this.id());
    if (!local) return this.strayCopy.set(null);
    this.d = normalize(local.data);
    if (local.location) this.setLocation(local.location);
    this.strayCopy.set(null);
    void this.save();
  }

  protected discardStrayCopy(): void {
    Drafts.clear(this.id());
    this.strayCopy.set(null);
  }

  // ---------------- location ----------------

  protected async capture(): Promise<void> {
    this.locating.set(true);
    this.locationError.set(null);
    try {
      this.setLocation(await this.gps.read());
      this.changed();
    } catch (e) {
      this.locationError.set((e as Error).message);
    } finally {
      this.locating.set(false);
    }
  }

  private setLocation(fix: GpsFix): void {
    this.location.set(fix);
    const c = this.c();
    if (c?.propertyLatitude != null && c.propertyLongitude != null)
      this.distance.set(distanceMetres(fix.latitude, fix.longitude, c.propertyLatitude, c.propertyLongitude));
  }

  protected readonly far = computed(() => (this.distance() ?? 0) > 300);

  protected mapLink(lat: number, lng: number): string {
    return `https://www.google.com/maps/search/?api=1&query=${lat},${lng}`;
  }

  protected directions(): string | null {
    const c = this.c();
    if (!c) return null;
    if (c.propertyLatitude != null && c.propertyLongitude != null)
      return `https://www.google.com/maps/dir/?api=1&destination=${c.propertyLatitude},${c.propertyLongitude}`;
    return c.propertyAddress ? mapsUrl({ latitude: null, longitude: null, address: c.propertyAddress, city: null }) : null;
  }

  protected tel(phone: string): string {
    return 'tel:' + phone.replace(/[^\d+]/g, '');
  }

  // ---------------- photos ----------------

  /** Camera for one tag (Photos step), or a quick photo from any step (tag asked after). */
  protected openCamera(input: HTMLInputElement, tag: PhotoTag | null): void {
    this.pendingCameraTag = tag;
    input.click();
  }

  protected async picked(event: Event, fromCamera: boolean): Promise<void> {
    const el = event.target as HTMLInputElement;
    const files = Array.from(el.files ?? []).filter((f) => f.type.startsWith('image/'));
    el.value = '';
    if (!files.length) return;
    const tag = this.pendingCameraTag;
    this.pendingCameraTag = null;
    if (fromCamera && tag) return this.addPhotos(files, tag, null, true);
    this.tagging.set({ files, tag: tag ?? DEFAULT_TAG[this.step()] ?? 'Other', fromCamera });
  }

  protected async confirmTag(tag: PhotoTag, caption: string | null): Promise<void> {
    const t = this.tagging();
    if (!t) return;
    this.tagging.set(null);
    await this.addPhotos(t.files, tag, caption, t.fromCamera);
  }

  /**
   * Camera photos are taken here and now, so they get this location and a printed date/location stamp.
   * Photos picked from the gallery may have been taken anywhere, so they get neither.
   */
  private async addPhotos(files: File[], tag: PhotoTag, caption: string | null, fromCamera: boolean): Promise<void> {
    this.preparing.set(true);
    try {
      const fix = fromCamera ? await this.gps.forPhoto() : null;
      const line = `${this.c()?.number ?? ''} · ${this.branding.branding().firmName}`;
      for (const f of files) {
        const takenAt = fromCamera ? new Date() : new Date(f.lastModified || Date.now());
        const p = await preparePhoto(f, fromCamera
          ? { takenAt, latitude: fix?.latitude ?? null, longitude: fix?.longitude ?? null, accuracyMetres: fix?.accuracyMetres, line }
          : undefined);
        await this.queue.add({
          assignmentId: this.id(), clientKey: newKey(), tag, caption, latitude: fix?.latitude ?? null, longitude: fix?.longitude ?? null,
          takenAt: takenAt.toISOString(), width: p.width, height: p.height, image: p.image, thumb: p.thumb,
          source: fromCamera ? 'Site' : 'Upload', stamped: fromCamera,
        });
      }
      this.toast.ok(files.length === 1 ? `Photo added: ${tagMeta(tag).label}` : `${files.length} photos added`,
        navigator.onLine ? undefined : 'They will upload when there is signal.');
    } catch (e) {
      this.toast.error('Could not add the photo', (e as Error).message);
    } finally {
      this.preparing.set(false);
    }
  }

  private localUrl(q: QueuedPhoto): string {
    let u = this.localUrls.get(q.clientKey);
    if (!u) {
      u = URL.createObjectURL(q.thumb);
      this.localUrls.set(q.clientKey, u);
    }
    return u;
  }

  private toViewer(p: ShownPhoto): ViewerPhoto {
    const c = this.c();
    const distance = p.server?.distanceFromPropertyMetres
      ?? (p.latitude != null && p.longitude != null && c?.propertyLatitude != null && c.propertyLongitude != null
        ? distanceMetres(p.latitude, p.longitude, c.propertyLatitude, c.propertyLongitude) : null);
    return {
      key: p.key, serverId: p.server?.id, localUrl: p.localUrl, tag: p.tag, caption: p.caption,
      includeInReport: p.server?.includeInReport ?? true, takenAt: p.takenAt, takenByName: p.server?.takenByName,
      latitude: p.latitude, longitude: p.longitude, distance, source: p.server?.source ?? p.queued?.source ?? 'Site',
      stamped: p.server?.stamped ?? p.queued?.stamped ?? false, error: p.queued?.error,
    };
  }

  protected open(p: ShownPhoto): void {
    const list = this.shown();
    this.viewing.set({ list: list.map((x) => this.toViewer(x)), start: Math.max(0, list.findIndex((x) => x.key === p.key)) });
  }

  protected async onPhotoSaved(ch: PhotoChange): Promise<void> {
    const p = ch.photo;
    if (p.serverId) {
      try {
        const updated = await firstValueFrom(this.photosApi.update(p.serverId, ch.tag, ch.caption, ch.includeInReport));
        this.photos.update((ps) => ps.map((x) => (x.id === updated.id ? updated : x)));
        this.schedule(300);
      } catch (e) {
        return this.toast.error('Could not change the photo', toProblem(e).message);
      }
    } else {
      const q = this.queued().find((x) => x.clientKey === p.key);
      if (q) await this.queue.add({ ...q, tag: ch.tag, caption: ch.caption, error: undefined });
    }
    this.viewing.set(null);
  }

  protected async onPhotoRemoved(p: ViewerPhoto): Promise<void> {
    if (p.serverId) {
      try {
        await firstValueFrom(this.photosApi.delete(p.serverId));
        this.photos.update((ps) => ps.filter((x) => x.id !== p.serverId));
        this.schedule(300);
      } catch (e) {
        return this.toast.error('Could not remove the photo', toProblem(e).message);
      }
    } else {
      await this.queue.remove(p.key);
    }
    this.viewing.set(null);
  }

  protected onPhotoRetry(p: ViewerPhoto): void {
    this.queue.retry(p.key);
    this.viewing.set(null);
  }

  protected isUploading(p: ShownPhoto): boolean {
    return this.queue.uploading() === p.key;
  }

  // ---------------- measurements ----------------

  protected addFloor(): void {
    this.d.measurements.floors.push({ name: this.floorName(this.d.measurements.floors.length), asPerDocuments: null, measured: null });
    this.changed();
  }

  protected removeFloor(i: number): void {
    this.d.measurements.floors.splice(i, 1);
    this.changed();
  }

  private floorName(i: number): string {
    return ['Ground floor', 'First floor', 'Second floor', 'Third floor', 'Fourth floor'][i] ?? `Floor ${i}`;
  }

  protected unit(): string {
    return this.d.measurements.unit === 'SqM' ? 'sq.m' : 'sq.ft';
  }

  protected plotArea(): number | null {
    const m = this.d.measurements;
    return m.plotLengthFt && m.plotBreadthFt ? Math.round(m.plotLengthFt * m.plotBreadthFt) : null;
  }

  protected usePlotArea(): void {
    const a = this.plotArea();
    if (a == null) return;
    this.d.measurements.landMeasured = this.d.measurements.unit === 'SqM' ? Math.round(a * 0.092903 * 100) / 100 : a;
    this.changed();
  }

  protected readonly specFields: [string, string][] = [
    ['foundation', 'Foundation'], ['walls', 'Walls'], ['roof', 'Roof'], ['flooring', 'Flooring'], ['doorsWindows', 'Doors & windows'],
    ['kitchen', 'Kitchen'], ['toilets', 'Toilets'], ['electrical', 'Electrical'], ['plumbing', 'Plumbing'],
  ];

  protected readonly structureOptions = computed(() => (this.detail()?.structureTypes ?? []).map((s) => ({ value: s, label: s })));

  protected choiceOptions(options: string[]): { value: string; label: string }[] {
    return options.map((x) => ({ value: x, label: x }));
  }

  protected answer(q: string): { question: string; answer: string | null } {
    return this.d.bankAnswers.find((a) => a.question.toLowerCase() === q.toLowerCase())!;
  }

  // ---------------- submit ----------------

  protected readonly waitingPhotos = computed(() => this.queued().filter((q) => !q.error).length);

  protected async submit(): Promise<void> {
    this.submitting.set(true);
    try {
      if (this.saveState() === 'pending' || this.saving) await this.save();
      if (this.saveState() !== 'saved' && this.saveState() !== 'idle') {
        this.toast.error('Not saved yet', 'Wait until the inspection is saved, then submit.');
        return;
      }
      const detail = await firstValueFrom(this.api.submit(this.id(), this.version));
      Drafts.clear(this.id());
      this.apply(detail);
      this.confirming.set(false);
      this.toast.ok('Inspection submitted', `${detail.case.number} moved to Inspection Done.`);
    } catch (e) {
      this.toast.error('Could not submit', toProblem(e).message);
      this.confirming.set(false);
    } finally {
      this.submitting.set(false);
    }
  }
}
