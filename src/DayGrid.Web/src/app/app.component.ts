import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

// AppComponent stays a one-line wrapper. The layout is chosen by the route table: public auth
// pages render in AuthLayoutComponent, signed-in pages in AppShellComponent (sidebar/topbar).
@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet],
  template: `<router-outlet></router-outlet>`
})
export class AppComponent {}
