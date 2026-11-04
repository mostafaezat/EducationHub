import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { statusSeverity, toDateOnly } from '../../features/students/student-utils';
import { StudentsService } from './students.service';

describe('StudentsService', () => {
  let service: StudentsService;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(StudentsService);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('sends list filters as query parameters and unwraps the result', () => {
    let total = -1;
    service
      .list({ pageNumber: 2, pageSize: 10, search: 'omar', status: 'Active' })
      .subscribe((page) => (total = page.totalItems));

    const req = backend.expectOne((r) => r.url === '/api/students');
    expect(req.request.params.get('search')).toBe('omar');
    expect(req.request.params.get('status')).toBe('Active');
    expect(req.request.params.get('pageNumber')).toBe('2');
    expect(req.request.params.has('gradeLevelId')).toBeFalse();
    req.flush({ succeeded: true, data: { items: [], totalItems: 7, pageNumber: 2, pageSize: 10 } });

    expect(total).toBe(7);
  });

  it('replaces guardians with a PUT of the whole list', () => {
    service.setGuardians('s1', [{ parentProfileId: 'p1', relationship: 'Mother', isPrimary: true }]).subscribe();

    const req = backend.expectOne('/api/students/s1/guardians');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({
      guardians: [{ parentProfileId: 'p1', relationship: 'Mother', isPrimary: true }]
    });
    req.flush({ succeeded: true, data: null });
  });

  it('reads the parent own-children endpoint', () => {
    service.myChildren().subscribe();
    backend.expectOne('/api/parents/me/children').flush({ succeeded: true, data: [] });
  });
});

describe('student utils', () => {
  it('formats a picker date as yyyy-MM-dd in local time', () => {
    expect(toDateOnly(new Date(2026, 8, 1))).toBe('2026-09-01');
    expect(toDateOnly(null)).toBeNull();
  });

  it('maps every status to a tag severity', () => {
    expect(statusSeverity('Active')).toBe('success');
    expect(statusSeverity('Graduated')).toBe('info');
    expect(statusSeverity('Withdrawn')).toBe('warn');
    expect(statusSeverity('Inactive')).toBe('secondary');
  });
});
