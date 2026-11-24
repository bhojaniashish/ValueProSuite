import { Component, computed, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Permission } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { Dialog } from '../../core/ui/dialog';
import { ToastService } from '../../core/ui/toast';
import { Paged } from '../users/users.api';
import { area, PROPERTY_TYPES, PropertiesApi, Property, PropertyFilters, PropertyQuery, propertyType } from './properties.api';
import { PropertyFormDialog } from './property-form-dialog';

type SortKey = 'code' | 'type' | 'owner' | 'city' | 'land';

@Component({
  selector: 'vp-properties-page',
  imports: [RouterLink, PropertyFormDialog, Dialog],
  template: `
    <div class="page-head">
      <div>
        <h2>Property Management</h2>
        <p>{{ filters()?.total ?? '–' }} propert{{ filters()?.total === 1 ? 'y' : 'ies' }} · survey, CTS, plot &amp; GPS records</p>
      </div>
      @if (canManage()) {
        <div class="actions"><button class="btn btn-pri" type="button" (click)="editing.set(null)"><i class="fa-solid fa-plus"></i> Add Property</button></div>
      }
    </div>

    <div class="card">
      <div class="toolbar">
        <div class="search">
          <i class="fa-solid fa-magnifying-glass"></i>
          <input type="search" placeholder="Search code, owner, address, survey / CTS / plot no." aria-label="Search properties"
            [value]="query().search" (input)="onSearch($any($event.target).value)" />
        </div>
        <select aria-label="Filter by type" [value]="query().type" (change)="patch({ type: $any($event.target).value, page: 1 })">
          <option value="">All Types</option>@for (t of types; track t.value) { <option [value]="t.value">{{ t.label }}</option> }
        </select>
        <select aria-label="Filter by city" [value]="query().city" (change)="patch({ city: $any($event.target).value, page: 1 })">
          <option value="">All Cities</option>@for (c of filters()?.cities ?? []; track c) { <option [value]="c">{{ c }}</option> }
        </select>
        <select aria-label="Filter by zone" [value]="query().zone" (change)="patch({ zone: $any($event.target).value, page: 1 })">
          <option value="">All Zones</option>@for (z of filters()?.zones ?? []; track z) { <option [value]="z">{{ z }}</option> }
        </select>
        @if (filtered()) { <button class="btn btn-ghost btn-sm" type="button" (click)="patch({ search: '', type: '', city: '', zone: '', page: 1 })"><i class="fa-solid fa-xmark"></i> Clear</button> }
        @if (loading()) { <i class="fa-solid fa-spinner fa-spin muted spin"></i> }
      </div>

      @if (loadError()) {
        <div class="empty"><i class="fa-solid fa-triangle-exclamation"></i><b>Could not load properties</b><span>{{ loadError() }}</span>
          <button class="btn btn-soft btn-sm" type="button" (click)="load()">Try again</button></div>
      } @else if (page(); as p) {
        @if (!p.items.length) {
          <div class="empty">
            <i class="fa-solid fa-map-location-dot"></i>
            <b>{{ filtered() ? 'No properties match' : 'No properties yet' }}</b>
            <span>{{ filtered() ? 'Try a different search or filter.' : 'Add a property here, or when creating an assignment.' }}</span>
          </div>
        } @else {
          <div class="table-wrap">
            <table class="data" [class.dim]="loading()">
              <thead>
                <tr>
                  <th class="sortable" (click)="sortBy('code')">Property <i class="fa-solid" [class]="sortIcon('code')"></i></th>
                  <th class="sortable" (click)="sortBy('owner')">Owner <i class="fa-solid" [class]="sortIcon('owner')"></i></th>
                  <th class="hide-md">Survey / CTS</th>
                  <th class="sortable hide-sm" (click)="sortBy('city')">Location <i class="fa-solid" [class]="sortIcon('city')"></i></th>
                  <th class="sortable hide-md" (click)="sortBy('land')">Land / Built-up <i class="fa-solid" [class]="sortIcon('land')"></i></th>
                  <th class="hide-md">Zone</th>
                  <th class="right" aria-label="Actions"></th>
                </tr>
              </thead>
              <tbody>
                @for (x of p.items; track x.id) {
                  <tr>
                    <td>
                      <a class="who link" [routerLink]="['/properties', x.id]">
                        <span class="ptype"><i class="fa-solid" [class]="type(x.type).icon"></i></span>
                        <div><b>{{ x.code }}</b><span class="small muted">{{ type(x.type).label }}</span></div>
                      </a>
                    </td>
                    <td>{{ x.ownerName }}</td>
                    <td class="hide-md"><span class="small tnum">{{ x.surveyNo || '—' }}</span><span class="small muted tnum block">{{ x.ctsNo || x.plotNo || '' }}</span></td>
                    <td class="hide-sm">{{ x.city || '—' }}<span class="small muted block">{{ x.state }}</span></td>
                    <td class="hide-md"><span class="small tnum">{{ area(x.landArea) }}</span><span class="small muted tnum block">{{ x.builtUpArea ? area(x.builtUpArea) : '—' }}</span></td>
                    <td class="hide-md">@if (x.zone) { <span class="badge b-gray">{{ x.zone }}</span> } @else { — }</td>
                    <td class="right nowrap">
                      <a class="btn-icon" [routerLink]="['/properties', x.id]" [attr.aria-label]="'View ' + x.code"><i class="fa-solid fa-eye"></i></a>
                      @if (canManage()) {
                        <button class="btn-icon" type="button" [attr.aria-label]="'Edit ' + x.code" (click)="editing.set(x)"><i class="fa-solid fa-pen"></i></button>
                        <button class="btn-icon danger" type="button" [attr.aria-label]="'Remove ' + x.code" (click)="deleting.set(x)"><i class="fa-solid fa-trash-can"></i></button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <div class="pager">
            <span class="small muted">{{ range() }}</span>
            <div class="pg">
              <button class="btn-icon" type="button" aria-label="Previous page" [disabled]="p.page <= 1" (click)="patch({ page: p.page - 1 })"><i class="fa-solid fa-chevron-left"></i></button>
              <span class="small tnum">Page {{ p.page }} of {{ pages() }}</span>
              <button class="btn-icon" type="button" aria-label="Next page" [disabled]="p.page >= pages()" (click)="patch({ page: p.page + 1 })"><i class="fa-solid fa-chevron-right"></i></button>
            </div>
          </div>
        }
      } @else {
        <div class="empty"><i class="fa-solid fa-spinner fa-spin"></i><span>Loading properties…</span></div>
      }
    </div>

    @if (editing() !== undefined) {
      <vp-property-form-dialog [property]="editing() ?? null" [zones]="filters()?.zones ?? []" (saved)="onSaved($event)" (closed)="editing.set(undefined)" />
    }
    @if (deleting(); as x) {
      <vp-dialog title="Remove property?" [subtitle]="x.code + ' · ' + x.ownerName" icon="fa-trash-can" tone="b-danger" size="sm" (closed)="deleting.set(null)">
        <p class="confirm">{{ x.code }} will be removed from the property register. Its past assignments keep their details.</p>
        <ng-container footer>
          <button class="btn btn-ghost" type="button" (click)="deleting.set(null)">Cancel</button>
          <button class="btn btn-danger" type="button" [disabled]="busy()" (click)="remove(x)"><i class="fa-solid fa-trash-can"></i> Remove</button>
        </ng-container>
      </vp-dialog>
    }
  `,
  styleUrl: './properties-page.scss',
})
export class PropertiesPage implements OnInit, OnDestroy {
  private readonly api = inject(PropertiesApi);
  private readonly toast = inject(ToastService);
  private readonly auth = inject(AuthService);

