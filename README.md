# Crockery Factory Management System — Backend

Phase 1 backend for a ceramics factory: kiln output, stock by product and quality grade,
dispatches to customers, and payments against a running customer balance.

Built to the *Backend Implementation Specification — Crockery Factory Management System,
Phase 1 (v0.1)*. .NET 8, EF Core 8, SQL Server Express, deployed on a factory LAN.

## Two ideas the rest of the code follows from

**Stock is an append-only ledger.** `stock.StockMovements` is the source of truth.
`stock.StockBalances` is a cache maintained inside the same transaction as the movement
that changed it, and is rebuildable from the ledger at any time. Nothing is ever hard
deleted: documents are cancelled with reversing movements, and both rows stay visible so
the history shows what happened rather than concealing it.

**Documents keep what they printed.** A dispatch stores the customer name, product code,
product name and unit rate as at the moment it was raised. Renaming a product or changing
a price never rewrites a bill that has already gone out of the gate.

## Layout

| Project | Holds |
|---|---|
| `src/CrockeryFactory.Domain` | Enums, `Money`, `StockKey`, every entity, `ICurrentUser`, the Identity user and role |
| `src/CrockeryFactory.Persistence` | `FactoryDbContext`, one `IEntityTypeConfiguration<T>` per entity, seeded reference data |
| `src/CrockeryFactory.Application` | Services, DTOs, validation and the business rules |
| `src/CrockeryFactory.Web` | Controllers, authentication, error mapping, and the single migration history under `Data/Migrations` |
| `tests/CrockeryFactory.UnitTests` | Value-object behaviour, mapping guards, session and policy rules |
| `src/CrockeryFactory.DevSeeder` | Development data generator, excluded from the release build |
| `tests/CrockeryFactory.IntegrationTests` | The API against a real SQL Server |

Modules are separated by namespace — `CrockeryFactory.Modules.Catalogue.*`,
`.Stock.*`, `.Production.*`, `.Sales.*`, `CrockeryFactory.Shared.*` — and each module's
configurations are applied to the model separately, so the boundary is visible in
`FactoryDbContext.OnModelCreating` rather than implied. Each module owns a database
schema: `catalogue`, `stock`, `production`, `sales`, `shared`, `auth`.

The specification's sample used one assembly per module. This solution keeps them in one
assembly; splitting later is a project-file change that touches no entity and no
configuration class.

## Running it

Requires the .NET 8 SDK and a reachable SQL Server.

```bash
dotnet build
dotnet test
```

The integration tests need a SQL Server and are skipped without one:

```bash
export CROCKERY_TEST_CONNECTION="Server=localhost,1433;User Id=sa;Password=...;TrustServerCertificate=True"
dotnet test
```

They run against a real SQL Server on purpose. The rules they exist to prove - the
non-negative check constraint, the filtered unique index on current prices, and
`rowversion` concurrency - are enforced by the database and do not exist on an in-memory
provider. A suite that passed on a fake provider would be worse than no suite, because it
would be believed.

### Database

Migrations are **never** applied from `Program.cs`. With a single instance that would
work, but a failed migration would then leave the application in a crash loop at a
factory nobody can reach, during working hours. They are applied deliberately, after a
backup, with output someone can read.

`dotnet ef` reads its connection string from `CROCKERY_DESIGNTIME_CONNECTION`, falling
back to LocalDB:

```bash
export CROCKERY_DESIGNTIME_CONNECTION="Server=localhost,1433;Database=CrockeryFactory;User Id=sa;Password=...;TrustServerCertificate=True"

dotnet ef database update --project src/CrockeryFactory.Web --startup-project src/CrockeryFactory.Web
```

For a site where a script is preferred over running the tool:

```bash
dotnet ef migrations script --idempotent --output upgrade.sql
```

### Adding a migration

```bash
dotnet ef migrations add DescriptiveName \
  --project src/CrockeryFactory.Web --startup-project src/CrockeryFactory.Web \
  --output-dir Data/Migrations
```

Rules that are not negotiable:

- `EnsureCreated()` is never used. It bypasses migrations and produces a database that
  can never be upgraded.
