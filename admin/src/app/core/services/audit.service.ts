import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { AuditAction, AuditLogDto, AuditSummaryDto, LoginHistoryDto, PaginatedList, PaginationQuery } from '../models';
import { ApiService } from './api.service';

@Injectable({ providedIn: 'root' })
export class AuditService {
  private readonly api = inject(ApiService);

  logs(
    query: PaginationQuery & {
      from?: string;
      to?: string;
      userId?: string;
      action?: AuditAction;
      entityName?: string;
      entityId?: string;
    }
  ): Observable<PaginatedList<AuditLogDto>> {
    return this.api.get<PaginatedList<AuditLogDto>>('/api/audit/logs', { ...query });
  }

  log(id: string): Observable<AuditLogDto> {
    return this.api.get<AuditLogDto>(`/api/audit/logs/${id}`);
  }

  summary(params: { from?: string; to?: string; top?: number }): Observable<AuditSummaryDto[]> {
    return this.api.get<AuditSummaryDto[]>('/api/audit/summary', { ...params });
  }

  logins(
    query: PaginationQuery & { userId?: string; succeededOnly?: boolean; failedOnly?: boolean }
  ): Observable<PaginatedList<LoginHistoryDto>> {
    return this.api.get<PaginatedList<LoginHistoryDto>>('/api/audit/logins', { ...query });
  }
}
