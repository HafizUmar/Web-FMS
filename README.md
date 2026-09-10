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
| `src/CrockeryFactory.Web` | Host, and the single migration history under `Data/Migrations` |
| `tests/CrockeryFactory.UnitTests` | Value-object behaviour and mapping guards |

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

## Status

Stage 1 of five is complete: solution, domain, EF mapping, and the `InitialCreate`
migration, verified against SQL Server 2022. Authentication, the module services and the
REST API follow in stages 2 to 5.
