import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  ChildLinkDto,
  CreateParentRequest,
  CreateStudentRequest,
  GuardianLinkRequest,
  PaginatedList,
  PaginationQuery,
  ParentDto,
  StudentDto,
  StudentStatus,
  UpdateParentRequest,
  UpdateStudentRequest
} from '../models';
import { ApiService } from './api.service';

export interface StudentListQuery extends PaginationQuery {
  search?: string;
  gradeLevelId?: string;
  status?: StudentStatus;
}

/** Students, parents and guardian links (Mission Phase 2.5). */
@Injectable({ providedIn: 'root' })
export class StudentsService {
  private readonly api = inject(ApiService);

  list(query: StudentListQuery): Observable<PaginatedList<StudentDto>> {
    return this.api.get<PaginatedList<StudentDto>>('/api/students', { ...query });
  }

  get(id: string): Observable<StudentDto> {
    return this.api.get<StudentDto>(`/api/students/${id}`);
  }

  create(request: CreateStudentRequest): Observable<StudentDto> {
    return this.api.post<StudentDto>('/api/students', request);
  }

  update(id: string, request: UpdateStudentRequest): Observable<StudentDto> {
    return this.api.put<StudentDto>(`/api/students/${id}`, request);
  }

  /** Replaces the student's guardians; an empty list clears them. */
  setGuardians(id: string, guardians: GuardianLinkRequest[]): Observable<StudentDto> {
    return this.api.put<StudentDto>(`/api/students/${id}/guardians`, { guardians });
  }

  remove(id: string): Observable<boolean> {
    return this.api.delete<boolean>(`/api/students/${id}`);
  }

  listParents(query: PaginationQuery & { search?: string }): Observable<PaginatedList<ParentDto>> {
    return this.api.get<PaginatedList<ParentDto>>('/api/parents', { ...query });
  }

  getParent(id: string): Observable<ParentDto> {
    return this.api.get<ParentDto>(`/api/parents/${id}`);
  }

  createParent(request: CreateParentRequest): Observable<ParentDto> {
    return this.api.post<ParentDto>('/api/parents', request);
  }

  updateParent(id: string, request: UpdateParentRequest): Observable<ParentDto> {
    return this.api.put<ParentDto>(`/api/parents/${id}`, request);
  }

  removeParent(id: string): Observable<boolean> {
    return this.api.delete<boolean>(`/api/parents/${id}`);
  }

  /** The signed-in parent's own children. */
  myChildren(): Observable<ChildLinkDto[]> {
    return this.api.get<ChildLinkDto[]>('/api/parents/me/children');
  }
}
