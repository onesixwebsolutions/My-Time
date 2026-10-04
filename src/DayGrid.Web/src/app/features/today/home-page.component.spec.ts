import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';

import { ChecklistsApi } from '../../core/api/checklists.api';
import { TodayApi, TodayDto } from '../../core/api/today.api';
import { HomePageComponent, withItemCompleted } from './home-page.component';

function makeDay(): TodayDto {
  return {
    date: '2026-10-04',
    dayOfWeek: 'Sunday',
    displayDate: 'Sunday, 4 October 2026',
    override: null,
    template: null,
    nowBlock: null,
    nextBlock: null,
    blocks: [
      {
        id: 'b1',
        title: 'Deep work',
        startTime: '09:00:00',
        endTime: '10:30:00',
        category: 'Work',
        color: '#3b82f6',
        location: null,
        state: 'upcoming',
        linkedItems: [{ itemId: 'i1', title: 'Stretch', isCompleted: false }]
      }
    ],
    checklists: [
      {
        checklistId: 'c1',
        name: 'Morning',
        color: '#22c55e',
        icon: '',
        completedCount: 0,
        totalCount: 3,
        items: [
          { itemId: 'i1', title: 'Stretch', anchorType: 'FixedTime', anchorTime: '07:15:00', priority: 'Normal', estimatedMinutes: null, isCompleted: false, completedAt: null, isOverdue: false, recurrenceLabel: null },
          { itemId: 'i2', title: 'Water', anchorType: 'Anytime', anchorTime: null, priority: 'Normal', estimatedMinutes: null, isCompleted: false, completedAt: null, isOverdue: false, recurrenceLabel: null },
          { itemId: 'i3', title: 'Read', anchorType: 'Anytime', anchorTime: null, priority: 'Normal', estimatedMinutes: null, isCompleted: false, completedAt: null, isOverdue: false, recurrenceLabel: null }
        ]
      }
    ],
    dueToday: [{ id: 'f1', title: 'Pay bill', dueTime: '18:45:00', priority: 'High' }],
    overdue: [],
    summary: { totalItems: 3, completedItems: 0, completionPercent: 0, blocksTotal: 1, blocksDone: 0, minutesScheduled: 90 }
  };
}

describe('withItemCompleted', () => {
  it('keeps the group badge, summary tiles and linked chips in step with the item', () => {
    const done = withItemCompleted(makeDay(), 'i1', true);
    expect(done.checklists[0].items[0].isCompleted).toBeTrue();
    expect(done.checklists[0].completedCount).toBe(1);
    expect(done.summary.completedItems).toBe(1);
    expect(done.summary.completionPercent).toBe(33); // integer percent, same as DayPlanBuilder
    expect(done.blocks[0].linkedItems[0].isCompleted).toBeTrue();

    const undone = withItemCompleted(done, 'i1', false);
    expect(undone.checklists[0].completedCount).toBe(0);
    expect(undone.summary.completionPercent).toBe(0);
    expect(undone.blocks[0].linkedItems[0].isCompleted).toBeFalse();
  });
});

describe('HomePageComponent', () => {
  let fixture: ComponentFixture<HomePageComponent>;
  let checklistsApi: jasmine.SpyObj<ChecklistsApi>;

  beforeEach(() => {
    checklistsApi = jasmine.createSpyObj<ChecklistsApi>('ChecklistsApi', ['completeItem', 'uncompleteItem']);
    checklistsApi.completeItem.and.returnValue(of(undefined));
    TestBed.configureTestingModule({
      imports: [HomePageComponent],
      providers: [
        { provide: TodayApi, useValue: { get: () => of(makeDay()) } },
        { provide: ChecklistsApi, useValue: checklistsApi },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({}) } } }
      ]
    });
    fixture = TestBed.createComponent(HomePageComponent);
    fixture.detectChanges();
  });

  it('shows times as HH:mm, not the API\'s HH:mm:ss', () => {
    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('09:00 – 10:30');
    expect(text).toContain('07:15');
    expect(text).toContain('18:45');
    expect(text).not.toMatch(/\d{2}:\d{2}:\d{2}/);
  });

  it('updates the checklist counters when an item is ticked', () => {
    const item: HTMLElement = fixture.nativeElement.querySelector('div.cursor-pointer');
    item.click();
    fixture.detectChanges();
    const text: string = fixture.nativeElement.textContent;
    expect(checklistsApi.completeItem).toHaveBeenCalledWith('i1', { date: '2026-10-04' });
    expect(text).toContain('1/3');
    expect(text).toContain('33%');
  });
});
