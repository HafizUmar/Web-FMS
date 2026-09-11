import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import {
  DailyStockResponse, DashboardResponse, OutstandingResponse,
  SalesGroupBy, SalesSummaryResponse,
} from './api.types';
import { ErrorCodes, ProblemDetails, isProblemDetails } from './problem-details';

/** Whether a report can be downloaded as a file, and the server's own reason when it cannot. */
export interface ExportAvailability {
  available: boolean;
  reason: string;
}

@Injectable({ providedIn: 'root' })
export class ReportsService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/reports';

  /** Cached server-side for a minute, so polling faster than that returns the same object. */
  dashboard(): Promise<DashboardResponse> {
    return firstValueFrom(this.http.get<DashboardResponse>(`${this.base}/dashboard`));
  }

  dailyStock(date?: string): Promise<DailyStockResponse> {
    const params = date ? new HttpParams().set('date', date) : undefined;
    return firstValueFrom(this.http.get<DailyStockResponse>(`${this.base}/daily-stock`, { params }));
  }

  outstanding(): Promise<OutstandingResponse> {
    return firstValueFrom(this.http.get<OutstandingResponse>(`${this.base}/outstanding`));
  }

  salesSummary(from?: string, to?: string, groupBy: SalesGroupBy = 'Customer'): Promise<SalesSummaryResponse> {
    let params = new HttpParams().set('groupBy', groupBy);
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);

    return firstValueFrom(this.http.get<SalesSummaryResponse>(`${this.base}/sales-summary`, { params }));
  }

  /**
   * RP-08 routes PDF and XLSX from every report, and the server answers 501 with a
   * detail written for the reader until the renderer is wired in.
   *
   * Asking the server once rather than hard-coding "coming soon" means the export
   * buttons quote the server's own explanation - and enable themselves the day the
   * renderer lands, with no client change.
   */
  async exportAvailability(): Promise<ExportAvailability> {
    try {
      await firstValueFrom(
        this.http.get(`${this.base}/daily-stock`, {
          params: new HttpParams().set('format', 'pdf'),
        }),
      );

      return { available: true, reason: '' };
    } catch (error) {
      const problem = error as ProblemDetails;

      if (isProblemDetails(problem) && problem.code === ErrorCodes.NotImplemented) {
        return { available: false, reason: problem.detail };
      }

      // The probe asks for JSON, so the day the renderer answers with PDF bytes the
      // parse fails on an HTTP 200. That failure means the file exists, not that the
      // export is missing.
      if (problem?.status === 200) return { available: true, reason: '' };

      return { available: false, reason: 'Downloads are not available right now.' };
    }
  }
}
