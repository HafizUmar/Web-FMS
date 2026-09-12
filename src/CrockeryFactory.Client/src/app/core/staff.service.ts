import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import {
  AttendanceSheet, AttendanceStatus, CreateEmployeeRequest, Employee,
  PayrollPreview, PayrollRun, UpdateEmployeeRequest, WageRate,
} from './api.types';

export interface MarkLine {
  employeeId: string;
  status: AttendanceStatus;
  overtimeHours: number;
  notes?: string | null;
}

@Injectable({ providedIn: 'root' })
export class StaffService {
  private readonly http = inject(HttpClient);

  // ----- Employees -----

  employees(includeInactive = false): Promise<Employee[]> {
    const params = new HttpParams().set('includeInactive', includeInactive);
    return firstValueFrom(this.http.get<Employee[]>('/api/v1/employees', { params }));
  }

  createEmployee(request: CreateEmployeeRequest): Promise<Employee> {
    return firstValueFrom(this.http.post<Employee>('/api/v1/employees', request));
  }

  updateEmployee(id: string, request: UpdateEmployeeRequest): Promise<Employee> {
    return firstValueFrom(this.http.put<Employee>(`/api/v1/employees/${id}`, request));
  }

  /** Never a delete: their payslips and attendance have to stay readable. */
  markAsLeft(id: string, leftOn: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`/api/v1/employees/${id}/deactivate`, { leftOn }));
  }

  wageRates(id: string): Promise<WageRate[]> {
    return firstValueFrom(this.http.get<WageRate[]>(`/api/v1/employees/${id}/wage-rates`));
  }

  setWageRate(id: string, dailyRate: number, effectiveFrom: string): Promise<WageRate> {
    return firstValueFrom(
      this.http.post<WageRate>(`/api/v1/employees/${id}/wage-rates`, { dailyRate, effectiveFrom }),
    );
  }

  // ----- Attendance -----

  /** Returns everyone on the roll for that date, marked or not. */
  sheet(date: string): Promise<AttendanceSheet> {
    const params = new HttpParams().set('date', date);
    return firstValueFrom(this.http.get<AttendanceSheet>('/api/v1/attendance', { params }));
  }

  /** The whole sheet at once - a clerk marks the crew, not one man at a time. */
  mark(date: string, lines: MarkLine[]): Promise<AttendanceSheet> {
    return firstValueFrom(this.http.post<AttendanceSheet>('/api/v1/attendance', { date, lines }));
  }

  // ----- Payroll -----

  /**
   * What the week would pay. Shares its whole calculation with the create path on the
   * server, so what is approved here is what gets written.
   */
  preview(from?: string, to?: string): Promise<PayrollPreview> {
    let params = new HttpParams();
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);

    return firstValueFrom(this.http.get<PayrollPreview>('/api/v1/payroll/preview', { params }));
  }

  payrollRuns(take = 20): Promise<PayrollRun[]> {
    const params = new HttpParams().set('take', take);
    return firstValueFrom(this.http.get<PayrollRun[]>('/api/v1/payroll', { params }));
  }

  createPayroll(periodStart: string, periodEnd: string, notes?: string | null): Promise<PayrollRun> {
    return firstValueFrom(
      this.http.post<PayrollRun>('/api/v1/payroll', { periodStart, periodEnd, notes }),
    );
  }

  cancelPayroll(id: string, reason: string): Promise<PayrollRun> {
    return firstValueFrom(this.http.post<PayrollRun>(`/api/v1/payroll/${id}/cancel`, { reason }));
  }
}

/**
 * The Saturday-to-Friday week containing a date, matching the server's PayrollMath.
 *
 * Duplicated rather than fetched because the screen needs it before it can ask for
 * anything - the default period is decided before the first request. The server is still
 * the authority: it recomputes the period it was given.
 */
export function weekContaining(date: Date): { start: string; end: string } {
  const offset = (date.getDay() - 6 + 7) % 7;

  const start = new Date(date);
  start.setDate(date.getDate() - offset);

  const end = new Date(start);
  end.setDate(start.getDate() + 6);

  return { start: iso(start), end: iso(end) };
}

function iso(date: Date): string {
  const month = `${date.getMonth() + 1}`.padStart(2, '0');
  const day = `${date.getDate()}`.padStart(2, '0');

  return `${date.getFullYear()}-${month}-${day}`;
}
