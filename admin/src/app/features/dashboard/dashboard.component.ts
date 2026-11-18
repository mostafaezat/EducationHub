import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { catchError, forkJoin, map, Observable, of } from 'rxjs';
import { TranslateService } from '../../core/i18n/translate.service';
import { AuditSummaryDto, DashboardKpisDto, FamilyStudentOverviewDto } from '../../core/models';
import { isFamilyOnly } from '../../core/nav';
import { AcademicService } from '../../core/services/academic.service';
import { AuditService } from '../../core/services/audit.service';
import { AuthService } from '../../core/services/auth.service';
import { BranchesService } from '../../core/services/branches.service';
import { FamilyService } from '../../core/services/family.service';
import { FinanceService } from '../../core/services/finance.service';
import { RolesService } from '../../core/services/roles.service';
import { TenantsService } from '../../core/services/tenants.service';
import { UsersService } from '../../core/services/users.service';
import { money, shortTime } from '../shared/ops-utils';

interface StatCard {
  label: string;
  value: number | null;
  icon: string;
  route: string;
}

interface StatDefinition {
  card: StatCard;
  load: () => Observable<number | null>;
}

@Component({
  selector: 'app-dashboard',
  imports: [CommonModule, RouterLink, ButtonModule, TableModule, TagModule],
  template: `
    <div class="page-header">
      <div>
        <h1>{{ t('common.dashboard') }}</h1>
        <div class="subtitle">{{ t(partOfDay) }}</div>
      </div>
    </div>

    @if (familyOnly) {
      <div class="family-grid">
        @for (child of family(); track child.studentProfileId) {
          <a class="family-card" routerLink="/my-family">
            <div class="family-head">
              <i class="pi pi-user"></i>
              <strong>{{ child.fullName }}</strong>
            </div>
            <div class="family-row">
              <span>{{ t('family.attendanceRate') }}</span>
              <strong>{{ child.attendance.attendanceRate !== null && child.attendance.attendanceRate !== undefined ? child.attendance.attendanceRate + '%' : '—' }}</strong>
            </div>
            <div class="family-row">
              <span>{{ t('family.nextSession') }}</span>
              @if (child.upcomingSessions[0]; as next) {
                <strong>{{ next.session.date }} · {{ time(next.session.startTime) }}</strong>
              } @else {
                <strong>—</strong>
              }
            </div>
            <div class="family-row">
              <span>{{ t('family.pendingHomework') }}</span>
              <strong>{{ pendingHomework(child) }}</strong>
            </div>
            <div class="family-row">
              <span>{{ t('family.outstandingFees') }}</span>
              <strong [class.warn-text]="(child.balance?.outstanding ?? 0) > 0">{{ money(child.balance?.outstanding) }}</strong>
            </div>
            <span class="family-link">{{ t('family.openDetails') }} <i class="pi pi-arrow-right"></i></span>
          </a>
        } @empty {
          <div class="card">
            <div class="state-panel"><i class="pi pi-users"></i>{{ t('family.empty') }}</div>
          </div>
        }
      </div>
    }

    @if (kpis(); as k) {
      <div class="kpi-grid">
        <a class="kpi" routerLink="/students"><span>{{ t('dashboard.kpi.activeStudents') }}</span><strong>{{ k.activeStudents }}</strong></a>
        <a class="kpi" routerLink="/groups"><span>{{ t('dashboard.kpi.activeGroups') }}</span><strong>{{ k.activeGroups }}</strong></a>
        <a class="kpi" routerLink="/teachers"><span>{{ t('dashboard.kpi.activeTeachers') }}</span><strong>{{ k.activeTeachers }}</strong></a>
        <a class="kpi" routerLink="/sessions"><span>{{ t('dashboard.kpi.sessionsToday') }}</span><strong>{{ k.sessionsToday }}</strong></a>
        <div class="kpi"><span>{{ t('dashboard.kpi.attendance30') }}</span><strong>{{ k.attendanceRate30Days !== null && k.attendanceRate30Days !== undefined ? k.attendanceRate30Days + '%' : '—' }}</strong></div>
        @if (k.outstandingFees !== null && k.outstandingFees !== undefined) {
          <a class="kpi warn" routerLink="/fees"><span>{{ t('dashboard.kpi.outstandingFees') }}</span><strong>{{ money(k.outstandingFees) }}</strong></a>
          <a class="kpi good" routerLink="/reports"><span>{{ t('dashboard.kpi.incomeThisMonth') }}</span><strong>{{ money(k.incomeThisMonth) }}</strong></a>
          <a class="kpi" routerLink="/reports"><span>{{ t('dashboard.kpi.expensesThisMonth') }}</span><strong>{{ money(k.expensesThisMonth) }}</strong></a>
        }
      </div>
    }

    @if (stats().length > 0) {
      <div class="stat-grid">
        @for (stat of stats(); track stat.label) {
          <a class="stat-card" [routerLink]="stat.route">
            <div class="stat-icon"><i [class]="stat.icon"></i></div>
            <div class="stat-body">
              <span class="stat-value">{{ stat.value ?? '—' }}</span>
              <span class="stat-label">{{ t(stat.label) }}</span>
            </div>
          </a>
        }
      </div>
    }

    <div class="dash-columns">
      <div class="card">
        <h3 class="card-title">{{ t('dashboard.session') }}</h3>
        @if (user(); as current) {
          <div class="profile-row"><span>{{ t('common.name') }}</span><strong>{{ current.fullName }}</strong></div>
          <div class="profile-row"><span>{{ t('common.email') }}</span><strong>{{ current.email }}</strong></div>
          <div class="profile-row">
            <span>{{ t('dashboard.roles') }}</span>
            <span class="tag-row">
              @for (role of current.roles; track role) {
                <p-tag [value]="role" severity="info" />
              }
            </span>
          </div>
          <div class="profile-row">
            <span>{{ t('dashboard.permissions') }}</span><strong>{{ current.permissions.length }}</strong>
          </div>
          <div class="profile-row">
            <span>{{ t('dashboard.emailConfirmed') }}</span>
            <p-tag
              [value]="current.emailConfirmed ? t('common.yes') : t('common.no')"
              [severity]="current.emailConfirmed ? 'success' : 'warn'"
            />
          </div>
          <div class="profile-row">
            <span>{{ t('dashboard.preferredLanguage') }}</span><strong>{{ current.preferredLanguage }}</strong>
          </div>
        }
      </div>

      @if (canViewAudit()) {
        <div class="card">
          <div class="card-head">
            <h3 class="card-title">{{ t('dashboard.recentActivity') }}</h3>
            <p-button [label]="t('dashboard.viewAudit')" icon="pi pi-history" [text]="true" (onClick)="goAudit()" />
          </div>
          @if (summary().length === 0) {
            <div class="state-panel">
              <i class="pi pi-inbox"></i>
              {{ t('dashboard.noActivity') }}
            </div>
          } @else {
            <p-table [value]="summary()" styleClass="p-datatable-sm">
              <ng-template pTemplate="header">
                <tr>
                  <th>{{ t('dashboard.action') }}</th>
                  <th>{{ t('dashboard.user') }}</th>
                  <th style="width:6rem">{{ t('dashboard.count') }}</th>
                </tr>
              </ng-template>
              <ng-template pTemplate="body" let-row>
                <tr>
                  <td><p-tag [value]="row.action" severity="secondary" /></td>
                  <td>{{ row.userName ?? t('dashboard.platform') }}</td>
                  <td><strong>{{ row.count }}</strong></td>
                </tr>
              </ng-template>
            </p-table>
          }
        </div>
      } @else {
        <div class="card">
          <h3 class="card-title">{{ t('dashboard.gettingStarted') }}</h3>
          <p class="muted">
            {{ t('dashboard.gettingStartedBody') }}
          </p>
        </div>
      }
    </div>
  `,
  styles: [
    `
      .kpi-grid {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
        gap: 0.75rem;
        margin-bottom: 1.25rem;
      }
      .kpi {
        display: flex;
        flex-direction: column;
        background: #fff;
        border: 1px solid #e3e8f0;
        border-radius: 12px;
        padding: 0.85rem 1.1rem;
        text-decoration: none;
        color: inherit;
      }
      .kpi span {
        font-size: 0.8rem;
        color: #64748b;
      }
      .kpi strong {
        font-size: 1.35rem;
      }
      .kpi.warn strong {
        color: #b91c1c;
      }
      .kpi.good strong {
        color: #15803d;
      }
      .stat-grid {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
        gap: 1rem;
        margin-bottom: 1.5rem;
      }
      .stat-card {
        display: flex;
        align-items: center;
        gap: 1rem;
        background: #fff;
        border: 1px solid #e3e8f0;
        border-radius: 12px;
        padding: 1rem 1.25rem;
        text-decoration: none;
        color: inherit;
        transition: box-shadow 0.15s ease;
      }
      .stat-card:hover {
        box-shadow: 0 8px 24px rgba(16, 35, 63, 0.08);
      }
      .stat-icon {
        width: 44px;
        height: 44px;
        border-radius: 10px;
        background: #eef4ff;
        color: #2563eb;
        display: flex;
        align-items: center;
        justify-content: center;
        font-size: 1.2rem;
      }
      .stat-body {
        display: flex;
        flex-direction: column;
      }
      .stat-value {
        font-size: 1.4rem;
        font-weight: 700;
      }
      .stat-label {
        font-size: 0.82rem;
        color: #64748b;
      }
      .dash-columns {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
        gap: 1.25rem;
      }
      .card-head {
        display: flex;
        align-items: center;
        justify-content: space-between;
      }
      .card-title {
        margin: 0 0 1rem;
        font-size: 1rem;
      }
      .muted {
        color: #64748b;
        font-size: 0.9rem;
      }
      .profile-row {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 1rem;
        padding: 0.5rem 0;
        border-bottom: 1px solid #f1f5f9;
        font-size: 0.9rem;
      }
      .profile-row:last-child {
        border-bottom: none;
      }
      .profile-row > span:first-child {
        color: #64748b;
      }
      .family-grid {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
        gap: 1rem;
        margin-bottom: 1.25rem;
      }
      .family-card {
        display: flex;
        flex-direction: column;
        gap: 0.45rem;
        background: #fff;
        border: 1px solid #e3e8f0;
        border-radius: 12px;
        padding: 1rem 1.1rem;
        color: inherit;
        text-decoration: none;
      }
      .family-card:hover {
        border-color: #2563eb;
      }
      .family-head {
        display: flex;
        align-items: center;
        gap: 0.5rem;
        font-size: 1.05rem;
        margin-bottom: 0.25rem;
      }
      .family-row {
        display: flex;
        justify-content: space-between;
        font-size: 0.9rem;
      }
      .family-row > span {
        color: #64748b;
      }
      .warn-text {
        color: #b45309;
      }
      .family-link {
        margin-top: 0.35rem;
        color: #2563eb;
        font-size: 0.85rem;
      }
    `
  ]
})
export class DashboardComponent {
  protected readonly t = inject(TranslateService).t;
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly tenants = inject(TenantsService);
  private readonly users = inject(UsersService);
  private readonly roles = inject(RolesService);
  private readonly branches = inject(BranchesService);
  private readonly academic = inject(AcademicService);
  private readonly audit = inject(AuditService);
  private readonly finance = inject(FinanceService);
  private readonly familyService = inject(FamilyService);

