import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { ToastService } from '../../core/ui/toast';
import { NotificationSetting, SettingsApi } from './settings.api';

type Channel = 'email' | 'sms' | 'inApp';

/** Which events notify the team, and how. Sending starts with Phase 4; preferences are kept now. */
@Component({
  selector: 'vp-notifications-section',
  template: `
    <div class="card">
      <div class="card-head">
        <h3>Notification preferences</h3>
        <span class="sub">Choose how the team hears about each event. Delivery starts in Phase 4.</span>
      </div>
      <div class="table-wrap">
        <table class="data">
          <thead>
            <tr>
              <th>Event</th>
              @for (c of channels; track c.key) { <th class="center">{{ c.label }}</th> }
            </tr>
          </thead>
          <tbody>
            @for (n of rows(); track n.event) {
              <tr>
                <td>
                  <b>{{ n.label }}</b>
                  <span class="small muted desc">{{ n.description }}</span>
                </td>
                @for (c of channels; track c.key) {
                  <td class="center">
                    <span class="switch">
                      <input type="checkbox" [checked]="n[c.key]" [disabled]="!canEdit() || busy()"
                        (change)="toggle(n.event, c.key)" [attr.aria-label]="n.label + ' by ' + c.label" />
                      <span class="track"></span>
                    </span>
                  </td>
                }
              </tr>
            }
          </tbody>
        </table>
      </div>
      @if (canEdit()) {
        <div class="section-foot">
          @if (dirty()) { <span class="status">Unsaved changes</span> }
          <button class="btn btn-ghost" type="button" [disabled]="!dirty() || busy()" (click)="rows.set(value())">Discard</button>
          <button class="btn btn-pri" type="button" [disabled]="!dirty() || busy()" (click)="save()">
            <i class="fa-solid" [class]="busy() ? 'fa-spinner fa-spin' : 'fa-floppy-disk'"></i> Save Changes
          </button>
        </div>
      }
    </div>
  `,
  styles: `
    .center { text-align: center !important; width: 90px; }
    .desc { display: block; margin-top: 2px; }
    td .switch { vertical-align: middle; }
  `,
})
export class NotificationsSection {
  private readonly api = inject(SettingsApi);
  private readonly toast = inject(ToastService);

  readonly value = input.required<NotificationSetting[]>();
  readonly canEdit = input(false);
  readonly saved = output<NotificationSetting[]>();

  protected readonly channels: { key: Channel; label: string }[] = [
    { key: 'email', label: 'Email' },
    { key: 'sms', label: 'SMS' },
    { key: 'inApp', label: 'In-app' },
  ];
  protected readonly rows = signal<NotificationSetting[]>([]);
  protected readonly busy = signal(false);
  protected readonly dirty = computed(() => JSON.stringify(this.rows()) !== JSON.stringify(this.value()));

  constructor() {
    effect(() => this.rows.set(this.value()));
  }

  protected toggle(event: string, channel: Channel): void {
    this.rows.update((rows) => rows.map((r) => (r.event === event ? { ...r, [channel]: !r[channel] } : r)));
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    try {
      const prefs = this.rows().map(({ event, email, sms, inApp }) => ({ event, email, sms, inApp }));
      this.saved.emit(await firstValueFrom(this.api.updateNotifications(prefs)));
      this.toast.ok('Notification preferences saved');
    } catch (e) {
      this.toast.error('Could not save', toProblem(e).message);
    } finally {
      this.busy.set(false);
    }
  }
}
