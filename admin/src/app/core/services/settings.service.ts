import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { TenantSettingsDto, UpdateTenantSettingsRequest } from '../models';
import { ApiService } from './api.service';

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private readonly api = inject(ApiService);

  get(): Observable<TenantSettingsDto> {
    return this.api.get<TenantSettingsDto>('/api/settings');
  }

  update(request: UpdateTenantSettingsRequest): Observable<TenantSettingsDto> {
    return this.api.put<TenantSettingsDto>('/api/settings', request);
  }
}
