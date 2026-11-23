import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { Toasts } from './core/ui/toast';

@Component({
  selector: 'vp-root',
  imports: [RouterOutlet, Toasts],
  template: '<router-outlet /><vp-toasts />',
})
export class App {}
