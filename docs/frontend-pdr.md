# Frontend Product Requirements Document
## Crockery Factory Management System — Phase 1

| | |
|---|---|
| **Version** | 1.0 |
| **Date** | September 2026 |
| **Backend** | `CrockeryFactory` Phase 1, branch `claude/funny-noether-g1mh7r` |
| **Base URL** | `/api/v1` |
| **Source of truth** | Every JSON example below was captured from the running API, not transcribed from source |

---

## 0. Read this before anything else

The brief that commissioned this document assumed four things about the backend that are
not true of the backend that exists. They are corrected here rather than propagated,
because a frontend built to the brief would not connect to this server.

| The brief said | The backend actually does | What the frontend must do |
|---|---|---|
| `POST /api/users/login` | `POST /api/v1/auth/login` | Use the real paths. Everything is under `/api/v1`. |
| Store a JWT token | **There is no token.** Authentication is an `HttpOnly`, `SameSite=Strict`, `Secure` cookie set by the server. The login response body contains no token, and there is a backend test asserting that. | Store nothing. Send `credentials: 'include'` on every request. |
| Refresh token flow, `POST /api/users/refresh` | **No such endpoint.** The cookie slides: 30 minutes of inactivity for Owner and Administrator, and a hard stop at 20:00 local for Clerks. | On `401`, redirect to login. Do not build a refresh interceptor — there is nothing to call. See §2.4. |
| Read policies from `UserPolicyConstants.cs` | That file does not exist. The constants live in `src/CrockeryFactory.Domain/Shared/Authorization/Roles.cs`. | The full matrix is reproduced in §5 so the frontend needs no access to the C# at all. |

One further gap, which is a **backend decision, not a frontend one**:

> **There are no HATEOAS links.** No response contains `_links`, `links`, or any
> equivalent. Conditional rendering of action buttons therefore cannot be driven by
> per-resource links, as the brief assumed.
>
> What exists instead is a **`permissions` array** returned by `POST /auth/login` and
> `GET /auth/me`, listing the policy names this user satisfies. It is generated from the
> same `PolicyMap` that registers the authorisation policies server-side, so it cannot
> drift from what the server will actually allow. §5 specifies conditional rendering
> against that array.
>
> This is weaker than HATEOAS in one specific way: `permissions` is per-user, not
> per-record, so it cannot express "you may cancel *this* dispatch but not *that* one".
> Two rules in this system are genuinely per-record — a Clerk may cancel today's
> production entry or dispatch but not an older one (PR-07). §5.3 gives the client-side
> rule for those, and notes the residual risk. **If per-record links are wanted, that is
> a backend change to specify and schedule.**

---

## 1. Conventions that apply to every call

### 1.1 Transport

| | |
|---|---|
| Base path | `/api/v1` — versioned from day one |
| Content type | `application/json`, request and response |
| Error content type | `application/problem+json` |
| Property casing | `camelCase` |
| Credentials | **Every** request must send the session cookie: `fetch(url, { credentials: 'include' })` or `axios.defaults.withCredentials = true` |

### 1.2 Enums travel as strings, never as numbers

```json
{ "grade": "First", "status": "Active", "method": "Cheque" }
```

