import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import {
  AppUserResponse, AuditEntryResponse, CreateUserRequest, FactorySetting, PagedResult,
  ReasonCode, ReasonCodeType, RebuildResult, UpdateReasonCodeRequest, UpdateUserRequest,
} from './api.types';

export interface AuditFilters {
  entityName?: string;
  userId?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}

@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);

  // ----- Users -----

  users(): Promise<AppUserResponse[]> {
    return firstValueFrom(this.http.get<AppUserResponse[]>('/api/v1/users'));
  }

  createUser(request: CreateUserRequest): Promise<AppUserResponse> {
    return firstValueFrom(this.http.post<AppUserResponse>('/api/v1/users', request));
  }

  updateUser(id: string, request: UpdateUserRequest): Promise<AppUserResponse> {
    return firstValueFrom(this.http.put<AppUserResponse>(`/api/v1/users/${id}`, request));
  }

  /** Never a delete: the documents this person entered still point at them. */
  deactivateUser(id: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`/api/v1/users/${id}/deactivate`, {}));
  }

  resetPassword(id: string, newPassword: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`/api/v1/users/${id}/reset-password`, { newPassword }));
  }

  // ----- Settings -----

  settings(): Promise<FactorySetting[]> {
    return firstValueFrom(this.http.get<FactorySetting[]>('/api/v1/settings'));
  }

  /** One request for the whole form, however many fields the user touched. */
  saveSettings(values: Record<string, string>): Promise<FactorySetting[]> {
    return firstValueFrom(this.http.put<FactorySetting[]>('/api/v1/settings', { values }));
  }

  // ----- Reason codes -----

  reasonCodes(type?: ReasonCodeType, includeInactive = true): Promise<ReasonCode[]> {
    let params = new HttpParams().set('includeInactive', includeInactive);
    if (type) params = params.set('type', type);

    return firstValueFrom(this.http.get<ReasonCode[]>('/api/v1/reason-codes', { params }));
  }

  updateReasonCode(id: string, request: UpdateReasonCodeRequest): Promise<ReasonCode> {
    return firstValueFrom(this.http.put<ReasonCode>(`/api/v1/reason-codes/${id}`, request));
  }

  deactivateReasonCode(id: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`/api/v1/reason-codes/${id}/deactivate`, {}));
  }

  // ----- Audit -----

  audit(filters: AuditFilters): Promise<PagedResult<AuditEntryResponse>> {
    let params = new HttpParams()
      .set('page', filters.page ?? 1)
      .set('pageSize', filters.pageSize ?? 50);

    if (filters.entityName) params = params.set('entityName', filters.entityName);
    if (filters.userId) params = params.set('userId', filters.userId);
    if (filters.from) params = params.set('from', filters.from);
    if (filters.to) params = params.set('to', filters.to);

    return firstValueFrom(
      this.http.get<PagedResult<AuditEntryResponse>>('/api/v1/audit', { params }),
    );
  }

  // ----- Maintenance -----

  /** Safe at any time: it only ever makes the cached balances match the ledger. */
  rebuildStockBalances(): Promise<RebuildResult> {
    return firstValueFrom(
      this.http.post<RebuildResult>('/api/v1/admin/rebuild-stock-balances', {}),
    );
  }
}
