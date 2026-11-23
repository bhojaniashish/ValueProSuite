import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { NAVIGATION } from '../../layout/navigation';

/** Phase 0 landing page: greets the user and lists the modules their role can open. Full dashboard in Phase 4. */
@Component({
  selector: 'vp-dashboard',
  imports: [RouterLink],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard {
  protected readonly auth = inject(AuthService);

  protected readonly firstName = computed(() => this.auth.user()?.fullName.split(' ')[0] ?? '');

  protected readonly modules = computed(() =>
    NAVIGATION.flatMap((g) => g.items).filter(
      (i) => i.path !== 'dashboard' && this.auth.hasPermission(i.permission),
    ),
  );
}
