import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  CreateTenantRequest,
  PaginatedList,
  PaginationQuery,
  TenantDto,
  UpdateTenantRequest
} from '../models';
import { ApiService } from './api.service';

@Injectable({ providedIn: 'root' })
export class TenantsService {
  private readonly api = inject(ApiService);

  list(query: PaginationQuery): Observable<PaginatedList<TenantDto>> {
    return this.api.get<PaginatedList<TenantDto>>('/api/tenants', { ...query });
  }

  get(id: string): Observable<TenantDto> {
    return this.api.get<TenantDto>(`/api/tenants/${id}`);
  }

  create(request: CreateTenantRequest): Observable<TenantDto> {
    return this.api.post<TenantDto>('/api/tenants', request);
  }

  update(id: string, request: UpdateTenantRequest): Observable<TenantDto> {
    return this.api.put<TenantDto>(`/api/tenants/${id}`, request);
  }

  remove(id: string): Observable<void> {
    return this.api.delete<void>(`/api/tenants/${id}`);
  }
}
