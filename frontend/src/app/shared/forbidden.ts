import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'vp-forbidden',
  imports: [RouterLink],
  template: `
    <div class="card">
      <i class="fa-solid fa-lock"></i>
      <h2>You don't have access to this page</h2>
      <p>Ask your administrator to grant your role access in User Management.</p>
      <a routerLink="/dashboard">Back to dashboard</a>
    </div>
  `,
  styles: `
    .card { max-width: 480px; margin: 48px auto; padding: 32px; text-align: center; background: var(--card);
      border: 1px solid var(--line); border-radius: var(--radius-lg); }
    i { font-size: 28px; color: var(--muted-2); }
    h2 { margin: 12px 0 8px; font-size: 20px; }
    p { margin: 0; color: var(--muted); }
    a { display: inline-block; margin-top: 18px; color: var(--brand); font-weight: 500; }
  `,
})
export class Forbidden {}
