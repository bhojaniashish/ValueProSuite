import { afterNextRender, Component, computed, effect, inject, Injector, input, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Permission } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { BrandingService } from '../../core/branding/branding.service';
import { BrandingSection } from './branding-section';
import { CommunicationSection } from './communication-section';
import { CompanySection } from './company-section';
import { GstSection } from './gst-section';
import { NotificationsSection } from './notifications-section';
import { Settings, SettingsApi } from './settings.api';

type Tab = 'company' | 'branding' | 'communication' | 'gst' | 'templates' | 'notifications';

const TABS: { key: Tab; label: string; icon: string }[] = [
  { key: 'company', label: 'Company', icon: 'fa-building' },
  { key: 'branding', label: 'Branding', icon: 'fa-palette' },
  { key: 'communication', label: 'Email & SMS', icon: 'fa-envelope' },
  { key: 'gst', label: 'GST', icon: 'fa-receipt' },
  { key: 'templates', label: 'Templates', icon: 'fa-file-lines' },
  { key: 'notifications', label: 'Notifications', icon: 'fa-bell' },
];

@Component({
  selector: 'vp-settings-page',
  imports: [CompanySection, BrandingSection, CommunicationSection, GstSection, NotificationsSection],
  template: `
    <div class="page-head">
      <div>
        <h2>Settings</h2>
        <p>Company profile, branding, integrations &amp; notification preferences</p>
      </div>
      @if (!canEdit()) { <span class="badge b-gray"><i class="fa-solid fa-lock"></i> Read only</span> }
    </div>

    <nav class="tabs" role="tablist" aria-label="Settings sections">
      @for (t of tabs; track t.key) {
        <button type="button" role="tab" [class.active]="current() === t.key" [attr.aria-selected]="current() === t.key" (click)="select(t.key)">
          <i class="fa-solid" [class]="t.icon"></i> {{ t.label }}
        </button>
      }
    </nav>

    @if (error()) {
      <div class="card card-pad"><div class="alert-inline danger"><i class="fa-solid fa-triangle-exclamation"></i> {{ error() }}</div></div>
    } @else if (settings(); as s) {
      @switch (current()) {
        @case ('company') { <vp-company-section [value]="s.company" [canEdit]="canEdit()" (saved)="patch({ company: $event }); brand.apply({ ...brand.branding(), firmName: $event.name })" /> }
        @case ('branding') { <vp-branding-section [value]="s.branding" [canEdit]="canEdit()" (saved)="patch({ branding: $event })" /> }
        @case ('communication') { <vp-communication-section [value]="s.communication" [canEdit]="canEdit()" (saved)="patch({ communication: $event })" /> }
        @case ('gst') { <vp-gst-section [value]="s.gst" [canEdit]="canEdit()" (saved)="patch({ gst: $event })" /> }
        @case ('notifications') { <vp-notifications-section [value]="s.notifications" [canEdit]="canEdit()" (saved)="patch({ notifications: $event })" /> }
        @case ('templates') {
          <div class="card later">
            <span class="ic"><i class="fa-solid fa-file-lines"></i></span>
            <h3>Report templates arrive in Phase 3</h3>
            <p>One default template per report type comes first; bank formats (SBI, HDFC and others) are added as templates when valuers share them.</p>
          </div>
        }
      }
    } @else {
      <div class="card card-pad muted"><i class="fa-solid fa-spinner fa-spin"></i> Loading settings…</div>
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: 18px; }
    .page-head { margin-bottom: 0; }
    .tabs { display: flex; gap: 4px; padding: 4px; overflow-x: auto; scrollbar-width: none; background: var(--card); border: 1px solid var(--line);
      border-radius: var(--radius); box-shadow: var(--shadow-sm); }
    .tabs button { display: flex; align-items: center; gap: 8px; flex: none; padding: 9px 14px; border-radius: 9px;
      font-size: 13.5px; font-weight: 600; color: var(--muted); white-space: nowrap; }
    .tabs button:hover { color: var(--brand); background: var(--bg-2); }
    .tabs button.active { color: #fff; background: var(--brand); }
    .later { padding: 40px 24px; text-align: center; }
    .later .ic { display: inline-grid; place-items: center; width: 52px; height: 52px; border-radius: 14px;
      background: var(--gold-soft); color: var(--gold); font-size: 22px; }
    .later h3 { margin: 14px 0 6px; }
    .later p { max-width: 460px; margin: 0 auto; color: var(--muted); line-height: 1.6; }
  `,
})
export class SettingsPage implements OnInit {
  private readonly api = inject(SettingsApi);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  protected readonly brand = inject(BrandingService);

  /** ?tab=… from the URL (bound by the router). */
  readonly tab = input<string>();

  protected readonly tabs = TABS;
  protected readonly current = computed<Tab>(() => (TABS.some((t) => t.key === this.tab()) ? (this.tab() as Tab) : 'company'));
  protected readonly canEdit = computed(() => this.auth.hasPermission(Permission.settingsManage));
  protected readonly settings = signal<Settings | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    // On narrow screens the tab bar scrolls; keep the selected tab visible.
    const injector = inject(Injector);
    effect(() => {
      this.current();
      afterNextRender(() => document.querySelector('.tabs .active')?.scrollIntoView({ block: 'nearest', inline: 'nearest' }), { injector });
    });
  }

  async ngOnInit(): Promise<void> {
    try {
      this.settings.set(await firstValueFrom(this.api.get()));
    } catch (e) {
      this.error.set(toProblem(e).message);
    }
  }

  protected select(tab: Tab): void {
    void this.router.navigate([], { queryParams: { tab: tab === 'company' ? null : tab }, replaceUrl: true });
  }

  protected patch(change: Partial<Settings>): void {
    this.settings.update((s) => (s ? { ...s, ...change } : s));
  }
}
