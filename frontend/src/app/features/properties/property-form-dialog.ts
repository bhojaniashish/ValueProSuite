import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Dialog } from '../../core/ui/dialog';
import { INDIAN_STATES } from '../settings/settings.api';
import { mapsUrl, PROPERTY_TYPES, PROPERTY_USAGES, PropertiesApi, Property, PropertyType, PropertyUsage, propertyType, SaveProperty } from './properties.api';

@Component({
  selector: 'vp-property-form-dialog',
  imports: [Dialog, ReactiveFormsModule],
  template: `
    <vp-dialog [title]="property() ? 'Edit Property' : 'Add Property'"
      [subtitle]="property() ? property()!.code : 'Capture full property & location details'" icon="fa-map-location-dot" (closed)="closed.emit()">
      <form id="propForm" class="form-grid" [formGroup]="form" (ngSubmit)="save()" novalidate>
        @if (error()) { <div class="alert-inline danger col-2"><i class="fa-solid fa-circle-exclamation"></i> {{ error() }}</div> }

        <div class="fld">
          <label for="ptype">Property Type <span class="req">*</span></label>
          <select id="ptype" formControlName="type">@for (t of types; track t.value) { <option [value]="t.value">{{ t.label }}</option> }</select>
        </div>
        <div class="fld" [class.invalid]="bad('ownerName')">
          <label for="owner">Owner Name <span class="req">*</span></label>
          <input id="owner" formControlName="ownerName" placeholder="Suresh Agarwal" />
          @if (bad('ownerName')) { <span class="err">{{ server()['ownerName'] ?? "Enter the owner's name." }}</span> }
        </div>
        <div class="fld col-2" [class.invalid]="bad('address')">
          <label for="paddr">Property Address <span class="req">*</span></label>
          <input id="paddr" formControlName="address" placeholder="Flat 904, Amanora Park Town, Hadapsar" />
          @if (bad('address')) { <span class="err">{{ server()['address'] ?? 'Enter the property address.' }}</span> }
        </div>
        <div class="fld"><label for="pcity">City</label><input id="pcity" formControlName="city" placeholder="Pune" /></div>
        <div class="fld">
          <label for="pstate">State</label>
          <select id="pstate" formControlName="state"><option value="">Select state</option>@for (s of states; track s.code) { <option [value]="s.name">{{ s.name }}</option> }</select>
        </div>
        <div class="fld" [class.invalid]="bad('pincode')">
          <label for="ppin">Pincode</label>
          <input id="ppin" class="tnum" formControlName="pincode" placeholder="411014" maxlength="6" inputmode="numeric" />
          @if (bad('pincode')) { <span class="err">Enter a 6-digit pincode.</span> }
        </div>
        <div class="fld"><label for="survey">Survey Number</label><input id="survey" formControlName="surveyNo" placeholder="Sy. No. 120/1" /></div>
        <div class="fld"><label for="cts">CTS Number</label><input id="cts" formControlName="ctsNo" placeholder="CTS 4507" /></div>
        <div class="fld"><label for="plot">Plot Number</label><input id="plot" formControlName="plotNo" placeholder="Plot 12" /></div>

        @if (kind().land) {
          <div class="fld" [class.invalid]="bad('landArea')">
            <label for="land">Land Area</label>
            <div class="fld-suf"><input id="land" type="number" min="0" step="any" formControlName="landArea" /><span>sq.ft</span></div>
            @if (sqm('landArea')) { <span class="hint">≈ {{ sqm('landArea') }} sq.m</span> }
          </div>
        }
        @if (kind().built) {
          <div class="fld" [class.invalid]="bad('builtUpArea')">
            <label for="built">Built-up Area</label>
            <div class="fld-suf"><input id="built" type="number" min="0" step="any" formControlName="builtUpArea" /><span>sq.ft</span></div>
            @if (sqm('builtUpArea')) { <span class="hint">≈ {{ sqm('builtUpArea') }} sq.m</span> }
          </div>
        }
        <div class="fld">
          <label for="zone">Zone</label>
          <input id="zone" formControlName="zone" list="zones" placeholder="Residential R1" />
          <datalist id="zones">@for (z of zones(); track z) { <option [value]="z"></option> }</datalist>
        </div>
        <div class="fld">
          <label for="usage">Property Usage</label>
          <select id="usage" formControlName="usage"><option value="">Not recorded</option>@for (u of usages; track u.value) { <option [value]="u.value">{{ u.label }}</option> }</select>
        </div>

        <div class="col-2 loc">
          <div class="loc-head">
            <b>GPS Location</b>
            <button class="btn btn-soft btn-sm" type="button" (click)="locate()" [disabled]="locating()">
              <i class="fa-solid" [class]="locating() ? 'fa-spinner fa-spin' : 'fa-location-crosshairs'"></i> Use my location
            </button>
            @if (hasCoords()) { <a class="small" [href]="maps()" target="_blank" rel="noopener">Open in Google Maps <i class="fa-solid fa-arrow-up-right-from-square"></i></a> }
          </div>
          <div class="form-grid">
            <div class="fld" [class.invalid]="bad('latitude')">
              <label for="lat">Latitude</label>
              <input id="lat" class="tnum" type="number" step="any" formControlName="latitude" placeholder="21.170240" />
            </div>
            <div class="fld" [class.invalid]="bad('longitude')">
              <label for="lng">Longitude</label>
              <input id="lng" class="tnum" type="number" step="any" formControlName="longitude" placeholder="72.831061" />
            </div>
          </div>
          @if (geoError()) { <span class="err">{{ geoError() }}</span> }
          @else if (bad('latitude') && !server()['latitude'] || bad('longitude') && !server()['longitude']) {
            <span class="err">In India, latitude is between 6 and 38 and longitude between 68 and 98. Are the two swapped?</span>
          }
          @else if (server()['latitude'] || server()['longitude']) { <span class="err">{{ server()['latitude'] ?? server()['longitude'] }}</span> }
          @else { <span class="hint">On site, "Use my location" fills these from the phone's GPS. The map view comes with GIS in Phase 5.</span> }
        </div>

        <div class="fld col-2"><label for="premarks">Remarks</label><textarea id="premarks" rows="2" formControlName="remarks"></textarea></div>
      </form>
      <ng-container footer>
        <button class="btn btn-ghost" type="button" (click)="closed.emit()">Cancel</button>
        <button class="btn btn-pri" type="submit" form="propForm" [disabled]="busy()">
          <i class="fa-solid" [class]="busy() ? 'fa-spinner fa-spin' : 'fa-check'"></i> Save Property
        </button>
      </ng-container>
    </vp-dialog>
  `,
  styles: `
    .loc { display: flex; flex-direction: column; gap: 10px; padding: 14px; border: 1px solid var(--line-2); border-radius: 12px; background: #fafbfe; }
    .loc-head { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; }
    .loc-head a { margin-left: auto; font-weight: 600; text-decoration: none; }
    .hint { font-size: 11.5px; color: var(--muted); }
    .err { font-size: 12px; color: var(--danger); }
  `,
})
export class PropertyFormDialog implements OnInit {
  private readonly api = inject(PropertiesApi);

