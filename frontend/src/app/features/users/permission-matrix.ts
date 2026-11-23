import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { ToastService } from '../../core/ui/toast';
import { PermissionMatrix, UsersApi } from './users.api';

/**
 * Role × permission grid. Each module has View plus Manage/Approve; ticking Manage ticks View,
 * unticking View clears the rest. Admin is locked to full access. Saves only the roles that changed.
 */
@Component({
  selector: 'vp-permission-matrix',
  template: `
    <div class="card" id="permissions">
      <div class="card-head">
        <span class="ic b-brand"><i class="fa-solid fa-shield-halved"></i></span>
        <h3>Role Permission Matrix</h3>
        <span class="sub">{{ canEdit() ? 'Click cells to change access' : 'Read only' }}</span>
        @if (canEdit()) {
          <div class="ml actions">
            @if (dirtyRoles().length) {
              <span class="small muted">{{ dirtyRoles().length }} role(s) changed</span>
              <button class="btn btn-ghost btn-sm" type="button" (click)="reset()" [disabled]="saving()">Discard</button>
            }
            <button class="btn btn-pri btn-sm" type="button" (click)="save()" [disabled]="!dirtyRoles().length || saving()">
              <i class="fa-solid" [class]="saving() ? 'fa-spinner fa-spin' : 'fa-floppy-disk'"></i> Save
            </button>
          </div>
        }
      </div>

      @if (matrix(); as m) {
        <div class="table-wrap">
          <table class="matrix">
            <thead>
              <tr>
                <th class="mod">Module</th>
                @for (role of m.roles; track role) {
                  <th [class.changed]="dirtyRoles().includes(role)">
                    {{ role }}
                    @if (isLocked(role)) { <i class="fa-solid fa-lock" title="Always full access"></i> }
                  </th>
                }
              </tr>
            </thead>
            <tbody>
              @for (module of m.modules; track module.key) {
                @for (action of module.actions; track action.key; let first = $first) {
                  <tr [class.first]="first">
                    <td class="mod">
                      @if (first) { <b>{{ module.label }}</b> }
                      <span class="act">{{ action.label }}</span>
                    </td>
                    @for (role of m.roles; track role) {
                      <td>
                        <button
                          type="button"
                          class="cell"
                          [class.on]="has(role, action.key)"
                          [disabled]="!canEdit() || isLocked(role) || saving()"
                          (click)="toggle(role, module.key, action.key)"
                          [attr.aria-pressed]="has(role, action.key)"
                          [attr.aria-label]="role + ': ' + module.label + ' ' + action.label"
                        >
                          <i class="fa-solid" [class]="has(role, action.key) ? 'fa-check' : 'fa-minus'"></i>
                        </button>
                      </td>
                    }
                  </tr>
                }
              }
            </tbody>
          </table>
        </div>
        <p class="foot small muted">
          <i class="fa-solid fa-circle-info"></i> Manage and Approve include View. Changes apply to signed-in users within a minute.
        </p>
      } @else {
        <div class="card-pad muted">Loading permissions…</div>
      }
    </div>
  `,
  styles: `
    .ic { display: grid; place-items: center; width: 34px; height: 34px; border-radius: 10px; font-size: 14px; }
    .actions { display: flex; align-items: center; gap: 8px; }
    table.matrix { width: 100%; border-collapse: collapse; min-width: 760px; }
    th, td { padding: 6px 8px; text-align: center; border-bottom: 1px solid var(--line-2); }
    th { padding: 12px 8px; font-size: 11.5px; font-weight: 600; color: var(--muted); text-transform: uppercase;
      letter-spacing: .03em; background: #fafbfe; border-bottom: 1px solid var(--line); }
    th i { margin-left: 4px; font-size: 10px; }
    th.changed { color: var(--brand); box-shadow: inset 0 -2px 0 var(--brand); }
    .mod { position: sticky; left: 0; z-index: 1; text-align: left; background: #fff; min-width: 170px; }
    th.mod { background: #fafbfe; }
    tr.first td { border-top: 1px solid var(--line); }
    tbody tr:first-child td { border-top: none; }
    td.mod b { display: block; font-size: 13.5px; }
    td.mod .act { font-size: 12px; color: var(--muted); }
    .cell { width: 30px; height: 30px; border-radius: 8px; font-size: 12px; color: var(--muted-2); background: var(--bg-2);
      transition: .12s var(--ease); }
    .cell.on { background: var(--ok-soft); color: var(--ok); }
    .cell:not(:disabled):hover { box-shadow: 0 0 0 2px var(--brand-soft); }
    .cell:disabled { cursor: default; opacity: .75; }
    .foot { margin: 0; padding: 12px 20px; border-top: 1px solid var(--line-2); }
  `,
})
export class PermissionMatrixCard implements OnInit {
  private readonly api = inject(UsersApi);
  private readonly toast = inject(ToastService);

  readonly canEdit = input(false);
  /** Emitted after a save, so the page can refresh the signed-in user's own permissions. */
  readonly changed = output<void>();

  protected readonly matrix = signal<PermissionMatrix | null>(null);
  private readonly original = signal<Record<string, string[]>>({});
  protected readonly saving = signal(false);

  protected readonly dirtyRoles = computed(() => {
    const m = this.matrix();
    if (!m) return [];
    const before = this.original();
    return m.roles.filter((r) => !sameSet(m.grants[r] ?? [], before[r] ?? []));
  });

  async ngOnInit(): Promise<void> {
    try {
      this.load(await firstValueFrom(this.api.matrix()));
    } catch (e) {
      this.toast.error('Could not load permissions', toProblem(e).message);
    }
  }

  protected isLocked(role: string): boolean {
    return this.matrix()?.lockedRoles.includes(role) ?? false;
  }

  protected has(role: string, permission: string): boolean {
    return this.matrix()?.grants[role]?.includes(permission) ?? false;
  }

  protected toggle(role: string, moduleKey: string, permission: string): void {
    const m = this.matrix();
    if (!m) return;
    const module = m.modules.find((x) => x.key === moduleKey)!;
    const view = module.actions[0].key;
    const set = new Set(m.grants[role] ?? []);

    if (set.has(permission)) {
      set.delete(permission);
      if (permission === view) module.actions.forEach((a) => set.delete(a.key)); // no Manage without View
    } else {
      set.add(permission);
      set.add(view);
    }
    this.matrix.set({ ...m, grants: { ...m.grants, [role]: [...set].sort() } });
  }

  protected reset(): void {
    const m = this.matrix();
    if (m) this.matrix.set({ ...m, grants: structuredClone(this.original()) });
  }

  protected async save(): Promise<void> {
    const m = this.matrix();
    if (!m) return;
    this.saving.set(true);
    try {
      let latest = m;
      for (const role of this.dirtyRoles()) {
        latest = await firstValueFrom(this.api.updateRole(role, m.grants[role] ?? []));
      }
      this.load(latest);
      this.toast.ok('Permissions saved', 'Signed-in users get the new access within a minute.');
      this.changed.emit();
    } catch (e) {
      this.toast.error('Could not save permissions', toProblem(e).message);
    } finally {
      this.saving.set(false);
    }
  }

  private load(m: PermissionMatrix): void {
    this.matrix.set(m);
    this.original.set(structuredClone(m.grants));
  }
}

function sameSet(a: string[], b: string[]): boolean {
  return a.length === b.length && a.every((x) => b.includes(x));
}
