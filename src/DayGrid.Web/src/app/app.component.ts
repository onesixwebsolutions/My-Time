import { Component } from '@angular/core';

import { AppShellComponent } from './layout/app-shell.component';

// Design choice: the sidebar/topbar chrome lives in its own AppShellComponent
// (layout/app-shell.component.ts) rather than being folded directly into
// AppComponent. AppComponent stays a one-line wrapper — this keeps the root
// of the app trivially testable and leaves room for a future auth/splash
// gate to sit outside the shell without touching layout code.
@Component({
  selector: 'app-root',
  standalone: true,
  imports: [AppShellComponent],
  template: `<app-shell></app-shell>`
})
export class AppComponent {}