Complete value lists are in [Appendix A](#appendix-a--enum-values).

### 1.3 Dates and timestamps

| Kind | Format | Example | Notes |
|---|---|---|---|
| Business date | `yyyy-MM-dd` | `"2026-09-11"` | No time, no zone. Send exactly this. |
| Timestamp | ISO 8601 | `"2026-09-11T11:32:18.7141046Z"` | **Always UTC.** |

> **Inconsistency to handle.** Timestamps returned immediately from a `POST` carry a
> trailing `Z`; the same value re-read through a `GET` may not (`"2026-09-11T11:32:18.7141046"`).
> This is EF Core returning `DateTimeKind.Unspecified` on read. **Treat every timestamp
> field as UTC regardless of whether `Z` is present** — append `Z` before parsing if it
> is missing. Do not let the browser interpret a bare timestamp as local time, or every
> "entered at" will be wrong by the factory's UTC offset.

### 1.4 Null fields are omitted, not sent as `null`

The API serialises with `DefaultIgnoreCondition = WhenWritingNull`. A field with no value
**is absent from the JSON entirely**.

```json
// A statement line that is a credit — note there is no "debit" key at all
{
  "date": "2026-09-11",
  "documentType": "Payment",
  "documentNumber": "R-2609-0001",
  "description": "Cheque CHQ-889231",
  "credit": 20000.00,
  "runningBalance": -1500.00
}
```

Use optional chaining and defaults everywhere. `row.debit ?? 0`, never `row.debit`.
Fields observed to disappear this way include: `debit`, `credit`, `capacityMl`,
`description`, `notes`, `reference`, `reasonDescription`, `referenceNumber`,
`batchReference`, `lastLoginAt`, `lastDispatchDate`, `lastPaymentDate`, `oldValues`,
`city`, `phone`, `address`, `unitRate`, `stockValue`.

### 1.5 Money and quantities

- Money is `decimal(18,2)` server-side and arrives as a JSON number: `36600.00`.
  **Never use `parseFloat` and then re-round for display arithmetic** — format for
  display only, and send back whatever the server gave you.
- Quantities are integers. A fractional quantity is a bug.
- Currency is PKR, single-currency by design. No currency code is returned.

### 1.6 Idempotency — required on every create

Every `POST` that creates a document accepts an `Idempotency-Key` header (any
client-generated GUID, max 64 chars).

```
Idempotency-Key: 7f3b9a02-4e11-4a55-9c2e-1d0a6f8b2c44
```

A repeat of the same key within **7 days** returns the **original response**, with
`Idempotency-Replayed: true` on it. Nothing is created twice.

**The frontend must generate one key per form submission attempt** — created when the
form is opened or the submit button is first pressed, and **reused across retries of that
same submission**. A new key per retry defeats the mechanism entirely.

A *rejected* request does not consume the key: the clerk fixes the field and resubmits
with the same key, and it goes through.

Endpoints that accept it: `POST /products`, `POST /customers`, `POST /production-entries`,
`POST /dispatches`, `POST /payments`, `POST /stock/adjustments`.

### 1.7 Concurrency — `ETag` and `If-Match` on updates

Single-record `GET`s return an `ETag`:

```
ETag: "AAAAAAAAB9E="
```

`PUT` requires it back verbatim in `If-Match`, quotes included:

```
If-Match: "AAAAAAAAB9E="
```

| Situation | Status | Code | What the user should be told |
|---|---|---|---|
| Header omitted | `428` | `IF_MATCH_REQUIRED` | "Reload the record and try again." A bug in the client, not the user's fault. |
| Header stale | `409` | `CONCURRENCY_CONFLICT` | "Someone else changed this while you were editing. Reload and re-apply your change." |
| `If-Match: *` | `428` | `IF_MATCH_REQUIRED` | Refused deliberately. Never send a wildcard. |

Applies to: `PUT /products/{id}`, `PUT /customers/{id}`.

---

## 2. Authentication flow

### 2.1 Login page

Route `/login`. The only anonymous page, alongside a health indicator.

**Layout**

| Element | Type | Notes |
|---|---|---|
| Factory name | Heading | Static, or from `GET /settings` once signed in |
| Username | text, autofocus, `autocomplete="username"` | Required |
| Password | password, `autocomplete="current-password"` | Required |
| Sign in | submit button | Disabled while in flight |
| Error banner | alert | Shows `detail` from the error body |

**Request**

```http
POST /api/v1/auth/login
Content-Type: application/json
```
```json
{
  "userName": "owner",
  "password": "Factory!Pass99"
}
```

**Response — `200 OK`**

```json
{
  "userId": "92d1cd95-2566-48c3-8d0d-d666f7265352",
  "userName": "owner",
  "fullName": "Owner Sahib",
  "roles": ["Owner"],
  "permissions": [
    "CanAdjustStock",
    "CanCancelHistorical",
    "CanManageCatalogue",
    "CanManageSettings",
    "CanManageUsers",
    "CanRecordTransactions",
    "CanSetPrices",
    "CanViewAudit",
    "CanViewReports"
  ],
  "sessionExpiresAt": "2026-09-11T12:02:17.8669399Z"
}
```

Accompanied by:

```
Set-Cookie: crockery.session=CfDJ8NkBYCK...; path=/; secure; samesite=strict; httponly
```

**The cookie is `HttpOnly` — JavaScript cannot read it, and must not try.** The browser
attaches it automatically provided every request sends credentials.

A Clerk's response differs only in `permissions`:

```json
{
  "userId": "fd37af42-b893-45de-b992-140088bb37ad",
  "userName": "clerk",
  "fullName": "Munshi",
  "roles": ["Clerk"],
  "permissions": ["CanAdjustStock", "CanRecordTransactions", "CanViewReports"],
  "sessionExpiresAt": "2026-09-11T12:02:18.5894612Z"
}
```

**Login failures**

`401 Unauthorized` — wrong password *or* unknown username. These are **byte-identical
apart from `traceId`**, deliberately: any difference would let someone enumerate staff
names. Do not try to distinguish them in the UI.

```json
{
  "type": "https://crockeryfactory/errors/invalid-credentials",
  "title": "Invalid credentials",
  "status": 401,
  "detail": "The username or password is not correct.",
  "code": "INVALID_CREDENTIALS",
  "traceId": "0HNOFV1B15D22"
}
```

`403 Forbidden` — correct password, deactivated account:

```json
{
  "type": "https://crockeryfactory/errors/user-inactive",
  "title": "Forbidden",
  "status": 403,
  "detail": "This account has been deactivated. Ask an administrator to reactivate it.",
  "code": "USER_INACTIVE",
  "traceId": "0HNOFV1B15D24"
}
```

`423 Locked` — ten consecutive failures, locked for 15 minutes:

```json
{
  "type": "https://crockeryfactory/errors/account-locked",
  "title": "Account locked",
  "status": 423,
  "detail": "This account is locked after too many failed attempts. Try again in 15 minutes, or ask an administrator to reset the password.",
  "code": "ACCOUNT_LOCKED",
  "traceId": "0HNOFV1B15D23"
}
```

Show `detail` verbatim in all three cases. The backend writes these to be read by a clerk.

### 2.2 After login — bootstrap

Call `GET /api/v1/auth/me` **once on every application start**, not only after login. The
cookie survives a page refresh, so a returning user is already signed in and the app must
discover that rather than bouncing them to the login page.

```http
GET /api/v1/auth/me
```
```json
{
  "userId": "92d1cd95-2566-48c3-8d0d-d666f7265352",
  "userName": "owner",
  "fullName": "Owner Sahib",
  "roles": ["Owner"],
  "permissions": ["CanAdjustStock", "CanCancelHistorical", "CanManageCatalogue", "CanManageSettings", "CanManageUsers", "CanRecordTransactions", "CanSetPrices", "CanViewAudit", "CanViewReports"],
  "sessionExpiresAt": "2026-09-11T12:02:18.2937722Z"
}
```

Bootstrap sequence:

```
1. GET /auth/me
   ├─ 200 → store the user in app state, render the app
   └─ 401 → render the login page
2. In parallel, once authenticated:
   GET /reason-codes      (cached for the session — dropdown data)
   GET /settings          (only if CanManageSettings)
```

When it returns `401`, render login. That is not an error to report.

### 2.3 Session expiry

`sessionExpiresAt` is a UTC instant.

- **Owner / Administrator**: 30 minutes, sliding. Every successful request pushes it out.
- **Clerk**: also sliding, but capped at **20:00 factory-local**, because the tablet is
  left on the packing bench overnight. A clerk signing in after 20:00 gets until 20:00
  the next day.

Recommended handling: show a warning banner at 2 minutes remaining offering "Stay signed
in" (any request refreshes a sliding session). Do **not** poll `/auth/me` on a timer purely
to keep the session alive — that defeats the timeout.

### 2.4 Handling `401` — no refresh, by design

There is no refresh endpoint. A `401` on any call other than login means the session is
over — expired, signed out elsewhere, the account was deactivated, or the password was
changed.

```js
// Response interceptor. Note there is deliberately NO retry and NO refresh call.
async function onResponse(response, originalRequest) {
  if (response.status !== 401) return response;

  // Login itself returns 401 for bad credentials — that is a form error, not a
  // dead session. Let the login page handle it.
  if (originalRequest.url.endsWith('/auth/login')) return response;

  clearUserState();
  redirectToLogin({ returnTo: currentPath, reason: 'session-expired' });
  return response;
}
```

Show "Your session has ended. Please sign in again." on the login page when `reason` is
set. Preserve `returnTo` and navigate back after a successful sign-in.

> **Why a deactivated user is logged out immediately.** Deactivating a user rolls their
> security stamp server-side, so their next request — any request — returns `401`. The
> frontend needs no special handling; the ordinary `401` path covers it.

### 2.5 Change password

`POST /api/v1/auth/change-password`

```json
{
  "currentPassword": "Factory!Pass99",
  "newPassword": "Correct!Horse42"
}
```

`204 No Content` on success. **The session is terminated deliberately** — changing a
password invalidates every session the user has open, including this one. The frontend
must redirect to login with "Password changed. Please sign in again."

Failure is `400 VALIDATION_FAILED` with field errors keyed `currentPassword` or
`newPassword`:

```json
{
  "type": "https://crockeryfactory/errors/validation-failed",
  "title": "Validation failed",
  "status": 400,
  "detail": "The password could not be changed.",
  "code": "VALIDATION_FAILED",
  "traceId": "0HNOFV1B15D31",
  "errors": [
    { "field": "newPassword", "message": "Passwords must be at least 10 characters.", "code": "VALIDATION_FAILED" }
  ]
}
```

Password rules enforced by the server: minimum 10 characters, at least one digit, at
least one lowercase letter. Mirror these client-side to save a round trip, but the server
is authoritative.

### 2.6 Logout

`POST /api/v1/auth/logout` → `204 No Content`.

```
1. POST /auth/logout          (ignore the result — proceed regardless)
2. Clear all in-memory user state and any cached lookup data
3. Navigate to /login
```

The cookie is cleared by the server's `Set-Cookie`. There is nothing in `localStorage` or
`sessionStorage` to clear, and nothing should ever have been put there.

---

## 3. The error model

### 3.1 Envelope

Every non-2xx response is RFC 7807 `application/problem+json`:

```json
{
  "type": "https://crockeryfactory/errors/stock-insufficient",
  "title": "Insufficient stock",
  "status": 422,
  "detail": "Only 2125 units of CUP-PDR-23369 (First) are in stock. 999999 were requested.",
  "code": "STOCK_INSUFFICIENT",
  "traceId": "0HNOFV1B15D2H",
  "errors": [
    { "field": "quantity", "message": "Only 2125 available", "code": "STOCK_INSUFFICIENT" }
  ]
}
```

| Field | Always present | Use |
|---|---|---|
| `code` | **yes** | **Switch on this.** The stable machine-readable identity of the error. |
| `detail` | yes | **Display this to the user verbatim.** Written for a clerk, and names products, numbers and dates. |
| `title` | yes | Banner heading if you want one |
| `status` | yes | HTTP status, mirrored |
| `traceId` | yes | Show in a collapsed "technical details" line. This is what a support call quotes. |
| `errors[]` | only for field-level failures | Bind to form fields by `field` |

**Never** switch on `detail` or `title` — they are prose and will be reworded.

### 3.2 How to display, by status

| Status | Presentation |
|---|---|
| `400` | Inline field errors from `errors[]`, bound by `field`. If `errors` is absent, show `detail` as a form-level banner. |
| `401` | Not an error message — redirect to login (§2.4). |
| `403` | Banner: `detail`. The button that produced it should have been hidden (§5) — reaching a 403 is a conditional-rendering bug worth logging. |
| `404` | Empty state on the page: "That record no longer exists." |
| `409` | Modal. `DUPLICATE_CODE` → field error on `code`. `CONCURRENCY_CONFLICT` → offer "Reload". `ALREADY_CANCELLED` → refresh the record. |
| `422` | **Banner with `detail` prominent.** These are business-rule refusals and `detail` explains what to do instead. Also bind `errors[]` if present. |
| `423` | Login page banner with `detail`. |
| `428` | Client bug. Reload the record silently and retry once; if it recurs, show `detail`. |
| `500` | "Something went wrong. Quote reference `{traceId}` if you report this." Never show anything else — the server deliberately returns no detail. |
| `501` | Disable the control and show `detail`. Applies to PDF/XLSX export today. |

### 3.3 Field paths in `errors[]`

Paths address the request body and use array indices:

```json
{
  "code": "VALIDATION_FAILED",
  "errors": [
    { "field": "code",                "message": "Code may contain only capital letters, digits and hyphens", "code": "VALIDATION_FAILED" },
    { "field": "name",                "message": "Name is required",                        "code": "VALIDATION_FAILED" },
    { "field": "capacityMl",          "message": "Capacity must be between 1 and 5000 ml",  "code": "VALIDATION_FAILED" },
    { "field": "prices[0].unitRate",  "message": "Rate must be greater than zero",          "code": "VALIDATION_FAILED" },
    { "field": "lines[1]",            "message": "This product and grade already appears on another line", "code": "DUPLICATE_LINE" },
    { "field": "values.Factory.Nmae", "message": "No such setting",                         "code": "VALIDATION_FAILED" }
  ]
}
```

Note `lines[1]` addresses a **whole line**, not a property of it — highlight the row.
Note `values.Factory.Nmae` contains dots *inside the key*; split on the first dot only.

### 3.4 Warnings are not errors

Several creates return `201 Created` **with** a `warnings` array. The document was saved.

```json
{
  "id": "3227e343-8dca-4e29-a720-3e00e8ab2501",
  "entryNumber": "P-2609-0002",
  "lossPercentage": 40.0,
  "status": "Active",
  "warnings": ["LOSS_UNUSUALLY_HIGH"]
}
```

| Code | Meaning | Suggested message |
|---|---|---|
| `LOSS_UNUSUALLY_HIGH` | Breakage above the configured threshold (default 25%) | "Loss on this entry is {loss}%. Saved — check the figures if that looks wrong." |
| `RATE_BELOW_LIST` | A line rate under half the list price | "One or more rates are well below the list price. Saved." |
| `PAYMENT_EXCEEDS_OUTSTANDING` | Receipt larger than the balance | "This receipt exceeds what the customer owes. Saved as an advance." |

Show these as a **non-blocking toast or inline notice in the success state**, alongside
the created document number. Never as an error, and never as a modal that must be
dismissed before the clerk can carry on — the entry succeeded.

> `RATE_BELOW_COST` appears in the backend's warning constants but is **never emitted**:
> Phase 1 defines no cost price on any entity, so there is nothing to compare against.
> Do not build UI for it.

---

## 4. Paging

### 4.1 Request

```
GET /api/v1/products?page=1&pageSize=50
```

| Parameter | Default | Max | Notes |
|---|---|---|---|
| `page` | `1` | — | 1-based. Values `< 1` are clamped to 1. |
| `pageSize` | `50` | `200` | Values `<= 0` become 50; values `> 200` are clamped to 200. |

The server clamps silently rather than erroring. The response always echoes the
**effective** `page` and `pageSize` — render the controls from those, not from what you
sent.

### 4.2 Response

```json
{
  "items": [
    { "id": "a27ab681-3bc3-4dbb-8c7a-feef63e7b8c6", "code": "CUP-PDR-23369", "name": "Cappuccino cup (large)" }
  ],
  "page": 1,
  "pageSize": 2,
  "totalCount": 1
}
```

`items` holds whatever that endpoint returns — the three fields above are an abbreviation.
The four envelope fields are identical on every paged endpoint.

> **There is no `totalPages`.** The brief expected one; the API does not return it.
> Compute it: `totalPages = Math.max(1, Math.ceil(totalCount / pageSize))`.

### 4.3 Which endpoints are paged

**Paged** (return the envelope above):
`GET /products`, `GET /customers`, `GET /dispatches`, `GET /payments`,
`GET /production-entries`, `GET /stock/{productId}/movements`, `GET /audit`

**Not paged** (return a bare array or a bespoke object — do not build page controls):
`GET /reason-codes` *(array)*, `GET /settings` *(array)*, `GET /users` *(array)*,
`GET /production-entries/summary` *(array)*, `GET /stock` *(object with `lines`)*,
`GET /customers/outstanding` *(object with `rows`)*, `GET /customers/{id}/statement`
*(object with `lines`)*, all `GET /reports/*`.

### 4.4 UI

| Control | Behaviour |
|---|---|
| Page size selector | `10 / 25 / 50 / 100 / 200`. Changing it resets `page` to 1. Persist the choice per table in `localStorage`. |
| Page controls | First / Previous / Next / Last, plus "Page *n* of *m*". Disable Previous on page 1 and Next on the last page. |
| Summary | "Showing 1–50 of 1,284" — computed as `(page-1)*pageSize + 1` to `min(page*pageSize, totalCount)`. |
| Empty | `totalCount === 0` → empty state, no page controls. |
| Out of range | If `page > totalPages` (e.g. after a filter change), refetch at page 1 rather than showing an empty table. |

```js
function pagingState(res) {
  const totalPages = Math.max(1, Math.ceil(res.totalCount / res.pageSize));
  return {
    ...res,
    totalPages,
    from: res.totalCount === 0 ? 0 : (res.page - 1) * res.pageSize + 1,
    to: Math.min(res.page * res.pageSize, res.totalCount),
    hasPrevious: res.page > 1,
    hasNext: res.page < totalPages,
  };
}
```

---

## 5. Conditional rendering

### 5.1 The `permissions` array is the mechanism

`POST /auth/login` and `GET /auth/me` both return `permissions` — the policy names this
user satisfies. It is produced by the same `PolicyMap` that registers the policies
server-side, so what the client is told and what the server will allow come from one
table and cannot drift.

```js
const can = (permission) => user.permissions.includes(permission);
```

**Hiding a control is a courtesy, not a security boundary.** Every endpoint is enforced
server-side and again in the service layer. Hiding the button stops the clerk being
offered something that will fail; it is not what stops them doing it.

### 5.2 The policy matrix

Reproduced from `src/CrockeryFactory.Domain/Shared/Authorization/Roles.cs` and
`src/CrockeryFactory.Web/Auth/PolicyMap.cs`. *(The brief referred to
`UserPolicyConstants.cs`; that file does not exist.)*

| Policy | Owner | Clerk | Administrator |
|---|:--:|:--:|:--:|
| `CanViewReports` | ✓ | ✓ | ✓ |
| `CanRecordTransactions` | ✓ | ✓ | ✗ |
| `CanAdjustStock` | ✓ | ✓ | ✗ |
| `CanManageCatalogue` | ✓ | ✗ | ✗ |
| `CanSetPrices` | ✓ | ✗ | ✗ |
| `CanCancelHistorical` | ✓ | ✗ | ✗ |
| `CanManageUsers` | ✓ | ✗ | ✓ |
| `CanManageSettings` | ✓ | ✗ | ✓ |
| `CanViewAudit` | ✓ | ✗ | ✓ |

Three exact `permissions` payloads, captured from the API:

Owner:

```json
["CanAdjustStock","CanCancelHistorical","CanManageCatalogue","CanManageSettings","CanManageUsers","CanRecordTransactions","CanSetPrices","CanViewAudit","CanViewReports"]
```

Clerk:

```json
["CanAdjustStock","CanRecordTransactions","CanViewReports"]
```

Administrator:

```json
["CanManageSettings","CanManageUsers","CanViewAudit","CanViewReports"]
```

Note the Administrator deliberately **cannot** record transactions or adjust stock. The
account that creates users must not also be able to move stock, or the separation means
nothing.

### 5.3 Per-record rules the `permissions` array cannot express

Two rules depend on the *record*, not the user (PR-07):

> A Clerk may cancel a production entry or a dispatch **dated today**. Anything older is
> the Owner's.

Client-side rule:

```js
function canCancel(user, documentDate /* "2026-09-11" */) {
  if (!user.permissions.includes('CanRecordTransactions')) return false;
  if (documentDate === todayLocalIso()) return true;      // same day — clerk may
  return user.permissions.includes('CanCancelHistorical'); // older — owner only
}
```

`todayLocalIso()` must use the **browser's local date**, matching the server's use of
factory-local time. Do not use the UTC date.

**Residual risk, stated plainly:** this duplicates a backend rule in the client. If the
backend's window changes, this goes stale and the user sees a button that 403s. The
`403 CANCELLATION_WINDOW_EXPIRED` path must therefore still be handled gracefully — show
`detail`, do not treat it as a crash:

```json
{
  "type": "https://crockeryfactory/errors/cancellation-window-expired",
  "title": "Forbidden",
  "status": 403,
  "detail": "This entry was not made today, so only the owner can cancel it. Record a stock adjustment if the figures need correcting.",
  "code": "CANCELLATION_WINDOW_EXPIRED",
  "traceId": "0HNOFV1B15D2J"
}
```

The clean fix is per-record links from the backend. Raise it if this duplication is
unacceptable.

### 5.4 Other state-dependent controls

These are driven by the record's own fields, which *are* in every response:

| Control | Show when |
|---|---|
| Cancel (any document) | `status === "Active"` and `canCancel(...)` above |
| Cancelled badge | `status === "Cancelled"` |
| Deactivate product | `isActive === true` and `can('CanManageCatalogue')` |
| Deactivate customer/user | `isActive === true` and the matching policy |
| Edit prices | `can('CanSetPrices')` |
| Print dispatch note | Always visible but **disabled**, tooltip "Not available yet" (endpoint returns `501`) |
| Export PDF / Excel | Same — disabled with the same tooltip |

### 5.5 Navigation

```js
const NAV = [
  { path: '/',                  label: 'Dashboard',   permission: 'CanViewReports' },
  { path: '/production',        label: 'Production',  permission: 'CanViewReports' },
  { path: '/stock',             label: 'Stock',       permission: 'CanViewReports' },
  { path: '/dispatches',        label: 'Dispatches',  permission: 'CanViewReports' },
  { path: '/customers',         label: 'Customers',   permission: 'CanViewReports' },
  { path: '/payments',          label: 'Payments',    permission: 'CanViewReports' },
  { path: '/products',          label: 'Products',    permission: 'CanViewReports' },
  { path: '/reports',           label: 'Reports',     permission: 'CanViewReports' },
  { path: '/admin/users',       label: 'Users',       permission: 'CanManageUsers' },
  { path: '/admin/settings',    label: 'Settings',    permission: 'CanManageSettings' },
  { path: '/admin/reason-codes',label: 'Reason codes',permission: 'CanViewReports' },
  { path: '/admin/audit',       label: 'Audit',       permission: 'CanViewAudit' },
];

const visible = NAV.filter(item => user.permissions.includes(item.permission));
```

Route guards must repeat the check — a typed URL must not render a page the nav hides.
A guard failure redirects to `/` with "You do not have access to that page."

Because an Administrator holds `CanViewReports` but not `CanRecordTransactions`, they see
Production and Dispatches as **read-only lists**: the pages render, the "New" buttons do
not. That is intended.

---

## 6. Pages

Each section below gives the endpoints, the exact JSON, the layout, the actions and the
validation the backend actually enforces.

---

### 6.1 Dashboard — `/`

**Permission:** `CanViewReports` (everyone)

#### `GET /api/v1/reports/dashboard`

```json
{
  "totalUnitsInStock": 565,
  "stockValue": 76275.00,
  "totalOutstanding": -5001500.00,
  "customersWithBalance": 1,
  "unitsProducedThisMonth": 600,
  "lossPercentageThisMonth": 40.0,
  "salesThisMonth": 0.00,
  "paymentsThisMonth": 5020000.00,
  "lowStockProducts": [],
  "topDebtors": [
    {
      "customerId": "61234432-8210-4e2a-b720-64fa2b4ee0fa",
      "code": "C-PDR-3851",
      "name": "Shalimar Traders",
      "city": "Gujrat",
      "phone": "03001234567",
      "openingBalance": 18500.00,
      "totalDispatched": 0,
      "totalPaid": 5020000.00,
      "outstanding": -5001500.00,
      "lastPaymentDate": "2026-09-11",
      "daysSinceLastPayment": 0
    }
  ],
  "generatedAt": "2026-09-11T11:32:21.4986861Z"
}
```

`lowStockProducts` uses the same shape as a `GET /stock` line (§6.3).

**Layout**

| Tile | Field | Format |
|---|---|---|
| Units in stock | `totalUnitsInStock` | Integer, thousands separated |
| Stock value | `stockValue` | PKR, 2 dp |
| Outstanding | `totalOutstanding` | PKR. **May be negative** — customers in advance. Label a negative "In advance" rather than showing a minus against "owed". |
| Customers with a balance | `customersWithBalance` | Integer |
| Produced this month | `unitsProducedThisMonth` | Integer |
| Loss this month | `lossPercentageThisMonth` | 2 dp + `%`. Amber above 25. |
| Sales this month | `salesThisMonth` | PKR |
| Payments this month | `paymentsThisMonth` | PKR |

Below the tiles: **Top debtors** table (name, city, outstanding, days since last payment)
and **Low stock** table (product, grade, quantity). Both link through.

Footer: "Figures as at {`generatedAt`}". **The response is cached server-side for 60
seconds**, so it is not sub-second fresh and must not be presented as live. Poll no more
often than every 60s; anything faster returns the same cached object.

**Actions:** none. Read-only.

---

### 6.2 Products — `/products`

**Read:** `CanViewReports` · **Write:** `CanManageCatalogue` · **Prices:** `CanSetPrices`

#### List — `GET /api/v1/products?search=&includeInactive=false&page=1&pageSize=50`

```json
{
  "items": [
    {
      "id": "a27ab681-3bc3-4dbb-8c7a-feef63e7b8c6",
      "code": "CUP-PDR-23369",
      "name": "Cappuccino cup (large)",
      "capacityMl": 200,
      "isActive": true,
      "prices": [
        { "grade": "First",  "unitRate": 135.00 },
        { "grade": "Second", "unitRate": 81.00 }
      ],
      "stock": [
        { "grade": "First",  "quantity": 2125 },
        { "grade": "Second", "quantity": 80 }
      ]
    }
  ],
  "page": 1,
  "pageSize": 2,
  "totalCount": 1
}
```

Prices and stock come back **inline, deliberately** — the dispatch screen needs product,
price and availability together, and three round trips on factory WiFi is how the 90
second target is missed. `stock` is `[]` for a product that has never moved.

**Table columns**

| Column | Source | Notes |
|---|---|---|
| Code | `code` | Monospace |
| Name | `name` | |
| Capacity | `capacityMl` | `"{n} ml"`, or `—` when absent |
| Price (First) | `prices[grade=First].unitRate` | PKR, `—` if no price on file |
| Price (Second) | `prices[grade=Second].unitRate` | PKR, `—` |
| Stock (First) | `stock[grade=First].quantity` | Integer, `0` when missing |
| Stock (Second) | `stock[grade=Second].quantity` | Integer, `0` |
| Status | `isActive` | "Active" / "Inactive" badge |
| Actions | | Edit, Prices, Deactivate — per §5 |

**Filters:** search box (matches code *or* name, server-side substring), "Include
inactive" checkbox. Both reset paging to page 1.

#### Get one — `GET /api/v1/products/{id}`

Returns `ETag`. **Call this before opening the edit form** — you need the ETag.

```
ETag: "AAAAAAAAB9E="
```
```json
{
  "id": "a27ab681-3bc3-4dbb-8c7a-feef63e7b8c6",
  "code": "CUP-PDR-23369",
  "name": "Cappuccino cup",
  "capacityMl": 180,
  "description": "Standard 180ml cappuccino cup",
  "isActive": true,
  "prices": [
    { "grade": "First",  "unitRate": 120.00 },
    { "grade": "Second", "unitRate": 72.00 }
  ],
  "createdAt": "2026-09-11T11:32:18.7141046"
}
```

#### Create — `POST /api/v1/products`

**Owner only.** Send `Idempotency-Key`.

```json
{
  "code": "CUP-PDR-23369",
  "name": "Cappuccino cup",
  "capacityMl": 180,
  "description": "Standard 180ml cappuccino cup",
  "prices": [
    { "grade": "First",  "unitRate": 120.00 },
    { "grade": "Second", "unitRate": 72.00 }
  ]
}
```

`201 Created`, with `Location: /api/v1/products/{id}` and an `ETag`:

```json
{
  "id": "a27ab681-3bc3-4dbb-8c7a-feef63e7b8c6",
  "code": "CUP-PDR-23369",
  "name": "Cappuccino cup",
  "capacityMl": 180,
  "description": "Standard 180ml cappuccino cup",
  "isActive": true,
  "prices": [
    { "grade": "First",  "unitRate": 120.00 },
    { "grade": "Second", "unitRate": 72.00 }
  ],
  "createdAt": "2026-09-11T11:32:18.7141046Z"
}
```

**Form**

| Field | Input | Validation (server-enforced — mirror it) |
|---|---|---|
| `code` | text, uppercase-on-blur | Required, ≤ 24 chars, `^[A-Z0-9\-]+$`. **Lowercase input is uppercased by the server** — do it client-side so the user sees what will be stored. |
| `name` | text | Required, ≤ 160 |
| `capacityMl` | number, optional | When supplied, 1–5000 |
| `description` | textarea, optional | ≤ 500 |
| `prices[]` | repeating: grade select + rate | ≥ 1 row; one row per grade; **no duplicate grades**; each rate > 0 and ≤ 1,000,000 |

Grade options come from `GET /settings` → `Grades.Enabled` (default `"1,2"` → First,
Second). Offering a disabled grade produces a `422` the clerk cannot act on.

**Errors**

`400 VALIDATION_FAILED` — all field problems in one response:

```json
{
  "type": "https://crockeryfactory/errors/validation-failed",
  "title": "Validation failed",
  "status": 400,
  "detail": "The product could not be saved.",
  "code": "VALIDATION_FAILED",
  "traceId": "0HNOFV1B15D26",
  "errors": [
    { "field": "code",               "message": "Code may contain only capital letters, digits and hyphens", "code": "VALIDATION_FAILED" },
    { "field": "name",               "message": "Name is required",                       "code": "VALIDATION_FAILED" },
    { "field": "capacityMl",         "message": "Capacity must be between 1 and 5000 ml", "code": "VALIDATION_FAILED" },
    { "field": "prices[0].unitRate", "message": "Rate must be greater than zero",          "code": "VALIDATION_FAILED" }
  ]
}
```

`400 DUPLICATE_GRADE` — same grade twice; the error sits on `prices`.

`409 DUPLICATE_CODE`:

```json
{
  "type": "https://crockeryfactory/errors/duplicate-code",
  "title": "Conflict",
  "status": 409,
  "detail": "Product code 'CUP-PDR-23369' is already in use.",
  "code": "DUPLICATE_CODE",
  "traceId": "0HNOFV1B15D27"
}
```

Bind to the `code` field. Codes are unique across active *and* inactive products.

`422 GRADE_NOT_ENABLED`:

```json
{
  "type": "https://crockeryfactory/errors/grade-not-enabled",
  "title": "Grade not enabled",
  "status": 422,
  "detail": "Grade Third is not in use at this factory. Grades in use: First, Second.",
  "code": "GRADE_NOT_ENABLED",
  "traceId": "0HNOFV1B15D29",
  "errors": [
    { "field": "prices", "message": "Third is not enabled", "code": "GRADE_NOT_ENABLED" }
  ]
}
```

#### Update — `PUT /api/v1/products/{id}`

**Owner only. Requires `If-Match`.** Prices are *not* editable here.

```json
{
  "code": "CUP-PDR-23369",
  "name": "Cappuccino cup (large)",
  "capacityMl": 200,
  "description": "Revised"
}
```

`200 OK` with the updated product and a fresh `ETag`.

`422 CODE_LOCKED` — the code cannot change once it appears on a document:

```json
{
  "type": "https://crockeryfactory/errors/code-locked",
  "title": "Code locked",
  "status": 422,
  "detail": "Product code 'CUP-PDR-23369' cannot be changed because it already appears on production entries or dispatches. Deactivate this product and create a new one instead.",
  "code": "CODE_LOCKED",
  "traceId": "0HNOFV1B15D2R"
}
```

Once a product has any movement, **render the code field read-only** with a hint
explaining why, rather than letting the clerk type a change that will be refused.

#### Set prices — `PUT /api/v1/products/{id}/prices`

**Owner only.** No `If-Match`. Audited.

```json
{
  "prices": [
    { "grade": "First",  "unitRate": 135.00 },
    { "grade": "Second", "unitRate": 81.00 }
  ],
  "effectiveFrom": "2026-09-11",
  "reason": "Clay and fuel both up this quarter"
}
```

`200 OK` returns the full product with the new prices.

| Rule | Error |
|---|---|
| `effectiveFrom` not before the current effective date | `422 EFFECTIVE_DATE_IN_PAST` |
| Same price validation as create | `400 VALIDATION_FAILED` |
| Grade must be enabled | `422 GRADE_NOT_ENABLED` |

```json
{
  "type": "https://crockeryfactory/errors/effective-date-in-past",
  "title": "Effective date in the past",
  "status": 422,
  "detail": "Prices are effective from 2026-09-11. A new rate cannot start before that date.",
  "code": "EFFECTIVE_DATE_IN_PAST",
  "traceId": "0HNOFV1B15D2S",
  "errors": [
    { "field": "effectiveFrom", "message": "Must be on or after 2026-09-11", "code": "EFFECTIVE_DATE_IN_PAST" }
  ]
}
```

Default `effectiveFrom` to today and set the date picker's minimum accordingly.
`reason` is optional but should be a prominent field — it lands in the audit trail and is
what someone reads when a bill is disputed.

A rate submitted unchanged is **silently skipped** server-side, so no history row is
written for a non-change. The response will look identical; that is correct.

#### Deactivate — `POST /api/v1/products/{id}/deactivate`

**Owner only.** No body. `204 No Content`. Idempotent — deactivating twice is not an error.

`422 PRODUCT_HAS_STOCK`:

```json
{
  "type": "https://crockeryfactory/errors/product-has-stock",
  "title": "Product has stock",
  "status": 422,
  "detail": "CUP-PDR-23369 still has stock in the godown (2125 at First, 80 at Second). Dispatch or adjust it to zero before deactivating the product.",
  "code": "PRODUCT_HAS_STOCK",
  "traceId": "0HNOFV1B15D2T"
}
```

Confirm before calling: "Deactivate {code}? It will no longer appear on new dispatches or
production entries. Existing records are unaffected."

---

### 6.3 Stock — `/stock`

**Read:** `CanViewReports` · **Adjust:** `CanAdjustStock` (Owner, Clerk — *not* Administrator)

#### Current stock — `GET /api/v1/stock?search=&grade=&onlyInStock=false&asOf=`

```json
{
  "lines": [
    {
      "productId": "a27ab681-3bc3-4dbb-8c7a-feef63e7b8c6",
      "productCode": "CUP-PDR-23369",
      "productName": "Cappuccino cup (large)",
      "grade": "First",
      "quantity": 2400,
      "unitRate": 135.00,
      "stockValue": 324000.00,
      "lastMovementAt": "2026-09-11T11:32:19.6636822"
    },
    {
      "productId": "a27ab681-3bc3-4dbb-8c7a-feef63e7b8c6",
      "productCode": "CUP-PDR-23369",
      "productName": "Cappuccino cup (large)",
      "grade": "Second",
      "quantity": 140,
      "unitRate": 81.00,
      "stockValue": 11340.00,
      "lastMovementAt": "2026-09-11T11:32:19.4889895"
    }
  ],
  "totalUnits": 2540,
  "totalValue": 335340.00,
  "asOf": "2026-09-11"
}
```

**Not paged.** One row per product **and grade** — stock is held per grade, and a row
mixing firsts and seconds would describe nothing.

`unitRate` and `stockValue` are **omitted entirely** when a product has no price on file.
Render `—` and exclude the row from the value total, which the server already does.

| Query parameter | Effect |
|---|---|
| `search` | Substring on code or name |
| `grade` | `First` / `Second` / `Third` |
| `onlyInStock` | `true` hides zero-quantity rows |
| `asOf` | `yyyy-MM-dd`. **Switches to a slower historical path** that sums the ledger. Omit it for the live figure. |

> Two different queries sit behind one endpoint. Without `asOf` the server reads the
> cached balances — fast, and what the clerk opens forty times a day. With `asOf` it sums
> the movement ledger — slower and correct for a past date. **Show a loading state and a
> "showing historical position" banner when `asOf` is set.**

**Table columns:** Code · Name · Grade · Quantity · Unit rate · Stock value · Last movement.
Footer row: `totalUnits`, `totalValue`.
Row click → movement history for that product and grade.

**Actions:** "Adjust stock" per row (if `CanAdjustStock`), "View movements" per row.

#### Movement history — `GET /api/v1/stock/{productId}/movements?grade=First&from=&to=&page=1&pageSize=50`

```json
{
  "items": [
    {
      "id": "340f0324-3e18-4f60-8b94-2e2bca96b598",
      "occurredOn": "2026-09-11",
      "grade": "First",
      "quantity": 600,
      "movementType": "ProductionReceipt",
      "referenceNumber": "P-2609-0002",
      "enteredBy": "Munshi",
      "createdAt": "2026-09-11T11:32:19.6636822",
      "runningBalance": 2400
    },
    {
      "id": "ec6327d4-63ff-4bb3-bc8e-8a623ab1ed0f",
      "occurredOn": "2026-09-11",
      "grade": "First",
      "quantity": 1800,
      "movementType": "ProductionReceipt",
      "referenceNumber": "P-2609-0001",
      "enteredBy": "Munshi",
      "createdAt": "2026-09-11T11:32:19.4889895",
      "runningBalance": 1800
    }
  ],
  "page": 1,
  "pageSize": 5,
  "totalCount": 2
}
```

Paged. **Newest first.** `reasonDescription` and `notes` are omitted when absent.

`runningBalance` is the balance **after** that movement, computed over the product's whole
history for that grade — not over the page and not over the date filter. It is what turns
"the stock is wrong" into "the stock went wrong on the 14th", so it must be displayed
prominently, not hidden behind a column toggle.

| Column | Source | Format |
|---|---|---|
| Date | `occurredOn` | `dd MMM yyyy` |
| Type | `movementType` | Humanised — see Appendix A |
| Reference | `referenceNumber` | Link to the document. **Omitted for movement types with no document table yet** — render `—`. |
| In | `quantity` when `> 0` | Green |
| Out | `-quantity` when `< 0` | Red, shown positive |
| Balance | `runningBalance` | Bold |
| Reason | `reasonDescription` | `—` when absent |
| Notes | `notes` | Truncate with tooltip |
| Entered by | `enteredBy` | Full name |

Filters: grade, date from/to. A grade filter is strongly recommended as the default,
because an unfiltered running balance interleaves two independent balances.

#### Create adjustment — `POST /api/v1/stock/adjustments`

**Owner or Clerk.** Send `Idempotency-Key`. Audited.

```json
{
  "productId": "a27ab681-3bc3-4dbb-8c7a-feef63e7b8c6",
  "grade": "First",
  "quantityChange": -35,
  "reasonCodeId": "a2f00000-0000-0000-0000-000000000001",
  "adjustedOn": "2026-09-11",
  "notes": "Chipped during the monthly count"
}
```

`201 Created`, `Location: /api/v1/stock/{productId}/movements`:

```json
{
  "id": "1b1eb4e1-8b3a-4045-b251-d6bc0e9c464f",
  "adjustmentNumber": "A-2609-0001",
  "productId": "a27ab681-3bc3-4dbb-8c7a-feef63e7b8c6",
  "productName": "Cappuccino cup (large)",
  "grade": "First",
  "quantityChange": -35,
  "resultingBalance": 2365,
  "reasonDescription": "Physical count correction",
  "adjustedOn": "2026-09-11",
  "createdAt": "2026-09-11T11:32:20.1337868Z"
}
```

**Form**

| Field | Input | Validation |
|---|---|---|
| `productId` | searchable select | Required, must exist |
| `grade` | select | Required, must be enabled |
| `quantityChange` | number, **signed** | **≠ 0.** Negative removes, positive adds. Label the sign explicitly: "Enter a negative number to reduce stock." |
| `reasonCodeId` | select from `GET /reason-codes?type=StockAdjustment` | Required, must be active and of that type |
| `adjustedOn` | date, default today | Not future; not more than **30 days** back (from `Backdate.Days.Adjustment`) |
| `notes` | textarea | **Required when `abs(quantityChange) > 500`** |

Show the current balance beside the field and a live "Resulting balance: n" preview.

**Errors**

| Code | Status | Cause |
|---|---|---|
| `QUANTITY_ZERO` | 400 | Change of zero |
| `NOTES_REQUIRED` | 400 | Large change with no note |
| `INVALID_REASON_CODE` | 400 | Wrong list or inactive — `detail` lists the valid codes |
| `DATE_IN_FUTURE` | 400 | |
| `DATE_TOO_OLD` | 422 | Beyond the window |
| `STOCK_INSUFFICIENT` | 422 | Would go negative |

```json
{
  "type": "https://crockeryfactory/errors/stock-insufficient",
  "title": "Insufficient stock",
  "status": 422,
  "detail": "Only 2365 units of CUP-PDR-23369 (First) are in stock. 999999 were requested.",
  "code": "STOCK_INSUFFICIENT",
  "traceId": "0HNOFV1B15D2F",
  "errors": [
    { "field": "quantity", "message": "Only 2365 available", "code": "STOCK_INSUFFICIENT" }
  ]
}
```

Note `detail` names the product code and grade — display it, do not replace it with a
generic message.

---

### 6.4 Production — `/production`

**Read:** `CanViewReports` · **Write:** `CanRecordTransactions` (Owner, Clerk)

> The highest-frequency write in the system, with a 45-second end-to-end target including
> typing. **Optimise this form above all others:** autofocus the product, keep the field
> count to what is below, support Enter-to-submit, and reopen empty for the next kiln load.

#### Create — `POST /api/v1/production-entries`

Send `Idempotency-Key`.

```json
{
  "productId": "a27ab681-3bc3-4dbb-8c7a-feef63e7b8c6",
  "entryDate": "2026-09-11",
  "quantityGood": 1800,
  "quantitySeconds": 140,
  "quantityBroken": 60,
  "breakageReasonCodeId": "a1f00000-0000-0000-0000-000000000001",
  "batchReference": "KILN-3/2026-09",
  "notes": "Morning unload"
}
```

`201 Created`:

```json
{
  "id": "52b4348b-9143-44a7-ab20-ae89215512aa",
  "entryNumber": "P-2609-0001",
  "productId": "a27ab681-3bc3-4dbb-8c7a-feef63e7b8c6",
  "productCode": "CUP-PDR-23369",
  "productName": "Cappuccino cup (large)",
  "entryDate": "2026-09-11",
  "quantityGood": 1800,
  "quantitySeconds": 140,
  "quantityBroken": 60,
  "totalFired": 2000,
  "lossPercentage": 3.00,
  "breakageReason": "Cracked in firing",
  "batchReference": "KILN-3/2026-09",
  "notes": "Morning unload",
  "status": "Active",
  "enteredBy": "Munshi",
  "createdAt": "2026-09-11T11:32:19.4148832Z",
  "warnings": []
}
```

**Stock effect, applied in the same transaction:** `quantityGood` → **First**,
`quantitySeconds` → **Second**. `quantityBroken` **never enters stock** — broken is a
shard on the floor, not a second. Say so in the form's help text, because clerks ask.

**Form**

| Field | Input | Validation |
|---|---|---|
| `productId` | searchable select, autofocus | Required, exists, **and active** (`422 PRODUCT_INACTIVE` otherwise) |
| `entryDate` | date, default today | Not future; within **7 days** (`Backdate.Days.Production`) |
| `quantityGood` | number | ≥ 0 |
| `quantitySeconds` | number | ≥ 0 |
| `quantityBroken` | number | ≥ 0 |
| *(computed)* | read-only | `totalFired` and `loss %`, live |
| `breakageReasonCodeId` | select from `GET /reason-codes?type=Breakage` | **Required when `quantityBroken > 0`**; reveal the field as soon as broken goes above zero |
| `batchReference` | text, optional | ≤ 48 |
| `notes` | textarea, optional | ≤ 500 |

Cross-field: `totalFired` must be **> 0** and **≤ 50,000**.

**Errors**

```json
// 400 — all three quantities zero
{
  "type": "https://crockeryfactory/errors/no-quantity",
  "title": "Validation failed",
  "status": 400,
  "detail": "This entry records no pieces at all. Enter what came out of the kiln.",
  "code": "NO_QUANTITY",
  "traceId": "0HNOFV1B15D2C",
  "errors": [
    { "field": "quantityGood", "message": "At least one quantity must be greater than zero", "code": "NO_QUANTITY" }
  ]
}
```

```json
// 400 — breakage with no reason
{
  "type": "https://crockeryfactory/errors/breakage-reason-required",
  "title": "Validation failed",
  "status": 400,
  "detail": "Breakages need a reason, so that the loss report can say what is going wrong.",
  "code": "BREAKAGE_REASON_REQUIRED",
  "traceId": "0HNOFV1B15D2D",
  "errors": [
    { "field": "breakageReasonCodeId", "message": "Required when anything is broken", "code": "BREAKAGE_REASON_REQUIRED" }
  ]
}
```

```json
// 400 — future date
{
  "type": "https://crockeryfactory/errors/date-in-future",
  "title": "Validation failed",
  "status": 400,
  "detail": "The date 2026-09-12 is in the future. Records are entered for work already done.",
  "code": "DATE_IN_FUTURE",
  "traceId": "0HNOFV1B15D2E",
  "errors": [
    { "field": "entryDate", "message": "Cannot be in the future", "code": "DATE_IN_FUTURE" }
  ]
}
```

`422 QUANTITY_IMPLAUSIBLE` — `totalFired` above 50,000.
`422 DATE_TOO_OLD` — beyond the backdating window; `detail` names the earliest date allowed.

**The high-loss warning is a `201`, not an error:**

```json
{
  "id": "3227e343-8dca-4e29-a720-3e00e8ab2501",
  "entryNumber": "P-2609-0002",
  "quantityGood": 600,
  "quantitySeconds": 0,
  "quantityBroken": 400,
  "totalFired": 1000,
  "lossPercentage": 40.0,
  "breakageReason": "Cracked in firing",
  "status": "Active",
  "enteredBy": "Munshi",
  "createdAt": "2026-09-11T11:32:19.6525021Z",
  "warnings": ["LOSS_UNUSUALLY_HIGH"]
}
```

**The entry was saved.** Show the entry number and a non-blocking notice. Do not show a
modal that must be dismissed, and never present it as a failure — a 40% loss is usually a
typo and occasionally a bad firing, and the system that refuses to record reality is the
one the clerk abandons.

#### List — `GET /api/v1/production-entries?productId=&from=&to=&includeCancelled=false&page=1&pageSize=50`

Paged; same entry shape as above, with `warnings: []` on every row.

**Columns:** Entry no. · Date · Product · Good · Seconds · Broken · Total · Loss % · Batch ·
Entered by · Status · Actions.
Amber the Loss % cell above 25. Strike through cancelled rows and badge them.

#### Get one — `GET /api/v1/production-entries/{id}`

Same shape. `warnings` is always `[]` on a read — warnings are a property of the moment of
creation.

#### Cancel — `POST /api/v1/production-entries/{id}/cancel`

```json
{ "reason": "Recorded against the wrong kiln load" }
```

`200 OK` returns the entry with `status: "Cancelled"`.

Reversing movements are written; **the original entry and its movements are never deleted**,
so both appear in the ledger. Explain that in the confirmation dialog.

| Rule | Status | Code |
|---|---|---|
| `reason` ≥ 10 characters | 400 | `REASON_REQUIRED` |
| Already cancelled | 409 | `ALREADY_CANCELLED` |
| Not today's, and caller lacks `CanCancelHistorical` | 403 | `CANCELLATION_WINDOW_EXPIRED` |
| Stock already dispatched | 422 | `STOCK_INSUFFICIENT_FOR_REVERSAL` |

```json
{
  "type": "https://crockeryfactory/errors/stock-insufficient-for-reversal",
  "title": "Cannot reverse this entry",
  "status": 422,
  "detail": "Entry P-2609-0001 cannot be cancelled: some of the CUP-PDR-23369 it produced has already been dispatched, so reversing it would leave negative stock. Record a stock adjustment instead, with the reason for the correction.",
  "code": "STOCK_INSUFFICIENT_FOR_REVERSAL",
  "traceId": "0HNOFV1B15D2U"
}
```

`detail` tells the clerk exactly what to do instead. Show it, and offer a
**"Record an adjustment"** button that opens the adjustment form pre-filled with the
product and grade.

The reason field needs a live character counter — a 10-character minimum that fails after
submission is a bad experience on a 45-second form.

#### Summary — `GET /api/v1/production-entries/summary?from=&to=&productId=&groupBy=Product`

`groupBy`: `Product` | `Day` | `Month`. **Bare array, not paged.** Excludes cancelled.

```json
[
  {
    "groupKey": "a27ab681-3bc3-4dbb-8c7a-feef63e7b8c6",
    "groupLabel": "CUP-PDR-23369 - Cappuccino cup (large)",
    "totalFired": 3000,
    "totalGood": 2400,
    "totalSeconds": 140,
    "totalBroken": 460,
    "lossPercentage": 15.33,
    "secondsPercentage": 4.67,
    "entryCount": 2
  }
]
```

Display `groupLabel`, never `groupKey` — the key is a Guid for `Product` grouping.

---

### 6.5 Customers — `/customers`

**Read:** `CanViewReports` · **Write:** `CanRecordTransactions` (Owner, Clerk)

#### List — `GET /api/v1/customers?search=&includeInactive=false&page=1&pageSize=50`

```json
{
  "items": [
    {
      "id": "61234432-8210-4e2a-b720-64fa2b4ee0fa",
      "code": "C-PDR-3851",
      "name": "Shalimar Traders",
      "city": "Gujrat",
      "phone": "03001234567",
      "address": "Main Bazaar, Gujrat",
      "openingBalance": 18500.00,
      "openingBalanceAsOf": "2026-07-13",
      "isActive": true,
      "notes": "Long-standing account",
      "createdAt": "2026-09-11T11:32:20.2888345Z"
    }
  ],
  "page": 1,
  "pageSize": 2,
  "totalCount": 1
}
```

`search` matches code, name, city **or** phone.

**Columns:** Code · Name · City · Phone · Opening balance · Status · Actions
(Edit, Statement, Deactivate, New dispatch, New payment).

#### Create — `POST /api/v1/customers`

Send `Idempotency-Key`.

```json
{
  "code": "C-PDR-3851",
  "name": "Shalimar Traders",
  "city": "Gujrat",
  "phone": "03001234567",
  "address": "Main Bazaar, Gujrat",
  "openingBalance": 18500.00,
  "openingBalanceAsOf": "2026-07-13",
  "notes": "Long-standing account"
}
```

`201 Created` with the customer as above.

| Field | Input | Validation |
|---|---|---|
| `code` | text, uppercased | Required, ≤ 24, `^[A-Z0-9\-]+$`, unique |
| `name` | text | Required, ≤ 160 |
| `city` | text, optional | ≤ 80 |
| `phone` | text, optional | ≤ 24 |
| `address` | textarea, optional | ≤ 300 |
| `openingBalance` | number, default 0 | `abs()` ≤ 100,000,000. **Negative is legal** — the factory owes the customer. |
| `openingBalanceAsOf` | date, optional | Free |
| `notes` | textarea, optional | ≤ 500 |

Warn beside the opening balance field: **"This can never be changed once the customer has
a dispatch or a payment."** That is the truth, and clerks should know before they type.

#### Update — `PUT /api/v1/customers/{id}`

**Requires `If-Match`.** Full replacement — send every field, including `openingBalance`
unchanged.

`422 OPENING_BALANCE_LOCKED` when `openingBalance` differs and any transaction exists:

```json
{
  "type": "https://crockeryfactory/errors/opening-balance-locked",
  "title": "Opening balance locked",
  "status": 422,
  "detail": "Shalimar Traders already has dispatches or payments, so the opening balance cannot be changed. Record an adjusting payment or dispatch instead.",
  "code": "OPENING_BALANCE_LOCKED",
  "traceId": "0HNOFV1B15D2V"
}
```

Every other field remains editable after trading starts. **Render `openingBalance`
read-only when the customer has any transaction** rather than letting the clerk type a
change that will be refused. Detect that from a non-empty
`GET /dispatches?customerId=` or `GET /payments?customerId=`, or simply from the
outstanding row's `totalDispatched`/`totalPaid` being non-zero.

#### Deactivate — `POST /api/v1/customers/{id}/deactivate`

No body, `204 No Content`, idempotent. A deactivated customer cannot receive dispatches
but **can still make payments** — they may be settling what they owe.

#### Outstanding — `GET /api/v1/customers/outstanding`

**The report that justifies the system to the owner.** One call, **sorted largest debt
first** without being asked. Not paged.

```json
{
  "rows": [
    {
      "customerId": "61234432-8210-4e2a-b720-64fa2b4ee0fa",
      "code": "C-PDR-3851",
      "name": "Shalimar Traders",
      "city": "Gujrat",
      "phone": "03001234567",
      "openingBalance": 18500.00,
      "totalDispatched": 36600.00,
      "totalPaid": 5020000.00,
      "outstanding": -4964900.00,
      "lastDispatchDate": "2026-09-11",
      "lastPaymentDate": "2026-09-11",
      "daysSinceLastPayment": 0
    }
  ],
  "totalOutstanding": -4964900.00,
  "asOf": "2026-09-11"
}
```

`outstanding = openingBalance + totalDispatched − totalPaid`, counting **active documents
only**.

**Do not re-sort by default.** The order is the report. Allow the user to re-sort
explicitly, but open on the server's order.

| Column | Notes |
|---|---|
| Name / Code | Link to statement |
| City, Phone | `—` when absent |
| Opening | PKR |
| Dispatched | PKR |
| Paid | PKR |
| **Outstanding** | Bold. **Negative means in advance** — show as "12,500 in advance" in a distinct colour, not as a red minus. |
| Last dispatch / Last payment | `—` when absent |
| Days since payment | Amber > 30, red > 60 |

Footer: `totalOutstanding`. Header: "As at {`asOf`}".

Inactive customers who owe nothing are excluded server-side; inactive customers who owe
money **are included**, because a debt does not disappear when an account is closed.

#### Statement — `GET /api/v1/customers/{id}/statement?from=&to=`

Defaults: `from` = first of the current month, `to` = today. Not paged.

```json
{
  "customerId": "61234432-8210-4e2a-b720-64fa2b4ee0fa",
  "customerName": "Shalimar Traders",
  "from": "2026-08-12",
  "to": "2026-09-11",
  "openingBalance": 18500.00,
  "lines": [
    {
      "date": "2026-09-11",
      "documentType": "Dispatch",
      "documentNumber": "D-2609-0001",
      "description": "Cancelled",
      "debit": 0,
      "runningBalance": 18500.00
    },
    {
      "date": "2026-09-11",
      "documentType": "Payment",
      "documentNumber": "R-2609-0001",
      "description": "Cheque CHQ-889231",
      "credit": 20000.00,
      "runningBalance": -1500.00
    },
    {
      "date": "2026-09-11",
      "documentType": "Payment",
      "documentNumber": "R-2609-0002",
      "description": "Cash",
      "credit": 5000000.00,
      "runningBalance": -5001500.00
    }
  ],
  "closingBalance": -5001500.00
}
```

Note precisely:

- A **debit line has no `credit` key**, and vice versa. Use `line.debit ?? null`.
- **Cancelled documents appear with a zero value and `description: "Cancelled"`**, and do
  not move the running balance. They are shown on purpose: a customer comparing this to
  his own file would otherwise find a gap in the numbering and assume something was being
  hidden from him. **Never filter them out.** Render them greyed and italic with a
  "Cancelled" badge.
- Everything before `from` is collapsed into `openingBalance`, so the statement balances
  for any window.

**Layout:** Header (customer name, period, opening balance) → table (Date · Type ·
Document no. · Description · Debit · Credit · Balance) → footer (closing balance).
The first row should be a synthetic "Opening balance" line so the arithmetic reads
top-to-bottom.

`closingBalance` must equal the last line's `runningBalance`. If it does not, that is a
backend bug worth reporting — do not paper over it.

---

### 6.6 Dispatches — `/dispatches`

**Read:** `CanViewReports` · **Write:** `CanRecordTransactions` (Owner, Clerk)

> The transaction with the most rules, measured at 90 seconds end to end. An
> `Idempotency-Key` is **strongly recommended** here above everywhere else: a duplicated
> dispatch means stock leaves the godown twice on paper that says once.

#### Create — `POST /api/v1/dispatches`

```json
{
  "customerId": "61234432-8210-4e2a-b720-64fa2b4ee0fa",
  "dispatchDate": "2026-09-11",
  "lines": [
    { "productId": "a27ab681-3bc3-4dbb-8c7a-feef63e7b8c6", "grade": "First",  "quantity": 240, "unitRate": null },
    { "productId": "a27ab681-3bc3-4dbb-8c7a-feef63e7b8c6", "grade": "Second", "quantity": 60,  "unitRate": 70.00 }
  ],
  "vehicleNumber": "GJT-4417",
  "notes": "Loaded at the east gate"
}
```

`unitRate: null` (or omitted) means **use the current list price**. Supplying a number
overrides it. Either way the resolved rate is written onto the line and never re-read.

`201 Created`:

```json
{
  "id": "4e9e0b1b-0b47-4211-a328-472f1d9ff42d",
  "dispatchNumber": "D-2609-0001",
  "customerId": "61234432-8210-4e2a-b720-64fa2b4ee0fa",
  "customerName": "Shalimar Traders",
  "dispatchDate": "2026-09-11",
  "lines": [
    {
      "lineNumber": 1,
      "productId": "a27ab681-3bc3-4dbb-8c7a-feef63e7b8c6",
      "productCode": "CUP-PDR-23369",
      "productName": "Cappuccino cup (large)",
      "grade": "First",
      "quantity": 240,
      "unitRate": 135.00,
      "lineAmount": 32400.00,
      "stockAfter": 2125
    },
    {
      "lineNumber": 2,
      "productId": "a27ab681-3bc3-4dbb-8c7a-feef63e7b8c6",
      "productCode": "CUP-PDR-23369",
      "productName": "Cappuccino cup (large)",
      "grade": "Second",
      "quantity": 60,
      "unitRate": 70.00,
      "lineAmount": 4200.00,
      "stockAfter": 80
    }
  ],
  "totalAmount": 36600.00,
  "customerBalanceAfter": 55100.00,
  "vehicleNumber": "GJT-4417",
  "notes": "Loaded at the east gate",
  "status": "Active",
  "enteredBy": "Munshi",
  "createdAt": "2026-09-11T11:32:20.4929166Z",
  "warnings": []
}
```

`customerBalanceAfter` exists **so the clerk can read the new balance aloud at the gate
without a second request.** Display it prominently in the success state — it is worth
about ten seconds of the 90-second budget.

`stockAfter` per line is populated **only on create**; on a later `GET` it reads `0`.
Do not display it on the read-only view.

**Form**

| Field | Input | Validation |
|---|---|---|
| `customerId` | searchable select | Required, exists, **active** |
| `dispatchDate` | date, default today | Not future; within **7 days** |
| `lines[]` | editable grid | ≥ 1 row, ≤ **50** rows |
| — `productId` | searchable select | Required |
| — `grade` | select | Required, enabled |
| — `quantity` | number | **> 0** |
| — `unitRate` | number, optional | **> 0** when supplied; blank = list price |
| `vehicleNumber` | text, optional | ≤ 24 |
| `notes` | textarea, optional | ≤ 500 |

**No duplicate `productId` + `grade` across lines.** Validate this client-side as rows are
added — each would pass the stock check alone and together take more than exists.

The line grid should show, live per row: available stock (from the products list), the
resolved rate, and the line amount; plus a running document total.

**Errors**

```json
// 422 — stock, naming the product and grade
{
  "type": "https://crockeryfactory/errors/stock-insufficient",
  "title": "Insufficient stock",
  "status": 422,
  "detail": "Only 2125 units of CUP-PDR-23369 (First) are in stock. 999999 were requested.",
  "code": "STOCK_INSUFFICIENT",
  "traceId": "0HNOFV1B15D2H",
  "errors": [
    { "field": "quantity", "message": "Only 2125 available", "code": "STOCK_INSUFFICIENT" }
  ]
}
```

> **Nothing was written.** Stock for every line is checked before any line is saved, so a
> four-line dispatch that fails on line three leaves no document, no movement and no
> balance change. The form keeps its state; the clerk fixes the quantity and resubmits
> with the same `Idempotency-Key`.
>
> Note the `field` is `"quantity"` without a line index. When several lines are short,
> `detail` names each product. **Bind the message to the form, and highlight rows by
> matching the product code in `detail`** — or simply show `detail` as a banner, which is
> the safer implementation.

```json
// 400 — same product and grade twice
{
  "type": "https://crockeryfactory/errors/duplicate-line",
  "title": "Validation failed",
  "status": 400,
  "detail": "The dispatch could not be saved.",
  "code": "DUPLICATE_LINE",
  "traceId": "0HNOFV1B15D2I",
  "errors": [
    { "field": "lines[1]", "message": "This product and grade already appears on another line", "code": "DUPLICATE_LINE" }
  ]
}
```

`lines[1]` is the **whole row**, zero-indexed — highlight row 2.

| Code | Status | Cause |
|---|---|---|
| `NO_LINES` | 400 | Empty `lines` |
| `TOO_MANY_LINES` | 400 | More than 50 |
| `CUSTOMER_INACTIVE` | 422 | Customer deactivated |
| `NO_PRICE_AVAILABLE` | 422 | No list price and none typed — `detail` names the product and grade |
| `GRADE_NOT_ENABLED` | 422 | |
| `DATE_IN_FUTURE` / `DATE_TOO_OLD` | 400 / 422 | |

**Warning on success:**

```json
{ "warnings": ["RATE_BELOW_LIST"] }
```

Emitted when a typed rate is under half the list price. `201` — the dispatch saved.

#### List — `GET /api/v1/dispatches?customerId=&from=&to=&includeCancelled=false&page=1&pageSize=50`

Paged. Rows carry their `lines`, but `customerBalanceAfter` and `stockAfter` are `0` —
they are creation-time figures. Do not render them in the list.

**Columns:** Dispatch no. · Date · Customer · Lines (count) · Total · Vehicle · Entered by ·
Status · Actions (View, Print [disabled], Cancel).

#### Get one — `GET /api/v1/dispatches/{id}`

Full dispatch with lines. `warnings: []`, `stockAfter: 0`.

#### Cancel — `POST /api/v1/dispatches/{id}/cancel`

```json
{ "reason": "Vehicle never left the yard" }
```

`200 OK` with `status: "Cancelled"`. Reversing movements return the stock and the
customer's balance drops by the dispatch total.

| Rule | Status | Code |
|---|---|---|
| `reason` ≥ 10 characters | 400 | `REASON_REQUIRED` |
| Already cancelled | 409 | `ALREADY_CANCELLED` |
| Not today's, no `CanCancelHistorical` | 403 | `CANCELLATION_WINDOW_EXPIRED` |
| Older than **90 days** | 422 | `CANCELLATION_TOO_LATE` |

```json
{
  "type": "https://crockeryfactory/errors/cancellation-too-late",
  "title": "Too late to cancel",
  "status": 422,
  "detail": "Dispatch D-2609-0001 is 104 days old and cannot be cancelled after 90 days. Record a sales return or an adjustment instead.",
  "code": "CANCELLATION_TOO_LATE",
  "traceId": "0HNOFV1B15D2W"
}
```

A printed dispatch is **never edited**, only cancelled and re-entered, and the
cancellation stays visible. There is deliberately no `PUT /dispatches/{id}` — do not build
an edit form. Offer **"Cancel and re-enter"**, which cancels and opens a new dispatch
pre-filled from the cancelled one.

#### Print — `GET /api/v1/dispatches/{id}/document?copies=original|duplicate|both`

**Returns `501` today.**

```json
{
  "type": "https://crockeryfactory/errors/not-implemented",
  "title": "Document rendering not available yet",
  "status": 501,
  "detail": "The printed dispatch note (both) is not available yet. The figures are all on the dispatch itself in the meantime.",
  "code": "NOT_IMPLEMENTED",
  "traceId": "0HNOFV1B15D2K"
}
```

Render the button **disabled with a tooltip carrying `detail`**. Build the call now so
enabling it later is a one-line change; when it is implemented it will return
`application/pdf`, to be handled as a blob download.

---

### 6.7 Payments — `/payments`

**Read:** `CanViewReports` · **Write:** `CanRecordTransactions` (Owner, Clerk)

#### Create — `POST /api/v1/payments`

Send `Idempotency-Key`.

```json
{
  "customerId": "61234432-8210-4e2a-b720-64fa2b4ee0fa",
  "paymentDate": "2026-09-11",
  "amount": 20000.00,
  "method": "Cheque",
  "reference": "CHQ-889231",
  "notes": "Part settlement"
}
```

`201 Created`:

```json
{
  "id": "4e2c369f-ef1d-4fdc-ba20-b4b4efa5c8a2",
  "paymentNumber": "R-2609-0001",
  "customerId": "61234432-8210-4e2a-b720-64fa2b4ee0fa",
  "customerName": "Shalimar Traders",
  "paymentDate": "2026-09-11",
  "amount": 20000.00,
  "method": "Cheque",
  "reference": "CHQ-889231",
  "customerBalanceAfter": 35100.00,
  "status": "Active",
  "enteredBy": "Munshi",
  "createdAt": "2026-09-11T11:32:20.9198246Z",
  "warnings": []
}
```

| Field | Input | Validation |
|---|---|---|
| `customerId` | searchable select | Required, exists. **Inactive customers are allowed** — they may be paying off a debt. |
| `paymentDate` | date, default today | Not future; within **30 days** |
| `amount` | number | **> 0** |
| `method` | select: Cash / BankTransfer / Cheque / Other | Required |
| `reference` | text | **Required for `Cheque` and `BankTransfer`**, ≤ 64. Optional otherwise. |
| `notes` | textarea, optional | ≤ 500 |

Change the `reference` label with the method: "Cheque number" / "Transfer reference" /
"Reference (optional)". Show the customer's current balance beside the amount, and a live
"Balance after" preview.

**Errors**

```json
{
  "type": "https://crockeryfactory/errors/reference-required",
  "title": "Validation failed",
  "status": 400,
  "detail": "Enter the transfer reference.",
  "code": "REFERENCE_REQUIRED",
  "traceId": "0HNOFV1B15D2L",
  "errors": [
    { "field": "reference", "message": "Required for this payment method", "code": "REFERENCE_REQUIRED" }
  ]
}
```

`detail` is tailored to the method — "Enter the cheque number." for `Cheque`.

`400 AMOUNT_INVALID` — zero or negative.

**Overpayment is a `201` with a warning:**

```json
{
  "id": "0b009a96-28ca-4151-8030-e121a03a305e",
  "paymentNumber": "R-2609-0002",
  "amount": 5000000,
  "method": "Cash",
  "customerBalanceAfter": -4964900.00,
  "status": "Active",
  "warnings": ["PAYMENT_EXCEEDS_OUTSTANDING"]
}
```

Saved. Advances are normal in this trade and a negative balance is legitimate. Show
"Receipt R-2609-0002 saved. This exceeds what the customer owed — the account is now
4,964,900 in advance."

Note `reference` is **absent** from that response because it was null for a cash receipt.

#### List — `GET /api/v1/payments?customerId=&from=&to=&includeCancelled=false&page=1&pageSize=50`

Paged. `customerBalanceAfter` is `0` in list rows — do not display it there.

**Columns:** Receipt no. · Date · Customer · Amount · Method · Reference · Entered by ·
Status · Actions.

#### Cancel — `POST /api/v1/payments/{id}/cancel`

```json
{ "reason": "Cheque was returned unpaid by the bank" }
```

Same rules as a dispatch cancellation, minus the 90-day ceiling. Cancelling a receipt
**increases** what the customer owes — say so in the confirmation: "This will add
{amount} back to {customer}'s balance."

---

### 6.8 Reports — `/reports`

**Permission:** `CanViewReports` (everyone)

A tabbed page. Every report accepts `format=` and returns `501` for anything but JSON.

#### Daily stock (RP-01) — `GET /api/v1/reports/daily-stock?date=2026-09-11`

```json
{
  "date": "2026-09-11",
  "rows": [
    {
      "productId": "a27ab681-3bc3-4dbb-8c7a-feef63e7b8c6",
      "productCode": "CUP-PDR-23369",
      "productName": "Cappuccino cup (large)",
      "grade": "First",
      "opening": 0,
      "received": 600,
      "dispatched": 0,
      "adjusted": -35,
      "closing": 565
    },
    {
      "productId": "a27ab681-3bc3-4dbb-8c7a-feef63e7b8c6",
      "productCode": "CUP-PDR-23369",
      "productName": "Cappuccino cup (large)",
      "grade": "Second",
      "opening": 0,
      "received": 0,
      "dispatched": 0,
      "adjusted": 0,
      "closing": 0
    }
  ],
  "totalOpening": 0,
  "totalReceived": 600,
  "totalDispatched": 0,
  "totalAdjusted": -35,
  "totalClosing": 565
}
```

**`dispatched` is reported as a positive number** — the sheet reads "dispatched 240", not
"dispatched −240". `adjusted` keeps its sign.

The arithmetic on every row is:

```
opening + received − dispatched + adjusted = closing
```

Render it in exactly that column order so it reads left to right. A dispatch raised and
cancelled on the same day nets to zero rather than inflating both columns — that is
correct, not a bug.

Date picker defaults to today, capped at today.

#### Outstanding (RP-02) — `GET /api/v1/reports/outstanding`

Identical payload to `GET /customers/outstanding` (§6.5), reached from the reports menu.
Reuse the component.

#### Production summary (RP-04) — `GET /api/v1/reports/production-summary?from=&to=&productId=&groupBy=Product`

Identical to `GET /production-entries/summary` (§6.4). Bare array.

#### Sales summary (RP-06) — `GET /api/v1/reports/sales-summary?from=&to=&groupBy=Customer`

`groupBy`: `Customer` | `Product` | `Month`.

```json
{
  "from": "2026-08-12",
  "to": "2026-09-11",
  "groupBy": "Customer",
  "rows": [
    {
      "groupKey": "61234432-8210-4e2a-b720-64fa2b4ee0fa",
      "groupLabel": "Shalimar Traders",
      "dispatchCount": 1,
      "totalQuantity": 300,
      "totalAmount": 36600.00
    }
  ],
  "totalQuantity": 0,
  "totalAmount": 0
}
```

Empty period returns `"rows": []` with zero totals — render an empty state, not a spinner.

`dispatchCount` counts **documents, not lines**: a four-line dispatch is one dispatch.
Sorted by `totalAmount` descending. Cancelled dispatches are excluded — they are not sales.

Defaults: `from` = first of the current month, `to` = today.

#### Dashboard (RP-07)

See §6.1.

#### Export

```
GET /api/v1/reports/daily-stock?format=xlsx
```
```json
{
  "type": "https://crockeryfactory/errors/not-implemented",
  "title": "Format not available yet",
  "status": 501,
  "detail": "This report is not available as 'xlsx' yet - only JSON. PDF and spreadsheet output are a later piece of work.",
  "code": "NOT_IMPLEMENTED",
  "traceId": "0HNOFV1B15D2M"
}
```

Render "Export PDF" and "Export Excel" **disabled**, tooltip = `detail`. Wire the calls now.

A client-side **CSV export of the loaded table is a reasonable interim** and needs no
backend — offer it if the factory needs data in Excel before the server-side exporter
lands.

---

### 6.9 Users — `/admin/users`

**Permission:** `CanManageUsers` (Owner, Administrator — **not** Clerk)

#### List — `GET /api/v1/users`

**Bare array, not paged.**

```json
[
  {
    "id": "daf85329-8980-455b-bc56-19ac139854be",
    "userName": "admin",
    "fullName": "Administrator",
    "roles": ["Administrator"],
    "isActive": true,
    "createdAt": "2026-09-11T11:32:17.1149675",
    "lastLoginAt": "2026-09-11T11:32:18.5756418"
  },
  {
    "id": "a403e799-bd6b-42e7-9a5e-3abec1ebc80e",
    "userName": "gone",
    "fullName": "Departed Clerk",
    "roles": ["Clerk"],
    "isActive": false,
    "createdAt": "2026-09-11T11:32:17.2140533"
  },
  {
    "id": "00000000-0000-0000-0000-00000000513e",
    "userName": "system",
    "fullName": "System",
    "roles": [],
    "isActive": false,
    "createdAt": "2026-01-01T00:00:00"
  }
]
```

`lastLoginAt` is **omitted** for a user who has never signed in — show "Never".

> **The `system` account** (`00000000-0000-0000-0000-00000000513e`) is seeded data rows
> are attributed to, not a login. It has no roles, `isActive: false`, and no password.
> **Filter it out of the user list**, or show it greyed and non-actionable. Offering
> "Reset password" on it would be nonsense.

**Columns:** Username · Full name · Role · Status · Created · Last login · Actions
(Edit, Reset password, Deactivate).

#### Create — `POST /api/v1/users`

```json
{
  "userName": "munshi6208",
  "fullName": "Abdul Rehman",
  "password": "Factory!Pass99",
  "role": "Clerk"
}
```

`201 Created`, `Location: /api/v1/users/{id}`:

```json
{
  "id": "ef76a384-741e-4938-9eb9-9bdc7f5d5020",
  "userName": "munshi6208",
  "fullName": "Abdul Rehman",
  "roles": ["Clerk"],
  "isActive": true,
  "createdAt": "2026-09-11T11:32:21.7531808Z"
}
```

| Field | Validation |
|---|---|
| `userName` | Required, unique |
| `fullName` | Required |
| `password` | ≥ 10 chars, ≥ 1 digit, ≥ 1 lowercase |
| `role` | Exactly one of `Owner`, `Clerk`, `Administrator` |

**One role per user**, sent as a string, returned as a one-element `roles` array. Use a
radio group or single select, never a multi-select.

`400 VALIDATION_FAILED` for a duplicate username or a weak password; errors are keyed
`userName` or `password`.

#### Update — `PUT /api/v1/users/{id}`

```json
{ "fullName": "Abdul Rehman Khan", "role": "Owner" }
```

No `If-Match`. Username and password are not changed here. **Changing the role rolls the
user's security stamp**, so their open sessions end immediately — warn before saving.

#### Deactivate — `POST /api/v1/users/{id}/deactivate`

No body, `204 No Content`. Never a delete (SE-14): every document they entered still
points at them.

Ends their live sessions at once. Confirmation: "Deactivate {fullName}? They will be
signed out immediately and cannot sign in again."

`422` when deactivating yourself:

```json
{
  "type": "https://crockeryfactory/errors/validation-failed",
  "title": "Cannot deactivate yourself",
  "status": 422,
  "detail": "You cannot deactivate the account you are signed in with. Ask another administrator to do it.",
  "code": "VALIDATION_FAILED",
  "traceId": "0HNOFV1B15D2X"
}
```

Better: disable the row action where `id === currentUser.userId`.

#### Reset password — `POST /api/v1/users/{id}/reset-password`

```json
{ "newPassword": "Correct!Horse42" }
```

`204 No Content`. Same password rules. **The password never appears in the audit trail** —
only the fact of the reset. Do not log it client-side either.

---

### 6.10 Reason codes — `/admin/reason-codes`

**Read:** `CanViewReports` (everyone — the dropdowns need it) · **Write:** `CanManageSettings`

#### List — `GET /api/v1/reason-codes?type=Breakage&includeInactive=false`

**Bare array.** Omit `type` for all lists.

```json
[
  { "id": "a1f00000-0000-0000-0000-000000000001", "type": "Breakage", "code": "CRACK",  "description": "Cracked in firing", "sortOrder": 1,  "isActive": true },
  { "id": "a1f00000-0000-0000-0000-000000000002", "type": "Breakage", "code": "WARP",   "description": "Warped",            "sortOrder": 2,  "isActive": true },
  { "id": "a1f00000-0000-0000-0000-000000000003", "type": "Breakage", "code": "GLAZE",  "description": "Glaze fault",       "sortOrder": 3,  "isActive": true },
  { "id": "a1f00000-0000-0000-0000-000000000004", "type": "Breakage", "code": "HANDLE", "description": "Handle failure",    "sortOrder": 4,  "isActive": true },
  { "id": "a1f00000-0000-0000-0000-000000000005", "type": "Breakage", "code": "OTHER",  "description": "Other",             "sortOrder": 99, "isActive": true }
]
```

Returned sorted by `type`, then `sortOrder`, then `code`. **Preserve that order in
dropdowns** — `sortOrder` is how the factory wants them presented, with "Other" at 99.

Display `description` in dropdowns, send `id`. `code` is for the admin screen only.

**Fetch once at bootstrap and cache for the session.** Every transaction form needs these
and they change a few times a year.

The same `code` legitimately appears in more than one list — `OTHER` exists in all four.
Uniqueness is per `type`, so **always filter by `type` when populating a dropdown**.

#### Create — `POST /api/v1/reason-codes`

```json
{ "type": "Breakage", "code": "KILNFAIL", "description": "Kiln temperature failure", "sortOrder": 5 }
```

`200 OK` with the created code.

| Field | Validation |
|---|---|
| `type` | One of the four (Appendix A). **Not editable afterwards.** |
| `code` | Required, ≤ 24, uppercased, unique within its `type` |
| `description` | Required, ≤ 160 |
| `sortOrder` | Integer |

`409 DUPLICATE_CODE` — `detail`: `"'KILNFAIL' already exists in the Breakage list."`

#### Update — `PUT /api/v1/reason-codes/{id}`

```json
{ "description": "Kiln temperature failure (revised)", "sortOrder": 6 }
```

**Only `description` and `sortOrder` are editable.** The `code` is stamped on documents
already recorded; changing it would rewrite what those documents say happened. Render code
and type read-only in the edit form.

#### Deactivate — `POST /api/v1/reason-codes/{id}/deactivate`

`204 No Content`. Deactivated rather than deleted — existing documents carry the reason and
reports that group by it must still be able to name it. Inactive codes stop appearing in
dropdowns but still render on historical records.

---

### 6.11 Settings — `/admin/settings`

**Permission:** `CanManageSettings` (Owner, Administrator)

#### Read — `GET /api/v1/settings`

**Bare array**, sorted by key.

```json
[
  { "key": "Backdate.Days.Adjustment",       "value": "30",               "description": "Days a stock adjustment may be backdated.", "updatedAt": "2026-01-01T00:00:00" },
  { "key": "Backdate.Days.Dispatch",         "value": "7",                "description": "Days a dispatch may be backdated.",         "updatedAt": "2026-01-01T00:00:00" },
  { "key": "Backdate.Days.Payment",          "value": "30",               "description": "Days a payment may be backdated.",          "updatedAt": "2026-01-01T00:00:00" },
  { "key": "Backdate.Days.Production",       "value": "7",                "description": "Days a production entry may be backdated.", "updatedAt": "2026-01-01T00:00:00" },
  { "key": "Dispatch.Cancellation.MaxDays",  "value": "90",               "description": "A dispatch older than this cannot be cancelled - CANCELLATION_TOO_LATE.", "updatedAt": "2026-01-01T00:00:00" },
  { "key": "Document.Prefix.Adjustment",     "value": "A",                "description": "Stock adjustment number prefix.",           "updatedAt": "2026-01-01T00:00:00" },
  { "key": "Document.Prefix.Dispatch",       "value": "D",                "description": "Dispatch number prefix.",                   "updatedAt": "2026-01-01T00:00:00" },
  { "key": "Document.Prefix.Payment",        "value": "R",                "description": "Payment receipt number prefix.",            "updatedAt": "2026-01-01T00:00:00" },
  { "key": "Document.Prefix.Production",     "value": "P",                "description": "Production entry number prefix.",           "updatedAt": "2026-01-01T00:00:00" },
  { "key": "Factory.Address",                "value": "",                 "description": "Printed under the factory name.",           "updatedAt": "2026-01-01T00:00:00" },
  { "key": "Factory.Name",                   "value": "Crockery Factory", "description": "Printed on every document header.",         "updatedAt": "2026-01-01T00:00:00" },
  { "key": "Factory.Phone",                  "value": "",                 "description": "Printed on dispatch documents.",            "updatedAt": "2026-01-01T00:00:00" },
  { "key": "Grades.Enabled",                 "value": "1,2",              "description": "Comma-separated QualityGrade values in use. A grade not listed is rejected with GRADE_NOT_ENABLED.", "updatedAt": "2026-01-01T00:00:00" },
  { "key": "Production.LossWarningPercent",  "value": "25",               "description": "Loss above this returns LOSS_UNUSUALLY_HIGH as a warning. It never blocks the entry.", "updatedAt": "2026-01-01T00:00:00" }
]
```

**All values are strings**, including numbers and the grade list. Parse on read, stringify
on write.

`Grades.Enabled` is `"1,2"` → `[First, Second]`. Map: `1` = First, `2` = Second, `3` = Third.
**Every grade dropdown in the application should be built from this setting**, not
hard-coded.

#### Update — `PUT /api/v1/settings`

A **partial** update — send only what changed.

```json
{ "values": { "Factory.Name": "Shahbaz Crockery Works" } }
```

`200 OK` returns the **complete** settings array. Replace the cached copy with it.

Unknown keys are refused rather than stored:

```json
{
  "type": "https://crockeryfactory/errors/validation-failed",
  "title": "Validation failed",
  "status": 400,
  "detail": "Unknown setting(s): Factory.Nmae.",
  "code": "VALIDATION_FAILED",
  "traceId": "0HNOFV1B15D2N",
  "errors": [
    { "field": "values.Factory.Nmae", "message": "No such setting", "code": "VALIDATION_FAILED" }
  ]
}
```

Since settings are created by migration, **render a fixed form from the returned keys**
rather than a free-form key/value editor — that makes an unknown key impossible.

Group them: Factory details · Grades · Document numbering · Backdating windows ·
Thresholds. Show each `description` as help text. Changes take effect **immediately**
server-side (the cache is dropped on write), so refresh any dependent dropdowns after
saving.

---

### 6.12 Audit — `/admin/audit`

**Permission:** `CanViewAudit` (Owner, Administrator — **not** Clerk)

#### `GET /api/v1/audit?entityName=&entityId=&userId=&from=&to=&page=1&pageSize=50`

Paged. **Read-only — there is no write endpoint.** An audit trail somebody can edit is not
an audit trail.

```json
{
  "items": [
    {
      "id": "e7045d04-775a-43fb-9ba3-f9ce1c7958fe",
      "entityName": "FactorySetting",
      "entityId": "00000000-0000-0000-0000-000000000000",
      "action": "Update",
      "newValues": "[{\"Key\":\"Factory.Name\",\"From\":\"Crockery Factory\",\"To\":\"Shahbaz Crockery Works\"}]",
      "userId": "daf85329-8980-455b-bc56-19ac139854be",
      "userName": "Administrator",
      "occurredAt": "2026-09-11T11:32:21.6426896"
    },
    {
      "id": "f4017786-c8ca-4191-953a-59d714d406a9",
      "entityName": "ProductionEntry",
      "entityId": "52b4348b-9143-44a7-ab20-ae89215512aa",
      "action": "Cancel",
      "oldValues": "{\"Status\":\"Active\"}",
      "newValues": "{\"Status\":\"Cancelled\",\"EntryNumber\":\"P-2609-0001\",\"CancellationReason\":\"Recorded against the wrong kiln load\",\"Reversed\":[{\"Grade\":\"First\",\"Quantity\":-1800},{\"Grade\":\"Second\",\"Quantity\":-140}]}",
      "userId": "fd37af42-b893-45de-b992-140088bb37ad",
      "userName": "Munshi",
      "occurredAt": "2026-09-11T11:32:21.3454457"
    }
  ],
  "page": 1,
  "pageSize": 3,
  "totalCount": 7
}
```

**Critical:** `oldValues` and `newValues` are **JSON-encoded strings, not objects**. Parse
them a second time:

```js
const oldValues = row.oldValues ? JSON.parse(row.oldValues) : null;
const newValues = row.newValues ? JSON.parse(row.newValues) : null;
```

Wrap in `try/catch` — shape is not guaranteed across entity types, and a malformed row
must not blank the page.

`oldValues` is **absent** on a create. `entityId` is
`00000000-0000-0000-0000-000000000000` for entity-wide actions (settings updates, stock
rebuilds) — render "—" rather than an empty Guid.

`userName` is a **snapshot** taken at the time of the action. Renaming a user does not
change historical audit rows; that is deliberate.

| Column | Notes |
|---|---|
| When | `occurredAt`, `dd MMM yyyy HH:mm` |
| Who | `userName` |
| What | `entityName` + `action` |
| Record | `entityId` → deep link where resolvable |
| Change | Expandable diff from the parsed old/new |

Render the diff as a two-column before/after table, not raw JSON.

Filters: entity type (`Product`, `ProductPrice`, `Customer`, `Dispatch`, `Payment`,
`ProductionEntry`, `StockAdjustment`, `StockBalance`, `ReasonCode`, `FactorySetting`,
`AppUser`), user, date range. Audited actions include `Create`, `Update`, `Cancel`,
`Deactivate`, `UpdatePrices`, `ResetPassword`, `Rebuild`.

---

### 6.13 Rebuild stock balances — `/admin/settings` (maintenance section)

**Permission:** `CanManageSettings`

#### `POST /api/v1/admin/rebuild-stock-balances`

No body.

```json
{
  "balancesExamined": 2,
  "balancesCorrected": 0,
  "balancesInserted": 0,
  "balancesRemoved": 0,
  "corrections": [],
  "completedAt": "2026-09-11T11:32:21.8878489Z"
}
```

With discrepancies, `corrections` carries human-readable lines:

```json
{
  "balancesExamined": 14,
  "balancesCorrected": 1,
  "balancesInserted": 0,
  "balancesRemoved": 0,
  "corrections": ["CUP-ESP-01 (First): 1240 -> 1198"],
  "completedAt": "2026-09-11T11:40:02.1122334Z"
}
```

This is the reconciliation tool for the day the cached balances and the ledger disagree.
**Safe to run at any time** — it only ever makes the balances match the movements, and the
run is recorded in the audit trail whether or not anything changed.

Present it as a maintenance action with an explanation, not a scary red button. After it
runs, show the counts; if `corrections` is non-empty, list them and suggest checking the
audit trail. All zeros is the good outcome: "No discrepancies found across 14 balances."

---

### 6.14 Health — unauthenticated

#### `GET /api/v1/health`

Anonymous. Use it on the login page to distinguish "the server is down" from "your
password is wrong".

```json
{
  "status": "Healthy",
  "databaseReachable": true,
  "migrationsCurrent": true,
  "pendingMigrations": [],
  "checkedAt": "2026-09-11T11:32:17.5236319Z"
}
```

`status`: `Healthy` · `Degraded` (database up, migrations pending) · plus `503` with
`databaseReachable: false` when the database is unreachable.

The response deliberately says **nothing about the data** — no row counts, no connection
string. Do not expect more from it.

---

## Appendix A — Enum values

All enums are sent and received as **strings**.

| Enum | Values | UI label |
|---|---|---|
| `QualityGrade` | `First`, `Second`, `Third` | "First" / "Second" / "Third". Which are selectable comes from `Grades.Enabled`. |
| `DocumentStatus` | `Active`, `Cancelled` | Badge |
| `PaymentMethod` | `Cash`, `BankTransfer`, `Cheque`, `Other` | "Cash" / "Bank transfer" / "Cheque" / "Other" |
| `ReasonCodeType` | `Breakage`, `StockAdjustment`, `SalesReturn`, `DispatchCancellation` | "Breakage" / "Stock adjustment" / "Sales return" / "Dispatch cancellation" |
| `StockMovementType` | `ProductionReceipt`, `Dispatch`, `DispatchCancellation`, `ProductionCancellation`, `Adjustment`, `CountCorrection`, `SalesReturn`, `OpeningBalance` | "Production receipt" / "Dispatch" / "Dispatch cancelled" / "Production cancelled" / "Adjustment" / "Count correction" / "Sales return" / "Opening balance" |
| `StockReferenceType` | `ProductionEntry`, `Dispatch`, `StockAdjustment`, `StockCount`, `SalesReturn`, `OpeningBalance` | Internal; not usually displayed |
| `ProductionGroupBy` | `Product`, `Day`, `Month` | |
| `SalesGroupBy` | `Customer`, `Product`, `Month` | |

`SalesReturn`, `StockCount` and `OpeningBalance` exist in the enums but have **no
documents in Phase 1** — a movement of those types returns no `referenceNumber`. Handle
them; do not build screens for them.

---

## Appendix B — Every error code

| Code | Status | Where | Display as |
|---|---|---|---|
| `VALIDATION_FAILED` | 400 | Everywhere | Field errors |
| `UNAUTHENTICATED` | 401 | Everywhere | Redirect to login |
| `INVALID_CREDENTIALS` | 401 | Login | Form banner |
| `FORBIDDEN` | 403 | Everywhere | Banner |
| `USER_INACTIVE` | 403 | Login | Form banner |
| `CANCELLATION_WINDOW_EXPIRED` | 403 | Cancel | Banner |
| `NOT_FOUND` | 404 | Everywhere | Empty state |
| `DUPLICATE_CODE` | 409 | Create/update | Field error on `code` |
| `CONCURRENCY_CONFLICT` | 409 | Update | Modal + Reload |
| `ALREADY_CANCELLED` | 409 | Cancel | Refresh record |
| `ACCOUNT_LOCKED` | 423 | Login | Form banner |
| `IF_MATCH_REQUIRED` | 428 | Update | Reload + retry once |
| `STOCK_INSUFFICIENT` | 422 | Dispatch, adjustment | Banner — names product |
| `STOCK_INSUFFICIENT_FOR_REVERSAL` | 422 | Production cancel | Banner + "Record adjustment" |
| `GRADE_NOT_ENABLED` | 422 | Product, dispatch, adjustment | Field error |
| `CODE_LOCKED` | 422 | Product update | Banner; field read-only |
| `EFFECTIVE_DATE_IN_PAST` | 422 | Prices | Field error |
| `PRODUCT_HAS_STOCK` | 422 | Deactivate | Banner |
| `PRODUCT_INACTIVE` | 422 | Production | Field error |
| `CUSTOMER_INACTIVE` | 422 | Dispatch | Field error |
| `OPENING_BALANCE_LOCKED` | 422 | Customer update | Banner; field read-only |
| `NO_PRICE_AVAILABLE` | 422 | Dispatch | Line error |
| `CANCELLATION_TOO_LATE` | 422 | Dispatch cancel | Banner |
| `DATE_TOO_OLD` | 422 | All dated documents | Field error |
| `QUANTITY_IMPLAUSIBLE` | 422 | Production | Field error |
| `QUANTITY_ZERO` | 400 | Adjustment | Field error |
| `NOTES_REQUIRED` | 400 | Adjustment | Field error |
| `INVALID_REASON_CODE` | 400 | Adjustment, production | Field error |
| `DATE_IN_FUTURE` | 400 | All dated documents | Field error |
| `NO_QUANTITY` | 400 | Production | Field error |
| `BREAKAGE_REASON_REQUIRED` | 400 | Production | Field error |
| `REASON_REQUIRED` | 400 | All cancellations | Field error |
| `NO_LINES` | 400 | Dispatch | Form banner |
| `TOO_MANY_LINES` | 400 | Dispatch | Form banner |
| `DUPLICATE_LINE` | 400 | Dispatch | Row error |
| `DUPLICATE_GRADE` | 400 | Product | Field error on `prices` |
| `AMOUNT_INVALID` | 400 | Payment | Field error |
| `REFERENCE_REQUIRED` | 400 | Payment | Field error |
| `NOT_IMPLEMENTED` | 501 | PDF/XLSX | Disabled control + tooltip |
| `INTERNAL_ERROR` | 500 | Everywhere | Generic + `traceId` |

## Appendix C — Warning codes

| Code | Emitted by | Status |
|---|---|---|
| `LOSS_UNUSUALLY_HIGH` | Production create | 201 |
| `RATE_BELOW_LIST` | Dispatch create | 201 |
| `PAYMENT_EXCEEDS_OUTSTANDING` | Payment create | 201 |
| `RATE_BELOW_COST` | **Never — not implemented.** No cost price exists in Phase 1. | — |

---

## Appendix D — Seeded reason code IDs

Written by the `InitialCreate` migration; identical on every install. Useful for fixtures
and test data. **Production code must still read them from `GET /reason-codes`** — a
factory may deactivate any of them.

**Breakage**

| Code | Description | ID |
|---|---|---|
| `CRACK` | Cracked in firing | `a1f00000-0000-0000-0000-000000000001` |
| `WARP` | Warped | `a1f00000-0000-0000-0000-000000000002` |
| `GLAZE` | Glaze fault | `a1f00000-0000-0000-0000-000000000003` |
| `HANDLE` | Handle failure | `a1f00000-0000-0000-0000-000000000004` |
| `OTHER` | Other | `a1f00000-0000-0000-0000-000000000005` |

**Stock adjustment**

| Code | Description | ID |
|---|---|---|
| `COUNT` | Physical count correction | `a2f00000-0000-0000-0000-000000000001` |
| `DAMAGE` | Damaged in store | `a2f00000-0000-0000-0000-000000000002` |
| `SAMPLE` | Issued as sample | `a2f00000-0000-0000-0000-000000000003` |
| `OPENING` | Opening balance | `a2f00000-0000-0000-0000-000000000004` |
| `OTHER` | Other | `a2f00000-0000-0000-0000-000000000005` |

`SalesReturn` reasons are prefixed `a3f00000-…`, `DispatchCancellation` `a4f00000-…`.
Neither has a document type in Phase 1.

---

## Appendix E — Build order

Dependencies mean some things must exist before others.

| Step | Build | Depends on |
|---|---|---|
| 1 | API client, credentials, `401` interceptor, error parser | — |
| 2 | Login, `/auth/me` bootstrap, session state | 1 |
| 3 | App shell, nav filtering, route guards | 2 |
| 4 | Paging component, reason-code and settings caches | 3 |
| 5 | Products (simplest full CRUD — exercises ETag, idempotency, all error shapes) | 4 |
| 6 | **Production entry** (highest frequency, 45s target) | 5 |
| 7 | Stock list, movements, adjustments | 5 |
| 8 | Customers | 4 |
| 9 | **Dispatches** (most rules, 90s target) | 5, 8 |
| 10 | Payments | 8 |
| 11 | Outstanding, statement | 8 |
| 12 | Reports, dashboard | 4 |
| 13 | Users, settings, reason codes, audit | 3 |

Build products before production: it is the smallest surface that exercises ETag
concurrency, idempotency, field errors, conflicts and business-rule refusals, so the
patterns are settled before the screens with a clock on them.

---

## Appendix F — Regenerating these examples

Every JSON body in this document was captured from the running API by
`tests/CrockeryFactory.IntegrationTests/ContractCapture.cs`. To refresh after a backend
change:

```bash
docker run -d --name crockery-sql -e ACCEPT_EULA=Y \
  -e MSSQL_SA_PASSWORD='<password>' -e MSSQL_PID=Express \
  -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest

export CROCKERY_TEST_CONNECTION="Server=localhost,1433;User Id=sa;Password=<password>;TrustServerCertificate=True"
export CAPTURE_DIR=/tmp/capture

dotnet test tests/CrockeryFactory.IntegrationTests --filter 'FullyQualifiedName~ContractCapture'
```

It writes one file per call to `$CAPTURE_DIR`, each with the request, the response, the
status and the relevant headers. Without `CAPTURE_DIR` set it is skipped, so it does not
run as part of the ordinary suite.
