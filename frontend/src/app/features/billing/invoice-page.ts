import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, input, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Permission } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { Dialog } from '../../core/ui/dialog';
import { ToastService } from '../../core/ui/toast';
import { AmountInput } from '../../shared/amount-input';
import { saveBlob } from '../photos/photos.api';
import {
  AddPayment, BillingApi, BillToOption, day, GST_RATES, GST_STATES, InvoiceDetail, money, PAYMENT_MODES, SaveInvoice, STATE_META, today,
} from './billing.api';

/** One invoice: edit and issue a draft; for an issued one, record payments (with TDS) or cancel it. */
@Component({
  selector: 'vp-invoice-page',
  imports: [FormsModule, RouterLink, Dialog, AmountInput],
  templateUrl: './invoice-page.html',
  styleUrl: './invoice-page.scss',
})
export class InvoicePage implements OnInit {
  private readonly api = inject(BillingApi);
  private readonly toast = inject(ToastService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);

  readonly id = input.required<string>();

  protected readonly money = money;
  protected readonly day = day;
  protected readonly stateMeta = STATE_META;
  protected readonly states = GST_STATES;
  protected readonly rates = GST_RATES;
  protected readonly modes = PAYMENT_MODES;
  protected readonly canManage = computed(() => this.auth.hasPermission(Permission.billingManage));

  protected readonly d = signal<InvoiceDetail | null>(null);
  protected readonly error = signal<string | null>(null);
  protected f!: SaveInvoice;
  protected readonly dirty = signal(false);
  protected readonly busy = signal<'save' | 'issue' | 'delete' | 'cancel' | 'pay' | null>(null);
  protected readonly fieldErrors = signal<Record<string, string>>({});
  protected readonly confirm = signal<'issue' | 'delete' | 'cancel' | null>(null);
  protected cancelReason = '';

  protected pay: AddPayment = this.blankPayment();
  protected readonly payError = signal<string | null>(null);
  protected readonly showPay = signal(false);

  protected readonly pdfUrl = signal<SafeResourceUrl | null>(null);
  protected readonly pdfLoading = signal(false);
  private rawUrl: string | null = null;

  /** Live totals while editing a draft (the server works out the same on save). */
  protected readonly live = computed(() => {
    this.tick();
    const d = this.d();
    if (!d || !this.f) return null;
    const taxable = this.f.lines.reduce((s, l) => s + (Number(l.amount) || 0), 0);
    const inter = !!d.firm.stateCode && !!this.f.billToStateCode && d.firm.stateCode !== this.f.billToStateCode;
    const gst = Math.round(taxable * this.f.gstRate) / 100;
    const half = Math.round((taxable * this.f.gstRate) / 2) / 100;
    const exact = inter ? taxable + gst : taxable + half * 2;
    return { taxable, inter, cgst: inter ? 0 : half, sgst: inter ? 0 : half, igst: inter ? gst : 0, total: Math.round(exact), roundOff: Math.round(exact) - exact };
  });
  private readonly tick = signal(0);

  constructor() {
    const warn = (e: BeforeUnloadEvent) => {
      if (this.dirty()) e.preventDefault();
    };
    window.addEventListener('beforeunload', warn);
    inject(DestroyRef).onDestroy(() => {
      window.removeEventListener('beforeunload', warn);
      if (this.rawUrl) URL.revokeObjectURL(this.rawUrl);
    });
  }

  async ngOnInit(): Promise<void> {
    try {
      this.apply(await firstValueFrom(this.api.get(this.id())));
    } catch (e) {
      this.error.set(toProblem(e).message);
    }
  }

  private apply(d: InvoiceDetail): void {
    this.d.set(d);
    this.f = {
      version: d.version, invoiceDate: d.invoiceDate, dueDate: d.dueDate, billTo: d.billTo, clientId: d.clientId, billToName: d.billToName,
      billToAddress: d.billToAddress, billToGstin: d.billToGstin, billToStateCode: d.billToStateCode, gstRate: d.gstRate,
      lines: d.lines.map((l) => ({ assignmentId: l.assignmentId, description: l.description, sac: l.sac, amount: l.amount })), notes: d.notes,
    };
    this.dirty.set(false);
    this.fieldErrors.set({});
    this.tick.update((t) => t + 1);
    void this.loadPdf();
  }

  protected changed(): void {
    this.dirty.set(true);
    this.tick.update((t) => t + 1);
  }

  protected caseNumber(assignmentId: string | null): string | null {
    return this.d()?.lines.find((l) => l.assignmentId === assignmentId)?.assignmentNumber ?? null;
  }

  protected useOption(o: BillToOption): void {
    this.f.billTo = o.billTo;
    this.f.clientId = o.clientId;
    this.f.billToName = o.name;
    this.f.billToAddress = o.address;
    this.f.billToGstin = o.gstin;
    this.f.billToStateCode = o.stateCode;
    this.changed();
  }

  protected isOption(o: BillToOption): boolean {
    return this.f.billTo === o.billTo && this.f.clientId === o.clientId && this.f.billToName === o.name;
  }

  /** A GSTIN tells the state: keep them in step. */
  protected gstinChanged(): void {
    const g = (this.f.billToGstin ?? '').trim().toUpperCase();
    this.f.billToGstin = g || null;
    if (/^[0-9]{2}/.test(g) && this.states.some((s) => s.code === g.slice(0, 2))) this.f.billToStateCode = g.slice(0, 2);
    this.changed();
  }

