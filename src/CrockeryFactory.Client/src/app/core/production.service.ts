import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import {
  CancelRequest,
  CreateProductionEntryRequest,
  PagedResult,
  ProductionEntry,
  ProductionGroupBy,
  ProductionSummaryRow,
} from './api.types';

export interface ProductionQuery {
  productId?: string;
  from?: string;
  to?: string;
  includeCancelled?: boolean;
  page?: number;
  pageSize?: number;
}

@Injectable({ providedIn: 'root' })
export class ProductionService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/production-entries';

  list(query: ProductionQuery): Promise<PagedResult<ProductionEntry>> {
    let params = new HttpParams()
      .set('page', String(query.page ?? 1))
      .set('pageSize', String(query.pageSize ?? 25));

    if (query.productId) params = params.set('productId', query.productId);
    if (query.from) params = params.set('from', query.from);
    if (query.to) params = params.set('to', query.to);
    if (query.includeCancelled) params = params.set('includeCancelled', 'true');

    return firstValueFrom(this.http.get<PagedResult<ProductionEntry>>(this.base, { params }));
  }

  get(id: string): Promise<ProductionEntry> {
    return firstValueFrom(this.http.get<ProductionEntry>(`${this.base}/${id}`));
  }

  create(request: CreateProductionEntryRequest, idempotencyKey: string): Promise<ProductionEntry> {
    return firstValueFrom(
      this.http.post<ProductionEntry>(this.base, request, {
        headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }),
      }),
    );
  }

  cancel(id: string, request: CancelRequest): Promise<ProductionEntry> {
    return firstValueFrom(this.http.post<ProductionEntry>(`${this.base}/${id}/cancel`, request));
  }

  summary(
    from?: string, to?: string, productId?: string, groupBy: ProductionGroupBy = 'Product',
  ): Promise<ProductionSummaryRow[]> {
    let params = new HttpParams().set('groupBy', groupBy);
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    if (productId) params = params.set('productId', productId);

    return firstValueFrom(this.http.get<ProductionSummaryRow[]>(`${this.base}/summary`, { params }));
  }
}
