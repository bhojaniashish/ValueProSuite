import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Permission } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { Dialog } from '../../core/ui/dialog';
import { timeAgo } from '../../core/ui/format';
import { ToastService } from '../../core/ui/toast';
import { BankBadge } from '../banks/bank-badge';
import { BillingApi, InvoiceListItem, money, STATE_META } from '../billing/billing.api';
import { DocumentStrip } from '../documents/document-strip';
import { PhotoStrip } from '../photos/photo-strip';
import { AssignmentFormDialog } from './assignment-form-dialog';
import { AssignmentDetail, AssignmentsApi, AssignmentStatus, caseTypeLabel, priorityBadge, rupees, shortDate, STAGES, statusMeta } from './assignments.api';
import { MoveDialog, MoveKind } from './move-dialog';

@Component({
  selector: 'vp-assignment-detail-page',
  imports: [RouterLink, BankBadge, AssignmentFormDialog, MoveDialog, Dialog, PhotoStrip, DocumentStrip],
  templateUrl: './assignment-detail-page.html',
  styleUrl: './assignment-detail-page.scss',
})
export class AssignmentDetailPage implements OnInit {
  private readonly api = inject(AssignmentsApi);
  private readonly toast = inject(ToastService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly id = input.required<string>();

  protected readonly stages = STAGES;
  protected readonly meta = statusMeta;
  protected readonly date = shortDate;
  protected readonly rupees = rupees;
  protected readonly ago = timeAgo;
  protected readonly priorityBadge = priorityBadge;
  protected readonly detail = signal<AssignmentDetail | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly editing = signal(false);
  protected readonly deleting = signal(false);
  protected readonly move = signal<{ kind: MoveKind; to: AssignmentStatus } | null>(null);
  protected readonly canViewProperties = computed(() => this.auth.hasPermission(Permission.propertiesView));
  protected readonly canViewInspection = computed(() => this.auth.hasPermission(Permission.inspectionsView));
  protected readonly canViewValuation = computed(() => this.auth.hasPermission(Permission.valuationView));
  protected readonly caseTypeLabel = caseTypeLabel;
  protected readonly canViewReport = computed(() => this.auth.hasPermission(Permission.reportsView));
  protected readonly canViewBilling = computed(() => this.auth.hasPermission(Permission.billingView));
  protected readonly canBill = computed(() => this.auth.hasPermission(Permission.billingManage));
  private readonly billing = inject(BillingApi);
  protected readonly invoices = signal<InvoiceListItem[]>([]);
  protected readonly billingBusy = signal(false);
  protected readonly money = money;
  protected readonly invoiceState = STATE_META;
  /** A submitted case with no live invoice can be billed from here. */
  protected readonly canCreateInvoice = computed(() =>
    this.canBill() && this.a().status === 'Submitted' && !this.invoices().some((i) => i.status !== 'Cancelled'));

  protected readonly a = computed(() => this.detail()!.assignment);
  protected readonly stageIndex = computed(() => STAGES.indexOf(this.a().status));
  protected readonly cancelled = computed(() => this.a().status === 'Cancelled');

  async ngOnInit(): Promise<void> {
    try {
      this.detail.set(await firstValueFrom(this.api.get(this.id())));
      void this.loadInvoices();
    } catch (e) {
      this.error.set(toProblem(e).status === 404 ? 'This assignment no longer exists.' : toProblem(e).message);
    }
  }

  private async loadInvoices(): Promise<void> {
    if (!this.canViewBilling()) return;
    try {
      this.invoices.set((await firstValueFrom(this.billing.list({ assignmentId: this.id(), pageSize: 10 }))).items);
    } catch {
      this.invoices.set([]);
    }
  }

  protected async createInvoice(): Promise<void> {
    this.billingBusy.set(true);
    try {
      const inv = await firstValueFrom(this.billing.create([this.id()]));
      void this.router.navigate(['/billing', inv.id]);
    } catch (e) {
      this.toast.error('Could not create the invoice', toProblem(e).message);
    } finally {
      this.billingBusy.set(false);
    }
  }

  protected visitText(iso: string): string {
    return new Date(iso).toLocaleString('en-IN', { day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit', timeZone: 'Asia/Kolkata' });
  }

  protected stageState(i: number): 'done' | 'current' | 'todo' {
    const at = this.stageIndex();
    if (this.cancelled()) return 'todo';
    return i < at ? 'done' : i === at ? 'current' : 'todo';
  }

  /** Simple forward moves happen at once; scheduling a visit asks for the date. */
  protected async next(): Promise<void> {
    const d = this.detail()!;
    const to = d.moves.next!;
    if (to === 'SiteVisitScheduled') {
      this.move.set({ kind: 'forward', to });
      return;
    }
    this.busy.set(true);
    try {
      this.onMoved(await firstValueFrom(this.api.move(d.assignment.id, to, null)));
    } catch (e) {
      this.toast.error('Could not move the assignment', toProblem(e).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected onMoved(d: AssignmentDetail): void {
    this.move.set(null);
    this.detail.set(d);
    this.toast.ok(statusMeta(d.assignment.status).label, d.assignment.number);
  }

  protected onSaved(d: AssignmentDetail): void {
    this.editing.set(false);
    this.detail.set(d);
    this.toast.ok('Assignment updated', d.assignment.number);
  }

  protected async remove(): Promise<void> {
    this.busy.set(true);
    try {
      await firstValueFrom(this.api.delete(this.id()));
      this.toast.ok('Assignment deleted', this.a().number);
      await this.router.navigateByUrl('/assignments');
    } catch (e) {
      this.toast.error('Could not delete', toProblem(e).message);
      this.deleting.set(false);
    } finally {
      this.busy.set(false);
    }
  }
}
