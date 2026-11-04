import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { API_BASE_URL } from '../app-config';
import { Result } from '../models';

export type ParamValue = string | number | boolean | Date | null | undefined;

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  get<T>(path: string, params?: Record<string, ParamValue>): Observable<T> {
    return this.http
      .get<Result<T>>(this.url(path), { params: this.toParams(params) })
      .pipe(map((result) => this.unwrap(result)));
  }

  post<T>(path: string, body?: unknown): Observable<T> {
    return this.http
      .post<Result<T>>(this.url(path), body ?? {})
      .pipe(map((result) => this.unwrap(result)));
  }

  put<T>(path: string, body?: unknown): Observable<T> {
    return this.http
      .put<Result<T>>(this.url(path), body ?? {})
      .pipe(map((result) => this.unwrap(result)));
  }

  delete<T>(path: string): Observable<T> {
    return this.http.delete<Result<T>>(this.url(path)).pipe(map((result) => this.unwrap(result)));
  }

  private url(path: string): string {
    return `${API_BASE_URL}${path}`;
  }

  private unwrap<T>(result: Result<T> | null | undefined): T {
    return (result?.data ?? undefined) as T;
  }

  private toParams(params?: Record<string, ParamValue>): HttpParams {
    let httpParams = new HttpParams();

    if (!params) {
      return httpParams;
    }

    for (const [key, value] of Object.entries(params)) {
      if (value === null || value === undefined || value === '') {
        continue;
      }

      httpParams = httpParams.set(
        key,
        value instanceof Date ? value.toISOString() : String(value)
      );
    }

    return httpParams;
  }
}
