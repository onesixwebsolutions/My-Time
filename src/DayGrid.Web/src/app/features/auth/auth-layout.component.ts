import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

/** Minimal centered layout (no sidebar) for the public auth pages. */
@Component({
  selector: 'app-auth-layout',
  standalone: true,
  imports: [RouterOutlet],
  template: `
    <main class="flex min-h-screen flex-col items-center justify-center px-4 py-10">
      <div class="mb-6 flex items-center gap-2.5">
        <div
          class="grid h-9 w-9 place-items-center rounded-[10px] bg-gradient-to-br from-accent to-purple-500 shadow-[0_4px_12px_rgba(99,102,241,.35)]"
        >
          <svg viewBox="0 0 128 128" class="h-[23px] w-[23px]" fill="#ffffff" aria-hidden="true">
            <circle cx="64" cy="24" r="9.5" />
            <circle cx="98.64" cy="44" r="9.5" />
            <circle cx="98.64" cy="84" r="9.5" />
            <circle cx="64" cy="104" r="9.5" />
            <circle cx="29.36" cy="84" r="9.5" />
            <circle cx="29.36" cy="44" r="9.5" />
            <circle cx="64" cy="64" r="15" />
          </svg>
        </div>
        <div>
          <b class="block text-[17px] tracking-tight text-text">One Six</b>
          <span class="block text-[11px] font-medium text-muted">Checklist &amp; Timetable</span>
        </div>
      </div>
      <div class="w-full max-w-[400px] rounded-card border border-border bg-raised p-6 shadow-[var(--shadow)]">
        <router-outlet></router-outlet>
      </div>
    </main>
  `
})
export class AuthLayoutComponent {}
