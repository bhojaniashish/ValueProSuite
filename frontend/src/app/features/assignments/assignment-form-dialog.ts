import { Component, computed, effect, inject, input, OnInit, output, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Dialog } from '../../core/ui/dialog';
import { Choice } from '../../shared/chips';
import { Typeahead, TypeaheadOption } from '../../shared/typeahead';
import { clientType } from '../clients/clients.api';
import { PropertiesApi, propertyType } from '../properties/properties.api';
import { AssignmentDetail, AssignmentListItem, AssignmentsApi, CASE_TYPES, CaseType, Lookups, Priority, PRIORITIES, PURPOSES, SaveAssignment, shortDate, statusMeta } from './assignments.api';

/** Today in India as yyyy-mm-dd (due dates are the firm's calendar days). */
export function todayIso(offsetDays = 0): string {
  const d = new Date(Date.now() + 5.5 * 3600_000 + offsetDays * 86_400_000);
  return d.toISOString().slice(0, 10);
}

@Component({
  selector: 'vp-assignment-form-dialog',
  imports: [Dialog, ReactiveFormsModule, Typeahead, Choice],
  template: `
    <vp-dialog [title]="detail() ? 'Edit Assignment' : 'New Assignment'"
      [subtitle]="detail() ? detail()!.assignment.number : 'Create a valuation assignment'" icon="fa-clipboard-list" (closed)="closed.emit()">
      @if (!lookups()) {
        <div class="muted"><i class="fa-solid fa-spinner fa-spin"></i> Loading…</div>
      } @else {
        <form id="asgForm" class="form-grid" [formGroup]="form" (ngSubmit)="save()" novalidate>
          @if (error()) { <div class="alert-inline danger col-2"><i class="fa-solid fa-circle-exclamation"></i> {{ error() }}</div> }
          @if (!lookups()!.banks.length) {
            <div class="alert-inline info col-2"><i class="fa-solid fa-circle-info"></i> Add an active bank under Masters → Banks first.</div>
          }

          <div class="fld">
            <label>Assignment Number</label>
            <input [value]="detail()?.assignment?.number ?? lookups()!.numberPreview + ' (on save)'" disabled class="tnum" />
          </div>
          <div class="fld">
            <label>Priority</label>
            <div class="seg" role="radiogroup" aria-label="Priority">
              @for (p of priorities; track p.value) {
                <label [class.on]="v().priority === p.value" [class]="'p-' + p.value"><input type="radio" formControlName="priority" [value]="p.value" [attr.aria-label]="p.label" />{{ p.label }}</label>
              }
            </div>
          </div>

          <div class="fld" [class.invalid]="bad('bankId')">
            <label for="abank">Bank <span class="req">*</span></label>
            <select id="abank" formControlName="bankId">
              <option value="">Choose bank</option>
              @for (b of lookups()!.banks; track b.id) { <option [value]="b.id">{{ b.name }} ({{ b.shortCode }})</option> }
            </select>
            @if (bad('bankId')) { <span class="err">{{ server()['bankId'] ?? 'Choose the bank.' }}</span> }
          </div>
          <div class="fld" [class.invalid]="bad('branchId')">
            <label for="abranch">Branch</label>
            <select id="abranch" formControlName="branchId" [attr.disabled]="!branches().length ? '' : null">
              <option value="">{{ v().bankId ? (branches().length ? 'Choose branch' : 'No branches added for this bank') : 'Choose the bank first' }}</option>
              @for (b of branches(); track b.id) { <option [value]="b.id">{{ b.name }}{{ b.city ? ' · ' + b.city : '' }}</option> }
            </select>
            @if (server()['branchId']) { <span class="err">{{ server()['branchId'] }}</span> }
          </div>
          <div class="fld"><label for="aref">Bank Reference</label><input id="aref" formControlName="bankReference" placeholder="Loan / case no." /></div>
          <div class="fld">
            <label for="apurpose">Purpose</label>
            <input id="apurpose" formControlName="purpose" list="purposes" placeholder="Home loan" />
            <datalist id="purposes">@for (p of purposes; track p) { <option [value]="p"></option> }</datalist>
          </div>

          <div class="fld col-2">
            <span class="lbl">Case type</span>
            <vp-choice label="Case type" [options]="caseTypes" [value]="v().caseType" (valueChange)="setCaseType($event)" />
            @if (v().caseType !== 'Valuation') {
              <span class="hint">A visit during construction: stage-wise progress and the amount the bank can release.</span>
            }
          </div>

          <div class="fld col-2" [class.invalid]="bad('applicantName')">
            <label for="applicant">Applicant <span class="req">*</span></label>
            <vp-typeahead inputId="applicant" placeholder="Type a name, mobile or client code"
              [search]="searchClients" hint="No matching client. The name will be used as typed."
              [(text)]="applicantText" [(pickedId)]="clientId" />
            @if (bad('applicantName')) { <span class="err">{{ server()['applicantName'] ?? 'Enter or pick the applicant.' }}</span> }
            @else { <span class="hint">{{ clientId() ? 'Linked to the client record.' : 'Pick an existing client to link it, or type a new name.' }}</span> }
          </div>
          <div class="fld col-2" [class.invalid]="bad('propertySummary')">
            <label for="property">Property <span class="req">*</span></label>
            <vp-typeahead inputId="property" placeholder="Type owner, address, survey no. or property code"
              [search]="searchProperties" hint="No matching property. The description will be used as typed."
              [(text)]="propertyText" [(pickedId)]="propertyId" />
            @if (bad('propertySummary')) { <span class="err">{{ server()['propertySummary'] ?? 'Describe or pick the property.' }}</span> }
            @else { <span class="hint">{{ propertyId() ? 'Linked to the property register.' : 'e.g. "Residential Flat, Vesu, Surat" — or pick one from Properties.' }}</span> }
          </div>

          @if (v().caseType !== 'Valuation') {
            <div class="fld col-2" [class.invalid]="!!server()['previousAssignmentId']">
              <label for="aprev">Follows the earlier case</label>
              @if (!propertyId()) {
                <span class="hint">Pick the property from the register above to choose its earlier case (original valuation or last stage visit).</span>
              } @else {
                <select id="aprev" formControlName="previousAssignmentId">
                  <option value="">None (first visit)</option>
                  @for (e of earlier(); track e.id) {
                    <option [value]="e.id">{{ e.number }} · {{ caseLabel(e) }} · {{ statusMeta(e.status).label }} · {{ shortDate(e.createdAt) }}</option>
                  }
                </select>
                @if (server()['previousAssignmentId']) { <span class="err">{{ server()['previousAssignmentId'] }}</span> }
                @else { <span class="hint">Its stage table and disbursements are carried forward.</span> }
              }
            </div>
          }

          <div class="fld" [class.invalid]="bad('valuerId')">
            <label for="avaluer">Assigned Valuer <span class="req">*</span></label>
            <select id="avaluer" formControlName="valuerId">
              <option value="">Choose valuer</option>
              @for (u of lookups()!.valuers; track u.id) { <option [value]="u.id">{{ u.name }} · {{ u.role }}</option> }
            </select>
            @if (bad('valuerId')) { <span class="err">{{ server()['valuerId'] ?? 'Choose the valuer.' }}</span> }
          </div>
          <div class="fld" [class.invalid]="bad('reviewerId')">
            <label for="areviewer">Reviewer</label>
            <select id="areviewer" formControlName="reviewerId">
              <option value="">Decide later</option>
              @for (u of reviewers(); track u.id) { <option [value]="u.id">{{ u.name }} · {{ u.role }}</option> }
            </select>
            @if (server()['reviewerId']) { <span class="err">{{ server()['reviewerId'] }}</span> }
          </div>
          <div class="fld" [class.invalid]="bad('dueDate')">
            <label for="adue">Due Date <span class="req">*</span></label>
            <input id="adue" type="date" formControlName="dueDate" [min]="detail() ? null : today" />
            @if (bad('dueDate')) { <span class="err">{{ server()['dueDate'] ?? 'Choose a due date from today on.' }}</span> }
          </div>
          <div class="fld" [class.invalid]="bad('fee')">
            <label for="afee">Valuation Fee</label>
            <div class="fld-pre"><span>₹</span><input id="afee" type="number" min="0" step="100" formControlName="fee" /></div>
            <span class="hint">Before GST. Invoices come with Billing.</span>
          </div>
          <div class="fld col-2"><label for="aremarks">Remarks</label><textarea id="aremarks" rows="2" formControlName="remarks"></textarea></div>
        </form>
      }
      <ng-container footer>
        <button class="btn btn-ghost" type="button" (click)="closed.emit()">Cancel</button>
        <button class="btn btn-pri" type="submit" form="asgForm" [disabled]="busy() || !lookups()?.banks?.length">
          <i class="fa-solid" [class]="busy() ? 'fa-spinner fa-spin' : 'fa-check'"></i> {{ detail() ? 'Save' : 'Create Assignment' }}
        </button>
      </ng-container>
    </vp-dialog>
  `,
  styles: `
    .seg { display: flex; gap: 6px; }
    .seg label { position: relative; flex: 1; display: grid; place-items: center; height: 40px; border: 1px solid var(--line); border-radius: 9px;
      font-size: 13px !important; font-weight: 600; color: var(--ink-2) !important; cursor: pointer; }
    .seg input { position: absolute; inset: 0; opacity: 0; margin: 0; cursor: pointer; }
    .seg label:has(input:focus-visible) { outline: 2px solid var(--brand); outline-offset: 2px; }
    .seg .on.p-High { border-color: var(--danger); background: var(--danger-soft); color: var(--danger) !important; }
    .seg .on.p-Medium { border-color: var(--warn); background: var(--warn-soft); color: var(--warn) !important; }
    .seg .on.p-Low { border-color: var(--muted-2); background: var(--bg-2); }
    .err { font-size: 12px; color: var(--danger); }
  `,
})
export class AssignmentFormDialog implements OnInit {
  private readonly api = inject(AssignmentsApi);
  private readonly properties = inject(PropertiesApi);

