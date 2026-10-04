import { DestroyRef, Injectable, inject, signal } from '@angular/core';

// Emits a 1-second-tick signal so live UI (now-card progress bar, red
// "now" line, elapsed/remaining timers) can update without any network
// call — plan section 6.4 "Live clock". Cleanup is via DestroyRef rather
// than an ngOnDestroy lifecycle hook since this is providedIn: 'root' and
// effectively lives for the app's lifetime, but wiring it correctly costs
// nothing and protects against future non-root use.
@Injectable({ providedIn: 'root' })
export class ClockService {
  readonly now = signal(new Date());

  constructor() {
    const intervalId = setInterval(() => this.now.set(new Date()), 1000);

    const destroyRef = inject(DestroyRef);
    destroyRef.onDestroy(() => clearInterval(intervalId));
  }
}
