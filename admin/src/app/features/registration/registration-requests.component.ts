import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { Subject, debounceTime, finalize } from 'rxjs';
import { TranslateService } from '../../core/i18n/translate.service';
import {
  GUARDIAN_RELATIONSHIPS,
  GuardianRelationship,
  NewGuardianAccountDto,
  REGISTRATION_STATUSES,
  RegistrationRequestDto,
  RegistrationRequestStatus
} from '../../core/models';
import { AcademicService } from '../../core/services/academic.service';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { RegistrationService } from '../../core/services/registration.service';
import { SchedulingService } from '../../core/services/scheduling.service';

interface Option {
  label: string;
  value: string;
}

const SEVERITY: Record<RegistrationRequestStatus, 'warn' | 'success' | 'danger'> = {
  Pending: 'warn',
  Approved: 'success',
  Rejected: 'danger'
};

/** Staff review of the requests parents send from the public registration page. */
@Component({
  selector: 'app-registration-requests',
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    ButtonModule,
    CheckboxModule,
    DialogModule,
    InputTextModule,
    SelectModule,
    TableModule,
    TagModule,
    TextareaModule
  ],
  template: `
    <div class="page-header">
      <div>
        <h1>{{ t('regRequests.title') }}</h1>
        <div class="subtitle">{{ t('regRequests.subtitle') }}</div>
      </div>
    </div>

    @if (publicLink(); as link) {
      <div class="card link-card">
        <div class="link-text">
          <strong>{{ t('regRequests.linkTitle') }}</strong>
          <small class="muted">{{ t('regRequests.linkHint') }}</small>
          <code dir="ltr">{{ link }}</code>
        </div>
        <p-button [label]="t('regRequests.copyLink')" icon="pi pi-copy" [outlined]="true" (onClick)="copyLink(link)" />
      </div>
    }

    <div class="toolbar">
      <input pInputText type="text" [placeholder]="t('regRequests.searchPlaceholder')" (input)="onSearch($event)" />
      <p-select
        [options]="statusOptions()"
        [ngModel]="statusFilter()"
        (ngModelChange)="onStatusFilter($event)"
        optionLabel="label"
        optionValue="value"
        [showClear]="true"
        [placeholder]="t('regRequests.allStatuses')"
      />
      <div class="spacer"></div>
      <p-button icon="pi pi-refresh" [text]="true" (onClick)="load()" />
    </div>

    <div class="card">
      <p-table
        [value]="rows()"
        [lazy]="true"
        [paginator]="true"
        [rows]="pageSize()"
        [first]="first()"
        [totalRecords]="total()"
        [loading]="loading()"
        (onLazyLoad)="onLazyLoad($event)"
        dataKey="id"
        styleClass="p-datatable-sm"
      >
        <ng-template pTemplate="header">
          <tr>
            <th style="width:3rem"></th>
            <th>{{ t('regRequests.col.student') }}</th>
            <th>{{ t('regRequests.col.guardian') }}</th>
            <th style="width:10rem">{{ t('regRequests.col.phone') }}</th>
            <th style="width:8rem">{{ t('regRequests.col.grade') }}</th>
            <th style="width:9rem">{{ t('regRequests.col.submitted') }}</th>
            <th style="width:8rem">{{ t('regRequests.col.status') }}</th>
            <th style="width:13rem">{{ t('common.actions') }}</th>
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-row let-expanded="expanded">
          <tr>
            <td>
              <p-button
                [pRowToggler]="row"
                [icon]="expanded ? 'pi pi-chevron-down' : 'pi pi-chevron-right'"
                [text]="true"
                [rounded]="true"
                [ariaLabel]="t('regRequests.details')"
              />
            </td>
            <td>{{ row.studentName }}</td>
            <td>{{ row.guardianName }} <span class="muted">· {{ t('students.relationship.' + row.relationship) }}</span></td>
            <td dir="ltr">{{ row.guardianPhone }}</td>
            <td>{{ row.gradeLevelName || '—' }}</td>
            <td>{{ row.createdAt | date: 'yyyy-MM-dd HH:mm' }}</td>
            <td><p-tag [value]="t('regRequests.status.' + row.status)" [severity]="severity(row.status)" /></td>
            <td>
              @if (row.status === 'Pending' && canManage()) {
                <p-button [label]="t('regRequests.approve')" icon="pi pi-check" size="small" (onClick)="openApprove(row)" />
                <p-button
                  [label]="t('regRequests.reject')"
                  icon="pi pi-times"
                  size="small"
                  severity="danger"
                  [text]="true"
                  (onClick)="openReject(row)"
                />
              } @else if (row.reviewedAt) {
                <small class="muted">{{ t('regRequests.reviewedAt', { date: (row.reviewedAt | date: 'yyyy-MM-dd') ?? '' }) }}</small>
              }
            </td>
          </tr>
        </ng-template>
        <ng-template pTemplate="rowexpansion" let-row>
          <tr>
            <td colspan="8">
              <div class="details">
                @if (row.birthDate) {
                  <div><span class="muted">{{ t('regRequests.birthDate') }}:</span> {{ row.birthDate }}</div>
                }
                @if (row.guardianEmail) {
                  <div><span class="muted">{{ t('regRequests.email') }}:</span> <span dir="ltr">{{ row.guardianEmail }}</span></div>
                }
                @if (row.school) {
                  <div><span class="muted">{{ t('regRequests.school') }}:</span> {{ row.school }}</div>
                }
                @if (row.preferredSubjects) {
                  <div><span class="muted">{{ t('regRequests.subjects') }}:</span> {{ row.preferredSubjects }}</div>
                }
                @if (row.notes) {
                  <div><span class="muted">{{ t('regRequests.notes') }}:</span> {{ row.notes }}</div>
                }
                @if (row.rejectionReason) {
                  <div>{{ t('regRequests.rejectionReason', { reason: row.rejectionReason }) }}</div>
                }
              </div>
            </td>
          </tr>
        </ng-template>
        <ng-template pTemplate="emptymessage">
          <tr>
            <td colspan="8" class="muted">{{ t('regRequests.empty') }}</td>
          </tr>
        </ng-template>
      </p-table>
    </div>

    <!-- Approve -->
    <p-dialog
      [visible]="approving() !== null"
      (visibleChange)="!$event && approving.set(null)"
      [modal]="true"
      [header]="t('regRequests.approveTitle', { name: approving()?.studentName ?? '' })"
      [style]="{ width: '40rem' }"
      [breakpoints]="{ '640px': '95vw' }"
    >
      <form [formGroup]="approveForm" class="form-grid">
        <small class="muted span-2">{{ t('regRequests.approveHint') }}</small>

        <div class="field">
          <label for="ap-student">{{ t('register.studentName') }} *</label>
          <input id="ap-student" pInputText formControlName="studentName" />
        </div>
        <div class="field">
          <label for="ap-birth">{{ t('register.birthDate') }}</label>
          <input id="ap-birth" pInputText type="date" formControlName="birthDate" />
        </div>
        <div class="field">
          <label for="ap-grade">{{ t('register.grade') }}</label>
          <p-select
            inputId="ap-grade"
            [options]="gradeOptions()"
            optionLabel="label"
            optionValue="value"
            formControlName="gradeLevelId"
            [showClear]="true"
            appendTo="body"
            styleClass="w-full"
          />
        </div>
        <div class="field">
          <label for="ap-group">{{ t('regRequests.group') }}</label>
          <p-select
            inputId="ap-group"
            [options]="groupOptions()"
            optionLabel="label"
            optionValue="value"
            formControlName="studyGroupId"
            [showClear]="true"
            [filter]="true"
            [placeholder]="t('regRequests.noGroup')"
            appendTo="body"
            styleClass="w-full"
          />
        </div>
        <div class="field">
          <label for="ap-guardian">{{ t('register.guardianName') }} *</label>
          <input id="ap-guardian" pInputText formControlName="guardianName" />
        </div>
        <div class="field">
          <label for="ap-relationship">{{ t('register.relationship') }}</label>
          <p-select
            inputId="ap-relationship"
            [options]="relationshipOptions()"
            optionLabel="label"
            optionValue="value"
            formControlName="relationship"
            appendTo="body"
            styleClass="w-full"
          />
        </div>
        <div class="field">
          <label for="ap-phone">{{ t('register.guardianPhone') }} *</label>
          <input id="ap-phone" pInputText dir="ltr" formControlName="guardianPhone" />
        </div>
        <div class="field">
          <label for="ap-email">{{ t('register.guardianEmail') }}</label>
          <input id="ap-email" pInputText dir="ltr" type="email" formControlName="guardianEmail" />
        </div>
        <label class="check span-2">
          <p-checkbox formControlName="createAccount" [binary]="true" inputId="ap-account" />
          <span>{{ t('regRequests.createAccount') }}</span>
        </label>
      </form>
      <ng-template pTemplate="footer">
        <p-button [label]="t('common.cancel')" [text]="true" (onClick)="approving.set(null)" />
        <p-button [label]="t('regRequests.approveSubmit')" icon="pi pi-check" [loading]="saving()" (onClick)="approve()" />
      </ng-template>
    </p-dialog>

    <!-- Reject -->
    <p-dialog
      [visible]="rejecting() !== null"
      (visibleChange)="!$event && rejecting.set(null)"
      [modal]="true"
      [header]="t('regRequests.rejectTitle', { name: rejecting()?.studentName ?? '' })"
      [style]="{ width: '30rem' }"
      [breakpoints]="{ '640px': '95vw' }"
    >
      <div class="field">
        <label for="rej-reason">{{ t('regRequests.reason') }} *</label>
        <textarea id="rej-reason" pTextarea rows="3" [formControl]="rejectReason"></textarea>
      </div>
      <ng-template pTemplate="footer">
        <p-button [label]="t('common.cancel')" [text]="true" (onClick)="rejecting.set(null)" />
        <p-button
          [label]="t('regRequests.rejectSubmit')"
          icon="pi pi-times"
          severity="danger"
          [loading]="saving()"
          [disabled]="rejectReason.invalid"
          (onClick)="reject()"
        />
      </ng-template>
    </p-dialog>

    <!-- Created guardian credentials: shown once, same as on the students page. -->
    <p-dialog
      [visible]="credentials() !== null"
      (visibleChange)="!$event && credentials.set(null)"
      [modal]="true"
      [header]="t('students.newGuardian.resultTitle')"
      [style]="{ width: '30rem' }"
      [breakpoints]="{ '640px': '95vw' }"
    >
      @if (credentials(); as c) {
        @if (c.accountCreated && c.temporaryPassword) {
          <p>{{ t('students.newGuardian.resultBody', { name: c.fullName }) }}</p>
          <div class="credential-row"><span>{{ t('students.newGuardian.loginName') }}</span><strong dir="ltr">{{ c.loginName }}</strong></div>
          <div class="credential-row">
            <span>{{ t('students.newGuardian.temporaryPassword') }}</span>
            <strong class="mono" dir="ltr">{{ c.temporaryPassword }}</strong>
          </div>
          <small class="muted">{{ t('students.newGuardian.resultHint') }}</small>
          @if (c.whatsAppSent) {
            <p class="whatsapp-note"><i class="pi pi-whatsapp"></i> {{ t('students.newGuardian.whatsAppSent', { phone: c.phoneNumber ?? '' }) }}</p>
          }
        } @else if (c.linkedExistingParent) {
          <p>{{ t('students.newGuardian.linkedExisting', { name: c.fullName }) }}</p>
        } @else {
          <p>{{ t('students.newGuardian.createdNoAccount', { name: c.fullName }) }}</p>
        }
      }
      <ng-template pTemplate="footer">
        @if (credentials()?.temporaryPassword) {
          <p-button [label]="t('students.newGuardian.copy')" icon="pi pi-copy" [text]="true" (onClick)="copyCredentials()" />
        }
        <p-button [label]="t('common.close')" (onClick)="credentials.set(null)" />
      </ng-template>
    </p-dialog>
  `,
  styles: [
    `
      .link-card {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 1rem;
        flex-wrap: wrap;
        margin-bottom: 1rem;
      }
      .link-text {
        display: flex;
        flex-direction: column;
        gap: 0.25rem;
        min-width: 0;
      }
      code {
        overflow-wrap: anywhere;
        background: #f1f5f9;
        padding: 0.25rem 0.5rem;
        border-radius: 6px;
      }
      .muted {
        color: #64748b;
      }
      .details {
        display: flex;
        flex-wrap: wrap;
        gap: 0.5rem 1.5rem;
        padding: 0.25rem 0.5rem;
      }
      .form-grid {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(15rem, 1fr));
        gap: 0.9rem;
      }
      .span-2 {
        grid-column: 1 / -1;
      }
      .field {
        display: flex;
        flex-direction: column;
        gap: 0.35rem;
      }
      .field input,
      .field textarea {
        width: 100%;
      }
      .check {
        display: flex;
        align-items: center;
        gap: 0.5rem;
      }
      .credential-row {
        display: flex;
        justify-content: space-between;
        padding: 0.4rem 0;
        border-bottom: 1px solid #f1f5f9;
      }
      .whatsapp-note {
        color: #15803d;
        font-size: 0.85rem;
        margin: 0.75rem 0 0;
      }
      .mono {
        font-family: ui-monospace, monospace;
        letter-spacing: 0.05em;
      }
      :host ::ng-deep .w-full {
        width: 100%;
      }
    `
  ]
})
export class RegistrationRequestsComponent implements OnInit {
  protected readonly t = inject(TranslateService).t;
  private readonly service = inject(RegistrationService);
  private readonly academic = inject(AcademicService);
  private readonly scheduling = inject(SchedulingService);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);
  private readonly fb = inject(FormBuilder);

  protected readonly rows = signal<RegistrationRequestDto[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly pageNumber = signal(1);
  protected readonly pageSize = signal(20);
  protected readonly first = signal(0);
  protected readonly search = signal('');
  protected readonly statusFilter = signal<RegistrationRequestStatus | null>('Pending');
  protected readonly publicLink = signal<string | null>(null);

  protected readonly approving = signal<RegistrationRequestDto | null>(null);
  protected readonly rejecting = signal<RegistrationRequestDto | null>(null);
  protected readonly credentials = signal<NewGuardianAccountDto | null>(null);

  protected readonly gradeOptions = signal<Option[]>([]);
  protected readonly groupOptions = signal<Option[]>([]);

  protected readonly canManage = signal(this.auth.hasPermission('RegistrationRequests.Manage'));

  protected readonly statusOptions = computed(() =>
    REGISTRATION_STATUSES.map((s) => ({ label: this.t(`regRequests.status.${s}`), value: s }))
  );

  protected readonly relationshipOptions = computed(() =>
    GUARDIAN_RELATIONSHIPS.map((r) => ({ label: this.t(`students.relationship.${r}`), value: r }))
  );

  protected readonly approveForm = this.fb.nonNullable.group({
    studentName: ['', [Validators.required, Validators.maxLength(200)]],
    birthDate: [''],
    gradeLevelId: [null as string | null],
    studyGroupId: [null as string | null],
    guardianName: ['', [Validators.required, Validators.maxLength(200)]],
    guardianPhone: ['', [Validators.required]],
    guardianEmail: ['', [Validators.email]],
    relationship: ['Father' as GuardianRelationship],
    createAccount: [true]
  });

  protected readonly rejectReason = this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(500)]);

  private readonly search$ = new Subject<string>();
  private lookupsLoaded = false;

  ngOnInit(): void {
    this.search$.pipe(debounceTime(300)).subscribe((value) => {
      this.search.set(value);
      this.resetPaging();
      this.load();
    });

    this.service.link().subscribe((slug) => this.publicLink.set(`${window.location.origin}/register/${slug}`));
    this.load();
  }

  protected severity(status: RegistrationRequestStatus): 'warn' | 'success' | 'danger' {
    return SEVERITY[status];
  }

  protected onSearch(event: Event): void {
    this.search$.next((event.target as HTMLInputElement).value);
  }

  protected onStatusFilter(value: RegistrationRequestStatus | null): void {
    this.statusFilter.set(value);
    this.resetPaging();
    this.load();
  }

  protected onLazyLoad(event: TableLazyLoadEvent): void {
    const first = event.first ?? 0;
    const rows = event.rows ?? this.pageSize();
    this.first.set(first);
    this.pageSize.set(rows);
    this.pageNumber.set(Math.floor(first / rows) + 1);
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.service
      .list({
        pageNumber: this.pageNumber(),
        pageSize: this.pageSize(),
        status: this.statusFilter() ?? undefined,
        search: this.search() || undefined
      })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe((page) => {
        this.rows.set(page.items);
        this.total.set(page.totalItems);
      });
  }

  protected copyLink(link: string): void {
    void navigator.clipboard?.writeText(link).then(() => this.notifications.success(this.t('regRequests.linkCopied')));
  }

  protected openApprove(row: RegistrationRequestDto): void {
    this.ensureLookups();
    this.approveForm.reset({
      studentName: row.studentName,
      birthDate: row.birthDate ?? '',
      gradeLevelId: row.gradeLevelId ?? null,
      studyGroupId: null,
      guardianName: row.guardianName,
      guardianPhone: row.guardianPhone,
      guardianEmail: row.guardianEmail ?? '',
      relationship: row.relationship,
      createAccount: true
    });
    this.approving.set(row);
  }

  protected approve(): void {
    const row = this.approving();

    if (!row) {
      return;
    }

    if (this.approveForm.invalid) {
      this.approveForm.markAllAsTouched();
      return;
    }

    const v = this.approveForm.getRawValue();
    this.saving.set(true);

    this.service
      .approve(row.id, {
        studyGroupId: v.studyGroupId,
        studentName: v.studentName.trim(),
        birthDate: v.birthDate || null,
        gradeLevelId: v.gradeLevelId,
        guardianName: v.guardianName.trim(),
        guardianPhone: v.guardianPhone.trim(),
        guardianEmail: v.guardianEmail.trim() || null,
        relationship: v.relationship,
        createAccount: v.createAccount
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe((result) => {
        this.approving.set(null);
        this.notifications.success(
          this.t(result.enrolled ? 'regRequests.approvedEnrolled' : 'regRequests.approved', { name: result.student.fullName })
        );
        if (result.student.newGuardian) {
          this.credentials.set(result.student.newGuardian);
        }
        this.load();
      });
  }

  protected openReject(row: RegistrationRequestDto): void {
    this.rejectReason.reset('');
    this.rejecting.set(row);
  }

  protected reject(): void {
    const row = this.rejecting();

    if (!row || this.rejectReason.invalid) {
      return;
    }

    this.saving.set(true);
    this.service
      .reject(row.id, this.rejectReason.value.trim())
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe(() => {
        this.rejecting.set(null);
        this.notifications.success(this.t('regRequests.rejected'));
        this.load();
      });
  }

  protected copyCredentials(): void {
    const c = this.credentials();

    if (!c?.temporaryPassword) {
      return;
    }

    const text = `${this.t('students.newGuardian.loginName')}: ${c.loginName ?? ''}\n${this.t('students.newGuardian.temporaryPassword')}: ${c.temporaryPassword}`;
    void navigator.clipboard?.writeText(text).then(() => this.notifications.success(this.t('students.newGuardian.copied')));
  }

  private resetPaging(): void {
    this.first.set(0);
    this.pageNumber.set(1);
  }

  private ensureLookups(): void {
    if (this.lookupsLoaded) {
      return;
    }

    this.lookupsLoaded = true;

    this.academic.listGrades({ pageNumber: 1, pageSize: 100 }).subscribe((page) =>
      this.gradeOptions.set(
        [...page.items].sort((a, b) => a.order - b.order).map((g) => ({ label: g.nameAr || g.name, value: g.id }))
      )
    );

    // Groups are optional: an account without Groups.View still approves, without enrolling.
    if (this.auth.hasPermission('Groups.View')) {
      this.scheduling.listGroups({ pageNumber: 1, pageSize: 100, activeOnly: true }).subscribe((page) =>
        this.groupOptions.set(
          page.items.map((g) => ({ label: g.subjectName ? `${g.name} · ${g.subjectName}` : g.name, value: g.id }))
        )
      );
    }
  }
}
