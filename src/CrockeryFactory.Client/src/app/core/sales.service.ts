import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import {
  CancelRequest, CreateCustomerRequest, CreateDispatchRequest, CreatePaymentRequest,
  Customer, Dispatch, OutstandingResponse, PagedResult, Payment,
  StatementResponse, UpdateCustomerRequest,
} from './api.types';

export interface CustomerQuery {
  search?: string;
  includeInactive?: boolean;
  page?: number;
  pageSize?: number;
}

export interface DocumentQuery {
  customerId?: string;
  from?: string;
  to?: string;
  includeCancelled?: boolean;
  page?: number;
  pageSize?: number;
}

export interface CustomerWithVersion {
  customer: Customer;
  etag: string | null;
}

@Injectable({ providedIn: 'root' })
export class SalesService {
  private readonly http = inject(HttpClient);

  // ---------------------------------------------------------------- customers

  listCustomers(query: CustomerQuery): Promise<PagedResult<Customer>> {
    let params = new HttpParams()
      .set('page', String(query.page ?? 1))
      .set('pageSize', String(query.pageSize ?? 25));

    if (query.search?.trim()) params = params.set('search', query.search.trim());
    if (query.includeInactive) params = params.set('includeInactive', 'true');

    return firstValueFrom(this.http.get<PagedResult<Customer>>('/api/v1/customers', { params }));
  }

  async getCustomer(id: string): Promise<CustomerWithVersion> {
    const response = await firstValueFrom(
      this.http.get<Customer>(`/api/v1/customers/${id}`, { observe: 'response' }),
    );

    return { customer: response.body!, etag: response.headers.get('ETag') };
  }

  createCustomer(request: CreateCustomerRequest, idempotencyKey: string): Promise<Customer> {
    return firstValueFrom(
      this.http.post<Customer>('/api/v1/customers', request, {
        headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }),
      }),
    );
  }

  updateCustomer(id: string, request: UpdateCustomerRequest, etag: string): Promise<Customer> {
    return firstValueFrom(
      this.http.put<Customer>(`/api/v1/customers/${id}`, request, {
        headers: new HttpHeaders({ 'If-Match': etag }),
      }),
    );
  }

  deactivateCustomer(id: string): Promise<unknown> {
    return firstValueFrom(this.http.post(`/api/v1/customers/${id}/deactivate`, {}));
  }

  outstanding(): Promise<OutstandingResponse> {
    return firstValueFrom(this.http.get<OutstandingResponse>('/api/v1/customers/outstanding'));
  }

  statement(id: string, from?: string, to?: string): Promise<StatementResponse> {
    let params = new HttpParams();
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);

    return firstValueFrom(
      this.http.get<StatementResponse>(`/api/v1/customers/${id}/statement`, { params }),
    );
  }

  // --------------------------------------------------------------- dispatches

  listDispatches(query: DocumentQuery): Promise<PagedResult<Dispatch>> {
    return firstValueFrom(
      this.http.get<PagedResult<Dispatch>>('/api/v1/dispatches', { params: this.documentParams(query) }),
    );
  }

  getDispatch(id: string): Promise<Dispatch> {
    return firstValueFrom(this.http.get<Dispatch>(`/api/v1/dispatches/${id}`));
  }

  createDispatch(request: CreateDispatchRequest, idempotencyKey: string): Promise<Dispatch> {
    return firstValueFrom(
      this.http.post<Dispatch>('/api/v1/dispatches', request, {
        headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }),
      }),
    );
  }

  cancelDispatch(id: string, request: CancelRequest): Promise<Dispatch> {
    return firstValueFrom(this.http.post<Dispatch>(`/api/v1/dispatches/${id}/cancel`, request));
  }

  // ----------------------------------------------------------------- payments

  listPayments(query: DocumentQuery): Promise<PagedResult<Payment>> {
    return firstValueFrom(
      this.http.get<PagedResult<Payment>>('/api/v1/payments', { params: this.documentParams(query) }),
    );
  }

  createPayment(request: CreatePaymentRequest, idempotencyKey: string): Promise<Payment> {
    return firstValueFrom(
      this.http.post<Payment>('/api/v1/payments', request, {
        headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }),
      }),
    );
  }

  cancelPayment(id: string, request: CancelRequest): Promise<Payment> {
    return firstValueFrom(this.http.post<Payment>(`/api/v1/payments/${id}/cancel`, request));
  }

  private documentParams(query: DocumentQuery): HttpParams {
    let params = new HttpParams()
      .set('page', String(query.page ?? 1))
      .set('pageSize', String(query.pageSize ?? 25));

    if (query.customerId) params = params.set('customerId', query.customerId);
    if (query.from) params = params.set('from', query.from);
    if (query.to) params = params.set('to', query.to);
    if (query.includeCancelled) params = params.set('includeCancelled', 'true');

    return params;
  }
}
