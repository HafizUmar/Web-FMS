import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import {
  AdjustmentResponse,
  CreateAdjustmentRequest,
  PagedResult,
  QualityGrade,
  StockMovementItem,
  StockResponse,
} from './api.types';

export interface StockQuery {
  search?: string;
  grade?: QualityGrade;
  onlyInStock?: boolean;
  /** Omit for the live figure. Supplying it switches the server to the slower ledger sum. */
  asOf?: string;
}

export interface MovementQuery {
  grade?: QualityGrade;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}

@Injectable({ providedIn: 'root' })
export class StockService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/stock';

  get(query: StockQuery): Promise<StockResponse> {
    let params = new HttpParams();

    if (query.search?.trim()) params = params.set('search', query.search.trim());
    if (query.grade) params = params.set('grade', query.grade);
    if (query.onlyInStock) params = params.set('onlyInStock', 'true');
    if (query.asOf) params = params.set('asOf', query.asOf);

    return firstValueFrom(this.http.get<StockResponse>(this.base, { params }));
  }

  movements(productId: string, query: MovementQuery): Promise<PagedResult<StockMovementItem>> {
    let params = new HttpParams()
      .set('page', String(query.page ?? 1))
      .set('pageSize', String(query.pageSize ?? 50));

    if (query.grade) params = params.set('grade', query.grade);
    if (query.from) params = params.set('from', query.from);
    if (query.to) params = params.set('to', query.to);

    return firstValueFrom(
      this.http.get<PagedResult<StockMovementItem>>(`${this.base}/${productId}/movements`, { params }),
    );
  }

  adjust(request: CreateAdjustmentRequest, idempotencyKey: string): Promise<AdjustmentResponse> {
    return firstValueFrom(
      this.http.post<AdjustmentResponse>(`${this.base}/adjustments`, request, {
        headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }),
      }),
    );
  }
}
