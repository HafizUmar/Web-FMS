import { IsoDate, IsoTimestamp, PagedResult } from './api.types';

/**
 * Timestamps come back with a trailing Z from a POST and without one from a GET - EF
 * returns DateTimeKind.Unspecified on read. Both are UTC. Without this, a bare timestamp
 * is parsed as browser-local and every "entered at" is wrong by the factory's offset.
 */
export function parseUtc(value: IsoTimestamp | undefined): Date | null {
  if (!value) return null;

  const normalised = /[Zz]$|[+-]\d{2}:\d{2}$/.test(value) ? value : `${value}Z`;
  const parsed = new Date(normalised);

  return Number.isNaN(parsed.getTime()) ? null : parsed;
}

/** Today in the browser's local timezone, which is what the server treats as factory-local. */
export function todayIso(): IsoDate {
  const now = new Date();
  const month = `${now.getMonth() + 1}`.padStart(2, '0');
  const day = `${now.getDate()}`.padStart(2, '0');

  return `${now.getFullYear()}-${month}-${day}`;
}

export function addDaysIso(date: IsoDate, days: number): IsoDate {
  const [year, month, day] = date.split('-').map(Number);
  const shifted = new Date(year, month - 1, day + days);

  return todayIsoFrom(shifted);
}

export function todayIsoFrom(date: Date): IsoDate {
  const month = `${date.getMonth() + 1}`.padStart(2, '0');
  const day = `${date.getDate()}`.padStart(2, '0');

  return `${date.getFullYear()}-${month}-${day}`;
}

/** Accepts a Material datepicker's Date, or a string already in the right shape. */
export function toIsoDate(value: Date | string | null | undefined): IsoDate | null {
  if (!value) return null;
  if (typeof value === 'string') return value.slice(0, 10);

  return todayIsoFrom(value);
}

/**
 * The API returns totalCount but not totalPages, so the client computes it. Always at
 * least 1, so an empty table still reads "Page 1 of 1" rather than "Page 1 of 0".
 */
export function pageCount(result: Pick<PagedResult<unknown>, 'totalCount' | 'pageSize'>): number {
  return Math.max(1, Math.ceil(result.totalCount / Math.max(1, result.pageSize)));
}

/** PKR, two decimals. Single-currency by design, so no currency code is rendered. */
export function money(value: number | undefined): string {
  if (value === undefined || value === null) return '—';

  return value.toLocaleString('en-PK', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

export function quantity(value: number | undefined): string {
  if (value === undefined || value === null) return '—';

  return value.toLocaleString('en-PK');
}

/** Movement types read as identifiers; a clerk should see words. */
export function humanise(value: string): string {
  return value
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/^./, (c) => c.toUpperCase());
}