  readonly detail = input<AssignmentDetail | null>(null);
  /** Pre-select a property (e.g. "New Assignment" from a property page). */
  readonly presetPropertyId = input<string | null>(null);
  readonly saved = output<AssignmentDetail>();
  readonly closed = output<void>();

  protected readonly priorities = PRIORITIES;
  protected readonly purposes = PURPOSES;
  protected readonly today = todayIso();
  protected readonly lookups = signal<Lookups | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly server = signal<Record<string, string>>({});

  protected readonly applicantText = signal('');
  protected readonly clientId = signal<string | null>(null);
  protected readonly propertyText = signal('');
  protected readonly propertyId = signal<string | null>(null);

  protected readonly form = inject(NonNullableFormBuilder).group({
    priority: ['Medium' as Priority],
    bankId: ['', Validators.required],
    branchId: [''],
    bankReference: ['', Validators.maxLength(60)],
    purpose: ['', Validators.maxLength(100)],
    valuerId: ['', Validators.required],
    reviewerId: [''],
    dueDate: [todayIso(7), Validators.required],
    fee: [0, [Validators.min(0)]],
    remarks: ['', Validators.maxLength(1000)],
    caseType: ['Valuation' as CaseType],
    previousAssignmentId: [''],
  });

  protected readonly caseTypes = CASE_TYPES.map((c) => ({ value: c.value, label: c.label }));
  protected readonly statusMeta = statusMeta;
  protected readonly shortDate = shortDate;
  /** Earlier cases on the chosen property, to link a stage visit / re-inspection to. */
  protected readonly earlier = signal<AssignmentListItem[]>([]);
  private earlierFor: string | null = null;
  /** Picking another property reloads its earlier cases (stage visits only). */
  private readonly watchProperty = effect(() => {
    const pid = this.propertyId();
    if (pid && this.v().caseType !== 'Valuation') void this.loadEarlier();
    if (!pid) this.earlier.set([]);
  });

