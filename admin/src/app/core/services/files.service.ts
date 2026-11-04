import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { API_BASE_URL } from '../app-config';
import { Result, StoredFileDto } from '../models';
import { ApiService } from './api.service';

export type FileOwner = 'Student' | 'Parent' | 'Teacher';

/** Documents attached to students, parents and teachers (Mission Phase 2.5b). */
@Injectable({ providedIn: 'root' })
export class FilesService {
  private readonly api = inject(ApiService);
  private readonly http = inject(HttpClient);

  list(ownerEntityName: FileOwner, ownerEntityId: string): Observable<StoredFileDto[]> {
    return this.api.get<StoredFileDto[]>('/api/files', { ownerEntityName, ownerEntityId });
  }

  upload(
    file: File,
    ownerEntityName: FileOwner,
    ownerEntityId: string,
    category: string,
    description?: string | null
  ): Observable<StoredFileDto> {
    const form = new FormData();
    form.append('File', file, file.name);
    form.append('Category', category);
    form.append('OwnerEntityName', ownerEntityName);
    form.append('OwnerEntityId', ownerEntityId);

    if (description) {
      form.append('Description', description);
    }

    return this.http
      .post<Result<StoredFileDto>>(`${API_BASE_URL}/api/files`, form)
      .pipe(map((result) => result.data as StoredFileDto));
  }

  /** Downloads the content through the authenticated client (the bearer token is attached). */
  download(id: string): Observable<Blob> {
    return this.http.get(`${API_BASE_URL}/api/files/${id}/content`, { responseType: 'blob' });
  }

  remove(id: string): Observable<boolean> {
    return this.api.delete<boolean>(`/api/files/${id}`);
  }
}
