import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Permission } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { Dialog } from '../../core/ui/dialog';
import { ToastService } from '../../core/ui/toast';
import { AssignmentMiniList } from '../assignments/assignment-mini-list';
import { area, mapsUrl, PropertiesApi, Property, PropertyDetail, propertyType, usageLabel } from './properties.api';
import { PropertyFormDialog } from './property-form-dialog';

@Component({
  selector: 'vp-property-detail-page',
  imports: [RouterLink, PropertyFormDialog, Dialog, AssignmentMiniList],
  template: `
    <a class="back small" routerLink="/properties"><i class="fa-solid fa-arrow-left"></i> All properties</a>
    @if (error()) {
      <div class="card card-pad"><div class="alert-inline danger"><i class="fa-solid fa-triangle-exclamation"></i> {{ error() }}</div></div>
    } @else if (detail(); as d) {
      <div class="card head">
        <span class="ptype"><i class="fa-solid" [class]="type().icon"></i></span>
        <div class="title">
          <h2>{{ d.property.code }} — {{ type().label }}</h2>
          <p class="muted">{{ d.property.ownerName }} · {{ place() }}</p>
        </div>
        <div class="actions">
          @if (canCreateAssignment()) {
            <a class="btn btn-pri btn-sm" routerLink="/assignments" [queryParams]="{ new: 1, property: d.property.id }"><i class="fa-solid fa-plus"></i> New Assignment</a>
          }
          @if (canManage()) {
            <button class="btn btn-ghost btn-sm" type="button" (click)="editing.set(true)"><i class="fa-solid fa-pen"></i> Edit</button>
            <button class="btn btn-ghost btn-sm danger" type="button" (click)="deleting.set(true)"><i class="fa-solid fa-trash-can"></i></button>
          }
        </div>
      </div>

      <div class="grid2">
        <div class="card card-pad">
          <h3>Details</h3>
          <dl>
            <div class="wide"><dt>Address</dt><dd>{{ d.property.address }}{{ d.property.pincode ? ' - ' + d.property.pincode : '' }}</dd></div>
            <div><dt>Survey No.</dt><dd class="tnum">{{ d.property.surveyNo || '—' }}</dd></div>
            <div><dt>CTS No.</dt><dd class="tnum">{{ d.property.ctsNo || '—' }}</dd></div>
            <div><dt>Plot No.</dt><dd class="tnum">{{ d.property.plotNo || '—' }}</dd></div>
            <div><dt>Land Area</dt><dd class="tnum">{{ area(d.property.landArea) }}</dd></div>
            <div><dt>Built-up</dt><dd class="tnum">{{ area(d.property.builtUpArea) }}</dd></div>
            <div><dt>Zone</dt><dd>{{ d.property.zone || '—' }}</dd></div>
            <div><dt>Usage</dt><dd>{{ usage() || '—' }}</dd></div>
            <div><dt>Latitude</dt><dd class="tnum">{{ d.property.latitude ?? '—' }}</dd></div>
            <div><dt>Longitude</dt><dd class="tnum">{{ d.property.longitude ?? '—' }}</dd></div>
            @if (d.property.remarks) { <div class="wide"><dt>Remarks</dt><dd>{{ d.property.remarks }}</dd></div> }
          </dl>
          <a class="btn btn-soft btn-sm map" [href]="maps()" target="_blank" rel="noopener">
            <i class="fa-solid fa-map-location-dot"></i> {{ d.property.latitude != null ? 'Locate on Google Maps' : 'Search address on Google Maps' }}
          </a>
        </div>
        <div class="card card-pad">
          <h3>Valuation history <span class="muted small">· {{ d.assignments.length }}</span></h3>
          <vp-assignment-mini-list [items]="d.assignments" empty="This property has not been valued yet." />
        </div>
      </div>

      @if (editing()) {
        <vp-property-form-dialog [property]="d.property" (saved)="onSaved($event)" (closed)="editing.set(false)" />
      }
      @if (deleting()) {
        <vp-dialog title="Remove property?" [subtitle]="d.property.code" icon="fa-trash-can" tone="b-danger" size="sm" (closed)="deleting.set(false)">
          <p class="confirm">{{ d.property.code }} will be removed from the property register. Its past assignments keep their details.</p>
          <ng-container footer>
            <button class="btn btn-ghost" type="button" (click)="deleting.set(false)">Cancel</button>
            <button class="btn btn-danger" type="button" [disabled]="busy()" (click)="remove()"><i class="fa-solid fa-trash-can"></i> Remove</button>
          </ng-container>
        </vp-dialog>
      }
    } @else {
      <div class="card card-pad muted"><i class="fa-solid fa-spinner fa-spin"></i> Loading…</div>
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: 18px; }
    .back { align-self: flex-start; display: inline-flex; gap: 6px; align-items: center; font-weight: 600; text-decoration: none; margin-bottom: -6px; }
    .head { display: flex; flex-wrap: wrap; align-items: center; gap: 16px; padding: 20px; }
    .ptype { display: grid; place-items: center; width: 52px; height: 52px; border-radius: 14px; background: var(--brand-soft); color: var(--brand); font-size: 20px; }
    .title { flex: 1; min-width: 200px; }
    .title h2 { margin: 0 0 4px; font-size: 20px; }
    .title p { margin: 0; }
    .actions { display: flex; flex-wrap: wrap; gap: 8px; }
    .btn.danger:hover { border-color: var(--danger); color: var(--danger); background: var(--danger-soft); }
    .grid2 { display: grid; grid-template-columns: 1.2fr 1fr; gap: 18px; align-items: start; }
    @media (max-width: 991px) { .grid2 { grid-template-columns: 1fr; } }
    h3 { margin: 0 0 12px; font-size: 15px; }
    dl { display: grid; grid-template-columns: repeat(3, 1fr); gap: 14px; margin: 0; }
    @media (max-width: 599px) { dl { grid-template-columns: 1fr 1fr; } }
    dl .wide { grid-column: 1 / -1; }
    dt { font-size: 11.5px; font-weight: 600; text-transform: uppercase; letter-spacing: .04em; color: var(--muted); }
    dd { margin: 3px 0 0; font-weight: 600; overflow-wrap: anywhere; }
    .map { margin-top: 16px; }
    .confirm { margin: 0; line-height: 1.55; }
  `,
})
export class PropertyDetailPage implements OnInit {
  private readonly api = inject(PropertiesApi);
  private readonly toast = inject(ToastService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly id = input.required<string>();

  protected readonly area = area;
  protected readonly detail = signal<PropertyDetail | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly editing = signal(false);
  protected readonly deleting = signal(false);
  protected readonly busy = signal(false);
  protected readonly canManage = computed(() => this.auth.hasPermission(Permission.propertiesManage));
  protected readonly canCreateAssignment = computed(() => this.auth.hasPermission(Permission.assignmentsManage));
  protected readonly type = computed(() => propertyType(this.detail()!.property.type));
  protected readonly usage = computed(() => usageLabel(this.detail()!.property.usage));
  protected readonly maps = computed(() => mapsUrl(this.detail()!.property));
  protected readonly place = computed(() => {
    const p = this.detail()!.property;
    return [p.city, p.state].filter((x) => !!x).join(', ') || p.address;
  });

  async ngOnInit(): Promise<void> {
    try {
      this.detail.set(await firstValueFrom(this.api.get(this.id())));
    } catch (e) {
      this.error.set(toProblem(e).status === 404 ? 'This property no longer exists.' : toProblem(e).message);
    }
  }

  protected onSaved(p: Property): void {
    this.editing.set(false);
    this.detail.update((d) => (d ? { ...d, property: p } : d));
    this.toast.ok('Property updated', p.code);
  }

  protected async remove(): Promise<void> {
    this.busy.set(true);
    try {
      await firstValueFrom(this.api.delete(this.id()));
      this.toast.ok('Property removed');
      await this.router.navigateByUrl('/properties');
    } catch (e) {
      this.toast.error('Could not remove property', toProblem(e).message);
      this.deleting.set(false);
    } finally {
      this.busy.set(false);
    }
  }
}
