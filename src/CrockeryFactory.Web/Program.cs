using System.Text.Json.Serialization;
using CrockeryFactory.Application.Abstractions;
using CrockeryFactory.Application.Catalogue;
using CrockeryFactory.Application.Common;
using CrockeryFactory.Application.Admin;
using CrockeryFactory.Application.Production;
using CrockeryFactory.Application.Reports;
using CrockeryFactory.Application.Sales;
using CrockeryFactory.Application.Staff;
using CrockeryFactory.Application.Services;
using CrockeryFactory.Application.Stock;
using CrockeryFactory.Domain.Abstractions;
using CrockeryFactory.Persistence;
using CrockeryFactory.Web.Infrastructure;
using CrockeryFactory.Shared.Identity;
using CrockeryFactory.Web.Auth;
using CrockeryFactory.Web.Infrastructure.Errors;
using CrockeryFactory.Web.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

builder.Services.AddDbContext<FactoryDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("FactoryDatabase"),
        sql => sql.MigrationsAssembly(typeof(Program).Assembly.FullName)));

// ---------------------------------------------------------------------------
// Identity and authentication
// ---------------------------------------------------------------------------

builder.Services
    .AddIdentityCore<AppUser>(options =>
    {
        options.User.RequireUniqueEmail = false;

        // SE-04. Ten consecutive failures, fifteen minutes. Deliberately generous on
        // attempts: this is a factory office, not the public internet, and a clerk
        // locked out mid-morning cannot record what is coming off the kiln.
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 10;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddRoles<AppRole>()
    .AddEntityFrameworkStores<FactoryDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        options.Cookie.Name = "crockery.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

        // SE-07 for Owner and Administrator. A clerk's session is additionally capped at
        // close of business by SessionValidator, which the sliding window cannot express.
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = SessionPolicy.SlidingWindow;

        options.Events.OnValidatePrincipal = SessionValidator.ValidateAsync;

        // This is an API. A browser redirect to a login page in place of a 401 would
        // reach the client as an opaque HTML body and a success status.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return WriteProblemAsync(context.HttpContext, StatusCodes.Status401Unauthorized,
                ErrorCodes.Unauthenticated, "Not signed in",
                "This request needs a signed-in user. Sign in and try again.");
        };

        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return WriteProblemAsync(context.HttpContext, StatusCodes.Status403Forbidden,
                ErrorCodes.Forbidden, "Forbidden",
                "Your role does not allow this action.");
        };
    });

// ---------------------------------------------------------------------------
// Authorisation
// ---------------------------------------------------------------------------

var authorization = builder.Services.AddAuthorizationBuilder();
PolicyMap.AddAll(authorization);

// An endpoint added later without an attribute is closed rather than open. The opposite
// default is how endpoints ship unprotected (spec section 4.2).
authorization.SetFallbackPolicy(new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser()
    .Build());

// ---------------------------------------------------------------------------
// Application services
// ---------------------------------------------------------------------------

builder.Services.AddScoped<IFactorySettings, FactorySettingsProvider>();
builder.Services.AddScoped<IAuditWriter, AuditWriter>();
builder.Services.AddScoped<IDocumentNumbers, DocumentNumbers>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IStockService, StockService>();
builder.Services.AddScoped<IStockQueries, StockQueries>();
builder.Services.AddScoped<IStockAdjustments, StockAdjustments>();
builder.Services.AddScoped<IProductionService, ProductionService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IDispatchService, DispatchService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IStaffService, StaffService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IReasonCodeService, ReasonCodeService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IStockRebuildService, StockRebuildService>();

// ---------------------------------------------------------------------------
// MVC and error handling
// ---------------------------------------------------------------------------

builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        // Enums cross the wire by name. "First" survives a reordering of the enum and
        // reads correctly in a log; 1 does neither.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    // Model binding failures must come back in the same shape as every other error,
    // carrying a code the client can switch on.
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .SelectMany(entry => entry.Value!.Errors.Select(error =>
                new FieldError(
                    Camel(entry.Key),
                    string.IsNullOrWhiteSpace(error.ErrorMessage) ? "Invalid value" : error.ErrorMessage,
                    ErrorCodes.ValidationFailed)))
            .ToList();

        var problem = ProblemDetailsExtensions.Build(
            context.HttpContext, StatusCodes.Status400BadRequest,
            ErrorCodes.ValidationFailed, "Validation failed",
            "The request could not be read. Check the highlighted fields.", errors);

        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Migrations are applied by the Setup tool, never here. With one instance startup
// migration would work; a failed one would then leave the application in a crash loop
// at a factory nobody can reach, during working hours (spec section 2.2).
//
// What does happen here is a read-only check that says plainly whether the database is
// reachable, migrated and usable. Without it the first sign of a missing database is a
// 500 on the login screen, which names neither the server nor the database.
await DatabaseStartupCheck.ReportAsync(app);

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();

// ---------------------------------------------------------------------------
// The Angular client
// ---------------------------------------------------------------------------
//
// Served from this host on purpose: the session cookie is SameSite=Strict, so a client
// on another origin would never have it sent. One origin also means one thing to deploy.
//
// A client-side route such as /products or /dispatches/{id} is not a file and has no
// endpoint, so it is rewritten to the shell BEFORE routing runs. Doing it here rather
// than with MapFallbackToFile keeps SPA navigation out of the authorisation pipeline
// entirely - the fallback policy closes every endpoint by default, and the login page
// lives inside the shell, so a protected shell would lock the user out of the screen
// that signs them in.
app.Use(async (context, next) =>
{
    var path = context.Request.Path;

    var isClientRoute =
        HttpMethods.IsGet(context.Request.Method) &&
        !path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase) &&
        // A path with an extension is a real asset request; if the file is missing it
        // must 404 rather than quietly return HTML, which is impossible to debug.
        !Path.HasExtension(path.Value);

    if (isClientRoute)
        context.Request.Path = "/index.html";

    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

// A request for an asset that got this far does not exist - static files has already had
// its chance at it. Answering here keeps it a 404. Left to fall through it would reach
// the authorisation middleware, whose fallback policy applies even to requests that
// matched no endpoint, and a missing script chunk would report itself as "not signed in".
app.Use(async (context, next) =>
{
    var path = context.Request.Path;

    var isMissingAsset =
        HttpMethods.IsGet(context.Request.Method) &&
        !path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase) &&
        Path.HasExtension(path.Value);

    if (isMissingAsset)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next();
});

// Explicit, so it runs after the static files above rather than being auto-inserted
// ahead of them.
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

static string Camel(string key) =>
    string.IsNullOrEmpty(key) || char.IsLower(key[0]) ? key : char.ToLowerInvariant(key[0]) + key[1..];

static Task WriteProblemAsync(HttpContext http, int status, string code, string title, string detail)
{
    http.Response.ContentType = "application/problem+json";
    return http.Response.WriteAsJsonAsync(
        ProblemDetailsExtensions.Build(http, status, code, title, detail));
}

/// <summary>Exposed so the integration test project can drive the host.</summary>
public partial class Program;
