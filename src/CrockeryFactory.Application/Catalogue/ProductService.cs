using CrockeryFactory.Application.Abstractions;
using CrockeryFactory.Application.Catalogue.Dtos;
using CrockeryFactory.Application.Common;
using CrockeryFactory.Modules.Catalogue.Entities;
using CrockeryFactory.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CrockeryFactory.Application.Catalogue;

public sealed class ProductService : IProductService
{
    private const int MaxPageSize = 200;

    private readonly FactoryDbContext _db;
    private readonly IFactorySettings _settings;
    private readonly IAuditWriter _audit;

    public ProductService(FactoryDbContext db, IFactorySettings settings, IAuditWriter audit)
    {
        _db = db;
        _settings = settings;
        _audit = audit;
    }

    public async Task<PagedResult<ProductListItem>> ListAsync(ProductQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 50 : query.PageSize, 1, MaxPageSize);

        // Explicit .Where rather than a global query filter. A global filter silently
        // excludes rows from reports and produces the class of bug where a total does
        // not match its own detail lines.
        var products = _db.Products.AsNoTracking();

        if (!query.IncludeInactive)
            products = products.Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            products = products.Where(p => p.Code.Contains(term) || p.Name.Contains(term));
        }

        var totalCount = await products.CountAsync(ct);

        var pageOfProducts = await products
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new { p.Id, p.Code, p.Name, p.CapacityMl, p.IsActive })
            .ToListAsync(ct);

        var ids = pageOfProducts.Select(p => p.Id).ToList();

        // Two extra queries for the whole page rather than two per row. At a page size
        // of 200 the per-row shape is 400 round trips, which is how PF-04 is missed.
        var prices = await _db.ProductPrices.AsNoTracking()
            .Where(pp => ids.Contains(pp.ProductId) && pp.EffectiveTo == null)
            .Select(pp => new { pp.ProductId, pp.Grade, pp.UnitRate })
            .ToListAsync(ct);

        var stock = await _db.StockBalances.AsNoTracking()
            .Where(sb => ids.Contains(sb.ProductId))
            .Select(sb => new { sb.ProductId, sb.Grade, sb.Quantity })
            .ToListAsync(ct);

        var items = pageOfProducts.Select(p => new ProductListItem(
            p.Id, p.Code, p.Name, p.CapacityMl, p.IsActive,
            prices.Where(x => x.ProductId == p.Id)
                  .OrderBy(x => x.Grade)
                  .Select(x => new GradePrice(x.Grade, x.UnitRate)).ToList(),
            stock.Where(x => x.ProductId == p.Id)
                 .OrderBy(x => x.Grade)
                 .Select(x => new GradeStock(x.Grade, x.Quantity)).ToList()))
            .ToList();

        return new PagedResult<ProductListItem>(items, page, pageSize, totalCount);
    }

    public async Task<ProductResponse> GetAsync(Guid id, CancellationToken ct = default)
    {
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw DomainException.NotFound("Product", id.ToString());

        return await ToResponseAsync(product, ct);
    }

    public Task<byte[]?> GetRowVersionAsync(Guid id, CancellationToken ct = default) =>
        _db.Products.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => p.RowVersion)
            .FirstOrDefaultAsync(ct)!;

    public async Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken ct = default)
    {
        var code = ProductValidation.NormaliseCode(request.Code);

        var errors = new List<FieldError>();
        ProductValidation.ValidateCode(code, errors);
        ProductValidation.ValidateName(request.Name, errors);
        ProductValidation.ValidateCapacity(request.CapacityMl, errors);
        ProductValidation.ValidatePrices(request.Prices, errors);
        ProductValidation.ThrowIfAny(errors, "The product could not be saved.");

        // Uniqueness spans active and inactive: a code freed by deactivation would make
        // two different products share a code in the history.
        if (await _db.Products.AnyAsync(p => p.Code == code, ct))
        {
            throw DomainException.Conflict(ErrorCodes.DuplicateCode,
                $"Product code '{code}' is already in use.");
        }

        foreach (var price in request.Prices)
            await _settings.RequireGradeEnabledAsync(price.Grade, "prices", ct);

        var now = _db.Clock.GetUtcNow().UtcDateTime;
        var userId = _db.CurrentUser.RequireUserId();

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = request.Name.Trim(),
            CapacityMl = request.CapacityMl,
            Description = request.Description?.Trim(),
            IsActive = true,
            CreatedAt = now,
            CreatedByUserId = userId
        };

        _db.Products.Add(product);

        foreach (var price in request.Prices)
        {
            _db.ProductPrices.Add(new ProductPrice
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                Grade = price.Grade,
                UnitRate = price.UnitRate,
                EffectiveFrom = DateOnly.FromDateTime(now),
                EffectiveTo = null,
                CreatedAt = now,
                CreatedByUserId = userId
            });
        }

        _audit.Record(nameof(Product), product.Id, "Create", null,
            new { product.Code, product.Name, product.CapacityMl, Prices = request.Prices });

        await _db.SaveChangesAsync(ct);

        return await ToResponseAsync(product, ct);
    }

    public async Task<ProductResponse> UpdateAsync(
        Guid id, UpdateProductRequest request, byte[] rowVersion, CancellationToken ct = default)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw DomainException.NotFound("Product", id.ToString());

        var code = ProductValidation.NormaliseCode(request.Code);

        var errors = new List<FieldError>();
        ProductValidation.ValidateCode(code, errors);
        ProductValidation.ValidateName(request.Name, errors);
        ProductValidation.ValidateCapacity(request.CapacityMl, errors);
        ProductValidation.ThrowIfAny(errors, "The product could not be saved.");

        if (code != product.Code)
        {
            // A code that appears on a printed document cannot change: the paper and the
            // system would disagree, and the paper is what the customer holds.
            if (await IsReferencedByATransactionAsync(id, ct))
            {
                throw DomainException.Unprocessable(ErrorCodes.CodeLocked, "Code locked",
                    $"Product code '{product.Code}' cannot be changed because it already appears " +
                    "on production entries or dispatches. Deactivate this product and create a new one instead.");
            }

            if (await _db.Products.AnyAsync(p => p.Code == code && p.Id != id, ct))
            {
                throw DomainException.Conflict(ErrorCodes.DuplicateCode,
                    $"Product code '{code}' is already in use.");
            }
        }

        var before = new { product.Code, product.Name, product.CapacityMl, product.Description };

        product.Code = code;
        product.Name = request.Name.Trim();
        product.CapacityMl = request.CapacityMl;
        product.Description = request.Description?.Trim();

        _db.Entry(product).Property(p => p.RowVersion).OriginalValue = rowVersion;

        _audit.Record(nameof(Product), product.Id, "Update", before,
            new { product.Code, product.Name, product.CapacityMl, product.Description });

        await SaveWithConcurrencyCheckAsync(ct);

        return await ToResponseAsync(product, ct);
    }

    public async Task<ProductResponse> UpdatePricesAsync(
        Guid id, UpdatePricesRequest request, CancellationToken ct = default)
    {
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw DomainException.NotFound("Product", id.ToString());

        var errors = new List<FieldError>();
        ProductValidation.ValidatePrices(request.Prices, errors);
        ProductValidation.ThrowIfAny(errors, "The prices could not be saved.");

        foreach (var price in request.Prices)
            await _settings.RequireGradeEnabledAsync(price.Grade, "prices", ct);

        var current = await _db.ProductPrices
            .Where(pp => pp.ProductId == id && pp.EffectiveTo == null)
            .ToListAsync(ct);

        // Backdating a price does not change bills already printed, so allowing it would
        // only produce a price history that disagrees with the documents.
        var earliest = current.Count == 0 ? (DateOnly?)null : current.Min(pp => pp.EffectiveFrom);
        if (earliest is { } floor && request.EffectiveFrom < floor)
        {
            throw DomainException.Unprocessable(ErrorCodes.EffectiveDateInPast, "Effective date in the past",
                $"Prices are effective from {floor:yyyy-MM-dd}. A new rate cannot start before that date.",
                new FieldError("effectiveFrom", $"Must be on or after {floor:yyyy-MM-dd}", ErrorCodes.EffectiveDateInPast));
        }

        var now = _db.Clock.GetUtcNow().UtcDateTime;
        var userId = _db.CurrentUser.RequireUserId();

        var before = current
            .OrderBy(pp => pp.Grade)
            .Select(pp => new { Grade = pp.Grade.ToString(), pp.UnitRate })
            .ToList();

        foreach (var price in request.Prices)
        {
            var superseded = current.FirstOrDefault(pp => pp.Grade == price.Grade);

            // An unchanged rate is left alone rather than superseded and re-inserted:
            // otherwise the history fills with rows that record no change.
            if (superseded is not null && superseded.UnitRate == price.UnitRate)
                continue;

            if (superseded is not null)
                superseded.EffectiveTo = request.EffectiveFrom;

            _db.ProductPrices.Add(new ProductPrice
            {
                Id = Guid.NewGuid(),
                ProductId = id,
                Grade = price.Grade,
                UnitRate = price.UnitRate,
                EffectiveFrom = request.EffectiveFrom,
                EffectiveTo = null,
                CreatedAt = now,
                CreatedByUserId = userId
            });
        }

        // BR-07 restricts price changes to the owner and SE-13 requires them audited -
        // this is the row someone reads when a bill is disputed months later.
        _audit.Record(nameof(ProductPrice), id, "UpdatePrices", before,
            new
            {
                Prices = request.Prices.OrderBy(p => p.Grade)
                    .Select(p => new { Grade = p.Grade.ToString(), p.UnitRate }),
                request.EffectiveFrom,
                request.Reason
            });

        await _db.SaveChangesAsync(ct);

        return await ToResponseAsync(product, ct);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw DomainException.NotFound("Product", id.ToString());

        if (!product.IsActive)
            return;   // Idempotent: deactivating twice is not an error worth raising.

        var heldStock = await _db.StockBalances
            .Where(sb => sb.ProductId == id && sb.Quantity != 0)
            .Select(sb => new { sb.Grade, sb.Quantity })
            .ToListAsync(ct);

        if (heldStock.Count > 0)
        {
            // Deactivating something sitting in the godown makes the stock sheet lie.
            var held = string.Join(", ", heldStock.Select(s => $"{s.Quantity} at {s.Grade}"));
            throw DomainException.Unprocessable(ErrorCodes.ProductHasStock, "Product has stock",
                $"{product.Code} still has stock in the godown ({held}). " +
                "Dispatch or adjust it to zero before deactivating the product.");
        }

        product.IsActive = false;

        _audit.Record(nameof(Product), product.Id, "Deactivate",
            new { IsActive = true }, new { IsActive = false });

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Checked in the order a product is most likely to have been used, so the common
    /// case answers on the first query rather than all three.
    /// </summary>
    private async Task<bool> IsReferencedByATransactionAsync(Guid productId, CancellationToken ct) =>
        await _db.StockMovements.AnyAsync(m => m.ProductId == productId, ct) ||
        await _db.ProductionEntries.AnyAsync(p => p.ProductId == productId, ct) ||
        await _db.DispatchLines.AnyAsync(l => l.ProductId == productId, ct);

    private async Task SaveWithConcurrencyCheckAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw DomainException.Conflict(ErrorCodes.ConcurrencyConflict,
                "Someone else changed this record while you were editing it. " +
                "Reload the product and apply your change again.");
        }
    }

    private async Task<ProductResponse> ToResponseAsync(Product product, CancellationToken ct)
    {
        var prices = await _db.ProductPrices.AsNoTracking()
            .Where(pp => pp.ProductId == product.Id && pp.EffectiveTo == null)
            .OrderBy(pp => pp.Grade)
            .Select(pp => new GradePrice(pp.Grade, pp.UnitRate))
            .ToListAsync(ct);

        return new ProductResponse(
            product.Id, product.Code, product.Name, product.CapacityMl, product.Description,
            product.IsActive, prices, product.CreatedAt);
    }
}
