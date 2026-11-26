import { HttpClient } from '@angular/common/http';
import { AfterViewInit, Component, DestroyRef, ElementRef, inject, OnInit, signal, viewChild } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { toProblem } from '../../core/api/problem';
import { ToastService } from '../../core/ui/toast';

interface MySignature { hasSignature: boolean; updatedAt: string | null; fullName: string; registrationNo: string | null }

/** Draw your signature with a finger or mouse (or upload a scan). It is printed on the reports you sign. */
@Component({
  selector: 'vp-my-signature',
  template: `
    <div class="page-head">
      <div>
        <h2>My signature</h2>
        <p>Printed above your name on the valuation reports you sign. Digital signatures (eSign / DSC) come later.</p>
      </div>
    </div>

    <div class="grid">
      <section class="card sec">
        <div class="sec-head"><h3>{{ current() ? 'Replace your signature' : 'Add your signature' }}</h3></div>
        <div class="sec-body">
          <div class="pad" [class.empty]="!drawn()">
            <canvas #pad aria-label="Signature pad: draw your signature here"></canvas>
            @if (!drawn()) { <span class="hint-text"><i class="fa-solid fa-signature"></i> Sign here</span> }
            <span class="line"></span>
          </div>
          <div class="row">
            <button class="btn btn-ghost btn-sm" type="button" (click)="clear()" [disabled]="!drawn()"><i class="fa-solid fa-eraser"></i> Clear</button>
            <label class="btn btn-ghost btn-sm pick"><i class="fa-solid fa-upload"></i> Upload a scan<input type="file" accept="image/png,image/jpeg" (change)="upload($event)" /></label>
            <span class="grow"></span>
            <button class="btn btn-pri" type="button" (click)="saveDrawn()" [disabled]="!drawn() || busy()">
              <i class="fa-solid" [class]="busy() ? 'fa-spinner fa-spin' : 'fa-check'"></i> Save signature
            </button>
          </div>
          <p class="small muted">Use a dark pen-like stroke. A scan works best as a PNG on a white or transparent background, up to 1 MB.</p>
        </div>
      </section>

      <section class="card sec">
        <div class="sec-head"><h3>As it appears on reports</h3></div>
        <div class="sec-body">
          <div class="preview">
            @if (imageUrl(); as url) { <img [src]="url" alt="Your saved signature" /> } @else { <span class="muted small">No signature saved yet.</span> }
            <b>{{ me()?.fullName }}</b>
            @if (me()?.registrationNo) { <span class="small">Reg. No. {{ me()!.registrationNo }}</span> }
            @else { <span class="small warn">No registration number on your profile. An admin can add it in User Management.</span> }
          </div>
          @if (current()) {
            <button class="btn btn-ghost btn-sm danger-text" type="button" (click)="remove()" [disabled]="busy()"><i class="fa-solid fa-trash-can"></i> Remove signature</button>
          }
        </div>
      </section>
    </div>
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: 16px; }
    .page-head { margin-bottom: 0; }
    .grid { display: grid; grid-template-columns: minmax(0, 1.4fr) minmax(0, 1fr); gap: 16px; align-items: start; }
    @media (max-width: 899px) { .grid { grid-template-columns: minmax(0, 1fr); } }
    .sec { padding: 0; }
    .sec-head { padding: 14px 18px; border-bottom: 1px solid var(--line-2); }
    .sec-head h3 { margin: 0; font-size: 15px; }
    .sec-body { display: flex; flex-direction: column; gap: 12px; padding: 16px 18px 18px; }
    .pad { position: relative; height: 220px; border: 1px dashed var(--line); border-radius: 12px; background: #fff; touch-action: none; }
    .pad canvas { position: absolute; inset: 0; width: 100%; height: 100%; cursor: crosshair; }
    .pad .line { position: absolute; left: 24px; right: 24px; bottom: 56px; border-bottom: 1px solid var(--line); pointer-events: none; }
    .hint-text { position: absolute; left: 24px; bottom: 64px; color: var(--muted-2); font-size: 14px; pointer-events: none; }
    .row { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
    .grow { flex: 1; }
    .pick { position: relative; overflow: hidden; cursor: pointer; }
    .pick input { position: absolute; inset: 0; opacity: 0; cursor: pointer; }
    .preview { display: flex; flex-direction: column; align-items: flex-end; gap: 2px; padding: 16px; border: 1px solid var(--line); border-radius: 12px; background: #fafbfe; text-align: right; }
    .preview img { max-width: 220px; max-height: 70px; object-fit: contain; }
    .warn { color: var(--warn); }
    .danger-text { align-self: flex-start; color: var(--danger); }
  `,
})
export class MySignaturePage implements OnInit, AfterViewInit {
  private readonly http = inject(HttpClient);
  private readonly toast = inject(ToastService);
  private readonly base = `${environment.apiUrl}/api/account/signature`;
  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('pad');

  protected readonly me = signal<MySignature | null>(null);
  protected readonly current = signal(false);
  protected readonly imageUrl = signal<string | null>(null);
  protected readonly drawn = signal(false);
  protected readonly busy = signal(false);
  private ctx!: CanvasRenderingContext2D;
  private drawing = false;
  private last: { x: number; y: number } | null = null;

