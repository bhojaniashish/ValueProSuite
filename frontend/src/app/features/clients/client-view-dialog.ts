import { Component, computed, input, output } from '@angular/core';
import { Dialog } from '../../core/ui/dialog';
import { Client, clientType } from './clients.api';

@Component({
  selector: 'vp-client-view-dialog',
  imports: [Dialog],
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
      <div class="block muted small"><i class="fa-solid fa-clipboard-list"></i> Assignments for this client will appear here once the Assignments module is built.</div>

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
export class ClientViewDialog {
  readonly client = input.required<Client>();
  readonly canEdit = input(false);
  readonly edit = output<void>();
  readonly closed = output<void>();

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
