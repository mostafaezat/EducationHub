import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AcademicYearDto,
  CreateAcademicYearRequest,
  CreateGradeLevelRequest,
  CreateSubjectRequest,
  GradeLevelDto,
  PaginatedList,
  PaginationQuery,
  SubjectDto,
  UpdateAcademicYearRequest,
  UpdateGradeLevelRequest,
  UpdateSubjectRequest
} from '../models';
import { ApiService } from './api.service';

@Injectable({ providedIn: 'root' })
export class AcademicService {
  private readonly api = inject(ApiService);

  listAcademicYears(
    query: PaginationQuery & { includeArchived?: boolean }
  ): Observable<PaginatedList<AcademicYearDto>> {
    return this.api.get<PaginatedList<AcademicYearDto>>('/api/academic-years', { ...query });
  }

  currentAcademicYear(): Observable<AcademicYearDto> {
    return this.api.get<AcademicYearDto>('/api/academic-years/current');
  }

  academicYearForDate(date: string): Observable<AcademicYearDto> {
    return this.api.get<AcademicYearDto>(`/api/academic-years/for-date/${date}`);
  }

  getAcademicYear(id: string): Observable<AcademicYearDto> {
    return this.api.get<AcademicYearDto>(`/api/academic-years/${id}`);
  }

  createAcademicYear(request: CreateAcademicYearRequest): Observable<AcademicYearDto> {
    return this.api.post<AcademicYearDto>('/api/academic-years', request);
  }

  updateAcademicYear(id: string, request: UpdateAcademicYearRequest): Observable<AcademicYearDto> {
    return this.api.put<AcademicYearDto>(`/api/academic-years/${id}`, request);
  }

  setCurrentAcademicYear(id: string): Observable<AcademicYearDto> {
    return this.api.post<AcademicYearDto>(`/api/academic-years/${id}/set-current`);
  }

  removeAcademicYear(id: string): Observable<boolean> {
    return this.api.delete<boolean>(`/api/academic-years/${id}`);
  }

  listGrades(
    query: PaginationQuery & { activeOnly?: boolean }
  ): Observable<PaginatedList<GradeLevelDto>> {
    return this.api.get<PaginatedList<GradeLevelDto>>('/api/grades', { ...query });
  }

  getGrade(id: string): Observable<GradeLevelDto> {
    return this.api.get<GradeLevelDto>(`/api/grades/${id}`);
  }

  createGrade(request: CreateGradeLevelRequest): Observable<GradeLevelDto> {
    return this.api.post<GradeLevelDto>('/api/grades', request);
  }

  updateGrade(id: string, request: UpdateGradeLevelRequest): Observable<GradeLevelDto> {
    return this.api.put<GradeLevelDto>(`/api/grades/${id}`, request);
  }

  removeGrade(id: string): Observable<boolean> {
    return this.api.delete<boolean>(`/api/grades/${id}`);
  }

  listSubjects(
    query: PaginationQuery & { activeOnly?: boolean }
  ): Observable<PaginatedList<SubjectDto>> {
    return this.api.get<PaginatedList<SubjectDto>>('/api/subjects', { ...query });
  }

  getSubject(id: string): Observable<SubjectDto> {
    return this.api.get<SubjectDto>(`/api/subjects/${id}`);
  }

  createSubject(request: CreateSubjectRequest): Observable<SubjectDto> {
    return this.api.post<SubjectDto>('/api/subjects', request);
  }

  updateSubject(id: string, request: UpdateSubjectRequest): Observable<SubjectDto> {
    return this.api.put<SubjectDto>(`/api/subjects/${id}`, request);
  }

  removeSubject(id: string): Observable<boolean> {
    return this.api.delete<boolean>(`/api/subjects/${id}`);
  }
}