  protected addLine(): void {
    this.f.lines.push({ assignmentId: null, description: '', sac: this.f.lines[0]?.sac ?? '997222', amount: 0 });
    this.changed();
  }

  protected removeLine(i: number): void {
    this.f.lines.splice(i, 1);
    this.changed();
  }

  protected err(key: string): string | null {
    const e = this.fieldErrors();
    return e[key] ?? e[key.charAt(0).toUpperCase() + key.slice(1)] ?? null;
  }

  protected async save(): Promise<boolean> {
    this.busy.set('save');
    try {
      this.apply(await firstValueFrom(this.api.save(this.id(), {
        ...this.f, billToName: this.f.billToName.trim(),
        lines: this.f.lines.map((l) => ({ ...l, description: l.description.trim(), amount: Number(l.amount) || 0 })),
      })));
      return true;
    } catch (e) {
      const p = toProblem(e);
      this.fieldErrors.set(p.fields);
      this.toast.error(e instanceof HttpErrorResponse && e.status === 409 ? 'Changed elsewhere' : 'Could not save', Object.values(p.fields)[0] ?? p.message);
      return false;
    } finally {
      this.busy.set(null);
    }
  }

  protected async saveDraft(): Promise<void> {
    if (await this.save()) this.toast.ok('Draft saved');
  }

  protected async issue(): Promise<void> {
    if (this.dirty() && !(await this.save())) return;
    this.busy.set('issue');
    try {
      const d = await firstValueFrom(this.api.issue(this.id()));
      this.confirm.set(null);
      this.apply(d);
      this.toast.ok('Invoice issued', `${d.number}: ${money(d.total)}`);
    } catch (e) {
      this.toast.error('Could not issue', toProblem(e).message);
    } finally {
      this.busy.set(null);
    }
  }

  protected async remove(): Promise<void> {
    this.busy.set('delete');
    try {
      await firstValueFrom(this.api.remove(this.id()));
      this.dirty.set(false);
      this.toast.ok('Draft deleted');
      void this.router.navigate(['/billing']);
    } catch (e) {
      this.toast.error('Could not delete', toProblem(e).message);
    } finally {
      this.busy.set(null);
    }
  }

  protected async cancel(): Promise<void> {
    if (!this.cancelReason.trim()) return;
    this.busy.set('cancel');
    try {
      this.apply(await firstValueFrom(this.api.cancel(this.id(), this.cancelReason.trim())));
      this.confirm.set(null);
      this.toast.ok('Invoice cancelled', 'Its cases can be billed again.');
    } catch (e) {
      this.toast.error('Could not cancel', toProblem(e).message);
    } finally {
      this.busy.set(null);
    }
  }

  // ---------------- payments ----------------

  private blankPayment(): AddPayment {
    return { receivedOn: today(), amount: 0, tds: 0, mode: 'BankTransfer', reference: null, note: null };
  }

  protected openPay(): void {
    const d = this.d();
    this.pay = { ...this.blankPayment(), amount: d?.outstanding ?? 0 };
    this.payError.set(null);
    this.showPay.set(true);
  }

  /** Banks deduct 10% TDS on the fee (before GST) under section 194J. */
  protected tds10(): void {
    const d = this.d();
    if (!d) return;
    this.pay.tds = Math.round(d.taxable * 0.1);
    this.pay.amount = Math.max(0, Math.round((d.outstanding - this.pay.tds) * 100) / 100);
  }

  protected async addPayment(): Promise<void> {
    this.busy.set('pay');
    this.payError.set(null);
    try {
      this.apply(await firstValueFrom(this.api.addPayment(this.id(), { ...this.pay, amount: Number(this.pay.amount) || 0, tds: Number(this.pay.tds) || 0 })));
      this.showPay.set(false);
      this.toast.ok('Payment recorded');
    } catch (e) {
      const p = toProblem(e);
      this.payError.set(Object.values(p.fields)[0] ?? p.message);
    } finally {
      this.busy.set(null);
    }
  }

  protected async removePayment(paymentId: string): Promise<void> {
    try {
      this.apply(await firstValueFrom(this.api.removePayment(this.id(), paymentId)));
      this.toast.ok('Payment removed');
    } catch (e) {
      this.toast.error('Could not remove', toProblem(e).message);
    }
  }

  protected modeLabel(m: string): string {
    return this.modes.find((x) => x.value === m)?.label ?? m;
  }

  // ---------------- PDF ----------------

  private async loadPdf(): Promise<void> {
    this.pdfLoading.set(true);
    try {
      const blob = await firstValueFrom(this.api.pdf(this.id()));
      if (this.rawUrl) URL.revokeObjectURL(this.rawUrl);
      this.rawUrl = URL.createObjectURL(blob);
      this.pdfUrl.set(this.sanitizer.bypassSecurityTrustResourceUrl(this.rawUrl + '#view=FitH&toolbar=0'));
    } catch {
      this.pdfUrl.set(null);
    } finally {
      this.pdfLoading.set(false);
    }
  }

  protected async download(): Promise<void> {
    const d = this.d();
    if (!d) return;
    try {
      const blob = await firstValueFrom(this.api.pdf(this.id(), true));
      saveBlob(blob, `${(d.number ?? 'draft-invoice').replace(/\//g, '-')}.pdf`);
    } catch (e) {
      this.toast.error('Could not download', toProblem(e).message);
    }
  }

  protected stateName(code: string | null): string {
    return this.states.find((s) => s.code === code)?.name ?? '—';
  }
}
