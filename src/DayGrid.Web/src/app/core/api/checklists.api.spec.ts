import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ChecklistItemRequest, ChecklistsApi, defaultRecurrence } from './checklists.api';

describe('ChecklistsApi', () => {
  let api: ChecklistsApi;
  let http: HttpTestingController;

  const itemRequest = (): ChecklistItemRequest => ({
    title: 'Stretch',
    priority: 'Normal',
    anchorType: 'Anytime',
    recurrence: defaultRecurrence()
  });

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(ChecklistsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('defaultRecurrence() is a non-recurring rule with fresh arrays', () => {
    const a = defaultRecurrence();
    const b = defaultRecurrence();
    expect(a.type).toBe('None');
    expect(a.interval).toBe(1);
    expect(a.daysOfWeek).not.toBe(b.daysOfWeek);
  });

  it('list() GETs with includeArchived=false by default', () => {
    api.list().subscribe();
    const req = http.expectOne('/api/v1/checklists?includeArchived=false');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('list(true) sends includeArchived=true', () => {
    api.list(true).subscribe();
    expect(http.expectOne('/api/v1/checklists?includeArchived=true').request.method).toBe('GET');
  });

  it('get() GETs a single checklist', () => {
    api.get('c1').subscribe();
    const req = http.expectOne('/api/v1/checklists/c1');
    expect(req.request.method).toBe('GET');
    req.flush({});
  });

  it('create() and update() send the body', () => {
    const body = { name: 'Morning', color: '#fff' };
    api.create(body).subscribe();
    let req = http.expectOne('/api/v1/checklists');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({});

    api.update('c1', body).subscribe();
    req = http.expectOne('/api/v1/checklists/c1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(body);
    req.flush({});
  });

  it('archive() PATCHes with archived query param and null body', () => {
    api.archive('c1', true).subscribe();
    const req = http.expectOne('/api/v1/checklists/c1/archive?archived=true');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toBeNull();
    req.flush({});
  });

  it('remove() DELETEs a checklist', () => {
    api.remove('c1').subscribe();
    expect(http.expectOne('/api/v1/checklists/c1').request.method).toBe('DELETE');
  });

  it('createItem() POSTs under the checklist', () => {
    const body = itemRequest();
    api.createItem('c1', body).subscribe();
    const req = http.expectOne('/api/v1/checklists/c1/items');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({});
  });

  it('updateItem() defaults isActive to true when omitted', () => {
    api.updateItem('i1', itemRequest()).subscribe();
    const req = http.expectOne('/api/v1/items/i1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.isActive).toBeTrue();
    expect(req.request.body.title).toBe('Stretch');
    req.flush({});
  });

  it('updateItem() preserves an explicit isActive=false', () => {
    api.updateItem('i1', { ...itemRequest(), isActive: false }).subscribe();
    const req = http.expectOne('/api/v1/items/i1');
    expect(req.request.body.isActive).toBeFalse();
    req.flush({});
  });

  it('removeItem() DELETEs /api/v1/items/{id}', () => {
    api.removeItem('i1').subscribe();
    expect(http.expectOne('/api/v1/items/i1').request.method).toBe('DELETE');
  });

  it('setItemActive() PATCHes with active query param', () => {
    api.setItemActive('i1', false).subscribe();
    const req = http.expectOne('/api/v1/items/i1/active?active=false');
    expect(req.request.method).toBe('PATCH');
    req.flush({});
  });

  it('completeItem() POSTs the completion body', () => {
    api.completeItem('i1', { date: '2026-10-04', status: 'Done' }).subscribe();
    const req = http.expectOne('/api/v1/items/i1/complete');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ date: '2026-10-04', status: 'Done' });
    req.flush(null);
  });

  it('uncompleteItem() DELETEs with the date param', () => {
    api.uncompleteItem('i1', '2026-10-04').subscribe();
    const req = http.expectOne('/api/v1/items/i1/complete?date=2026-10-04');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
