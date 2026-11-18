import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { API_BASE_URL } from '../app-config';
import { FamilyStudentOverviewDto, Result, SubmissionFileDto } from '../models';
import { ApiService } from './api.service';

/** The signed-in student's / parent's own family view (scoped server-side to the caller). */
@Injectable({ providedIn: 'root' })
export class FamilyService {
  private readonly api = inject(ApiService);
  private readonly http = inject(HttpClient);

  overview(): Observable<FamilyStudentOverviewDto[]> {
    return this.api.get<FamilyStudentOverviewDto[]>('/api/my/family');
  }

  /** Uploads a homework answer for the student (a parent passes which child). */
  submitHomework(homeworkId: string, studentProfileId: string, file: File, note: string | null): Observable<SubmissionFileDto> {
    const form = new FormData();
    form.append('File', file, file.name);
    form.append('StudentProfileId', studentProfileId);

    if (note) {
      form.append('Note', note);
    }

    return this.http
      .post<Result<SubmissionFileDto>>(`${API_BASE_URL}/api/my/homework/${homeworkId}/submission`, form)
      .pipe(map((result) => result.data as SubmissionFileDto));
  }

  removeHomeworkFile(homeworkId: string, fileId: string): Observable<boolean> {
    return this.api.delete<boolean>(`/api/my/homework/${homeworkId}/files/${fileId}`);
  }

  downloadOwnFile(fileId: string): Observable<Blob> {
    return this.http.get(`${API_BASE_URL}/api/my/files/${fileId}/content`, { responseType: 'blob' });
  }
}
