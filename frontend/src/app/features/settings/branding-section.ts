import { Component, computed, effect, inject, input, OnDestroy, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { BrandingService } from '../../core/branding/branding.service';
import { ToastService } from '../../core/ui/toast';
import { BrandingSettings, SettingsApi } from './settings.api';

const HEX = /^#[0-9A-Fa-f]{6}$/;
const MAX_BYTES = 2 * 1024 * 1024;
const TYPES = ['image/png', 'image/jpeg', 'image/webp', 'image/svg+xml'];

/** Logo, report watermark and colours. Colour changes preview live across the app until saved or discarded. */
@Component({
  selector: 'vp-branding-section',
  template: `
    <div class="card">
      <div class="card-head">
        <h3>Branding</h3>
        <span class="sub">Logo and colours used in the app, on reports and on invoices.</span>
      </div>
      <div class="card-pad grid">
        @for (img of images; track img.key) {
          <div class="fld">
            <label>{{ img.label }}</label>
            <div class="drop" [class.has]="url(img.key)">
              @if (url(img.key); as src) {
                <img [src]="src" [alt]="img.label" />
              } @else {
                <i class="fa-solid" [class]="img.icon"></i>
                <span class="small muted">No {{ img.label.toLowerCase() }} yet</span>
              }
              @if (uploading() === img.key) { <div class="busy"><i class="fa-solid fa-spinner fa-spin"></i></div> }
            </div>
            @if (canEdit()) {
              <div class="row">
                <label class="btn btn-soft btn-sm upload">
                  <i class="fa-solid fa-upload"></i> {{ url(img.key) ? 'Replace' : 'Upload' }}
                  <input type="file" [accept]="accept" (change)="upload(img.key, $event)" [disabled]="!!uploading()" />
                </label>
                @if (url(img.key)) {
                  <button class="btn btn-ghost btn-sm" type="button" (click)="remove(img.key)" [disabled]="!!uploading()">Remove</button>
                }
              </div>
            }
            <span class="hint">{{ img.hint }}</span>
          </div>
        }

        <div class="fld">
          <label for="primary">Primary Brand Color</label>
          <div class="color">
            <input type="color" [value]="primary()" (input)="setColor('primary', $any($event.target).value)" [disabled]="!canEdit()" aria-label="Pick primary colour" />
            <input id="primary" class="tnum" [value]="primary()" (input)="setColor('primary', $any($event.target).value)" [disabled]="!canEdit()" maxlength="7" />
          </div>
          <span class="hint">Buttons, links, the active menu item.</span>
        </div>
        <div class="fld">
          <label for="accent">Accent Color</label>
          <div class="color">
            <input type="color" [value]="accent()" (input)="setColor('accent', $any($event.target).value)" [disabled]="!canEdit()" aria-label="Pick accent colour" />
            <input id="accent" class="tnum" [value]="accent()" (input)="setColor('accent', $any($event.target).value)" [disabled]="!canEdit()" maxlength="7" />
          </div>
          <span class="hint">Highlights and badges.</span>
        </div>

        <div class="fld col-2">
          <label>Preview</label>
          <div class="preview">
            <button class="btn btn-pri" type="button" tabindex="-1">Primary button</button>
            <button class="btn btn-soft" type="button" tabindex="-1">Soft button</button>
            <span class="badge b-brand">Brand badge</span>
            <span class="badge b-gold">Accent badge</span>
          </div>
        </div>
      </div>
      @if (canEdit()) {
        <div class="section-foot">
          @if (colorsDirty()) { <span class="status">Previewing unsaved colours</span> }
          <button class="btn btn-ghost" type="button" [disabled]="!colorsDirty() || busy()" (click)="discard()">Discard</button>
          <button class="btn btn-pri" type="button" [disabled]="!colorsDirty() || !colorsValid() || busy()" (click)="saveColors()">
            <i class="fa-solid" [class]="busy() ? 'fa-spinner fa-spin' : 'fa-floppy-disk'"></i> Save Colours
          </button>
        </div>
      }
    </div>
  `,
  styles: `
    .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 20px 18px; }
    .col-2 { grid-column: span 2; }
    @media (max-width: 599px) { .grid { grid-template-columns: 1fr; } .col-2 { grid-column: span 1; } }
    .drop { position: relative; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 6px;
      height: 130px; border: 2px dashed var(--line); border-radius: 12px; background: #fafbfe; color: var(--muted-2); }
    .drop i { font-size: 26px; }
    .drop.has { border-style: solid; background: repeating-conic-gradient(#f2f4f9 0 25%, #fff 0 50%) 50% / 16px 16px; }
    .drop img { max-width: 80%; max-height: 100px; object-fit: contain; }
    .busy { position: absolute; inset: 0; display: grid; place-items: center; background: rgba(255,255,255,.7); border-radius: 10px; color: var(--brand); font-size: 22px; }
    .row { display: flex; gap: 8px; }
    .upload { position: relative; overflow: hidden; cursor: pointer; }
    .upload input { position: absolute; inset: 0; opacity: 0; cursor: pointer; }
    .color { display: flex; gap: 8px; }
    .color input[type='color'] { flex: none; width: 48px; padding: 4px; cursor: pointer; }
    .preview { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; padding: 14px; border: 1px solid var(--line-2); border-radius: 10px; }
  `,
})
export class BrandingSection implements OnDestroy {
  private readonly api = inject(SettingsApi);
  private readonly toast = inject(ToastService);
  private readonly brand = inject(BrandingService);

  readonly value = input.required<BrandingSettings>();
  readonly canEdit = input(false);
  readonly saved = output<BrandingSettings>();

  protected readonly accept = TYPES.join(',');
  protected readonly images = [
    { key: 'logo' as const, label: 'Company Logo', icon: 'fa-image', hint: 'PNG, JPG, WebP or SVG · up to 2 MB · square works best.' },
    { key: 'watermark' as const, label: 'Report Watermark', icon: 'fa-stamp', hint: 'Printed faintly behind report pages (Phase 3).' },
  ];

  protected readonly primary = signal('#2B50C4');
  protected readonly accent = signal('#C2922E');
  protected readonly busy = signal(false);
  protected readonly uploading = signal<'logo' | 'watermark' | null>(null);

  protected readonly colorsValid = computed(() => HEX.test(this.primary()) && HEX.test(this.accent()));
  protected readonly colorsDirty = computed(
    () =>
      this.primary().toUpperCase() !== this.value().primaryColor.toUpperCase() ||
      this.accent().toUpperCase() !== this.value().accentColor.toUpperCase(),
  );

  constructor() {
    effect(() => {
      const v = this.value();
      this.primary.set(v.primaryColor);
      this.accent.set(v.accentColor);
    });
  }

  ngOnDestroy(): void {
    if (this.colorsDirty()) this.brand.restore(); // leaving the tab drops an unsaved preview
  }

  protected url(key: 'logo' | 'watermark'): string | null {
    return key === 'logo' ? this.value().logoUrl : this.value().watermarkUrl;
  }

  protected setColor(which: 'primary' | 'accent', value: string): void {
    (which === 'primary' ? this.primary : this.accent).set(value.trim());
    if (this.colorsValid()) this.brand.preview(this.primary(), this.accent());
  }

  protected discard(): void {
    this.primary.set(this.value().primaryColor);
    this.accent.set(this.value().accentColor);
    this.brand.restore();
  }

  protected async saveColors(): Promise<void> {
    this.busy.set(true);
    try {
      const b = await firstValueFrom(this.api.updateBranding(this.primary(), this.accent()));
      this.emit(b);
      this.toast.ok('Colours saved', 'Everyone sees the new colours next time they open the app.');
    } catch (e) {
      this.toast.error('Could not save colours', toProblem(e).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected async upload(key: 'logo' | 'watermark', event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = ''; // allow choosing the same file again
    if (!file) return;
    if (!TYPES.includes(file.type)) return this.toast.error('Unsupported file', 'Use a PNG, JPG, WebP or SVG image.');
    if (file.size > MAX_BYTES) return this.toast.error('File too large', 'The image must be 2 MB or smaller.');

    this.uploading.set(key);
    try {
      this.emit(await firstValueFrom(this.api.uploadImage(key, file)));
      this.toast.ok(key === 'logo' ? 'Logo updated' : 'Watermark updated');
    } catch (e) {
      this.toast.error('Upload failed', toProblem(e).message);
    } finally {
      this.uploading.set(null);
    }
  }

  protected async remove(key: 'logo' | 'watermark'): Promise<void> {
    this.uploading.set(key);
    try {
      this.emit(await firstValueFrom(this.api.removeImage(key)));
      this.toast.ok(key === 'logo' ? 'Logo removed' : 'Watermark removed');
    } catch (e) {
      this.toast.error('Could not remove', toProblem(e).message);
    } finally {
      this.uploading.set(null);
    }
  }

  private emit(b: BrandingSettings): void {
    this.saved.emit(b);
    const current = this.brand.branding();
    this.brand.apply({ ...current, primaryColor: b.primaryColor, accentColor: b.accentColor, logoUrl: b.logoUrl });
  }
}
