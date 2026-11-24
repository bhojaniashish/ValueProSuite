import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AssignmentListItem, shortDate, statusMeta } from './assignments.api';

/** Compact list of assignments, used on property and client pages. */
@Component({
  selector: 'vp-assignment-mini-list',
  imports: [RouterLink],
  template: `
    @if (!items().length) {
      <p class="none">{{ empty() }}</p>
    } @else {
      <ul>
        @for (a of items(); track a.id) {
          <li>
            <a [routerLink]="['/assignments', a.id]">
              <span class="num tnum">{{ a.number }}</span>
              <span class="who small muted">{{ a.bankShortCode }}{{ a.branchName ? ' · ' + a.branchName : '' }} · {{ a.applicantName }}</span>
            </a>
            <span class="badge" [class]="meta(a.status).badge">{{ meta(a.status).label }}</span>
            <span class="due small" [class.late]="a.isOverdue">{{ date(a.dueDate) }}</span>
          </li>
        }
      </ul>
    }
  `,
  styles: `
    .none { margin: 0; color: var(--muted); font-size: 13px; }
    ul { margin: 0; padding: 0; list-style: none; }
    li { display: flex; flex-wrap: wrap; align-items: center; gap: 6px 10px; padding: 10px 0; border-bottom: 1px solid var(--line-2); }
    li:last-child { border-bottom: none; }
    a { display: flex; flex: 1 1 220px; flex-direction: column; min-width: 0; color: inherit; text-decoration: none; }
    a:hover .num { color: var(--brand); }
    .num { font-weight: 700; }
    .due { min-width: 54px; text-align: right; color: var(--muted); }
    .due.late { color: var(--danger); font-weight: 600; }
  `,
})
export class AssignmentMiniList {
  readonly items = input.required<AssignmentListItem[]>();
  readonly empty = input('No assignments yet.');
  protected readonly meta = statusMeta;
  protected readonly date = shortDate;
}
