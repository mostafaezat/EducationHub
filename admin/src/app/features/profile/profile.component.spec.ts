import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ACCESS_EXPIRY_KEY, ACCESS_TOKEN_KEY, USER_KEY } from '../../core/app-config';
import { ProfileComponent } from './profile.component';

/** QA-05: My profile must work for roles without Permissions.View (no catalog request). */
describe('ProfileComponent', () => {
  afterEach(() => localStorage.clear());

  it("lists the user's own permissions by area without calling /api/permissions", () => {
    localStorage.setItem(ACCESS_TOKEN_KEY, 'token');
    localStorage.setItem(ACCESS_EXPIRY_KEY, String(Date.now() + 15 * 60_000));
    localStorage.setItem(
      USER_KEY,
      JSON.stringify({
        id: 'u1',
        fullName: 'Demo Teacher 1',
        email: 'teacher.one@educationhub.local',
        tenantId: 't1',
        tenantName: 'Demo',
        roles: ['Teacher'],
        permissions: ['Sessions.View', 'Attendance.Mark', 'Sessions.Manage', 'Dashboard.View']
      })
    );

    TestBed.configureTestingModule({
      imports: [ProfileComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    });

    const fixture = TestBed.createComponent(ProfileComponent);
    fixture.detectChanges();

    const backend = TestBed.inject(HttpTestingController);
    backend.expectNone((r) => r.url.includes('/api/permissions'));
    backend.verify();

    const el = fixture.nativeElement as HTMLElement;
    const areas = [...el.querySelectorAll('.perm-area h4')].map((h) => h.textContent?.trim());
    expect(areas).toEqual(['Attendance', 'Dashboard', 'Sessions']);

    const chips = [...el.querySelectorAll('.perm-chip')].map((c) => c.textContent?.trim());
    expect(chips).toEqual(['Attendance.Mark', 'Dashboard.View', 'Sessions.Manage', 'Sessions.View']);
    expect(el.querySelector('.state-panel')).toBeNull();
  });
});