  protected readonly user = this.auth.user;
  protected readonly stats = signal<StatCard[]>([]);
  protected readonly summary = signal<AuditSummaryDto[]>([]);
  protected readonly kpis = signal<DashboardKpisDto | null>(null);
  protected readonly money = money;
  protected readonly time = shortTime;
  protected readonly family = signal<FamilyStudentOverviewDto[]>([]);

  /** Students and parents get their own children's summary instead of centre-wide figures. */
  protected readonly familyOnly = isFamilyOnly(this.auth.roles());
  protected readonly canViewAudit = signal(this.auth.hasPermission('AuditLogs.View'));

  protected readonly partOfDay = (() => {
    const hour = new Date().getHours();
    return hour < 12
      ? 'dashboard.greetingMorning'
      : hour < 18
        ? 'dashboard.greetingAfternoon'
        : 'dashboard.greetingEvening';
  })();

  constructor() {
    if (this.familyOnly) {
      this.loadFamily();
      return;
    }

    this.loadKpis();
    this.loadStats();
    this.loadSummary();
  }

  protected pendingHomework(child: FamilyStudentOverviewDto): number {
    return child.homework.filter((h) => h.status === 'Pending' || h.status === 'Missing').length;
  }

  private loadFamily(): void {
    this.familyService
      .overview()
      .pipe(catchError(() => of<FamilyStudentOverviewDto[]>([])))
      .subscribe((rows) => this.family.set(rows ?? []));
  }

