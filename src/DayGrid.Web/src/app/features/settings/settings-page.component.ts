import { Component, OnInit, inject, signal } from '@angular/core';

import { AppSettings, SettingsApi } from '../../core/api/settings.api';
import { AuthService } from '../../core/auth/auth.service';

// Honest placeholder — email settings form and appearance settings land in
// Phase 6 / Phase 7 (plan section 7). Renders the real app_settings row today.
@Component({
  selector: 'app-settings-page',
  template: `
    <div class="mb-5">
      <h1 class="text-[22px] font-bold tracking-tight text-text">Settings</h1>
    </div>

    <div class="mb-5 rounded-card border border-dashed border-border bg-raised px-4 py-3.5 text-[12.2px] leading-relaxed text-muted">
      <b class="text-text">Coming in Phase 6 / Phase 7.</b>
      Email preferences (with a Send test email button) and appearance settings will replace this
      read-only view.
    </div>

    @if (settings(); as s) {
      <dl class="grid grid-cols-2 gap-x-6 gap-y-3 rounded-card border border-border bg-raised p-5 text-[13px]">
        <dt class="text-muted">Account</dt>
        <dd class="min-w-0 font-medium [overflow-wrap:anywhere]" data-testid="settings-account-email">{{ user()?.email }}</dd>
        <dt class="text-muted">Time zone</dt>
        <dd class="font-medium">{{ s.timeZone }}</dd>
        <dt class="text-muted">Day window</dt>
        <dd class="font-medium">{{ s.dayStart }} – {{ s.dayEnd }}</dd>
        <dt class="text-muted">Default slot size</dt>
        <dd class="font-medium">{{ s.defaultSlotMinutes }} min</dd>
        <dt class="text-muted">Email notifications</dt>
        <dd class="min-w-0 font-medium">
          {{ s.emailEnabled ? 'Enabled' : 'Disabled' }}
          @if (s.emailEnabled) {
            <span class="text-muted [overflow-wrap:anywhere]">· sent to {{ user()?.email }}</span>
          }
        </dd>
        <dt class="text-muted">Theme</dt>
        <dd class="font-medium">{{ s.theme }}</dd>
      </dl>
    } @else if (loadError()) {
      <p class="text-[13px] text-muted">{{ loadError() }}</p>
    } @else {
      <p class="text-[13px] text-muted">Loading settings…</p>
    }
  `
})
export class SettingsPageComponent implements OnInit {
  private readonly api = inject(SettingsApi);
  protected readonly user = inject(AuthService).currentUser;
  protected readonly settings = signal<AppSettings | null>(null);
  protected readonly loadError = signal<string | null>(null);

  ngOnInit(): void {
    this.api.get().subscribe({
      next: (s) => this.settings.set(s),
      error: () => this.loadError.set('Could not load settings.')
    });
  }
}
