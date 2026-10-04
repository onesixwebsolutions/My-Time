import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AssignmentRequest, BlockRequest, TemplateRequest, TimetableApi } from './timetable.api';

describe('TimetableApi', () => {
  let api: TimetableApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(TimetableApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  describe('templates', () => {
    const body: TemplateRequest = { name: 'Weekday', dayStart: '06:00', dayEnd: '22:00', slotMinutes: 30 };

    it('listTemplates() and getTemplate() GET', () => {
      api.listTemplates().subscribe();
      expect(http.expectOne('/api/v1/timetable/templates').request.method).toBe('GET');
      api.getTemplate('t1').subscribe();
      expect(http.expectOne('/api/v1/timetable/templates/t1').request.method).toBe('GET');
    });

    it('createTemplate() POSTs and updateTemplate() PUTs the body', () => {
      api.createTemplate(body).subscribe();
      let req = http.expectOne('/api/v1/timetable/templates');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(body);

      api.updateTemplate('t1', body).subscribe();
      req = http.expectOne('/api/v1/timetable/templates/t1');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(body);
    });

    it('deleteTemplate() DELETEs and setDefaultTemplate() PATCHes', () => {
      api.deleteTemplate('t1').subscribe();
      expect(http.expectOne('/api/v1/timetable/templates/t1').request.method).toBe('DELETE');

      api.setDefaultTemplate('t1').subscribe();
      const req = http.expectOne('/api/v1/timetable/templates/t1/default');
      expect(req.request.method).toBe('PATCH');
      expect(req.request.body).toBeNull();
    });
  });

  describe('blocks', () => {
    const body: BlockRequest = {
      title: 'Deep work',
      startTime: '09:00',
      endTime: '11:00',
      category: 'Work',
      allowOverlap: false,
      notifyAtStart: true,
      sortOrder: 0
    };

    it('listBlocks() and createBlock() are nested under the template', () => {
      api.listBlocks('t1').subscribe();
      expect(http.expectOne('/api/v1/timetable/templates/t1/blocks').request.method).toBe('GET');

      api.createBlock('t1', body).subscribe();
      const req = http.expectOne((r) => r.method === 'POST' && r.url === '/api/v1/timetable/templates/t1/blocks');
      expect(req.request.body).toEqual(body);
    });

    it('updateBlock() PUTs and deleteBlock() DELETEs /blocks/{id}', () => {
      api.updateBlock('b1', body).subscribe();
      const req = http.expectOne('/api/v1/timetable/blocks/b1');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(body);

      api.deleteBlock('b1').subscribe();
      expect(http.expectOne('/api/v1/timetable/blocks/b1').request.method).toBe('DELETE');
    });
  });

  describe('assignments', () => {
    it('list/create/delete', () => {
      const body: AssignmentRequest = { templateId: 't1', scope: 'Weekday', dayOfWeek: 'Monday', priority: 0 };

      api.listAssignments().subscribe();
      expect(http.expectOne('/api/v1/timetable/assignments').request.method).toBe('GET');

      api.createAssignment(body).subscribe();
      const req = http.expectOne((r) => r.method === 'POST' && r.url === '/api/v1/timetable/assignments');
      expect(req.request.body).toEqual(body);

      api.deleteAssignment('a1').subscribe();
      expect(http.expectOne('/api/v1/timetable/assignments/a1').request.method).toBe('DELETE');
    });
  });
});
