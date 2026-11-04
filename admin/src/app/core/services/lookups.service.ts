import { Injectable, inject } from '@angular/core';
import { catchError, map, Observable, of } from 'rxjs';
import { Option } from '../../features/shared/ops-utils';
import { AcademicService } from './academic.service';
import { AuthService } from './auth.service';
import { FinanceService } from './finance.service';
import { SchedulingService } from './scheduling.service';
import { StudentsService } from './students.service';
import { TeachersService } from './teachers.service';

/**
 * Dropdown options for the operational pages. Each lookup degrades to an empty list when the
 * caller lacks the permission behind it, so a page never fails because a picker cannot load.
 */
@Injectable({ providedIn: 'root' })
export class LookupsService {
  private readonly auth = inject(AuthService);
  private readonly scheduling = inject(SchedulingService);
  private readonly studentsApi = inject(StudentsService);
  private readonly teachersApi = inject(TeachersService);
  private readonly academic = inject(AcademicService);
  private readonly finance = inject(FinanceService);

  groups(): Observable<Option[]> {
    return this.guard('Groups.View', () =>
      this.scheduling
        .listGroups({ pageNumber: 1, pageSize: 100, activeOnly: true })
        .pipe(map((page) => page.items.map((g) => ({ label: g.subjectName ? `${g.name} · ${g.subjectName}` : g.name, value: g.id }))))
    );
  }

  students(search?: string): Observable<Option[]> {
    return this.guard('Students.View', () =>
      this.studentsApi
        .list({ pageNumber: 1, pageSize: 50, search: search || undefined, status: 'Active' })
        .pipe(
          map((page) =>
            page.items.map((s) => ({
              label: s.studentNumber ? `${s.fullName} (${s.studentNumber})` : s.fullName,
              value: s.id
            }))
          )
        )
    );
  }

  teachers(): Observable<Option[]> {
    return this.guard('Teachers.View', () =>
      this.teachersApi
        .list({ pageNumber: 1, pageSize: 100 })
        .pipe(map((page) => page.items.map((t) => ({ label: t.fullName, value: t.id }))))
    );
  }

  subjects(): Observable<Option[]> {
    return this.guard('Subjects.View', () =>
      this.academic
        .listSubjects({ pageNumber: 1, pageSize: 100 })
        .pipe(map((page) => page.items.filter((s) => s.isActive).map((s) => ({ label: s.name, value: s.id }))))
    );
  }

  grades(): Observable<Option[]> {
    return this.guard('Grades.View', () =>
      this.academic
        .listGrades({ pageNumber: 1, pageSize: 100 })
        .pipe(map((page) => page.items.map((g) => ({ label: g.name, value: g.id }))))
    );
  }

  cashAccounts(): Observable<Option[]> {
    return this.guard('Finance.View', () =>
      this.finance
        .cashAccounts()
        .pipe(map((rows) => rows.filter((a) => a.isActive).map((a) => ({ label: a.name, value: a.id }))))
    );
  }

  private guard(permission: string, load: () => Observable<Option[]>): Observable<Option[]> {
    if (!this.auth.hasPermission(permission)) {
      return of([]);
    }

    return load().pipe(catchError(() => of<Option[]>([])));
  }
}
