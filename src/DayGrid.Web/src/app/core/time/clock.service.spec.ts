import { TestBed } from '@angular/core/testing';

import { ClockService } from './clock.service';

describe('ClockService', () => {
  beforeEach(() => {
    jasmine.clock().install();
    jasmine.clock().mockDate(new Date('2026-10-04T09:00:00'));
  });

  afterEach(() => jasmine.clock().uninstall());

  it('starts at the current time', () => {
    const clock = TestBed.inject(ClockService);
    expect(clock.now().getTime()).toBe(new Date('2026-10-04T09:00:00').getTime());
  });

  it('ticks every second', () => {
    const clock = TestBed.inject(ClockService);

    jasmine.clock().tick(1000);
    expect(clock.now().getSeconds()).toBe(1);

    jasmine.clock().tick(2000);
    expect(clock.now().getSeconds()).toBe(3);
  });

  it('stops ticking once its injector is destroyed', () => {
    const clock = TestBed.inject(ClockService);
    TestBed.resetTestingModule();

    const frozen = clock.now().getTime();
    jasmine.clock().tick(5000);
    expect(clock.now().getTime()).toBe(frozen);
  });
});
