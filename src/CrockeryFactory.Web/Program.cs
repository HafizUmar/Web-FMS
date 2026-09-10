using System.Text.Json.Serialization;
using CrockeryFactory.Application.Abstractions;
using CrockeryFactory.Application.Catalogue;
using CrockeryFactory.Application.Common;
using CrockeryFactory.Application.Production;
using CrockeryFactory.Application.Sales;
using CrockeryFactory.Application.Services;
using CrockeryFactory.Application.Stock;
using CrockeryFactory.Domain.Abstractions;
using CrockeryFactory.Persistence;
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
