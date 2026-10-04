import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { SimpleTask, TasksApi } from './tasks.api';

describe('TasksApi', () => {
  let api: TasksApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(TasksApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('list() GETs /api/v1/tasks without params by default', () => {
    const tasks = [{ id: '1' } as SimpleTask];
    let result: SimpleTask[] | undefined;
    api.list().subscribe((r) => (result = r));

    const req = http.expectOne('/api/v1/tasks');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.keys().length).toBe(0);
    req.flush(tasks);
    expect(result).toEqual(tasks);
  });

  it('list() forwards status and q as query params', () => {
    api.list('Done', 'milk').subscribe();
    const req = http.expectOne('/api/v1/tasks?status=Done&q=milk');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('create() POSTs the request body', () => {
    api.create({ title: 'Buy milk', priority: 'High' }).subscribe();
    const req = http.expectOne('/api/v1/tasks');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ title: 'Buy milk', priority: 'High' });
    req.flush({});
  });

  it('update() PUTs to /api/v1/tasks/{id}', () => {
    const body = { title: 'T', notes: 'n', priority: 'Low' as const };
    api.update('abc', body).subscribe();
    const req = http.expectOne('/api/v1/tasks/abc');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(body);
    req.flush({});
  });

  it('updateStatus() PATCHes { status }', () => {
    api.updateStatus('abc', 'Done').subscribe();
    const req = http.expectOne('/api/v1/tasks/abc/status');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ status: 'Done' });
    req.flush({});
  });

  it('remove() DELETEs /api/v1/tasks/{id}', () => {
    api.remove('abc').subscribe();
    const req = http.expectOne('/api/v1/tasks/abc');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('reorder() PUTs the item array', () => {
    const items = [
      { id: 'a', sortOrder: 0 },
      { id: 'b', sortOrder: 1 }
    ];
    api.reorder(items).subscribe();
    const req = http.expectOne('/api/v1/tasks/reorder');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(items);
    req.flush(null);
  });
});