  readonly property = input<Property | null>(null);
  readonly zones = input<string[]>([]);
  readonly saved = output<Property>();
  readonly closed = output<void>();

  protected readonly types = PROPERTY_TYPES;
  protected readonly usages = PROPERTY_USAGES;
  protected readonly states = INDIAN_STATES;
  protected readonly busy = signal(false);
  protected readonly locating = signal(false);
  protected readonly geoError = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly server = signal<Record<string, string>>({});

  protected readonly form = inject(NonNullableFormBuilder).group({
    type: ['ResidentialFlat' as PropertyType],
    ownerName: ['', [Validators.required, Validators.maxLength(200)]],
    address: ['', [Validators.required, Validators.maxLength(500)]],
    city: [''],
    state: [''],
    pincode: ['', Validators.pattern(/^[1-9][0-9]{5}$/)],
    surveyNo: [''],
    ctsNo: [''],
    plotNo: [''],
    landArea: [null as number | null, Validators.min(0)],
    builtUpArea: [null as number | null, Validators.min(0)],
    zone: [''],
    usage: ['' as PropertyUsage | ''],
    latitude: [null as number | null, [Validators.min(6), Validators.max(38)]],
    longitude: [null as number | null, [Validators.min(68), Validators.max(98)]],
    remarks: [''],
  });

