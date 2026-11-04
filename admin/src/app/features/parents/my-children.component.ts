import { CommonModule } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { finalize } from 'rxjs';
import { TranslateService } from '../../core/i18n/translate.service';
import { ChildLinkDto } from '../../core/models';
import { StudentsService } from '../../core/services/students.service';
import { statusSeverity } from '../students/student-utils';

/** A signed-in parent's own children (scoped server-side to the caller's account). */
@Component({
  selector: 'app-my-children',
  imports: [CommonModule, ButtonModule, TableModule, TagModule],
  template: `
    <div class="page-header">
      <div>
        <h1>{{ t('myChildren.title') }}</h1>
        <div class="subtitle">{{ t('myChildren.subtitle') }}</div>
      </div>
      <p-button icon="pi pi-refresh" [text]="true" (onClick)="load()" />
    </div>

    <div class="card">
      <p-table [value]="rows()" [loading]="loading()" styleClass="p-datatable-sm">
        <ng-template pTemplate="header">
          <tr>
            <th>{{ t('students.fullName') }}</th>
            <th style="width:9rem">{{ t('students.studentNumber') }}</th>
            <th style="width:9rem">{{ t('students.grade') }}</th>
            <th style="width:9rem">{{ t('students.relationshipLabel') }}</th>
            <th style="width:8rem">{{ t('common.status') }}</th>
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-row>
          <tr>
            <td>{{ row.studentFullName }}</td>
            <td>{{ row.studentNumber || '—' }}</td>
            <td>{{ row.gradeLevelName || '—' }}</td>
            <td>{{ t('students.relationship.' + row.relationship) }}</td>
            <td><p-tag [value]="t('students.status.' + row.status)" [severity]="severity(row.status)" /></td>
          </tr>
        </ng-template>
        <ng-template pTemplate="emptymessage">
          <tr>
            <td colspan="5">
              <div class="state-panel"><i class="pi pi-users"></i>{{ t('myChildren.empty') }}</div>
            </td>
          </tr>
        </ng-template>
      </p-table>
    </div>
  `,
  styles: [
    `
      .subtitle {
        color: #64748b;
        font-size: 0.85rem;
      }
    `
  ]
})
export class MyChildrenComponent implements OnInit {
  private readonly service = inject(StudentsService);

  protected readonly t = inject(TranslateService).t;
  protected readonly severity = statusSeverity;
  protected readonly rows = signal<ChildLinkDto[]>([]);
  protected readonly loading = signal(false);

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.service
      .myChildren()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe((children) => this.rows.set(children ?? []));
  }
}
