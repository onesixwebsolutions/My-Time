import { Component } from '@angular/core';

// Minimal placeholder for /insights — streaks & completion charts. See the
// note in calendar-page.component.ts: this follows plan section 7's Phase 7
// (Polish & extras) rather than the route table's "(Phase 5)" label.
@Component({
  selector: 'app-insights-page',
  template: `
    <div class="mb-5">
      <h1 class="text-[22px] font-bold tracking-tight text-text">Insights</h1>
    </div>
    <div class="rounded-card border border-dashed border-border bg-raised px-4 py-6 text-center text-[13px] text-muted">
      <b class="text-text">Coming in Phase 7 — Polish &amp; extras.</b>
      Streak tracking and completion charts (fed by <code class="text-[12px]">/api/v1/stats</code>) will
      live here.
    </div>
  `
})
export class InsightsPageComponent {}
