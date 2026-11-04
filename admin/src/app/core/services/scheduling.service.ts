import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AttendanceRowDto,
  AttendanceStatus,
  ClassSessionDto,
  EnrollmentDto,
  GenerateSessionsResultDto,
  PaginatedList,
  PaginationQuery,
  ScheduleSlotDto,
  ScheduleSlotRequest,
  SessionStatus,
  StudyGroupDto,
  StudyGroupRequest
} from '../models';
import { ApiService } from './api.service';

export interface GroupListQuery extends PaginationQuery {
  search?: string;
  subjectId?: string;
  teacherProfileId?: string;
  activeOnly?: boolean;
}

export interface SessionListQuery extends PaginationQuery {
  from?: string;
  to?: string;
  studyGroupId?: string;
  teacherProfileId?: string;
  status?: SessionStatus;
}

/** Study groups, enrollments, weekly slots, sessions and attendance (Mission Phase 3). */
@Injectable({ providedIn: 'root' })
export class SchedulingService {
  private readonly api = inject(ApiService);

  listGroups(query: GroupListQuery): Observable<PaginatedList<StudyGroupDto>> {
    return this.api.get<PaginatedList<StudyGroupDto>>('/api/groups', { ...query });
  }

  getGroup(id: string): Observable<StudyGroupDto> {
    return this.api.get<StudyGroupDto>(`/api/groups/${id}`);
  }

  createGroup(request: StudyGroupRequest): Observable<StudyGroupDto> {
    return this.api.post<StudyGroupDto>('/api/groups', request);
  }

  updateGroup(id: string, request: Partial<StudyGroupRequest>): Observable<StudyGroupDto> {
    return this.api.put<StudyGroupDto>(`/api/groups/${id}`, request);
  }

  removeGroup(id: string): Observable<boolean> {
    return this.api.delete<boolean>(`/api/groups/${id}`);
  }

  enrollments(groupId: string, activeOnly = false): Observable<EnrollmentDto[]> {
    return this.api.get<EnrollmentDto[]>(`/api/groups/${groupId}/enrollments`, { activeOnly });
  }

  enroll(groupId: string, studentProfileId: string, enrolledOn?: string): Observable<EnrollmentDto> {
    return this.api.post<EnrollmentDto>(`/api/groups/${groupId}/enrollments`, {
      studentProfileId,
      enrolledOn: enrolledOn || null
    });
  }

  withdraw(groupId: string, enrollmentId: string): Observable<boolean> {
    return this.api.delete<boolean>(`/api/groups/${groupId}/enrollments/${enrollmentId}`);
  }

  addSlot(groupId: string, request: ScheduleSlotRequest): Observable<ScheduleSlotDto> {
    return this.api.post<ScheduleSlotDto>(`/api/groups/${groupId}/slots`, request);
  }

  removeSlot(groupId: string, slotId: string): Observable<boolean> {
    return this.api.delete<boolean>(`/api/groups/${groupId}/slots/${slotId}`);
  }

  studentEnrollments(studentId: string): Observable<EnrollmentDto[]> {
    return this.api.get<EnrollmentDto[]>(`/api/students/${studentId}/enrollments`);
  }

  listSessions(query: SessionListQuery): Observable<PaginatedList<ClassSessionDto>> {
    return this.api.get<PaginatedList<ClassSessionDto>>('/api/sessions', { ...query });
  }

  generateSessions(from: string, to: string, studyGroupId?: string | null): Observable<GenerateSessionsResultDto> {
    return this.api.post<GenerateSessionsResultDto>('/api/sessions/generate', {
      from,
      to,
      studyGroupId: studyGroupId || null
    });
  }

  updateSession(
    id: string,
    request: { status?: SessionStatus; topic?: string | null; notes?: string | null }
  ): Observable<ClassSessionDto> {
    return this.api.put<ClassSessionDto>(`/api/sessions/${id}`, request);
  }

  attendance(sessionId: string): Observable<AttendanceRowDto[]> {
    return this.api.get<AttendanceRowDto[]>(`/api/sessions/${sessionId}/attendance`);
  }

  markAttendance(
    sessionId: string,
    records: { studentProfileId: string; status: AttendanceStatus; notes?: string | null }[]
  ): Observable<AttendanceRowDto[]> {
    return this.api.put<AttendanceRowDto[]>(`/api/sessions/${sessionId}/attendance`, { records });
  }
}