  protected readonly v = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() }) as () => ReturnType<typeof this.form.getRawValue>;
  protected readonly branches = computed(() => this.lookups()?.banks.find((b) => b.id === this.v().bankId)?.branches ?? []);
  protected readonly reviewers = computed(() => (this.lookups()?.reviewers ?? []).filter((r) => r.id !== this.v().valuerId));

  protected readonly searchClients = async (q: string): Promise<TypeaheadOption[]> =>
    (await firstValueFrom(this.api.searchClients(q))).map((c) => ({
      id: c.id, label: c.name, badge: clientType(c.type).label, sub: [c.code, c.mobile, c.city].filter(Boolean).join(' · '),
    }));

  protected readonly searchProperties = async (q: string): Promise<TypeaheadOption[]> =>
    (await firstValueFrom(this.api.searchProperties(q))).map((p) => ({
      id: p.id, label: p.summary, badge: p.code, sub: `${propertyType(p.type).label} · Owner: ${p.ownerName}`,
    }));

  async ngOnInit(): Promise<void> {
    // Changing bank clears a branch of the old bank.
    this.form.controls.bankId.valueChanges.subscribe(() => this.form.controls.branchId.setValue(''));
    this.form.valueChanges.subscribe(() => this.server.set({}));

    try {
      this.lookups.set(await firstValueFrom(this.api.lookups()));
    } catch (e) {
      this.error.set(toProblem(e).message);
      return;
    }

    const d = this.detail();
    if (d) {
      const a = d.assignment;
      this.form.setValue({
        priority: a.priority, bankId: a.bankId, branchId: '', bankReference: d.bankReference ?? '', purpose: d.purpose ?? '',
        valuerId: a.valuerId, reviewerId: d.reviewerId ?? '', dueDate: a.dueDate, fee: a.fee, remarks: d.remarks ?? '',
        caseType: a.caseType ?? 'Valuation', previousAssignmentId: d.previousAssignmentId ?? '',
      });
      this.form.controls.branchId.setValue(a.branchId ?? '');
      this.applicantText.set(a.applicantName);
      this.clientId.set(a.clientId);
      this.propertyText.set(a.propertySummary);
      this.propertyId.set(a.propertyId);
      if (a.caseType && a.caseType !== 'Valuation') void this.loadEarlier();
    } else {
      const l = this.lookups()!;
      if (l.banks.length === 1) this.form.controls.bankId.setValue(l.banks[0].id);
      const preset = this.presetPropertyId();
      if (preset) {
        try {
          const p = (await firstValueFrom(this.properties.get(preset))).property;
          this.propertyText.set([propertyType(p.type).label, p.address, p.city].filter((x) => !!x).join(', '));
          this.propertyId.set(p.id);
          this.applicantText.set(p.ownerName);
        } catch {
          /* property not visible or removed: leave the fields empty */
        }
      }
    }
  }

  protected setCaseType(t: string | null): void {
    this.form.controls.caseType.setValue((t ?? 'Valuation') as CaseType);
    if (t && t !== 'Valuation') void this.loadEarlier();
    else this.form.controls.previousAssignmentId.setValue('');
  }

  protected caseLabel(a: AssignmentListItem): string {
    return CASE_TYPES.find((c) => c.value === a.caseType)?.short ?? 'Valuation';
  }

  private async loadEarlier(): Promise<void> {
    const pid = this.propertyId();
    if (!pid || pid === this.earlierFor) return;
    this.earlierFor = pid;
    try {
      const page = await firstValueFrom(this.api.list({ propertyId: pid, sort: 'created', desc: true, page: 1, pageSize: 20 }));
      const self = this.detail()?.assignment.id;
      this.earlier.set(page.items.filter((a) => a.id !== self));
      // A new stage visit usually follows the latest case on the property.
      if (!this.detail() && !this.form.controls.previousAssignmentId.value && this.earlier().length)
        this.form.controls.previousAssignmentId.setValue(this.earlier()[0].id);
    } catch {
      this.earlier.set([]);
    }
  }

  protected bad(name: string): boolean {
    if (this.server()[name]) return true;
    if (name === 'applicantName') return this.touched() && !this.applicantText().trim();
    if (name === 'propertySummary') return this.touched() && !this.propertyText().trim();
    const c = this.form.get(name);
    return !!c && c.touched && c.invalid;
  }

  private readonly touched = signal(false);

  protected async save(): Promise<void> {
    this.touched.set(true);
    if (this.form.invalid || !this.applicantText().trim() || !this.propertyText().trim()) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const blank = (s: string) => s.trim() || null;
    const body: SaveAssignment = {
      bankId: v.bankId,
      branchId: v.branchId || null,
      bankReference: blank(v.bankReference),
      purpose: blank(v.purpose),
      applicantName: this.applicantText().trim(),
      clientId: this.clientId(),
      applicantPhone: null,
      propertySummary: this.propertyText().trim(),
      propertyId: this.propertyId(),
      valuerId: v.valuerId,
      reviewerId: v.reviewerId || null,
      priority: v.priority,
      dueDate: v.dueDate,
      siteVisitAt: null,
      fee: Number(v.fee) || 0,
      remarks: blank(v.remarks),
      caseType: v.caseType,
      previousAssignmentId: v.caseType === 'Valuation' ? null : v.previousAssignmentId || null,
    };
    this.busy.set(true);
    this.error.set(null);
    try {
      const d = this.detail();
      this.saved.emit(await firstValueFrom(d ? this.api.update(d.assignment.id, body) : this.api.create(body)));
    } catch (e) {
      const p = toProblem(e);
      this.server.set(p.fields);
      if (!Object.keys(p.fields).length) this.error.set(p.message);
    } finally {
      this.busy.set(false);
    }
  }
}
