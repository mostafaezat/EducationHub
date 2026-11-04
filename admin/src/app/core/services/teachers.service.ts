import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  CompensationRuleDto,
  CreateCompensationRuleRequest,
  CreateTeacherRequest,
  PaginatedList,
  PaginationQuery,
  TeacherDto,
  UpdateCompensationRuleRequest,
  UpdateTeacherRequest
} from '../models';
import { ApiService } from './api.service';

@Injectable({ providedIn: 'root' })
export class TeachersService {
  private readonly api = inject(ApiService);

  list(query: PaginationQuery): Observable<PaginatedList<TeacherDto>> {
    // The teachers endpoint filters on its own `search` parameter (name, e-mail, staff
    // number); the generic `searchTerm` cannot see the joined account fields.
    return this.api.get<PaginatedList<TeacherDto>>('/api/teachers', {
      ...query,
      searchTerm: undefined,
      search: query.searchTerm
    });
  }

  get(id: string): Observable<TeacherDto> {
    return this.api.get<TeacherDto>(`/api/teachers/${id}`);
  }

  create(request: CreateTeacherRequest): Observable<TeacherDto> {
    return this.api.post<TeacherDto>('/api/teachers', request);
  }

  update(id: string, request: UpdateTeacherRequest): Observable<TeacherDto> {
    return this.api.put<TeacherDto>(`/api/teachers/${id}`, request);
  }

  /** Replaces the teacher's subject set; an empty list clears it. */
  assignSubjects(id: string, subjectIds: string[]): Observable<TeacherDto> {
    return this.api.put<TeacherDto>(`/api/teachers/${id}/subjects`, { subjectIds });
  }

  remove(id: string): Observable<boolean> {
    return this.api.delete<boolean>(`/api/teachers/${id}`);
  }

  listRules(
    query: PaginationQuery & { teacherProfileId?: string }
  ): Observable<PaginatedList<CompensationRuleDto>> {
    return this.api.get<PaginatedList<CompensationRuleDto>>('/api/compensation-rules', {
      ...query
    });
  }

  createRule(request: CreateCompensationRuleRequest): Observable<CompensationRuleDto> {
    return this.api.post<CompensationRuleDto>('/api/compensation-rules', request);
  }

  updateRule(id: string, request: UpdateCompensationRuleRequest): Observable<CompensationRuleDto> {
    return this.api.put<CompensationRuleDto>(`/api/compensation-rules/${id}`, request);
  }

  removeRule(id: string): Observable<boolean> {
    return this.api.delete<boolean>(`/api/compensation-rules/${id}`);
  }
}
