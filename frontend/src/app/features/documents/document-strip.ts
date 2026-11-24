import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { CaseDocuments, DocumentsApi } from './documents.api';

/** The case's document checklist at a glance, with a link to its Documents page (shown on the assignment). */
@Component({
  selector: 'vp-document-strip',
  imports: [RouterLink],
  template: `
    <div class="top">
      <b><i class="fa-solid fa-folder-tree"></i> Documents</b>
      @if (c(); as c) {
        <span class="small muted">{{ c.documents.length }} recorded · checklist {{ done() }}/{{ c.checklist.length }}</span>
      }
      <a class="small link" [routerLink]="['/documents', assignmentId()]">Manage <i class="fa-solid fa-arrow-right"></i></a>
    </div>
    @if (c(); as c) {
      @if (pending().length) {
        <span class="small pending"><i class="fa-solid fa-circle-dot"></i> Pending: {{ pending().join(', ') }}</span>
      } @else if (c.checklist.length) {
        <span class="small ok"><i class="fa-solid fa-circle-check"></i> Checklist complete</span>
      }
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: 8px; }
    .top { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 12px; }
    .top b i { color: var(--brand); margin-right: 4px; }
    .link { margin-left: auto; color: var(--brand); font-weight: 600; text-decoration: none; }
    .pending { color: var(--warn); }
    .ok { color: var(--ok); }
  `,
})
export class DocumentStrip implements OnInit {
  private readonly api = inject(DocumentsApi);
  readonly assignmentId = input.required<string>();
  protected readonly c = signal<CaseDocuments | null>(null);
  protected readonly done = computed(() => (this.c()?.checklist ?? []).filter((k) => k.recorded || k.notProvided).length);
  protected readonly pending = computed(() => (this.c()?.checklist ?? []).filter((k) => !k.recorded && !k.notProvided).map((k) => k.label));

  async ngOnInit(): Promise<void> {
    try {
      this.c.set(await firstValueFrom(this.api.case(this.assignmentId())));
    } catch {
      this.c.set(null);
    }
  }
}
