import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { shortTime, toTimeOnly } from '../../features/shared/ops-utils';
import { AuthServiceStub } from '../testing/auth-stub';
import { AuthService } from './auth.service';
import { FinanceService } from './finance.service';
import { LearningService } from './learning.service';
import { LookupsService } from './lookups.service';
import { SchedulingService } from './scheduling.service';

describe('Operations services (Phases 3-6)', () => {
  let backend: HttpTestingController;
  let auth: AuthServiceStub;

  beforeEach(() => {
    auth = new AuthServiceStub();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: AuthService, useValue: auth }]
    });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('generates sessions with a null group when none is chosen', () => {
    TestBed.inject(SchedulingService).generateSessions('2026-11-01', '2026-11-30', '').subscribe();

    const req = backend.expectOne('/api/sessions/generate');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ from: '2026-11-01', to: '2026-11-30', studyGroupId: null });
    req.flush({ succeeded: true, data: { created: 4, skipped: 0 } });
  });

  it('marks attendance with a PUT of the records', () => {
    TestBed.inject(SchedulingService)
      .markAttendance('s1', [{ studentProfileId: 'a', status: 'Present' }])
      .subscribe();

    const req = backend.expectOne('/api/sessions/s1/attendance');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ records: [{ studentProfileId: 'a', status: 'Present' }] });
    req.flush({ succeeded: true, data: [] });
  });

  it('reads the finance summary for a date range', () => {
    let net = 0;
    TestBed.inject(FinanceService)
      .summary('2026-11-01', '2026-11-30')
      .subscribe((s) => (net = s.net));

    const req = backend.expectOne((r) => r.url === '/api/finance/summary');
    expect(req.request.params.get('from')).toBe('2026-11-01');
    expect(req.request.params.get('to')).toBe('2026-11-30');
    req.flush({ succeeded: true, data: { net: 125.5 } });

    expect(net).toBe(125.5);
  });

  it('reads the caller notification feed', () => {
    let unread = -1;
    TestBed.inject(LearningService)
      .myNotifications(5)
      .subscribe((f) => (unread = f.unreadCount));

    const req = backend.expectOne((r) => r.url === '/api/notifications');
    expect(req.request.params.get('take')).toBe('5');
    req.flush({ succeeded: true, data: { unreadCount: 3, items: [] } });

    expect(unread).toBe(3);
  });

  it('lookups return an empty list without calling the API when the permission is missing', () => {
    auth.permissionSet.clear();
    let count = -1;
    TestBed.inject(LookupsService)
      .groups()
      .subscribe((o) => (count = o.length));

    backend.expectNone('/api/groups');
    expect(count).toBe(0);
  });

  it('lookups load groups when the permission is present', () => {
    auth.permissionSet.add('Groups.View');
    let labels: string[] = [];
    TestBed.inject(LookupsService)
      .groups()
      .subscribe((o) => (labels = o.map((x) => x.label)));

    const req = backend.expectOne((r) => r.url === '/api/groups');
    expect(req.request.params.get('pageSize')).toBe('100');
    req.flush({ succeeded: true, data: { items: [{ id: 'g1', name: 'Math A', subjectName: 'Math' }], totalItems: 1 } });

    expect(labels).toEqual(['Math A · Math']);
  });

  it('converts times between the picker and the API', () => {
    expect(toTimeOnly('16:00')).toBe('16:00:00');
    expect(shortTime('16:00:00')).toBe('16:00');
  });
});
