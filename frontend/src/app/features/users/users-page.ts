import { Component, computed, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { toProblem } from '../../core/api/problem';
import { Permission } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { Dialog } from '../../core/ui/dialog';
import { avatarColor, initials, timeAgo } from '../../core/ui/format';
import { ToastService } from '../../core/ui/toast';
import { PermissionMatrixCard } from './permission-matrix';
import { TempPasswordDialog } from './temp-password-dialog';
import { UserFormDialog, UserSaved } from './user-form-dialog';
import { Paged, ROLE_BADGE, ROLES, USER_STATUSES, UserListItem, UserQuery, UsersApi, UserStatus, UserSummary } from './users.api';

type SortKey = 'name' | 'role' | 'status' | 'lastlogin';

@Component({
  selector: 'vp-users-page',
  imports: [UserFormDialog, TempPasswordDialog, PermissionMatrixCard, Dialog],
  templateUrl: './users-page.html',
  styleUrl: './users-page.scss',
})
export class UsersPage implements OnInit, OnDestroy {
  private readonly api = inject(UsersApi);
  private readonly toast = inject(ToastService);
  protected readonly auth = inject(AuthService);

  protected readonly roles = ROLES;
  protected readonly statuses = USER_STATUSES;
  protected readonly canManage = computed(() => this.auth.hasPermission(Permission.usersManage));

  protected readonly query = signal<UserQuery>({ page: 1, pageSize: 10, sort: 'name', desc: false, search: '', role: '', status: '' });
  protected readonly page = signal<Paged<UserListItem> | null>(null);
  protected readonly summary = signal<UserSummary | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);

  protected readonly pages = computed(() => {
    const p = this.page();
    return p ? Math.max(1, Math.ceil(p.total / p.pageSize)) : 1;
  });
  protected readonly range = computed(() => {
    const p = this.page();
    if (!p || p.total === 0) return '0 users';
    const from = (p.page - 1) * p.pageSize + 1;
    return `${from}–${Math.min(p.total, from + p.items.length - 1)} of ${p.total}`;
  });
  protected readonly filtered = computed(() => {
    const q = this.query();
    return !!(q.search || q.role || q.status);
  });

  // Dialog state
  protected readonly editing = signal<UserListItem | null | undefined>(undefined); // undefined = closed, null = new
  protected readonly confirmReset = signal<UserListItem | null>(null);
  protected readonly tempPassword = signal<{ title: string; name: string; email: string; password: string } | null>(null);
  protected readonly resetting = signal(false);

  private searchTimer?: ReturnType<typeof setTimeout>;
  private requestSeq = 0;

  ngOnInit(): void {
    void this.load();
  }

  ngOnDestroy(): void {
    clearTimeout(this.searchTimer);
  }

  // ----- helpers for the template -----
  protected readonly initials = initials;
  protected readonly avatarColor = avatarColor;
  protected readonly timeAgo = timeAgo;
  protected roleBadge(role: string): string { return ROLE_BADGE[role] ?? 'b-gray'; }
  protected status(s: UserStatus) { return USER_STATUSES.find((x) => x.value === s)!; }
  protected isSelf(u: UserListItem): boolean { return u.id === this.auth.user()?.id; }

  // ----- list -----
  protected onSearch(value: string): void {
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.patch({ search: value.trim(), page: 1 }), 300);
  }

  protected onFilter(key: 'role' | 'status', value: string): void {
    this.patch({ [key]: value, page: 1 });
  }

  protected clearFilters(): void {
    this.patch({ search: '', role: '', status: '', page: 1 });
  }

  protected sortBy(key: SortKey): void {
    const q = this.query();
    this.patch({ sort: key, desc: q.sort === key ? !q.desc : false, page: 1 });
  }

  protected sortIcon(key: SortKey): string {
    const q = this.query();
    return q.sort !== key ? 'fa-sort' : q.desc ? 'fa-sort-down' : 'fa-sort-up';
  }

  protected goTo(page: number): void {
    if (page >= 1 && page <= this.pages()) this.patch({ page });
  }

  private patch(change: Partial<UserQuery>): void {
    this.query.update((q) => ({ ...q, ...change }));
    void this.load();
  }

  protected async load(): Promise<void> {
    const seq = ++this.requestSeq; // ignore answers to older requests (fast typing)
    this.loading.set(true);
    try {
      const [page, summary] = await Promise.all([
        firstValueFrom(this.api.list(this.query())),
        firstValueFrom(this.api.summary()),
      ]);
      if (seq !== this.requestSeq) return;
      this.page.set(page);
      this.summary.set(summary);
      this.loadError.set(null);
    } catch (e) {
      if (seq === this.requestSeq) this.loadError.set(toProblem(e).message);
    } finally {
      if (seq === this.requestSeq) this.loading.set(false);
    }
  }

  // ----- add / edit -----
  protected onSaved(event: UserSaved): void {
    this.editing.set(undefined);
    if (event.kind === 'created') {
      const { user, temporaryPassword } = event.result;
      this.toast.ok('User added', `${user.fullName} can now sign in.`);
      this.tempPassword.set({ title: 'User created', name: user.fullName, email: user.email, password: temporaryPassword });
    } else {
      this.toast.ok('User updated', `${event.user.fullName} saved.`);
    }
    void this.load();
  }

  // ----- reset password -----
  protected async resetPassword(): Promise<void> {
    const user = this.confirmReset();
    if (!user) return;
    this.resetting.set(true);
    try {
      const res = await firstValueFrom(this.api.resetPassword(user.id));
      this.confirmReset.set(null);
      this.tempPassword.set({ title: 'Password reset', name: user.fullName, email: user.email, password: res.temporaryPassword });
    } catch (e) {
      this.toast.error('Could not reset password', toProblem(e).message);
    } finally {
      this.resetting.set(false);
    }
  }

  protected onPermissionsChanged(): void {
    void this.auth.refreshProfile();
  }
}
