import { Injectable, signal } from '@angular/core';
import { GpsFix, InspectionData, InspectionDetail } from './inspections.api';

// ---------------- photos: resize on the phone before upload ----------------

export interface PreparedPhoto { image: Blob; thumb: Blob; width: number; height: number }

const MAX_SIDE = 1600;
const THUMB_SIDE = 360;

/**
 * Phone photos are 3–8 MB; banks' reports need far less. Resizing to 1600 px (JPEG, ~300 KB) keeps uploads quick on
 * mobile data. EXIF orientation is applied, so photos are upright.
 */
export async function preparePhoto(file: Blob): Promise<PreparedPhoto> {
  const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
  try {
    const [image, size] = await draw(bitmap, MAX_SIDE, 0.82);
    const [thumb] = await draw(bitmap, THUMB_SIDE, 0.7);
    return { image, thumb, width: size.w, height: size.h };
  } finally {
    bitmap.close();
  }
}

async function draw(src: ImageBitmap, maxSide: number, quality: number): Promise<[Blob, { w: number; h: number }]> {
  const scale = Math.min(1, maxSide / Math.max(src.width, src.height));
  const w = Math.round(src.width * scale);
  const h = Math.round(src.height * scale);
  const canvas = document.createElement('canvas');
  canvas.width = w;
  canvas.height = h;
  canvas.getContext('2d')!.drawImage(src, 0, 0, w, h);
  const blob = await new Promise<Blob | null>((res) => canvas.toBlob(res, 'image/jpeg', quality));
  if (!blob) throw new Error('Could not process the photo.');
  return [blob, { w, h }];
}

export function newKey(): string {
  return typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
        const r = (Math.random() * 16) | 0;
        return (c === 'x' ? r : (r & 0x3) | 0x8).toString(16);
      });
}

// ---------------- location ----------------

@Injectable({ providedIn: 'root' })
export class Gps {
  /** The last position read, reused for photos taken within two minutes. */
  readonly last = signal<GpsFix | null>(null);

  get supported(): boolean {
    return 'geolocation' in navigator;
  }

  /** Reads the current position (high accuracy). Rejects with a message the valuer can act on. */
  read(timeoutMs = 20000): Promise<GpsFix> {
    return new Promise((resolve, reject) => {
      if (!this.supported) return reject(new Error('This device cannot share its location.'));
      navigator.geolocation.getCurrentPosition(
        (p) => {
          const fix: GpsFix = {
            latitude: +p.coords.latitude.toFixed(6),
            longitude: +p.coords.longitude.toFixed(6),
            accuracyMetres: Math.round(p.coords.accuracy),
            at: new Date(p.timestamp).toISOString(),
          };
          this.last.set(fix);
          resolve(fix);
        },
        (e) =>
          reject(new Error(e.code === e.PERMISSION_DENIED
            ? 'Location is blocked for this site. Allow location in the browser settings and try again.'
            : e.code === e.TIMEOUT ? 'Could not get a location fix in time. Step outside or near a window and try again.'
            : 'Location is not available right now.')),
        { enableHighAccuracy: true, timeout: timeoutMs, maximumAge: 10000 },
      );
    });
  }

  /** A recent fix if there is one, else a quick read; null if neither works (photos still save without it). */
  async forPhoto(): Promise<GpsFix | null> {
    const l = this.last();
    if (l?.at && Date.now() - new Date(l.at).getTime() < 120000) return l;
    try {
      return await this.read(6000);
    } catch {
      return null;
    }
  }
}

// ---------------- the inspection kept on the phone ----------------

export interface LocalDraft {
  /** Server version this copy was based on. */
  version: number;
  data: InspectionData;
  location: GpsFix | null;
  /** Changes not yet on the server. */
  dirty: boolean;
  savedAt: string;
  /** The case and photos as last seen, so the inspection opens even with no signal. */
  detail?: InspectionDetail;
}

/** Every change is written to the phone first, so nothing is lost when signal drops or the page is closed. */
export const Drafts = {
  key: (assignmentId: string) => `vp.inspection.${assignmentId}`,
  get(assignmentId: string): LocalDraft | null {
    try {
      const raw = localStorage.getItem(this.key(assignmentId));
      return raw ? (JSON.parse(raw) as LocalDraft) : null;
    } catch {
      return null;
    }
  },
  put(assignmentId: string, d: LocalDraft): void {
    try {
      localStorage.setItem(this.key(assignmentId), JSON.stringify(d));
    } catch {
      /* storage full or blocked: the server copy still works */
    }
  },
  clear(assignmentId: string): void {
    try {
      localStorage.removeItem(this.key(assignmentId));
    } catch {
      /* ignore */
    }
  },
};