- A migration already applied at a client site is never edited. Fix forward.
- Read the generated `Up` before committing. EF occasionally emits a drop-and-recreate
  for a column change, which on a live factory database is data loss.
- Descriptive names — `AddSalesReturnTables`, not `Update3`.
- Reference data is seeded in migrations; sample data never is.

### A local SQL Server for development

```bash
docker run -d --name crockery-sql \
  -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='<password>' -e MSSQL_PID=Express \
  -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest
```

Note when querying by hand: `sqlcmd` runs with `QUOTED_IDENTIFIER` off by default, which
SQL Server refuses for any write to a table carrying a filtered index — `catalogue.ProductPrices`
is one. Start such a session with `SET QUOTED_IDENTIFIER ON;`.

## Seeded reference data

Applied by the migration, so it exists on a fresh install: 18 reason codes across the
four lists, the three roles (Owner, Clerk, Administrator), 14 factory settings, and a
`system` account that seeded rows are attributed to.

The `system` account is not a login. It is inactive, has no password hash, and is locked
out to a date past any plausible use, so all three checks refuse it independently.

Settings hold the answers that spec section 5 leaves open, so a different answer from the
factory is a settings change rather than a deploy: enabled grades (BE-3), document number
prefixes (BE-4) and the backdating windows (BE-5).

## The API

Base path `/api/v1`, JSON in camelCase, business dates as `yyyy-MM-dd` and timestamps in
UTC. Errors are RFC 7807 `ProblemDetails` carrying a stable `code` the client switches on,
a `traceId`, and per-field errors where they apply. A 500 never leaks exception detail -
the full error is logged against the trace id instead.

**Authentication** is a cookie: `HttpOnly`, `SameSite=Strict`, `Secure`, no token in the
body. Owner and Administrator sessions slide by 30 minutes; a clerk's session ends at
20:00 local, because the tablet is left on the packing bench. Deactivating a user ends
their session on its next request rather than when the cookie happens to expire.

**Authorisation** is by policy, never by a role name in an attribute, and a fallback
policy means an endpoint added later without an attribute is closed rather than open.
`PolicyMap` is the single definition of which roles satisfy which policy, so the
permission list handed to the client cannot drift from what the server will actually
allow.

**Idempotency**: every POST that creates a document accepts an `Idempotency-Key`. A repeat
within seven days replays the original response and marks it `Idempotency-Replayed: true`.
A rejected request does not burn the key - the clerk fixes the field and retries from the
same screen. This is not theoretical: a tablet on marginal factory WiFi will send a
request, lose the reply, and send it again.

**Concurrency**: single-record responses carry an `ETag` taken from `RowVersion`, and
updates require `If-Match`. A stale version is `409 CONCURRENCY_CONFLICT`; a missing one is
`428`, because "you never read this record" and "someone changed it since you read it" send
the clerk to do different things. `If-Match: *` is refused rather than treated as agreement.

## How stock moves

`StockService` is the only writer to the ledger. Everything that changes stock - a firing,
a dispatch, an adjustment, a cancellation - goes through it, which is what makes the
polymorphic `ReferenceId` tractable: there is no foreign key from a movement to the four
different tables it might point at, so referential integrity there is one class's job
rather than everybody's.

Two properties it guarantees:

- **Every movement is checked before any is written.** A four-line dispatch that fails on
  line three leaves nothing behind.
- **It never calls `SaveChanges`.** The caller owns the transaction, so the ledger row,
  the balance update, the document and the audit entry commit together or not at all. A
  movement that commits without the document that caused it is exactly the corruption the
  ledger exists to prevent.

`StockBalances` is a cache of the ledger, updated in that same transaction. When the two
disagree, the ledger wins - the rebuild endpoint in stage 5 is the reconciliation tool.

Cancelling never deletes. A cancelled production entry keeps its row and its original
movements, and gains reversing ones, so the history explains itself. Where the stock has
already left the godown the reversal would go negative, and the entry cannot be cancelled
at all - the clerk is told to record an adjustment instead, because "insufficient stock"
on a cancellation reads as nonsense without that sentence.

## The customer balance

Opening balance, plus active dispatches, minus active payments. Payments are not
allocated to particular bills: the customer pays something against what he owes, and
forcing invoice-level allocation would only make the clerk invent it.

