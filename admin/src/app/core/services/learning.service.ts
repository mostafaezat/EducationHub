import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  ExamDto,
  ExamResultRowDto,
  HomeworkDto,
  NotificationFeedDto,
  SubmissionRowDto,
  SubmissionStatus
} from '../models';
import { ApiService } from './api.service';

/** Homework, exams and the signed-in user's notifications (Mission Phase 6). */
@Injectable({ providedIn: 'root' })
export class LearningService {
  private readonly api = inject(ApiService);

  homework(query: { studyGroupId?: string; dueFrom?: string; dueTo?: string }): Observable<HomeworkDto[]> {
    return this.api.get<HomeworkDto[]>('/api/homework', { ...query });
  }

  createHomework(request: {
    studyGroupId: string;
    title: string;
    description?: string | null;
    dueDate: string;
    maxGrade?: number | null;
  }): Observable<HomeworkDto> {
    return this.api.post<HomeworkDto>('/api/homework', request);
  }

  updateHomework(
    id: string,
    request: { title?: string; description?: string | null; dueDate?: string; maxGrade?: number | null }
  ): Observable<HomeworkDto> {
    return this.api.put<HomeworkDto>(`/api/homework/${id}`, request);
  }

  removeHomework(id: string): Observable<boolean> {
    return this.api.delete<boolean>(`/api/homework/${id}`);
  }

  submissions(id: string): Observable<SubmissionRowDto[]> {
    return this.api.get<SubmissionRowDto[]>(`/api/homework/${id}/submissions`);
  }

  saveSubmissions(
    id: string,
    submissions: { studentProfileId: string; status: SubmissionStatus; grade?: number | null; feedback?: string | null }[]
  ): Observable<SubmissionRowDto[]> {
    return this.api.put<SubmissionRowDto[]>(`/api/homework/${id}/submissions`, { submissions });
  }

  exams(studyGroupId?: string): Observable<ExamDto[]> {
    return this.api.get<ExamDto[]>('/api/exams', { studyGroupId });
  }

  createExam(request: {
    studyGroupId: string;
    title: string;
    examDate: string;
    maxScore: number;
    notes?: string | null;
  }): Observable<ExamDto> {
    return this.api.post<ExamDto>('/api/exams', request);
  }

  updateExam(
    id: string,
    request: { title?: string; examDate?: string; maxScore?: number; notes?: string | null }
  ): Observable<ExamDto> {
    return this.api.put<ExamDto>(`/api/exams/${id}`, request);
  }

  removeExam(id: string): Observable<boolean> {
    return this.api.delete<boolean>(`/api/exams/${id}`);
  }

  results(id: string): Observable<ExamResultRowDto[]> {
    return this.api.get<ExamResultRowDto[]>(`/api/exams/${id}/results`);
  }

  saveResults(
    id: string,
    results: { studentProfileId: string; score?: number | null; isAbsent: boolean; notes?: string | null }[]
  ): Observable<ExamResultRowDto[]> {
    return this.api.put<ExamResultRowDto[]>(`/api/exams/${id}/results`, { results });
  }

  myNotifications(take = 20, unreadOnly = false): Observable<NotificationFeedDto> {
    return this.api.get<NotificationFeedDto>('/api/notifications', { take, unreadOnly });
  }

  markRead(id: string): Observable<number> {
    return this.api.post<number>(`/api/notifications/${id}/read`);
  }

  markAllRead(): Observable<number> {
    return this.api.post<number>('/api/notifications/read-all');
  }
}
