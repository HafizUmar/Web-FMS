/**
 * The API contract, transcribed from docs/frontend-pdr.md.
 *
 * Three properties of the wire format drive the types below and are easy to get wrong:
 *
 *  - Enums arrive as strings, never numbers.
 *  - A null field is OMITTED from the JSON entirely, so every optional field is `?`
 *    rather than `| null`.
 *  - Business dates are 'yyyy-MM-dd' strings and timestamps are ISO UTC strings. Neither
 *    is a Date. Parsing them at the boundary would mean re-formatting them on the way
 *    back out, and a timezone bug in between.
 */

export type IsoDate = string;      // 'yyyy-MM-dd'
export type IsoTimestamp = string; // '2026-09-11T11:32:18.7141046Z' - see parseUtc()

export type QualityGrade = 'First' | 'Second' | 'Third';
export type DocumentStatus = 'Active' | 'Cancelled';
export type PaymentMethod = 'Cash' | 'BankTransfer' | 'Cheque' | 'Other';
export type ReasonCodeType =
  | 'Breakage'
  | 'StockAdjustment'
  | 'SalesReturn'
  | 'DispatchCancellation';

export type StockMovementType =
  | 'ProductionReceipt'
  | 'Dispatch'
  | 'DispatchCancellation'
  | 'ProductionCancellation'
  | 'Adjustment'
  | 'CountCorrection'
  | 'SalesReturn'
  | 'OpeningBalance';

/** Every paged endpoint returns exactly this. Note there is no totalPages - see pageCount(). */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

// ---------------------------------------------------------------------------
// Auth
// ---------------------------------------------------------------------------

export interface LoginRequest {
  userName: string;
  password: string;
}

export interface CurrentUser {
  userId: string;
  userName: string;
  fullName: string;
  roles: string[];
  permissions: Permission[];
  sessionExpiresAt: IsoTimestamp;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

// ---------------------------------------------------------------------------
// Catalogue
// ---------------------------------------------------------------------------

export interface GradePrice {
  grade: QualityGrade;
  unitRate: number;
}

export interface GradeStock {
  grade: QualityGrade;
  quantity: number;
}

export interface ProductListItem {
  id: string;
  code: string;
  name: string;
  capacityMl?: number;
  isActive: boolean;
  prices: GradePrice[];
  stock: GradeStock[];
}

export interface Product {
  id: string;
  code: string;
  name: string;
  capacityMl?: number;
  description?: string;
  isActive: boolean;
  prices: GradePrice[];
  createdAt: IsoTimestamp;
}

export interface CreateProductRequest {
  code: string;
  name: string;
  capacityMl?: number | null;
  description?: string | null;
  prices: GradePrice[];
}

export interface UpdateProductRequest {
  code: string;
  name: string;
  capacityMl?: number | null;
  description?: string | null;
}

export interface UpdatePricesRequest {
  prices: GradePrice[];
  effectiveFrom: IsoDate;
  reason?: string | null;
}

// ---------------------------------------------------------------------------
// Shared lookups
// ---------------------------------------------------------------------------

export interface ReasonCode {
  id: string;
  type: ReasonCodeType;
  code: string;
  description: string;
  sortOrder: number;
  isActive: boolean;
}

export interface FactorySetting {
  key: string;
  value: string;
  description?: string;
  updatedAt: IsoTimestamp;
}

export interface HealthResponse {
  status: string;
  databaseReachable: boolean;
  migrationsCurrent: boolean;
  pendingMigrations: string[];
  checkedAt: IsoTimestamp;
}

// ---------------------------------------------------------------------------
// Permissions - the names the server returns in CurrentUser.permissions
// ---------------------------------------------------------------------------

export const PERMISSIONS = [
  'CanRecordTransactions',
  'CanAdjustStock',
  'CanManageCatalogue',
  'CanSetPrices',
  'CanCancelHistorical',
  'CanViewReports',
  'CanManageUsers',
  'CanManageSettings',
  'CanViewAudit',
] as const;

export type Permission = (typeof PERMISSIONS)[number];

// ---------------------------------------------------------------------------
// Production
// ---------------------------------------------------------------------------

export interface ProductionEntry {
  id: string;
  entryNumber: string;
  productId: string;
  productCode: string;
  productName: string;
  entryDate: IsoDate;
  quantityGood: number;
  quantitySeconds: number;
  quantityBroken: number;
  totalFired: number;
  lossPercentage: number;
  breakageReason?: string;
  batchReference?: string;
  notes?: string;
  status: DocumentStatus;
  enteredBy: string;
  createdAt: IsoTimestamp;
  warnings: string[];
}

export interface CreateProductionEntryRequest {
  productId: string;
  entryDate: IsoDate;
  quantityGood: number;
  quantitySeconds: number;
  quantityBroken: number;
  breakageReasonCodeId?: string | null;
  batchReference?: string | null;
  notes?: string | null;
}

export interface CancelRequest {
  reason: string;
}

export interface ProductionSummaryRow {
  groupKey: string;
  groupLabel: string;
  totalFired: number;
  totalGood: number;
  totalSeconds: number;
  totalBroken: number;
  lossPercentage: number;
  secondsPercentage: number;
  entryCount: number;
}

export type ProductionGroupBy = 'Product' | 'Day' | 'Month';

// ---------------------------------------------------------------------------
// Stock
// ---------------------------------------------------------------------------

export interface StockLine {
  productId: string;
  productCode: string;
  productName: string;
  grade: QualityGrade;
  quantity: number;
  unitRate?: number;
  stockValue?: number;
  lastMovementAt?: IsoTimestamp;
}

export interface StockResponse {
  lines: StockLine[];
  totalUnits: number;
  totalValue: number;
  asOf: IsoDate;
}

export interface StockMovementItem {
  id: string;
  occurredOn: IsoDate;
  grade: QualityGrade;
  quantity: number;
  movementType: StockMovementType;
  referenceNumber?: string;
  reasonDescription?: string;
  notes?: string;
  enteredBy: string;
  createdAt: IsoTimestamp;
  runningBalance: number;
}

export interface CreateAdjustmentRequest {
  productId: string;
  grade: QualityGrade;
  quantityChange: number;
  reasonCodeId: string;
  adjustedOn: IsoDate;
  notes?: string | null;
}

export interface AdjustmentResponse {
  id: string;
  adjustmentNumber: string;
  productId: string;
  productName: string;
  grade: QualityGrade;
  quantityChange: number;
  resultingBalance: number;
  reasonDescription: string;
  adjustedOn: IsoDate;
  createdAt: IsoTimestamp;
}
