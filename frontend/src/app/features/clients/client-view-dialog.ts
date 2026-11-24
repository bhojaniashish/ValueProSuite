import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Permission } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { Dialog } from '../../core/ui/dialog';
import { AssignmentMiniList } from '../assignments/assignment-mini-list';
import { AssignmentListItem, AssignmentsApi } from '../assignments/assignments.api';
import { Paged } from '../users/users.api';
import { Client, clientType } from './clients.api';

@Component({
  selector: 'vp-client-view-dialog',
  imports: [Dialog, AssignmentMiniList],
  template: `
    <vp-dialog [title]="client().name" [subtitle]="type().label + ' · ' + client().code" [icon]="type().icon" [tone]="type().tone" (closed)="closed.emit()">
      <div class="grid">
        @for (f of fields(); track f.label) {
          <div class="item">
            <span class="ic"><i class="fa-solid" [class]="f.icon"></i></span>
            <div>
              <span class="label">{{ f.label }}</span>
              @if (f.href) { <a [href]="f.href">{{ f.value }}</a> } @else { <b class="tnum">{{ f.value }}</b> }
            </div>
          </div>
        }
      </div>
      <div class="block">
        <span class="label">Address</span>
        <p>{{ address() || '—' }}</p>
      </div>
      <div class="block">
        <span class="label">Remarks</span>
        <p>{{ client().remarks || '—' }}</p>
      </div>
      @if (canSeeAssignments()) {
        <div class="block">
          <span class="label">Assignments @if (assignments(); as a) { · {{ a.total }} }</span>
          @if (assignments(); as a) {
            <vp-assignment-mini-list [items]="a.items" empty="No assignments for this client yet." />
            @if (a.total > a.items.length) {
              <p class="small muted">Showing the latest {{ a.items.length }} of {{ a.total }}. Search the client's name on the Assignments page for all.</p>
            }
          } @else {
            <p class="muted small"><i class="fa-solid fa-spinner fa-spin"></i> Loading…</p>
          }
        </div>
      }

      <ng-container footer>
        <button class="btn btn-ghost" type="button" (click)="closed.emit()">Close</button>
        @if (canEdit()) {
          <button class="btn btn-pri" type="button" (click)="edit.emit()"><i class="fa-solid fa-pen"></i> Edit</button>
        }
      </ng-container>
    </vp-dialog>
  `,
  styles: `
    .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 14px; }
    @media (max-width: 599px) { .grid { grid-template-columns: 1fr; } }
    .item { display: flex; gap: 12px; align-items: center; min-width: 0; }
    .item > div { display: flex; flex-direction: column; min-width: 0; }
    .item b, .item a { overflow-wrap: anywhere; font-weight: 600; }
    .ic { display: grid; place-items: center; flex: none; width: 36px; height: 36px; border-radius: 10px; background: var(--bg-2); color: var(--muted); }
    .label { font-size: 11.5px; font-weight: 600; text-transform: uppercase; letter-spacing: .04em; color: var(--muted); }
    .block { margin-top: 16px; padding-top: 14px; border-top: 1px solid var(--line-2); }
    .block p { margin: 4px 0 0; line-height: 1.55; }
  `,
})
export class ClientViewDialog implements OnInit {
  private readonly api = inject(AssignmentsApi);
  readonly client = input.required<Client>();
  readonly canEdit = input(false);
  readonly edit = output<void>();
  readonly closed = output<void>();

  private readonly auth = inject(AuthService);
  protected readonly canSeeAssignments = computed(() => this.auth.hasPermission(Permission.assignmentsView));
  protected readonly assignments = signal<Paged<AssignmentListItem> | null>(null);

  async ngOnInit(): Promise<void> {
    if (!this.canSeeAssignments()) return;
    try {
      this.assignments.set(await firstValueFrom(this.api.list({ clientId: this.client().id, sort: 'created', desc: true, page: 1, pageSize: 5 })));
    } catch {
      this.assignments.set({ items: [], total: 0, page: 1, pageSize: 5 });
    }
  }

  protected readonly type = computed(() => clientType(this.client().type));
  protected readonly address = computed(() => {
    const c = this.client();
    const tail = [c.city, c.state].filter(Boolean).join(', ');
    return [c.address, tail].filter(Boolean).join(', ') + (c.pincode ? ` - ${c.pincode}` : '');
  });
  protected readonly fields = computed(() => {
    const c = this.client();
    return [
      { label: 'Mobile', value: c.mobile, icon: 'fa-phone', href: `tel:${c.mobile.replace(/\s/g, '')}` },
      { label: 'Email', value: c.email ?? '—', icon: 'fa-envelope', href: c.email ? `mailto:${c.email}` : null },
      { label: 'PAN', value: c.pan ?? '—', icon: 'fa-id-card', href: null },
      { label: 'GST', value: c.gstin ?? 'Not registered', icon: 'fa-receipt', href: null },
      { label: 'City', value: c.city ?? '—', icon: 'fa-location-dot', href: null },
      { label: 'Pincode', value: c.pincode ?? '—', icon: 'fa-map-pin', href: null },
    ];
  });
}
