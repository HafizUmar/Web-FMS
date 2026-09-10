using CrockeryFactory.Domain.Abstractions;
using CrockeryFactory.Persistence;
using CrockeryFactory.Web.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

builder.Services.AddDbContext<FactoryDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("FactoryDatabase"),
        sql => sql.MigrationsAssembly(typeof(Program).Assembly.FullName)));

var app = builder.Build();

// Migrations are applied by the Setup tool, never here. With one instance startup
// migration would work; a failed one would then leave the application in a crash loop
// at a factory nobody can reach, during working hours (spec section 2.2).

app.MapGet("/", () => Results.Ok(new { service = "CrockeryFactory", status = "ok" }));

app.Run();

/// <summary>Exposed so the integration test project can drive the host.</summary>
public partial class Program;
