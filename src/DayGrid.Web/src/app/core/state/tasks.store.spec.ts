import { TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';

import { SimpleTask, TasksApi } from '../api/tasks.api';
import { TasksStore } from './tasks.store';

function task(overrides: Partial<SimpleTask> = {}): SimpleTask {
  return {
    id: 't1',
    title: 'Task',
    notes: null,
    priority: 'Normal',
    status: 'Open',
    sortOrder: 0,
    completedAt: null,
    createdAt: '2026-10-01T00:00:00Z',
    updatedAt: '2026-10-01T00:00:00Z',
    ...overrides
  };
}

describe('TasksStore', () => {
  let store: TasksStore;
  let api: jasmine.SpyObj<TasksApi>;

  beforeEach(() => {
    api = jasmine.createSpyObj<TasksApi>('TasksApi', ['list', 'create', 'update', 'updateStatus', 'remove']);
    TestBed.configureTestingModule({ providers: [TasksStore, { provide: TasksApi, useValue: api }] });
    store = TestBed.inject(TasksStore);
  });

  describe('load()', () => {
    it('sets loading while in flight and stores the result', () => {
      const response = new Subject<SimpleTask[]>();
      api.list.and.returnValue(response);

      store.load();
      expect(store.loading()).toBeTrue();

      response.next([task(), task({ id: 't2', status: 'Done' })]);
      response.complete();

      expect(store.loading()).toBeFalse();
      expect(store.tasks().length).toBe(2);
      expect(store.openCount()).toBe(1);
      expect(store.doneCount()).toBe(1);
      expect(store.error()).toBeNull();
    });

    it('sets an error and clears loading on failure', () => {
      api.list.and.returnValue(throwError(() => new Error('boom')));
      store.load();
      expect(store.loading()).toBeFalse();
      expect(store.error()).toBe('Could not load tasks.');
    });
  });

  describe('add()', () => {
    it('ignores blank titles', () => {
      store.add('   ');
      expect(api.create).not.toHaveBeenCalled();
      expect(store.tasks()).toEqual([]);
    });

    it('prepends an optimistic task, then swaps in the server copy', () => {
      store.tasks.set([task({ id: 'existing' })]);
      const response = new Subject<SimpleTask>();
      api.create.and.returnValue(response);

      store.add('  New thing  ');

      expect(api.create).toHaveBeenCalledWith({ title: 'New thing' });
      const optimistic = store.tasks()[0];
      expect(optimistic.id).toMatch(/^temp-/);
      expect(optimistic.title).toBe('New thing');
      expect(store.tasks().length).toBe(2);

      response.next(task({ id: 'server-id', title: 'New thing' }));
      expect(store.tasks().map((t) => t.id)).toEqual(['server-id', 'existing']);
    });

    it('rolls back the optimistic task on error', () => {
      store.tasks.set([task({ id: 'existing' })]);
      api.create.and.returnValue(throwError(() => new Error('nope')));

      store.add('Doomed');

      expect(store.tasks().map((t) => t.id)).toEqual(['existing']);
      expect(store.error()).toBe('Could not add task.');
    });
  });

  describe('update()', () => {
    const changes = { title: 'Renamed', notes: 'n', priority: 'High' as const };

    it('does nothing for an unknown id', () => {
      store.update('missing', changes);
      expect(api.update).not.toHaveBeenCalled();
    });

    it('applies changes optimistically and keeps the saved result', () => {
      store.tasks.set([task()]);
      const response = new Subject<SimpleTask>();
      api.update.and.returnValue(response);

      store.update('t1', changes);
      expect(store.tasks()[0].title).toBe('Renamed');
      expect(api.update).toHaveBeenCalledWith('t1', changes);

      response.next(task({ ...changes, updatedAt: '2026-10-04T00:00:00Z' }));
      expect(store.tasks()[0].updatedAt).toBe('2026-10-04T00:00:00Z');
    });

    it('restores the previous list on error', () => {
      const original = [task()];
      store.tasks.set(original);
      api.update.and.returnValue(throwError(() => new Error('x')));

      store.update('t1', changes);

      expect(store.tasks()).toEqual(original);
      expect(store.error()).toBe('Could not save task.');
    });
  });

  describe('toggle()', () => {
    it('marks an open task done optimistically with completedAt', () => {
      store.tasks.set([task()]);
      api.updateStatus.and.returnValue(of(task({ status: 'Done' })));

      store.toggle('t1');

      expect(api.updateStatus).toHaveBeenCalledWith('t1', 'Done');
      expect(store.tasks()[0].status).toBe('Done');
      expect(store.tasks()[0].completedAt).not.toBeNull();
    });

    it('reopens a done task and clears completedAt', () => {
      store.tasks.set([task({ status: 'Done', completedAt: '2026-10-01T00:00:00Z' })]);
      api.updateStatus.and.returnValue(of(task()));

      store.toggle('t1');

      expect(api.updateStatus).toHaveBeenCalledWith('t1', 'Open');
      expect(store.tasks()[0].status).toBe('Open');
      expect(store.tasks()[0].completedAt).toBeNull();
    });

    it('rolls back on error', () => {
      store.tasks.set([task()]);
      api.updateStatus.and.returnValue(throwError(() => new Error('x')));

      store.toggle('t1');

      expect(store.tasks()[0].status).toBe('Open');
      expect(store.error()).toBe('Could not update task.');
    });

    it('does nothing for an unknown id', () => {
      store.toggle('missing');
      expect(api.updateStatus).not.toHaveBeenCalled();
    });
  });

  describe('remove()', () => {
    it('removes optimistically', () => {
      store.tasks.set([task(), task({ id: 't2' })]);
      api.remove.and.returnValue(of(undefined));

      store.remove('t1');

      expect(api.remove).toHaveBeenCalledWith('t1');
      expect(store.tasks().map((t) => t.id)).toEqual(['t2']);
    });

    it('restores the task on error', () => {
      store.tasks.set([task(), task({ id: 't2' })]);
      api.remove.and.returnValue(throwError(() => new Error('x')));

      store.remove('t1');

      expect(store.tasks().map((t) => t.id)).toEqual(['t1', 't2']);
      expect(store.error()).toBe('Could not remove task.');
    });
  });
});