  protected goAudit(): void {
    void this.router.navigate(['/audit']);
  }

  private loadStats(): void {
    const definitions: StatDefinition[] = [];

    if (this.auth.hasPermission('Platform.ManageTenants')) {
      definitions.push({
        card: { label: 'nav.tenants', value: null, icon: 'pi pi-building', route: '/tenants' },
        load: () => this.total(() => this.tenants.list({ pageNumber: 1, pageSize: 1 }))
      });
    }

    if (this.auth.hasPermission('Users.View')) {
      definitions.push({
        card: { label: 'nav.users', value: null, icon: 'pi pi-users', route: '/users' },
        load: () => this.total(() => this.users.list({ pageNumber: 1, pageSize: 1 }))
      });
    }

    if (this.auth.hasPermission('Roles.View')) {
      definitions.push({
        card: { label: 'nav.roles', value: null, icon: 'pi pi-id-card', route: '/roles' },
        load: () => this.total(() => this.roles.list({ pageNumber: 1, pageSize: 1 }))
      });
    }

    if (this.auth.hasPermission('Branches.View')) {
      definitions.push({
        card: { label: 'nav.branches', value: null, icon: 'pi pi-sitemap', route: '/branches' },
        load: () => this.total(() => this.branches.list({ pageNumber: 1, pageSize: 1 }))
      });
    }

    if (this.auth.hasPermission('AcademicYears.View')) {
      definitions.push({
        card: { label: 'nav.academicYears', value: null, icon: 'pi pi-calendar', route: '/academic-years' },
        load: () => this.total(() => this.academic.listAcademicYears({ pageNumber: 1, pageSize: 1 }))
      });
    }

    if (this.auth.hasPermission('Grades.View')) {
      definitions.push({
        card: { label: 'nav.grades', value: null, icon: 'pi pi-list', route: '/grades' },
        load: () => this.total(() => this.academic.listGrades({ pageNumber: 1, pageSize: 1 }))
      });
    }

    if (this.auth.hasPermission('Subjects.View')) {
      definitions.push({
        card: { label: 'nav.subjects', value: null, icon: 'pi pi-book', route: '/subjects' },
        load: () => this.total(() => this.academic.listSubjects({ pageNumber: 1, pageSize: 1 }))
      });
    }

    if (definitions.length === 0) {
      return;
    }

    forkJoin(definitions.map((definition) => definition.load())).subscribe((values) => {
      this.stats.set(
        definitions.map((definition, index) => ({ ...definition.card, value: values[index] }))
      );
    });
  }

  private total(load: () => Observable<{ totalItems: number }>): Observable<number | null> {
    return load().pipe(
      map((page) => page.totalItems),
      catchError(() => of<number | null>(null))
    );
  }

  private loadKpis(): void {
    // Platform administrators have no tenant, so there is nothing operational to count.
    const tenantId = this.auth.user()?.tenantId;
    const isPlatform = !tenantId || tenantId === '00000000-0000-0000-0000-000000000000';

    if (!this.auth.hasPermission('Dashboard.View') || isPlatform) {
      return;
    }

    this.finance
      .kpis()
      .pipe(catchError(() => of<DashboardKpisDto | null>(null)))
      .subscribe((k) => this.kpis.set(k));
  }

  private loadSummary(): void {
    if (!this.canViewAudit()) {
      return;
    }

    this.audit
      .summary({ top: 10 })
      .pipe(catchError(() => of<AuditSummaryDto[]>([])))
      .subscribe((summary) => this.summary.set(summary));
  }
}
