import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { API_BASE_URL } from '../app-config';
import {
  ApproveRegistrationRequest,
  ApproveRegistrationResultDto,
  PaginatedList,
  PaginationQuery,
  PublicRegistrationFormDto,
  RegistrationRequestDto,
  RegistrationRequestStatus,
  Result,
  SubmitRegistrationRequest
} from '../models';
import { ApiService } from './api.service';
import { AuthService } from './auth.service';

export interface RegistrationRequestQuery extends PaginationQuery {
  status?: RegistrationRequestStatus;
  search?: string;
}

/** The public pre-registration form and the staff review of its requests. */
@Injectable({ providedIn: 'root' })
export class RegistrationService {
  private readonly api = inject(ApiService);
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  // ---- Public (anonymous): the interceptor sends no token for /api/public/, so the
  // language header is set here to get server messages in the page's language.

  form(slug: string): Observable<PublicRegistrationFormDto> {
    return this.http
      .get<Result<PublicRegistrationFormDto>>(this.publicUrl(slug), { headers: this.languageHeaders() })
      .pipe(map((r) => r.data as PublicRegistrationFormDto));
  }

  submit(slug: string, request: SubmitRegistrationRequest): Observable<string> {
    return this.http
      .post<Result<unknown>>(this.publicUrl(slug), request, { headers: this.languageHeaders() })
      .pipe(map((r) => r.message ?? ''));
  }

  // ---- Staff

  list(query: RegistrationRequestQuery): Observable<PaginatedList<RegistrationRequestDto>> {
    return this.api.get<PaginatedList<RegistrationRequestDto>>('/api/registration-requests', { ...query });
  }

  link(): Observable<string> {
    return this.api
      .get<{ tenantSlug: string }>('/api/registration-requests/link')
      .pipe(map((d) => d.tenantSlug));
  }

  approve(id: string, request: ApproveRegistrationRequest): Observable<ApproveRegistrationResultDto> {
    return this.api.post<ApproveRegistrationResultDto>(`/api/registration-requests/${id}/approve`, request);
  }

  reject(id: string, reason: string): Observable<RegistrationRequestDto> {
    return this.api.post<RegistrationRequestDto>(`/api/registration-requests/${id}/reject`, { reason });
  }

  private publicUrl(slug: string): string {
    return `${API_BASE_URL}/api/public/registration/${encodeURIComponent(slug)}`;
  }

  private languageHeaders(): HttpHeaders {
    return new HttpHeaders({ 'Accept-Language': this.auth.preferredLanguage() });
  }
}