  constructor() {
    const resize = () => this.fit();
    window.addEventListener('resize', resize);
    inject(DestroyRef).onDestroy(() => {
      window.removeEventListener('resize', resize);
      const u = this.imageUrl();
      if (u) URL.revokeObjectURL(u);
    });
  }

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  ngAfterViewInit(): void {
    const c = this.canvas().nativeElement;
    this.ctx = c.getContext('2d')!;
    this.fit();
    c.addEventListener('pointerdown', (e) => {
      c.setPointerCapture(e.pointerId);
      this.drawing = true;
      this.last = this.point(e);
    });
    c.addEventListener('pointermove', (e) => {
      if (!this.drawing || !this.last) return;
      const p = this.point(e);
      // Pen pressure when available makes the stroke look hand-written.
      this.ctx.lineWidth = 1.6 + (e.pressure && e.pointerType === 'pen' ? e.pressure * 2.4 : 1.2);
      this.ctx.beginPath();
      this.ctx.moveTo(this.last.x, this.last.y);
      this.ctx.lineTo(p.x, p.y);
      this.ctx.stroke();
      this.last = p;
      this.drawn.set(true);
    });
    const end = () => {
      this.drawing = false;
      this.last = null;
    };
    c.addEventListener('pointerup', end);
    c.addEventListener('pointercancel', end);
  }

  /** Match the canvas to its on-screen size (sharp on high-DPI phones). */
  private fit(): void {
    const c = this.canvas().nativeElement;
    const r = c.getBoundingClientRect();
    const dpr = window.devicePixelRatio || 1;
    c.width = Math.round(r.width * dpr);
    c.height = Math.round(r.height * dpr);
    this.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    this.ctx.lineCap = 'round';
    this.ctx.lineJoin = 'round';
    this.ctx.strokeStyle = '#1a2340';
    this.drawn.set(false);
  }

  private point(e: PointerEvent): { x: number; y: number } {
    const r = this.canvas().nativeElement.getBoundingClientRect();
    return { x: e.clientX - r.left, y: e.clientY - r.top };
  }

  protected clear(): void {
    const c = this.canvas().nativeElement;
    this.ctx.clearRect(0, 0, c.width, c.height);
    this.drawn.set(false);
  }

  private async load(): Promise<void> {
    const me = await firstValueFrom(this.http.get<MySignature>(this.base));
    this.me.set(me);
    this.current.set(me.hasSignature);
    const old = this.imageUrl();
    if (old) URL.revokeObjectURL(old);
    this.imageUrl.set(me.hasSignature
      ? URL.createObjectURL(await firstValueFrom(this.http.get(`${this.base}/image`, { responseType: 'blob' })))
      : null);
  }

  /** Saves the drawing cropped to the ink, on a transparent background. */
  protected async saveDrawn(): Promise<void> {
    const blob = await this.cropped();
    if (blob) await this.send(blob, 'signature.png');
  }

  private cropped(): Promise<Blob | null> {
    const c = this.canvas().nativeElement;
    const { width, height } = c;
    const px = this.ctx.getImageData(0, 0, width, height).data;
    let minX = width, minY = height, maxX = 0, maxY = 0;
    for (let y = 0; y < height; y++)
      for (let x = 0; x < width; x++)
        if (px[(y * width + x) * 4 + 3] > 0) {
          if (x < minX) minX = x;
          if (x > maxX) maxX = x;
          if (y < minY) minY = y;
          if (y > maxY) maxY = y;
        }
    if (maxX <= minX || maxY <= minY) return Promise.resolve(null);
    const pad = 8;
    const out = document.createElement('canvas');
    out.width = maxX - minX + pad * 2;
    out.height = maxY - minY + pad * 2;
    out.getContext('2d')!.drawImage(c, minX - pad, minY - pad, out.width, out.height, 0, 0, out.width, out.height);
    return new Promise((res) => out.toBlob(res, 'image/png'));
  }

  protected async upload(e: Event): Promise<void> {
    const el = e.target as HTMLInputElement;
    const f = el.files?.[0];
    el.value = '';
    if (!f) return;
    if (!/^image\/(png|jpeg)$/.test(f.type)) return this.toast.error('Choose a PNG or JPEG image.');
    if (f.size > 1024 * 1024) return this.toast.error('The image can be up to 1 MB.');
    await this.send(f, f.name);
  }

  private async send(blob: Blob, name: string): Promise<void> {
    this.busy.set(true);
    try {
      const form = new FormData();
      form.append('file', blob, name);
      await firstValueFrom(this.http.put(this.base, form));
      this.clear();
      await this.load();
      this.toast.ok('Signature saved', 'It will be printed on the reports you sign.');
    } catch (e) {
      this.toast.error('Could not save', toProblem(e).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(): Promise<void> {
    this.busy.set(true);
    try {
      await firstValueFrom(this.http.delete(this.base));
      await this.load();
      this.toast.ok('Signature removed');
    } catch (e) {
      this.toast.error('Could not remove', toProblem(e).message);
    } finally {
      this.busy.set(false);
    }
  }
}