  private readonly v = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });
  protected readonly kind = computed(() => propertyType((this.v().type ?? 'ResidentialFlat') as PropertyType));
  protected readonly hasCoords = computed(() => this.v().latitude != null && this.v().longitude != null);
  protected readonly maps = computed(() => mapsUrl({ latitude: this.v().latitude ?? null, longitude: this.v().longitude ?? null, address: '', city: null }));

  ngOnInit(): void {
    const p = this.property();
    if (p) {
      this.form.setValue({
        type: p.type, ownerName: p.ownerName, address: p.address, city: p.city ?? '', state: p.state ?? '', pincode: p.pincode ?? '',
        surveyNo: p.surveyNo ?? '', ctsNo: p.ctsNo ?? '', plotNo: p.plotNo ?? '', landArea: p.landArea, builtUpArea: p.builtUpArea,
        zone: p.zone ?? '', usage: p.usage ?? '', latitude: p.latitude, longitude: p.longitude, remarks: p.remarks ?? '',
      });
    }
    this.form.valueChanges.subscribe(() => this.server.set({}));
  }

  protected sqm(name: 'landArea' | 'builtUpArea'): string | null {
    const v = Number(this.v()[name]);
    return v > 0 ? (v * 0.092903).toLocaleString('en-IN', { maximumFractionDigits: 1 }) : null;
  }

  protected bad(name: keyof typeof this.form.controls): boolean {
    const c = this.form.controls[name];
    return (c.touched && c.invalid) || !!this.server()[name];
  }

  protected locate(): void {
    this.geoError.set(null);
    if (!navigator.geolocation) return this.geoError.set('This device cannot share its location.');
    this.locating.set(true);
    navigator.geolocation.getCurrentPosition(
      (pos) => {
        this.locating.set(false);
        this.form.patchValue({ latitude: +pos.coords.latitude.toFixed(6), longitude: +pos.coords.longitude.toFixed(6) });
        this.form.markAsDirty();
      },
      (err) => {
        this.locating.set(false);
        this.geoError.set(err.code === err.PERMISSION_DENIED ? 'Location permission was denied. Allow it in the browser and try again.' : 'Could not get the location. Try again outdoors, or type it in.');
      },
      { enableHighAccuracy: true, timeout: 15000 },
    );
  }

  protected async save(): Promise<void> {
    const v = this.form.getRawValue();
    if ((v.latitude == null) !== (v.longitude == null)) {
      this.server.set({ latitude: 'Enter both latitude and longitude, or neither.' });
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const blank = (s: string) => s.trim() || null;
    const kind = propertyType(v.type);
    const body: SaveProperty = {
      type: v.type, ownerName: v.ownerName.trim(), ownerClientId: this.property()?.ownerClientId ?? null, address: v.address.trim(),
      city: blank(v.city), state: blank(v.state), pincode: blank(v.pincode), surveyNo: blank(v.surveyNo), ctsNo: blank(v.ctsNo), plotNo: blank(v.plotNo),
      landArea: kind.land && v.landArea ? Number(v.landArea) : null,
      builtUpArea: kind.built && v.builtUpArea ? Number(v.builtUpArea) : null,
      zone: blank(v.zone), usage: v.usage || null,
      latitude: v.latitude != null ? Number(v.latitude) : null, longitude: v.longitude != null ? Number(v.longitude) : null,
      remarks: blank(v.remarks),
    };
    this.busy.set(true);
    this.error.set(null);
    try {
      const p = this.property();
      this.saved.emit(await firstValueFrom(p ? this.api.update(p.id, body) : this.api.create(body)));
    } catch (e) {
      const p = toProblem(e);
      this.server.set(p.fields);
      if (!Object.keys(p.fields).length) this.error.set(p.message);
    } finally {
      this.busy.set(false);
    }
  }
}
