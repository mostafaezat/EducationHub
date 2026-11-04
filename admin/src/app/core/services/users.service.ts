import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AdminResetPasswordRequest,
  AssignRolesRequest,
  ChangeUserStatusRequest,
  CreateUserRequest,
  PaginatedList,
  PaginationQuery,
  UpdateUserRequest,
  UserDto
} from '../models';
import { ApiService } from './api.service';

@Injectable({ providedIn: 'root' })
export class UsersService {
  private readonly api = inject(ApiService);

  list(
    query: PaginationQuery & { activeOnly?: boolean; role?: string; branchId?: string }
  ): Observable<PaginatedList<UserDto>> {
    return this.api.get<PaginatedList<UserDto>>('/api/users', { ...query });
  }

  get(id: string): Observable<UserDto> {
    return this.api.get<UserDto>(`/api/users/${id}`);
  }

  create(request: CreateUserRequest): Observable<UserDto> {
    return this.api.post<UserDto>('/api/users', request);
  }

  update(id: string, request: UpdateUserRequest): Observable<UserDto> {
    return this.api.put<UserDto>(`/api/users/${id}`, request);
  }

  remove(id: string): Observable<void> {
    return this.api.delete<void>(`/api/users/${id}`);
  }

  setStatus(id: string, request: ChangeUserStatusRequest): Observable<void> {
    return this.api.post<void>(`/api/users/${id}/status`, request);
  }

  resetPassword(id: string, request: AdminResetPasswordRequest): Observable<void> {
    return this.api.post<void>(`/api/users/${id}/reset-password`, request);
  }

  assignRoles(id: string, request: AssignRolesRequest): Observable<UserDto> {
    return this.api.put<UserDto>(`/api/users/${id}/roles`, request);
  }

  byRole(role: string, query: PaginationQuery): Observable<PaginatedList<UserDto>> {
    return this.api.get<PaginatedList<UserDto>>(`/api/users/by-role/${role}`, { ...query });
  }
}
