/**
 * The RFC 7807 error envelope, and the rules for turning one into something a clerk can
 * act on.
 *
 * The server writes `detail` for a factory clerk to read - it names products, numbers and
 * dates - so it is displayed verbatim rather than replaced with a generic message. `code`
 * is the stable identity to branch on; `detail` and `title` are prose and will be
 * reworded.
 */

export interface FieldError {
  field: string;
  message: string;
  code: string;
}

export interface ProblemDetails {
  type?: string;
  title: string;
  status: number;
  detail: string;
  code: string;
  traceId?: string;
  errors?: FieldError[];
}

export const ErrorCodes = {
  ValidationFailed: 'VALIDATION_FAILED',
  Unauthenticated: 'UNAUTHENTICATED',
  InvalidCredentials: 'INVALID_CREDENTIALS',
  UserInactive: 'USER_INACTIVE',
  AccountLocked: 'ACCOUNT_LOCKED',
  Forbidden: 'FORBIDDEN',
  NotFound: 'NOT_FOUND',
  DuplicateCode: 'DUPLICATE_CODE',
  DuplicateGrade: 'DUPLICATE_GRADE',
  ConcurrencyConflict: 'CONCURRENCY_CONFLICT',
  IfMatchRequired: 'IF_MATCH_REQUIRED',
  AlreadyCancelled: 'ALREADY_CANCELLED',
  GradeNotEnabled: 'GRADE_NOT_ENABLED',
  CodeLocked: 'CODE_LOCKED',
  EffectiveDateInPast: 'EFFECTIVE_DATE_IN_PAST',
  ProductHasStock: 'PRODUCT_HAS_STOCK',
  StockInsufficient: 'STOCK_INSUFFICIENT',
  NotImplemented: 'NOT_IMPLEMENTED',
  InternalError: 'INTERNAL_ERROR',
} as const;

/** A response that is not a ProblemDetails at all - a proxy error, a dropped connection. */
export function isProblemDetails(value: unknown): value is ProblemDetails {
  return (
    typeof value === 'object' &&
    value !== null &&
    typeof (value as ProblemDetails).code === 'string' &&
    typeof (value as ProblemDetails).detail === 'string'
  );
}

/**
 * Always returns something displayable, including for the cases where the server never
 * answered at all. A blank error banner is worse than a wrong one.
 */
export function toProblemDetails(error: unknown, status = 0): ProblemDetails {
  if (isProblemDetails(error)) return error;

  if (status === 0) {
    return {
      title: 'Cannot reach the server',
      status: 0,
      detail:
        'The server did not respond. Check that it is running and that the network is up, then try again.',
      code: 'NETWORK_ERROR',
    };
  }

  return {
    title: 'Unexpected error',
    status,
    detail: `The server returned an unexpected response (${status}).`,
    code: ErrorCodes.InternalError,
  };
}

/** Field errors keyed by field path, for binding to form controls. */
export function fieldErrorMap(problem: ProblemDetails): Map<string, string> {
  const map = new Map<string, string>();

  for (const error of problem.errors ?? []) {
    // First one wins: a field with two problems should show the first, not the last.
    if (!map.has(error.field)) map.set(error.field, error.message);
  }

  return map;
}
