import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  BranchDto,
  CreateBranchRequest,
  PaginatedList,
  PaginationQuery,
  UpdateBranchRequest
} from '../models';
import { ApiService } from './api.service';

@Injectable({ providedIn: 'root' })
export class BranchesService {
  private readonly api = inject(ApiService);

  list(query: PaginationQuery & { activeOnly?: boolean }): Observable<PaginatedList<BranchDto>> {
    return this.api.get<PaginatedList<BranchDto>>('/api/branches', { ...query });
  }

  get(id: string): Observable<BranchDto> {
    return this.api.get<BranchDto>(`/api/branches/${id}`);
  }

  create(request: CreateBranchRequest): Observable<BranchDto> {
    return this.api.post<BranchDto>('/api/branches', request);
  }

  update(id: string, request: UpdateBranchRequest): Observable<BranchDto> {
    return this.api.put<BranchDto>(`/api/branches/${id}`, request);
  }

  remove(id: string): Observable<void> {
    return this.api.delete<void>(`/api/branches/${id}`);
  }
}
