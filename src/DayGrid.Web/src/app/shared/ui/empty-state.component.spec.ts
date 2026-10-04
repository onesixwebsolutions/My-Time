import { ComponentFixture, TestBed } from '@angular/core/testing';

import { EmptyStateComponent } from './empty-state.component';

describe('EmptyStateComponent', () => {
  let fixture: ComponentFixture<EmptyStateComponent>;
  let el: HTMLElement;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [EmptyStateComponent] });
    fixture = TestBed.createComponent(EmptyStateComponent);
    el = fixture.nativeElement;
  });

  it('shows the default message and no button', () => {
    fixture.detectChanges();
    expect(el.querySelector('p')!.textContent!.trim()).toBe('Nothing here yet.');
    expect(el.querySelector('button')).toBeNull();
  });

  it('renders a custom message', () => {
    fixture.componentRef.setInput('message', 'No tasks yet.');
    fixture.detectChanges();
    expect(el.querySelector('p')!.textContent!.trim()).toBe('No tasks yet.');
  });

  it('renders the action button and emits on click', () => {
    fixture.componentRef.setInput('actionLabel', 'Add task');
    fixture.detectChanges();

    const emitted = jasmine.createSpy('action');
    fixture.componentInstance.action.subscribe(emitted);

    const button = el.querySelector('button')!;
    expect(button.textContent!.trim()).toBe('Add task');
    button.click();
    expect(emitted).toHaveBeenCalledTimes(1);
  });
});
