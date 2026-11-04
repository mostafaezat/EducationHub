import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AssignPermissionsRequest,
  CreateRoleRequest,
  PaginatedList,
  PaginationQuery,
  PermissionAreaDto,
  RoleDto,
  UpdateRoleRequest
} from '../models';
import { ApiService } from './api.service';

@Injectable({ providedIn: 'root' })
export class RolesService {
  private readonly api = inject(ApiService);

  list(
    query: PaginationQuery & { activeOnly?: boolean }
  ): Observable<PaginatedList<RoleDto>> {
    return this.api.get<PaginatedList<RoleDto>>('/api/roles', { ...query });
  }

  get(id: string): Observable<RoleDto> {
    return this.api.get<RoleDto>(`/api/roles/${id}`);
  }

  create(request: CreateRoleRequest): Observable<RoleDto> {
    return this.api.post<RoleDto>('/api/roles', request);
  }

  update(id: string, request: UpdateRoleRequest): Observable<RoleDto> {
    return this.api.put<RoleDto>(`/api/roles/${id}`, request);
  }

  assignPermissions(id: string, request: AssignPermissionsRequest): Observable<RoleDto> {
    return this.api.put<RoleDto>(`/api/roles/${id}/permissions`, request);
  }

  remove(id: string): Observable<boolean> {
    return this.api.delete<boolean>(`/api/roles/${id}`);
  }

  catalog(): Observable<PermissionAreaDto[]> {
    return this.api.get<PermissionAreaDto[]>('/api/permissions');
  }
}
