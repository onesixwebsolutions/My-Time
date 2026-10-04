import { ComponentFixture, TestBed } from '@angular/core/testing';

import { SimpleTask } from '../../core/api/tasks.api';
import { TaskRowComponent } from './task-row.component';

function task(overrides: Partial<SimpleTask> = {}): SimpleTask {
  return {
    id: 't1',
    title: 'Write specs',
    notes: 'with coverage',
    priority: 'High',
    status: 'Open',
    sortOrder: 0,
    completedAt: null,
    createdAt: '2026-10-01T00:00:00Z',
    updatedAt: '2026-10-01T00:00:00Z',
    ...overrides
  };
}

describe('TaskRowComponent', () => {
  let fixture: ComponentFixture<TaskRowComponent>;
  let el: HTMLElement;

  const button = (label: string) => el.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`)!;
  const buttonByText = (text: string) =>
    Array.from(el.querySelectorAll('button')).find((b) => b.textContent!.trim() === text)!;

  function render(t: SimpleTask): void {
    fixture.componentRef.setInput('task', t);
    fixture.detectChanges();
  }

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [TaskRowComponent] });
    fixture = TestBed.createComponent(TaskRowComponent);
    el = fixture.nativeElement;
  });

  it('renders title, priority and notes', () => {
    render(task());
    expect(el.querySelector('b')!.textContent!.trim()).toBe('Write specs');
    expect(el.textContent).toContain('High');
    expect(el.textContent).toContain('with coverage');
    expect(fixture.componentInstance.priorityColor).toBe('#f59e0b');
  });

  it('falls back to defaults for an unknown priority', () => {
    render(task({ priority: 'Bogus' as SimpleTask['priority'] }));
    expect(fixture.componentInstance.priorityColor).toBe('#94a3b8');
    expect(fixture.componentInstance.priorityLabel).toBe('Normal');
  });

  it('styles done tasks and labels the toggle accordingly', () => {
    render(task({ status: 'Done' }));
    expect(el.querySelector('b')!.classList).toContain('line-through');
    expect(button('Mark open')).not.toBeNull();
  });

  it('emits toggle and remove', () => {
    render(task());
    const toggled = jasmine.createSpy('toggle');
    const removed = jasmine.createSpy('remove');
    fixture.componentInstance.toggle.subscribe(toggled);
    fixture.componentInstance.remove.subscribe(removed);

    button('Mark done').click();
    button('Remove task').click();

    expect(toggled).toHaveBeenCalledTimes(1);
    expect(removed).toHaveBeenCalledTimes(1);
  });

  it('edits inline and emits a trimmed save payload', async () => {
    render(task());
    const saved = jasmine.createSpy('save');
    fixture.componentInstance.save.subscribe(saved);

    button('Edit task').click();
    fixture.detectChanges();
    await fixture.whenStable();

    const input = el.querySelector<HTMLInputElement>('input')!;
    const textarea = el.querySelector<HTMLTextAreaElement>('textarea')!;
    expect(input.value).toBe('Write specs');

    input.value = '  Renamed  ';
    input.dispatchEvent(new Event('input'));
    textarea.value = '   ';
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    buttonByText('Save').click();
    fixture.detectChanges();

    expect(saved).toHaveBeenCalledOnceWith({ title: 'Renamed', notes: null, priority: 'High' });
    expect(el.querySelector('input')).toBeNull();
  });

  it('does not emit save for a blank title', async () => {
    render(task());
    const saved = jasmine.createSpy('save');
    fixture.componentInstance.save.subscribe(saved);

    button('Edit task').click();
    fixture.detectChanges();
    await fixture.whenStable();

    const input = el.querySelector<HTMLInputElement>('input')!;
    input.value = '   ';
    input.dispatchEvent(new Event('input'));
    buttonByText('Save').click();
    fixture.detectChanges();

    expect(saved).not.toHaveBeenCalled();
    expect(el.querySelector('input')).not.toBeNull();
  });

  it('cancel leaves edit mode without emitting', async () => {
    render(task());
    const saved = jasmine.createSpy('save');
    fixture.componentInstance.save.subscribe(saved);

    button('Edit task').click();
    fixture.detectChanges();
    buttonByText('Cancel').click();
    fixture.detectChanges();

    expect(saved).not.toHaveBeenCalled();
    expect(el.querySelector('input')).toBeNull();
  });
});
