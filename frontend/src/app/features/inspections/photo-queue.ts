import { inject, Injectable, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom, Subject } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { InspectionsApi, PhotoItem, PhotoMeta } from './inspections.api';

export interface QueuedPhoto extends PhotoMeta {
  assignmentId: string;
  image: Blob;
  thumb: Blob;
  /** Set when the server refused the photo (not a network problem); it stays until removed. */
  error?: string;
}

const DB = 'valupro-field';
const STORE = 'photos';

function open(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB, 1);
    req.onupgradeneeded = () => req.result.createObjectStore(STORE, { keyPath: 'clientKey' });
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
}

async function tx<T>(mode: IDBTransactionMode, run: (s: IDBObjectStore) => IDBRequest<T>): Promise<T> {
  const db = await open();
  try {
    return await new Promise<T>((resolve, reject) => {
      const r = run(db.transaction(STORE, mode).objectStore(STORE));
      r.onsuccess = () => resolve(r.result);
      r.onerror = () => reject(r.error);
    });
  } finally {
    db.close();
  }
}

/**
 * Photos wait on the phone (IndexedDB) until they reach the server, so a photo taken in a basement with no signal
 * is uploaded later, even after the page is closed and opened again.
 */
@Injectable({ providedIn: 'root' })
export class PhotoQueue {
  private readonly api = inject(InspectionsApi);
  private running = false;
  private retryTimer?: ReturnType<typeof setTimeout>;

  /** Photos still on the phone, all cases. */
  readonly items = signal<QueuedPhoto[]>([]);
  readonly uploading = signal<string | null>(null);
  /** Emits each photo once the server has it. */
  readonly uploaded = new Subject<{ assignmentId: string; photo: PhotoItem }>();

  constructor() {
    window.addEventListener('online', () => void this.run());
    void this.refresh().then(() => this.run());
  }

  get available(): boolean {
    return 'indexedDB' in window;
  }

  async add(p: QueuedPhoto): Promise<void> {
    if (this.available) {
      try {
        await tx('readwrite', (s) => s.put(p));
      } catch {
        /* storage blocked: still try to upload from memory */
      }
    }
    this.items.update((xs) => [...xs.filter((x) => x.clientKey !== p.clientKey), p]);
    void this.run();
  }

  async remove(clientKey: string): Promise<void> {
    if (this.available) await tx('readwrite', (s) => s.delete(clientKey)).catch(() => undefined);
    this.items.update((xs) => xs.filter((x) => x.clientKey !== clientKey));
  }

  private async refresh(): Promise<void> {
    if (!this.available) return;
    try {
      const all = await tx<QueuedPhoto[]>('readonly', (s) => s.getAll() as IDBRequest<QueuedPhoto[]>);
      const mem = this.items();
      this.items.set([...all, ...mem.filter((m) => !all.some((a) => a.clientKey === m.clientKey))]);
    } catch {
      /* ignore */
    }
  }

  /** Uploads waiting photos one at a time; on a network failure waits and tries again. */
  async run(): Promise<void> {
    if (this.running) return;
    this.running = true;
    clearTimeout(this.retryTimer);
    try {
      for (;;) {
        const next = this.items().find((x) => !x.error);
        if (!next) break;
        if (!navigator.onLine) return this.later();
        this.uploading.set(next.clientKey);
        try {
          const { assignmentId, image, thumb, error: _e, ...meta } = next;
          const photo = await firstValueFrom(this.api.upload(assignmentId, meta, image, thumb));
          await this.remove(next.clientKey);
          this.uploaded.next({ assignmentId, photo });
        } catch (e) {
          if (e instanceof HttpErrorResponse && (e.status === 0 || e.status >= 500 || e.status === 401)) return this.later();
          const error = toProblem(e).message;
          this.items.update((xs) => xs.map((x) => (x.clientKey === next.clientKey ? { ...x, error } : x)));
          if (this.available) await tx('readwrite', (s) => s.put({ ...next, error })).catch(() => undefined);
        }
      }
    } finally {
      this.uploading.set(null);
      this.running = false;
    }
  }

  private later(): void {
    this.running = false;
    this.uploading.set(null);
    this.retryTimer = setTimeout(() => void this.run(), 20000);
  }

  /** Try a refused photo again (e.g. after the case was sent back for changes). */
  retry(clientKey: string): void {
    this.items.update((xs) => xs.map((x) => (x.clientKey === clientKey ? { ...x, error: undefined } : x)));
    void this.run();
  }
}