  protected readonly types = PROPERTY_TYPES;
  protected readonly type = propertyType;
  protected readonly area = area;
  protected readonly canManage = computed(() => this.auth.hasPermission(Permission.propertiesManage));
  protected readonly query = signal<PropertyQuery>({ page: 1, pageSize: 10, search: '', type: '', city: '', zone: '' });
  protected readonly page = signal<Paged<Property> | null>(null);
  protected readonly filters = signal<PropertyFilters | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly editing = signal<Property | null | undefined>(undefined);
  protected readonly deleting = signal<Property | null>(null);
  protected readonly busy = signal(false);

  protected readonly pages = computed(() => {
    const p = this.page();
    return p ? Math.max(1, Math.ceil(p.total / p.pageSize)) : 1;
  });
  protected readonly range = computed(() => {
    const p = this.page();
    if (!p || !p.total) return '0 properties';
    const from = (p.page - 1) * p.pageSize + 1;
    return `${from}–${Math.min(p.total, from + p.items.length - 1)} of ${p.total}`;
  });
  protected readonly filtered = computed(() => {
    const q = this.query();
    return !!(q.search || q.type || q.city || q.zone);
  });

  private timer?: ReturnType<typeof setTimeout>;
  private seq = 0;

  ngOnInit(): void {
    void this.load();
  }

  ngOnDestroy(): void {
    clearTimeout(this.timer);
  }

  protected onSearch(value: string): void {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.patch({ search: value.trim(), page: 1 }), 300);
  }

  protected sortBy(key: SortKey): void {
    const q = this.query();
    this.patch({ sort: key, desc: q.sort === key ? !q.desc : key === 'code', page: 1 });
  }

  protected sortIcon(key: SortKey): string {
    const q = this.query();
    const sort = q.sort ?? 'code';
    const desc = q.sort ? q.desc : true;
    return sort !== key ? 'fa-sort' : desc ? 'fa-sort-down' : 'fa-sort-up';
  }

  protected patch(change: Partial<PropertyQuery>): void {
    this.query.update((q) => ({ ...q, ...change }));
    void this.load();
  }

  protected async load(): Promise<void> {
    const seq = ++this.seq;
    this.loading.set(true);
    try {
      const [page, filters] = await Promise.all([firstValueFrom(this.api.list(this.query())), firstValueFrom(this.api.filters())]);
      if (seq !== this.seq) return;
      this.page.set(page);
      this.filters.set(filters);
      this.loadError.set(null);
    } catch (e) {
      if (seq === this.seq) this.loadError.set(toProblem(e).message);
    } finally {
      if (seq === this.seq) this.loading.set(false);
    }
  }

  protected onSaved(p: Property): void {
    const isNew = !this.editing();
    this.editing.set(undefined);
    this.toast.ok(isNew ? 'Property added' : 'Property updated', `${p.code} · ${p.ownerName}`);
    void this.load();
  }

  protected async remove(p: Property): Promise<void> {
    this.busy.set(true);
    try {
      await firstValueFrom(this.api.delete(p.id));
      this.deleting.set(null);
      this.toast.ok('Property removed', p.code);
      void this.load();
    } catch (e) {
      this.toast.error('Could not remove property', toProblem(e).message);
    } finally {
      this.busy.set(false);
    }
  }
}