That sum lives in exactly one place, `CustomerBalances`. Three implementations of it -
one for the outstanding report, one for the statement's closing balance, one for the
figure read aloud at the gate - would eventually disagree, and the customer would find it
before we did.

The opening balance locks the moment anything is posted against the account. Changing it
afterwards silently rewrites every historical balance, including ones the customer has
already been shown.

## Development data

`CrockeryFactory.DevSeeder` generates a database the application could plausibly have
produced. That distinction is the whole design: rows written straight into the tables
would violate BR-01 within a day of simulated trading - a dispatch of 500 cups against 80
in stock - and a database that could not have been created by the application is useless
for testing the rules it exists to exercise. So the generator walks forward one day at a
time carrying a running balance, exactly as `StockService` does, and skips a line where
the stock is not there rather than clamping it. Clamping would produce a suspiciously
tidy database in which nothing is ever short.

```bash
export DOTNET_ENVIRONMENT=Development
dotnet run --project src/CrockeryFactory.DevSeeder -- --months 3 --i-understand   --connection "Server=localhost,1433;Database=CrockeryDemo;User Id=sa;Password=...;TrustServerCertificate=True"
```

It refuses to run four ways, because only one of them has to fail open for a demo database
to reach a factory: the environment must be Development, `--i-understand` must be present,
and the database must contain no products and no dispatches.

`--years 5` produces the PF-14 performance dataset.

## Measured against five years of data

Generated with `--years 5`: 4,646 production entries, 12,241 dispatches, 22,970 dispatch
lines, 32,262 stock movements. Timed on SQL Server 2022:

| Query | Time |
|---|---|
| Movement history, running balance, page 60 of the ledger | 33 ms |
| RP-01 daily stock | 9 ms |
| RP-02 outstanding, all customers | 8 ms |
| RP-06 sales summary over a full year | 20 ms |
| Current stock (PF-05) | < 1 ms |

The dataset was also checked for the invariants it claims to hold: no negative balance
anywhere, every cached balance equal to the sum of its ledger rows, every dispatch total
equal to the sum of its lines, no duplicate document numbers, and ledger receipts exactly
equal to good plus seconds - broken pieces never entered stock.

## Known gap

`RATE_BELOW_COST` (spec section 3.7) is **not implemented**, because Phase 1's entities
define no cost price anywhere - there is nothing to compare a rate against. Implementing
it needs a cost field on `Product` or `ProductPrice` and a decision about what "cost"
means here (clay and glaze only, or a loaded rate including fuel and labour). Raised as an
open item rather than guessed at. `RATE_BELOW_LIST` is implemented and warns at half the
list rate.

## Frontend contract

`docs/frontend-pdr.md` is the frontend Product Requirements Document: every endpoint with
real captured request and response JSON, per-page layouts, validation rules, error
handling, conditional rendering and paging.

Its examples are not transcribed from the DTOs — they are captured from the running API by
`tests/CrockeryFactory.IntegrationTests/ContractCapture.cs`, which is skipped unless
`CAPTURE_DIR` is set. Regenerate them after any contract change rather than editing the
document by hand.

## Status

All five stages are complete.

- **Stage 1** - solution, domain, EF mapping, `InitialCreate` migration.
- **Stage 2** - authentication and the authorisation matrix, RFC 7807 error mapping,
  idempotency, ETag concurrency, the audit writer, document numbering, and the product
  endpoints.
- **Stage 3** - the stock ledger and its cached balances, stock and movement queries with
  a running balance, stock adjustments, and production entries with cancellation.
- **Stage 4** - customers with the outstanding report and statement, dispatches with rate
  resolution and snapshotting, and payments.
- **Stage 5** - the daily stock, outstanding, production, sales and dashboard reports;
  users, reason codes, settings and the read-only audit query; the stock-balance rebuild;
  the anonymous health endpoint; and the development seeder.

180 tests pass, the 124 integration tests among them against SQL Server 2022.

Still outstanding for Phase 1: QuestPDF rendering behind the document and report
endpoints (they are routed, authorised and return `501` today), spreadsheet export, and
the `RATE_BELOW_COST` warning described under Known gap above.
