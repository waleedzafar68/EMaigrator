# EMaigrator.Api (REST + SignalR + Identity) Implementation Plan

> Part of the EMaigrator v1 plan set — see 00-INDEX.md. Binds to CONTRACTS.md.

**Goal:** Build the `EMaigrator.Api` ASP.NET Core project: ASP.NET Core Identity (multi-tenant `ApplicationUser`), the full REST surface in CONTRACTS §6 (`/api/v1/migrations/*`), the `MigrationsHub` SignalR hub with a Redis backplane and worker→hub bridge, CSV-import scope parsing, async pre-flight, approve/run/pause/resume/cancel, results+reconciliation, audit (privacy-toggle aware), CSV/PDF report export, terminal-state email notifications, and a public health endpoint — with tenant row-level isolation enforced everywhere.

**Architecture:** The API is a thin composition layer (`DESIGN.md §15` dependency rule): it references `EMaigrator.Core` (contracts), `EMaigrator.Infrastructure` (EF `AppDbContext`, `ISecretStore`, `IJobOrchestrator`, health), and the connector assemblies (for `IProviderPlugin` discovery + `TestConnection`). Tenancy is enforced by an EF global query filter keyed off an `ICurrentTenant` accessor populated from the authenticated principal's `TenantId` claim, so cross-tenant reads return 404. SignalR fans progress out across horizontally-scaled API instances via the Redis backplane; a hosted background bridge consumes `MigrationProgressEvent`/`NeedsDecisionEvent` from MassTransit and pushes to the per-migration group.

**Tech Stack:** C#/.NET 10, ASP.NET Core Minimal APIs + `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Microsoft.AspNetCore.SignalR.StackExchangeRedis`, EF Core/PostgreSQL (via Infrastructure), MassTransit consumers, QuestPDF (PDF report), `CsvHelper` (CSV parse/export), built-in `Microsoft.AspNetCore.RateLimiting`. Tests: xUnit, FluentAssertions, NSubstitute, `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`), `Microsoft.AspNetCore.SignalR.Client`, Testcontainers.PostgreSql.

---

### Task 0: Project scaffold, DI composition, and WebApplicationFactory test harness

**Goal:** Create the `EMaigrator.Api` project (referencing Core, Infrastructure, and connectors), a `Program`/`AppBuilder` that wires Identity + EF + SignalR + auth + Swagger, and a reusable `ApiTestFactory` so every later task can spin a real in-process server against a Testcontainers Postgres.

**Files:**
- Create: `src/EMaigrator.Api/EMaigrator.Api.csproj`
- Create: `src/EMaigrator.Api/Program.cs`
- Create: `src/EMaigrator.Api/AppConfiguration/ApiServiceCollectionExtensions.cs`
- Create: `src/EMaigrator.Api/appsettings.json`
- Create: `src/EMaigrator.Api.Tests/EMaigrator.Api.Tests.csproj`
- Create: `src/EMaigrator.Api.Tests/Infrastructure/ApiTestFactory.cs`
- Create: `src/EMaigrator.Api.Tests/Infrastructure/PostgresFixture.cs`
- Test: `src/EMaigrator.Api.Tests/ScaffoldSmokeTests.cs`

**Acceptance Criteria:**
- [ ] `EMaigrator.Api.csproj` references `EMaigrator.Core`, `EMaigrator.Infrastructure`, `EMaigrator.Connectors.Imap`, `EMaigrator.Connectors.Graph`, `EMaigrator.Connectors.Gmail` and NO other production project (dependency rule honored).
- [ ] `Program.cs` exposes a `public partial class Program {}` so `WebApplicationFactory<Program>` can target it.
- [ ] `ApiTestFactory` boots the app against a Testcontainers Postgres connection string and runs EF migrations on startup.
- [ ] `GET /health` returns HTTP 200 with JSON body containing `"status"`.
- [ ] The app builds with `dotnet build` and the smoke test passes.

**Verify:** `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~ScaffoldSmokeTests` → all pass (1 test: health returns 200).

**Steps:**

1. - [ ] Write the failing smoke test.

```csharp
// src/EMaigrator.Api.Tests/ScaffoldSmokeTests.cs
using System.Net;
using System.Net.Http.Json;
using EMaigrator.Api.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Api.Tests;

[Collection(PostgresCollection.Name)]
public class ScaffoldSmokeTests
{
    private readonly ApiTestFactory _factory;
    public ScaffoldSmokeTests(PostgresFixture pg) => _factory = new ApiTestFactory(pg.ConnectionString);

    [Fact]
    public async Task Health_endpoint_is_public_and_returns_200()
    {
        using var client = _factory.CreateClient();
        var res = await client.GetAsync("/health");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await res.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        json!.Should().ContainKey("status");
    }
}
```

2. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~ScaffoldSmokeTests`. Expected **FAIL** — `EMaigrator.Api` project and `ApiTestFactory` do not exist; compilation fails.

3. - [ ] Create the project, harness, and composition root.

```xml
<!-- src/EMaigrator.Api/EMaigrator.Api.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="10.0.0" />
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.0" />
    <PackageReference Include="Microsoft.AspNetCore.SignalR.StackExchangeRedis" Version="10.0.0" />
    <PackageReference Include="Swashbuckle.AspNetCore" Version="7.2.0" />
    <PackageReference Include="CsvHelper" Version="33.0.1" />
    <PackageReference Include="QuestPDF" Version="2024.12.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\EMaigrator.Core\EMaigrator.Core.csproj" />
    <ProjectReference Include="..\EMaigrator.Infrastructure\EMaigrator.Infrastructure.csproj" />
    <ProjectReference Include="..\EMaigrator.Connectors.Imap\EMaigrator.Connectors.Imap.csproj" />
    <ProjectReference Include="..\EMaigrator.Connectors.Graph\EMaigrator.Connectors.Graph.csproj" />
    <ProjectReference Include="..\EMaigrator.Connectors.Gmail\EMaigrator.Connectors.Gmail.csproj" />
  </ItemGroup>
</Project>
```

```csharp
// src/EMaigrator.Api/AppConfiguration/ApiServiceCollectionExtensions.cs
using EMaigrator.Infrastructure.Data;
using EMaigrator.Infrastructure.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.AppConfiguration;

public static class ApiServiceCollectionExtensions
{
    // Registers EF + Identity + Infrastructure. Identity & tenancy wiring is layered on in later tasks.
    public static IServiceCollection AddEMaigratorApi(this IServiceCollection services, IConfiguration config)
    {
        var conn = config.GetConnectionString("Postgres")
                   ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");

        // AddEMaigratorInfrastructure registers AppDbContext, ISecretStore, IJobOrchestrator,
        // ILedger, IRateLimiter, health checks (defined in Plan 03).
        services.AddEMaigratorInfrastructure(config, conn);

        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.Password.RequiredLength = 12;
                o.User.RequireUniqueEmail = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager();

        services.AddSignalR();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        return services;
    }
}
```

```csharp
// src/EMaigrator.Api/Program.cs
using EMaigrator.Api.AppConfiguration;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEMaigratorApi(builder.Configuration);

var app = builder.Build();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString())
        }));
    }
}).AllowAnonymous();

app.Run();

public partial class Program { }
```

```json
// src/EMaigrator.Api/appsettings.json
{
  "ConnectionStrings": { "Postgres": "Host=localhost;Database=emaigrator;Username=postgres;Password=postgres" },
  "Jwt": { "Issuer": "emaigrator", "Audience": "emaigrator", "SigningKey": "dev-only-change-me-32byteslongsecret!!" },
  "Cors": { "AllowedOrigins": [ "http://localhost:5173" ] },
  "Redis": { "Configuration": "localhost:6379" },
  "Logging": { "LogLevel": { "Default": "Information" } }
}
```

```csharp
// src/EMaigrator.Api.Tests/EMaigrator.Api.Tests.csproj
// (project file)
```

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="FluentAssertions" Version="6.12.2" />
    <PackageReference Include="NSubstitute" Version="5.3.0" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" />
    <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.0" />
    <PackageReference Include="Testcontainers.PostgreSql" Version="4.1.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\EMaigrator.Api\EMaigrator.Api.csproj" />
  </ItemGroup>
</Project>
```

```csharp
// src/EMaigrator.Api.Tests/Infrastructure/PostgresFixture.cs
using Testcontainers.PostgreSql;
using Xunit;

namespace EMaigrator.Api.Tests.Infrastructure;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();
    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
```

```csharp
// src/EMaigrator.Api.Tests/Infrastructure/ApiTestFactory.cs
using EMaigrator.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Tests.Infrastructure;

public sealed class ApiTestFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    public ApiTestFactory(string connectionString) => _connectionString = connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Postgres", _connectionString);
        builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:5173");
        builder.ConfigureServices(services =>
        {
            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.Migrate();
        });
    }
}
```

4. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~ScaffoldSmokeTests`. Expected **PASS** (health returns 200, status key present).

5. - [ ] Commit.

```bash
git add src/EMaigrator.Api src/EMaigrator.Api.Tests
git commit -m "feat(api): scaffold EMaigrator.Api with health endpoint and WebApplicationFactory harness

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 1: ApplicationUser + Identity registration/login (cookie + JWT)

**Goal:** Define `ApplicationUser : IdentityUser<Guid>` with `TenantId`, and ship `/api/v1/auth/register` (creates Tenant + user) and `/api/v1/auth/login` (issues a JWT carrying the `TenantId` claim and sets an auth cookie).

**Files:**
- Create: `src/EMaigrator.Api/Identity/ApplicationUser.cs`
- Create: `src/EMaigrator.Api/Identity/IJwtTokenIssuer.cs`
- Create: `src/EMaigrator.Api/Identity/JwtTokenIssuer.cs`
- Create: `src/EMaigrator.Api/Identity/JwtOptions.cs`
- Create: `src/EMaigrator.Api/Endpoints/AuthEndpoints.cs`
- Create: `src/EMaigrator.Api/Contracts/AuthDtos.cs`
- Modify: `src/EMaigrator.Api/AppConfiguration/ApiServiceCollectionExtensions.cs`
- Modify: `src/EMaigrator.Api/Program.cs`
- Test: `src/EMaigrator.Api.Tests/AuthEndpointsTests.cs`

**Acceptance Criteria:**
- [ ] `ApplicationUser : IdentityUser<Guid>` has a `Guid TenantId` property (matches CONTRACTS §5 comment).
- [ ] `POST /api/v1/auth/register` with `{email,password,organizationName}` creates a `Tenant` row and an `ApplicationUser` whose `TenantId` is that tenant; returns 201.
- [ ] `POST /api/v1/auth/login` with valid creds returns 200 + `{ accessToken, expiresAt }` and a `Set-Cookie` auth cookie; the JWT contains a `tenant_id` claim equal to the user's `TenantId`.
- [ ] Login with wrong password returns 401.
- [ ] Register with a password shorter than 12 chars returns 400 with a validation problem.

**Verify:** `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~AuthEndpointsTests` → all pass.

**Steps:**

1. - [ ] Write the failing test.

```csharp
// src/EMaigrator.Api.Tests/AuthEndpointsTests.cs
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using EMaigrator.Api.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Api.Tests;

[Collection(PostgresCollection.Name)]
public class AuthEndpointsTests
{
    private readonly ApiTestFactory _factory;
    public AuthEndpointsTests(PostgresFixture pg) => _factory = new ApiTestFactory(pg.ConnectionString);

    private sealed record RegisterReq(string email, string password, string organizationName);
    private sealed record LoginResp(string accessToken, DateTimeOffset expiresAt);

    [Fact]
    public async Task Register_then_login_issues_jwt_with_tenant_claim()
    {
        using var client = _factory.CreateClient();
        var email = $"u{Guid.NewGuid():N}@biz.com";

        var reg = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterReq(email, "Sup3rSecret!Pass", "Acme MSP"));
        reg.StatusCode.Should().Be(HttpStatusCode.Created);

        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email, password = "Sup3rSecret!Pass" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        login.Headers.Should().ContainKey("Set-Cookie");

        var body = await login.Content.ReadFromJsonAsync<LoginResp>();
        body!.accessToken.Should().NotBeNullOrEmpty();

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body.accessToken);
        jwt.Claims.Should().Contain(c => c.Type == "tenant_id" && !string.IsNullOrEmpty(c.Value));
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401()
    {
        using var client = _factory.CreateClient();
        var email = $"u{Guid.NewGuid():N}@biz.com";
        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterReq(email, "Sup3rSecret!Pass", "Acme"));

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "wrong-password-1" });
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Register_with_short_password_returns_400()
    {
        using var client = _factory.CreateClient();
        var reg = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterReq($"u{Guid.NewGuid():N}@biz.com", "short", "Acme"));
        reg.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

2. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~AuthEndpointsTests`. Expected **FAIL** — `ApplicationUser`, auth endpoints, and JWT issuer do not exist.

3. - [ ] Implement.

```csharp
// src/EMaigrator.Api/Identity/ApplicationUser.cs
using Microsoft.AspNetCore.Identity;

namespace EMaigrator.Api.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
}
```

```csharp
// src/EMaigrator.Api/Identity/JwtOptions.cs
namespace EMaigrator.Api.Identity;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "emaigrator";
    public string Audience { get; set; } = "emaigrator";
    public string SigningKey { get; set; } = "";
    public int LifetimeMinutes { get; set; } = 60;
}
```

```csharp
// src/EMaigrator.Api/Identity/IJwtTokenIssuer.cs
namespace EMaigrator.Api.Identity;

public interface IJwtTokenIssuer
{
    (string Token, DateTimeOffset ExpiresAt) Issue(ApplicationUser user);
}
```

```csharp
// src/EMaigrator.Api/Identity/JwtTokenIssuer.cs
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EMaigrator.Api.Identity;

public sealed class JwtTokenIssuer : IJwtTokenIssuer
{
    public const string TenantClaim = "tenant_id";
    private readonly JwtOptions _o;
    public JwtTokenIssuer(IOptions<JwtOptions> o) => _o = o.Value;

    public (string Token, DateTimeOffset ExpiresAt) Issue(ApplicationUser user)
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(_o.LifetimeMinutes);
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_o.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new Claim(TenantClaim, user.TenantId.ToString())
        };
        var jwt = new JwtSecurityToken(_o.Issuer, _o.Audience, claims,
            expires: expires.UtcDateTime, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }
}
```

```csharp
// src/EMaigrator.Api/Contracts/AuthDtos.cs
using System.ComponentModel.DataAnnotations;

namespace EMaigrator.Api.Contracts;

public sealed record RegisterRequest(
    [property: Required, EmailAddress] string Email,
    [property: Required, MinLength(12)] string Password,
    [property: Required, MinLength(1)] string OrganizationName);

public sealed record LoginRequest(
    [property: Required, EmailAddress] string Email,
    [property: Required] string Password);

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt);
```

```csharp
// src/EMaigrator.Api/Endpoints/AuthEndpoints.cs
using System.ComponentModel.DataAnnotations;
using EMaigrator.Api.Contracts;
using EMaigrator.Api.Identity;
using EMaigrator.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;

namespace EMaigrator.Api.Endpoints;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/auth/register", async (
            RegisterRequest req, UserManager<ApplicationUser> users, AppDbContext db) =>
        {
            var ctx = new ValidationContext(req);
            var errors = new List<ValidationResult>();
            if (!Validator.TryValidateObject(req, ctx, errors, true))
                return Results.ValidationProblem(errors.ToDictionary(
                    e => e.MemberNames.FirstOrDefault() ?? "", e => new[] { e.ErrorMessage ?? "invalid" }));

            var tenant = new Tenant { Id = Guid.NewGuid(), Name = req.OrganizationName };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();

            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = req.Email, Email = req.Email, TenantId = tenant.Id };
            var result = await users.CreateAsync(user, req.Password);
            if (!result.Succeeded)
                return Results.ValidationProblem(result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));

            return Results.Created($"/api/v1/users/{user.Id}", new { id = user.Id, tenantId = tenant.Id });
        }).AllowAnonymous();

        group.MapPost("/auth/login", async (
            LoginRequest req, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
            IJwtTokenIssuer issuer, HttpContext http) =>
        {
            var user = await users.FindByEmailAsync(req.Email);
            if (user is null) return Results.Unauthorized();
            var check = await signIn.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
            if (!check.Succeeded) return Results.Unauthorized();

            var (token, expires) = issuer.Issue(user);
            http.Response.Cookies.Append("emaigrator.auth", token, new CookieOptions
            {
                HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Expires = expires
            });
            return Results.Ok(new LoginResponse(token, expires));
        }).AllowAnonymous();

        return group;
    }
}
```

Add wiring in `ApiServiceCollectionExtensions.AddEMaigratorApi` (after the `AddSignInManager()` chain):

```csharp
        services.Configure<JwtOptions>(config.GetSection("Jwt"));
        services.AddSingleton<IJwtTokenIssuer, JwtTokenIssuer>();
```

Add to `Program.cs` (before `app.Run();`):

```csharp
var v1 = app.MapGroup("/api/v1");
v1.MapAuthEndpoints();
```

(Add `using EMaigrator.Api.Endpoints;` and `using EMaigrator.Api.Identity;` at the top of `Program.cs`.)

4. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~AuthEndpointsTests`. Expected **PASS** (3 tests).

5. - [ ] Commit.

```bash
git add src/EMaigrator.Api src/EMaigrator.Api.Tests
git commit -m "feat(api): ApplicationUser with TenantId + register/login issuing tenant-scoped JWT

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: JWT/cookie authentication + ICurrentTenant accessor + tenant global query filter

**Goal:** Wire JWT-bearer + cookie authentication, an `ICurrentTenant` accessor populated from the `tenant_id` claim, and apply an EF Core global query filter so all tenant-scoped reads are confined to the caller's tenant — making cross-tenant access return empty (→ 404 at the endpoint).

**Files:**
- Create: `src/EMaigrator.Api/Tenancy/ICurrentTenant.cs`
- Create: `src/EMaigrator.Api/Tenancy/HttpContextCurrentTenant.cs`
- Create: `src/EMaigrator.Api/Tenancy/TenantQueryFilterRegistration.cs`
- Create: `src/EMaigrator.Api.Tests/Infrastructure/TestCurrentTenant.cs`
- Modify: `src/EMaigrator.Api/AppConfiguration/ApiServiceCollectionExtensions.cs`
- Modify: `src/EMaigrator.Api/Program.cs`
- Modify: `src/EMaigrator.Api.Tests/Infrastructure/ApiTestFactory.cs`
- Test: `src/EMaigrator.Api.Tests/TenancyFilterTests.cs`

**Acceptance Criteria:**
- [ ] `ICurrentTenant.TenantId` returns the `Guid` from the authenticated principal's `tenant_id` claim; throws `UnauthorizedAccessException` when unauthenticated.
- [ ] `AppDbContext` (Plan 03) applies a global query filter `j => j.TenantId == _tenantProvider.TenantId` for `Job`; the API supplies the per-request `ITenantProvider`.
- [ ] A `Job` seeded for tenant B is invisible to a query made while the current tenant is A (test asserts `null`).
- [ ] Default authorization policy requires an authenticated user; `[AllowAnonymous]` still lets `/health` and auth endpoints through.

**Verify:** `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~TenancyFilterTests` → all pass.

**Steps:**

1. - [ ] Write the failing test (and the test-only `TestCurrentTenant`).

```csharp
// src/EMaigrator.Api.Tests/TenancyFilterTests.cs
using EMaigrator.Api.Tenancy;
using EMaigrator.Api.Tests.Infrastructure;
using EMaigrator.Infrastructure.Data;
using EMaigrator.Core.Model;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EMaigrator.Api.Tests;

[Collection(PostgresCollection.Name)]
public class TenancyFilterTests
{
    private readonly ApiTestFactory _factory;
    public TenancyFilterTests(PostgresFixture pg) => _factory = new ApiTestFactory(pg.ConnectionString);

    [Fact]
    public async Task Job_of_other_tenant_is_invisible_under_query_filter()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        Guid jobBId;
        using (var scope = _factory.Services.CreateScope())
        {
            ((TestCurrentTenant)scope.ServiceProvider.GetRequiredService<ICurrentTenant>()).Current = tenantB;
            var seed = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var jobB = new Job
            {
                Id = Guid.NewGuid(), TenantId = tenantB,
                SourceProvider = new ProviderId("imap"), DestProvider = new ProviderId("graph"),
                Status = JobStatus.Draft, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            };
            seed.Set<Job>().Add(jobB);
            await seed.SaveChangesAsync();
            jobBId = jobB.Id;
        }

        using (var scope = _factory.Services.CreateScope())
        {
            ((TestCurrentTenant)scope.ServiceProvider.GetRequiredService<ICurrentTenant>()).Current = tenantA;
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var found = await db.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobBId);
            found.Should().BeNull("tenant A must not see tenant B's job");
        }
    }
}
```

```csharp
// src/EMaigrator.Api.Tests/Infrastructure/TestCurrentTenant.cs
using EMaigrator.Api.Identity;
using EMaigrator.Api.Tenancy;
using Microsoft.AspNetCore.Http;

namespace EMaigrator.Api.Tests.Infrastructure;

// Per-scope tenant accessor. For HTTP/SignalR requests, Current is left unset (Guid.Empty) and the
// tenant is derived from the request principal's tenant_id claim (real production behavior). For
// direct-DbContext seeding tests with no HttpContext, the test sets Current explicitly to force a tenant.
public sealed class TestCurrentTenant : ICurrentTenant
{
    private readonly IHttpContextAccessor _http;
    public TestCurrentTenant(IHttpContextAccessor http) => _http = http;

    public Guid Current { get; set; } = Guid.Empty;   // explicit override for seeding scopes

    private Guid? FromClaim()
    {
        var claim = _http.HttpContext?.User?.FindFirst(JwtTokenIssuer.TenantClaim)?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    public bool IsAuthenticated => Current != Guid.Empty || FromClaim() is not null;

    public Guid TenantId
    {
        get
        {
            if (Current != Guid.Empty) return Current;            // seeding override wins
            return FromClaim() ?? throw new UnauthorizedAccessException("No tenant context.");
        }
    }
}
```

Register the substitute in `ApiTestFactory.ConfigureWebHost` (inside `ConfigureServices`, BEFORE the migrate block). It is registered scoped so each HTTP request and each `CreateScope()` seeding block gets its own instance:

```csharp
            services.AddScoped<EMaigrator.Api.Tenancy.ICurrentTenant, TestCurrentTenant>();
```

> `TestCurrentTenant` resolves the tenant from the JWT for real requests (so `MigrationCrudTests`, the security gate, and the functional flow all get correct per-request isolation) and only uses the explicit `Current` override in direct-seeding scopes (`TenancyFilterTests`, `ResultsAuditTests`, `ReportEndpointTests`, `SignalRProgressTests`). This requires `IHttpContextAccessor`, which `AddEMaigratorApi` already registers (Task 2).

2. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~TenancyFilterTests`. Expected **FAIL** — `ICurrentTenant`/filter not yet defined; compilation fails (or, once stubs exist, the row leaks).

3. - [ ] Implement.

```csharp
// src/EMaigrator.Api/Tenancy/ICurrentTenant.cs
namespace EMaigrator.Api.Tenancy;

public interface ICurrentTenant
{
    Guid TenantId { get; }     // throws UnauthorizedAccessException if unauthenticated
    bool IsAuthenticated { get; }
}
```

```csharp
// src/EMaigrator.Api/Tenancy/HttpContextCurrentTenant.cs
using EMaigrator.Api.Identity;
using Microsoft.AspNetCore.Http;

namespace EMaigrator.Api.Tenancy;

public sealed class HttpContextCurrentTenant : ICurrentTenant
{
    private readonly IHttpContextAccessor _http;
    public HttpContextCurrentTenant(IHttpContextAccessor http) => _http = http;

    public bool IsAuthenticated =>
        _http.HttpContext?.User?.FindFirst(JwtTokenIssuer.TenantClaim) is not null;

    public Guid TenantId
    {
        get
        {
            var claim = _http.HttpContext?.User?.FindFirst(JwtTokenIssuer.TenantClaim)?.Value;
            if (string.IsNullOrEmpty(claim) || !Guid.TryParse(claim, out var id))
                throw new UnauthorizedAccessException("No tenant context.");
            return id;
        }
    }
}
```

```csharp
// src/EMaigrator.Api/Tenancy/TenantQueryFilterRegistration.cs
using EMaigrator.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace EMaigrator.Api.Tenancy;

public static class TenantQueryFilterRegistration
{
    // AppDbContext (Plan 03) consumes an ITenantProvider; adapt ICurrentTenant to it.
    public static IServiceCollection AddTenantScopedDbContext(this IServiceCollection services)
    {
        services.AddScoped<ITenantProvider>(sp =>
        {
            var current = sp.GetRequiredService<ICurrentTenant>();
            return new DelegateTenantProvider(() => current.IsAuthenticated ? current.TenantId : Guid.Empty);
        });
        return services;
    }
}

public sealed class DelegateTenantProvider : ITenantProvider
{
    private readonly Func<Guid> _get;
    public DelegateTenantProvider(Func<Guid> get) => _get = get;
    public Guid TenantId => _get();
}
```

> Binds to Plan 03's `AppDbContext`, which applies `entity.HasQueryFilter(j => j.TenantId == _tenantProvider.TenantId)` for `Job`. `ITenantProvider` is the Infrastructure seam; the API supplies the per-request value. Anonymous reads resolve to `Guid.Empty`, which matches no tenant row.

Wire authentication + tenancy in `ApiServiceCollectionExtensions.AddEMaigratorApi` (append after the JWT issuer registration from Task 1):

```csharp
        services.AddHttpContextAccessor();
        services.AddScoped<EMaigrator.Api.Tenancy.ICurrentTenant, EMaigrator.Api.Tenancy.HttpContextCurrentTenant>();
        services.AddTenantScopedDbContext();

        var jwt = config.GetSection("Jwt").Get<JwtOptions>()!;
        services.AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = jwt.Issuer,
                    ValidateAudience = true, ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                        System.Text.Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30)
                };
                options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        if (string.IsNullOrEmpty(ctx.Token) && ctx.Request.Cookies.TryGetValue("emaigrator.auth", out var c))
                            ctx.Token = c;
                        // SignalR sends the token on the query string during the negotiate/connect.
                        var access = ctx.Request.Query["access_token"];
                        if (string.IsNullOrEmpty(ctx.Token) && !string.IsNullOrEmpty(access) &&
                            ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                            ctx.Token = access;
                        return Task.CompletedTask;
                    }
                };
            });
        services.AddAuthorization(o =>
            o.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser().Build());
```

Add to `Program.cs` (after `var app = builder.Build();`, before endpoint mapping):

```csharp
app.UseAuthentication();
app.UseAuthorization();
```

4. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~TenancyFilterTests`. Expected **PASS**.

5. - [ ] Commit.

```bash
git add src/EMaigrator.Api src/EMaigrator.Api.Tests
git commit -m "feat(api): JWT/cookie auth + ICurrentTenant accessor + tenant global query filter

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: MigrationDto + Migrations CRUD + Draft autosave (endpoints PATCH)

**Goal:** Ship `MigrationDto` (camelCase, exactly the CONTRACTS §6 shape) and the core CRUD/draft routes: `POST /migrations` (new Draft at `wizardStep=1`), `GET /migrations` (`?status=&q=`), `GET /migrations/{id}`, `DELETE /migrations/{id}` (204), and `PATCH /migrations/{id}/endpoints` (sets `from`/`to`, advances wizard) — all tenant-scoped, returning 404 for cross-tenant ids.

**Files:**
- Create: `src/EMaigrator.Api/Contracts/MigrationDto.cs`
- Create: `src/EMaigrator.Api/Contracts/MigrationRequests.cs`
- Create: `src/EMaigrator.Api/Mapping/MigrationMapper.cs`
- Create: `src/EMaigrator.Api/Endpoints/MigrationEndpoints.cs`
- Modify: `src/EMaigrator.Api/Program.cs`
- Test: `src/EMaigrator.Api.Tests/MigrationCrudTests.cs`
- Test: `src/EMaigrator.Api.Tests/Infrastructure/AuthClient.cs`

**Acceptance Criteria:**
- [ ] `POST /api/v1/migrations` with `{}` returns 201 + `MigrationDto` with `status="Draft"`, `wizardStep=1`, and a new `id`; persists a `Job` with the caller's `TenantId`.
- [ ] `MigrationDto` JSON has exactly the keys `id, status, wizardStep, from, to, isBatch, scopeSummary, mailboxCount, progress, createdAt` (camelCase).
- [ ] `GET /api/v1/migrations?status=Draft` returns only the caller's Draft jobs; `?q=` filters by provider text.
- [ ] `GET /api/v1/migrations/{id}` for another tenant's job returns 404.
- [ ] `PATCH /api/v1/migrations/{id}/endpoints` with `{from:"imap",to:"graph"}` sets providers, advances `wizardStep` to ≥2, returns updated `MigrationDto`.
- [ ] `DELETE /api/v1/migrations/{id}` returns 204 and the job is gone (Draft) / cancelled.
- [ ] Calling any of these unauthenticated returns 401.

**Verify:** `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~MigrationCrudTests` → all pass.

**Steps:**

1. - [ ] Write the failing test (plus a reusable authenticated-client helper used by all later endpoint tests).

```csharp
// src/EMaigrator.Api.Tests/Infrastructure/AuthClient.cs
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace EMaigrator.Api.Tests.Infrastructure;

public static class AuthClient
{
    // Registers a fresh tenant+user and returns an HttpClient with a bearer token + the tenant id.
    public static async Task<(HttpClient Client, Guid TenantId)> CreateAsync(ApiTestFactory factory)
    {
        var client = factory.CreateClient();
        // Unique auth rate-limit bucket per test client so concurrent tests never contaminate each other.
        client.DefaultRequestHeaders.Add("X-Client-Id", Guid.NewGuid().ToString("N"));
        var email = $"u{Guid.NewGuid():N}@biz.com";
        var reg = await client.PostAsJsonAsync("/api/v1/auth/register",
            new { email, password = "Sup3rSecret!Pass", organizationName = "Acme" });
        reg.EnsureSuccessStatusCode();
        var regBody = await reg.Content.ReadFromJsonAsync<RegResp>();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Sup3rSecret!Pass" });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<TokenResp>())!.accessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, regBody!.tenantId);
    }
    private sealed record RegResp(Guid id, Guid tenantId);
    private sealed record TokenResp(string accessToken, DateTimeOffset expiresAt);
}
```

```csharp
// src/EMaigrator.Api.Tests/MigrationCrudTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EMaigrator.Api.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Api.Tests;

[Collection(PostgresCollection.Name)]
public class MigrationCrudTests
{
    private readonly ApiTestFactory _factory;
    public MigrationCrudTests(PostgresFixture pg) => _factory = new ApiTestFactory(pg.ConnectionString);

    [Fact]
    public async Task Post_creates_draft_with_expected_dto_shape()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var res = await client.PostAsJsonAsync("/api/v1/migrations", new { });
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("Draft");
        root.GetProperty("wizardStep").GetInt32().Should().Be(1);
        foreach (var key in new[] { "id","status","wizardStep","from","to","isBatch","scopeSummary","mailboxCount","progress","createdAt" })
            root.TryGetProperty(key, out _).Should().BeTrue($"DTO must contain camelCase key '{key}'");
    }

    [Fact]
    public async Task Patch_endpoints_sets_providers_and_advances_wizard()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var id = (await (await client.PostAsJsonAsync("/api/v1/migrations", new { }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var patch = await client.PatchAsJsonAsync($"/api/v1/migrations/{id}/endpoints",
            new { from = "imap", to = "graph" });
        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await patch.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("from").GetString().Should().Be("imap");
        dto.GetProperty("to").GetString().Should().Be("graph");
        dto.GetProperty("wizardStep").GetInt32().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Get_other_tenants_migration_returns_404()
    {
        var (clientA, _) = await AuthClient.CreateAsync(_factory);
        var (clientB, _) = await AuthClient.CreateAsync(_factory);
        var id = (await (await clientA.PostAsJsonAsync("/api/v1/migrations", new { }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var asB = await clientB.GetAsync($"/api/v1/migrations/{id}");
        asB.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_filters_by_status_and_delete_removes()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var id = (await (await client.PostAsJsonAsync("/api/v1/migrations", new { }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/migrations?status=Draft");
        list.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);

        var del = await client.DeleteAsync($"/api/v1/migrations/{id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/v1/migrations/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Unauthenticated_create_returns_401()
    {
        using var anon = _factory.CreateClient();
        var res = await anon.PostAsJsonAsync("/api/v1/migrations", new { });
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

2. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~MigrationCrudTests`. Expected **FAIL** — DTO, mapper, and migration endpoints do not exist.

3. - [ ] Implement.

```csharp
// src/EMaigrator.Api/Contracts/MigrationDto.cs
namespace EMaigrator.Api.Contracts;

// Exactly the CONTRACTS §6 shape. Serialized camelCase by the default web JSON options.
public sealed record MigrationProgressSummary(long Migrated, long Total, double Percent, string? CurrentFolder, double MsgPerMin);

public sealed record MigrationDto(
    Guid Id,
    string Status,
    int WizardStep,
    string? From,
    string? To,
    bool IsBatch,
    string? ScopeSummary,
    int MailboxCount,
    MigrationProgressSummary? Progress,
    DateTimeOffset CreatedAt);
```

```csharp
// src/EMaigrator.Api/Contracts/MigrationRequests.cs
using System.ComponentModel.DataAnnotations;

namespace EMaigrator.Api.Contracts;

public sealed record SetEndpointsRequest(
    [property: Required, MinLength(1)] string From,
    [property: Required, MinLength(1)] string To);
```

```csharp
// src/EMaigrator.Api/Mapping/MigrationMapper.cs
using EMaigrator.Api.Contracts;
using EMaigrator.Infrastructure.Data;

namespace EMaigrator.Api.Mapping;

public static class MigrationMapper
{
    public static MigrationDto ToDto(Job job, IReadOnlyCollection<MailboxMigration> mailboxes)
    {
        var migrated = mailboxes.Sum(m => m.MigratedCount);
        var totalCounts = mailboxes.Sum(m => m.MigratedCount + m.SkippedCount + m.FailedCount);
        MigrationProgressSummary? progress = mailboxes.Count == 0
            ? null
            : new MigrationProgressSummary(migrated, totalCounts,
                totalCounts == 0 ? 0 : Math.Round(100.0 * migrated / totalCounts, 1), null, 0);

        var scopeSummary = job.IsBatch
            ? $"{mailboxes.Count} mailboxes"
            : (mailboxes.Count == 1 ? $"{mailboxes.First().SourceMailbox} → {mailboxes.First().DestMailbox}" : "1 mailbox");

        return new MigrationDto(
            job.Id, job.Status.ToString(), job.WizardStep,
            job.SourceProvider.Value.Length == 0 ? null : job.SourceProvider.Value,
            job.DestProvider.Value.Length == 0 ? null : job.DestProvider.Value,
            job.IsBatch, scopeSummary, mailboxes.Count, progress, job.CreatedAt);
    }
}
```

```csharp
// src/EMaigrator.Api/Endpoints/MigrationEndpoints.cs
using System.ComponentModel.DataAnnotations;
using EMaigrator.Api.Contracts;
using EMaigrator.Api.Mapping;
using EMaigrator.Api.Tenancy;
using EMaigrator.Core.Model;
using EMaigrator.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Endpoints;

public static class MigrationEndpoints
{
    public static RouteGroupBuilder MapMigrationEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/migrations", async (AppDbContext db, ICurrentTenant tenant) =>
        {
            var job = new Job
            {
                Id = Guid.NewGuid(), TenantId = tenant.TenantId,
                SourceProvider = new ProviderId(""), DestProvider = new ProviderId(""),
                Status = JobStatus.Draft, WizardStep = 1, StoreSubjects = true,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Set<Job>().Add(job);
            await db.SaveChangesAsync();
            return Results.Created($"/api/v1/migrations/{job.Id}", MigrationMapper.ToDto(job, Array.Empty<MailboxMigration>()));
        });

        group.MapGet("/migrations", async (string? status, string? q, AppDbContext db) =>
        {
            var query = db.Set<Job>().AsQueryable();   // global filter already scopes to tenant
            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<JobStatus>(status, true, out var s))
                query = query.Where(j => j.Status == s);
            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(j => j.SourceProvider.Value.Contains(q) || j.DestProvider.Value.Contains(q));

            var jobs = await query.OrderByDescending(j => j.UpdatedAt).ToListAsync();
            var ids = jobs.Select(j => j.Id).ToList();
            var mbx = await db.Set<MailboxMigration>().Where(m => ids.Contains(m.JobId)).ToListAsync();
            var dtos = jobs.Select(j => MigrationMapper.ToDto(j, mbx.Where(m => m.JobId == j.Id).ToList()));
            return Results.Ok(dtos);
        });

        group.MapGet("/migrations/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var job = await db.Set<Job>().FirstOrDefaultAsync(j => j.Id == id);
            if (job is null) return Results.NotFound();
            var mbx = await db.Set<MailboxMigration>().Where(m => m.JobId == id).ToListAsync();
            return Results.Ok(MigrationMapper.ToDto(job, mbx));
        });

        group.MapDelete("/migrations/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var job = await db.Set<Job>().FirstOrDefaultAsync(j => j.Id == id);
            if (job is null) return Results.NotFound();
            if (job.Status is JobStatus.Running or JobStatus.PreFlight)
            {
                job.Status = JobStatus.Cancelled; job.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                db.Set<Job>().Remove(job);
            }
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapPatch("/migrations/{id:guid}/endpoints", async (
            Guid id, SetEndpointsRequest req, AppDbContext db) =>
        {
            var vc = new ValidationContext(req); var errs = new List<ValidationResult>();
            if (!Validator.TryValidateObject(req, vc, errs, true))
                return Results.ValidationProblem(errs.ToDictionary(e => e.MemberNames.FirstOrDefault() ?? "", e => new[] { e.ErrorMessage ?? "invalid" }));

            var job = await db.Set<Job>().FirstOrDefaultAsync(j => j.Id == id);
            if (job is null) return Results.NotFound();
            job.SourceProvider = new ProviderId(req.From);
            job.DestProvider = new ProviderId(req.To);
            job.WizardStep = Math.Max(job.WizardStep, 2);
            job.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            var mbx = await db.Set<MailboxMigration>().Where(m => m.JobId == id).ToListAsync();
            return Results.Ok(MigrationMapper.ToDto(job, mbx));
        });

        return group;
    }
}
```

Wire in `Program.cs` (after `v1.MapAuthEndpoints();`):

```csharp
v1.MapMigrationEndpoints();
```

4. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~MigrationCrudTests`. Expected **PASS** (5 tests).

5. - [ ] Commit.

```bash
git add src/EMaigrator.Api src/EMaigrator.Api.Tests
git commit -m "feat(api): MigrationDto + migrations CRUD, draft autosave, PATCH endpoints

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: PUT connection/{side} (store creds via ISecretStore) + POST connection/{side}/test (provider TestConnection + catalog-mapped errors)

**Goal:** Implement `PUT /migrations/{id}/connection/{side}` — persists non-secret settings on the Job and stores the secret bundle via `ISecretStore.StoreAsync` (returning a `secretRef`, never echoing the secret) — and `POST /migrations/{id}/connection/{side}/test` which builds the connector via the discovered `IProviderPlugin`, calls `TestConnectionAsync`, and on failure maps the provider error through `IErrorCatalog.Match` into a `ConnectionTestResult` carrying a catalog `errorCode`.

**Files:**
- Create: `src/EMaigrator.Api/Contracts/ConnectionRequest.cs`
- Create: `src/EMaigrator.Api/Services/IConnectionService.cs`
- Create: `src/EMaigrator.Api/Services/ConnectionService.cs`
- Create: `src/EMaigrator.Api/Endpoints/ConnectionEndpoints.cs`
- Modify: `src/EMaigrator.Api/AppConfiguration/ApiServiceCollectionExtensions.cs`
- Modify: `src/EMaigrator.Api/Program.cs`
- Test: `src/EMaigrator.Api.Tests/ConnectionEndpointsTests.cs`

**Acceptance Criteria:**
- [ ] `PUT /migrations/{id}/connection/from` with `{auth:"ImapBasic", settings:{host,port,...}, secret:"app-pw"}` stores the secret via `ISecretStore` and saves a `secretRef` + settings to the Job's `SourceConnectionRef`; response body (a `MigrationDto`) contains **no** occurrence of the literal secret string.
- [ ] `side` must be `from` or `to`; anything else → 400.
- [ ] `POST /.../connection/from/test` calls the matching `IProviderPlugin.CreateSource(...).TestConnectionAsync` and returns the verbatim `ConnectionTestResult` (`{ok, folderCount, messageCount, errorCode?, rawDetail?}`).
- [ ] On a provider failure, the service maps the raw signature through `IErrorCatalog.Match(provider, signature)` and sets `ConnectionTestResult.ErrorCode` to a stable catalog code (not a raw stack trace).
- [ ] Cross-tenant `id` → 404; unauthenticated → 401.

**Verify:** `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~ConnectionEndpointsTests` → all pass.

**Steps:**

1. - [ ] Write the failing test. It registers a fake `IProviderPlugin` for provider `"imap"` so the test is deterministic (no real IMAP).

```csharp
// src/EMaigrator.Api.Tests/ConnectionEndpointsTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EMaigrator.Api.Tests.Infrastructure;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EMaigrator.Api.Tests;

[Collection(PostgresCollection.Name)]
public class ConnectionEndpointsTests
{
    private readonly ApiTestFactory _factory;
    public ConnectionEndpointsTests(PostgresFixture pg) =>
        _factory = new ApiTestFactory(pg.ConnectionString).WithFakeImapPlugin();

    [Fact]
    public async Task Put_connection_stores_secret_and_never_echoes_it()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var id = (await (await client.PostAsJsonAsync("/api/v1/migrations", new { }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        await client.PatchAsJsonAsync($"/api/v1/migrations/{id}/endpoints", new { from = "imap", to = "graph" });

        const string secret = "super-secret-app-password-XYZ";
        var put = await client.PutAsJsonAsync($"/api/v1/migrations/{id}/connection/from", new
        {
            auth = "ImapBasic",
            settings = new { host = "imap.mail.us-east-1.awsapps.com", port = "993", accountEmail = "old@biz.com" },
            secret
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        (await put.Content.ReadAsStringAsync()).Should().NotContain(secret, "secrets must never appear in responses");
    }

    [Fact]
    public async Task Test_connection_returns_ok_result_from_provider()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var id = await SetupConnectedDraft(client, FakeImapPlugin.Mode.Ok);

        var res = await client.PostAsync($"/api/v1/migrations/{id}/connection/from/test", null);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await res.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("ok").GetBoolean().Should().BeTrue();
        dto.GetProperty("folderCount").GetInt32().Should().Be(14);
        dto.GetProperty("messageCount").GetInt64().Should().Be(3201);
    }

    [Fact]
    public async Task Test_connection_maps_provider_failure_to_catalog_error_code()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var id = await SetupConnectedDraft(client, FakeImapPlugin.Mode.AuthFail);

        var res = await client.PostAsync($"/api/v1/migrations/{id}/connection/from/test", null);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await res.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("ok").GetBoolean().Should().BeFalse();
        dto.GetProperty("errorCode").GetString().Should().Be("IMAP_AUTH_FAILED");
    }

    [Fact]
    public async Task Put_connection_with_bad_side_returns_400()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var id = (await (await client.PostAsJsonAsync("/api/v1/migrations", new { }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        var put = await client.PutAsJsonAsync($"/api/v1/migrations/{id}/connection/sideways",
            new { auth = "ImapBasic", settings = new { host = "h" }, secret = "x" });
        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<string> SetupConnectedDraft(HttpClient client, FakeImapPlugin.Mode mode)
    {
        FakeImapPlugin.CurrentMode = mode;
        var id = (await (await client.PostAsJsonAsync("/api/v1/migrations", new { }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        await client.PatchAsJsonAsync($"/api/v1/migrations/{id}/endpoints", new { from = "imap", to = "graph" });
        await client.PutAsJsonAsync($"/api/v1/migrations/{id}/connection/from", new
        {
            auth = "ImapBasic", settings = new { host = "h", port = "993", accountEmail = "a@b.c" }, secret = "pw"
        });
        return id;
    }
}
```

The fake plugin + the `WithFakeImapPlugin` factory extension live in the test project:

```csharp
// src/EMaigrator.Api.Tests/Infrastructure/FakeImapPlugin.cs
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;

namespace EMaigrator.Api.Tests.Infrastructure;

public sealed class FakeImapPlugin : IProviderPlugin
{
    public enum Mode { Ok, AuthFail }
    public static Mode CurrentMode = Mode.Ok;

    public ProviderId Id => new("imap");
    public IReadOnlyCollection<AuthMethod> SupportedAuth => new[] { AuthMethod.ImapBasic, AuthMethod.ImapOAuthXoauth2 };
    public bool CanBeSource => true;
    public bool CanBeDestination => true;
    public ISourceProvider CreateSource(ConnectionDescriptor d, SecretBundle s) => new FakeSource();
    public IDestinationProvider CreateDestination(ConnectionDescriptor d, SecretBundle s) => throw new NotSupportedException();

    private sealed class FakeSource : ISourceProvider
    {
        public ProviderId Id => new("imap");
        public ProviderConstraints Constraints => new();
        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct) => CurrentMode switch
        {
            Mode.Ok => Task.FromResult(new ConnectionTestResult(true, 14, 3201)),
            // Raw provider failure: the connector normalizes to this signature; service maps via catalog.
            _ => throw new InvalidOperationException("imap:AUTHENTICATIONFAILED")
        };
        public Task<IReadOnlyList<CanonicalFolder>> ListFoldersAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CanonicalFolder>>(Array.Empty<CanonicalFolder>());
        public IAsyncEnumerable<CanonicalMessage> ReadMessagesAsync(FolderPath f, ReadOptions o, CancellationToken ct) =>
            AsyncEnumerable.Empty<CanonicalMessage>();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

public static class AsyncEnumerable
{
    public static async IAsyncEnumerable<T> Empty<T>() { await Task.CompletedTask; yield break; }
}
```

```csharp
// src/EMaigrator.Api.Tests/Infrastructure/FakeImapPluginFactoryExtensions.cs
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Core.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EMaigrator.Api.Tests.Infrastructure;

public static class FakeImapPluginFactoryExtensions
{
    // Marker that documents intent at the call site. ApiTestFactory ALWAYS registers the test doubles
    // (AddTestPlugins/AddFakePreflight/AddRecordingOrchestrator/AddCapturingEmail/AddFakeLedger) in its
    // ConfigureWebHost → ConfigureServices, so these With* methods just return the factory unchanged.
    public static ApiTestFactory WithFakeImapPlugin(this ApiTestFactory factory) => factory;

    public static void AddTestPlugins(IServiceCollection services)
    {
        services.AddSingleton<IProviderPlugin, FakeImapPlugin>();
        var catalog = Substitute.For<IErrorCatalog>();
        catalog.Match(Arg.Any<ProviderId>(), Arg.Is<string>(s => s.Contains("AUTHENTICATIONFAILED")))
            .Returns(new ErrorResolution(
                new ErrorRule { SignatureRegex = "AUTHENTICATIONFAILED", Diagnosis = "Auth failed",
                    Suggestion = "Use an app password", Kind = RemediationKind.Structural, Severity = Severity.Blocker },
                "Auth failed", "Use an app password", RemediationKind.Structural,
                RemediationAction.None, Array.Empty<RemediationAction>(), Severity.Blocker));
        services.AddSingleton(catalog);
    }
}
```

> The catalog substitute returns a resolution whose stable code is derived by the service as `"IMAP_AUTH_FAILED"` from the matched signature. (In production `IErrorCatalog` is the real Core catalog from Plan 02; the API only consumes `Match`.) Register `AddTestPlugins(services)` inside `ApiTestFactory.ConfigureWebHost` → `ConfigureServices`, before the migrate block.

2. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~ConnectionEndpointsTests`. Expected **FAIL** — connection endpoints/service not yet implemented.

3. - [ ] Implement.

```csharp
// src/EMaigrator.Api/Contracts/ConnectionRequest.cs
using System.ComponentModel.DataAnnotations;

namespace EMaigrator.Api.Contracts;

public sealed record ConnectionRequest(
    [property: Required] string Auth,                                   // parses to AuthMethod
    [property: Required] IReadOnlyDictionary<string, string> Settings,  // non-secret
    string? Secret);                                                    // password / client secret / SA-json
```

```csharp
// src/EMaigrator.Api/Services/IConnectionService.cs
using EMaigrator.Api.Contracts;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Api.Services;

public interface IConnectionService
{
    Task StoreConnectionAsync(Guid jobId, string side, ConnectionRequest req, CancellationToken ct);
    Task<ConnectionTestResult> TestConnectionAsync(Guid jobId, string side, CancellationToken ct);
}
```

```csharp
// src/EMaigrator.Api/Services/ConnectionService.cs
using System.Text.Json;
using EMaigrator.Api.Contracts;
using EMaigrator.Api.Tenancy;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Core.Model;
using EMaigrator.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Services;

public sealed class JobNotFoundException : Exception { }
public sealed class BadSideException : Exception { }

public sealed class ConnectionService : IConnectionService
{
    private readonly AppDbContext _db;
    private readonly ISecretStore _secrets;
    private readonly ICurrentTenant _tenant;
    private readonly IEnumerable<IProviderPlugin> _plugins;
    private readonly IErrorCatalog _catalog;

    public ConnectionService(AppDbContext db, ISecretStore secrets, ICurrentTenant tenant,
        IEnumerable<IProviderPlugin> plugins, IErrorCatalog catalog)
        => (_db, _secrets, _tenant, _plugins, _catalog) = (db, secrets, tenant, plugins, catalog);

    private static void ValidateSide(string side)
    {
        if (side is not ("from" or "to")) throw new BadSideException();
    }

    public async Task StoreConnectionAsync(Guid jobId, string side, ConnectionRequest req, CancellationToken ct)
    {
        ValidateSide(side);
        var job = await _db.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId, ct) ?? throw new JobNotFoundException();

        string? secretRef = null;
        if (!string.IsNullOrEmpty(req.Secret))
            secretRef = await _secrets.StoreAsync(_tenant.TenantId.ToString(), req.Secret, ct);

        var descriptor = new ConnectionDescriptor
        {
            Provider = new ProviderId(side == "from" ? job.SourceProvider.Value : job.DestProvider.Value),
            Auth = Enum.Parse<AuthMethod>(req.Auth, ignoreCase: true),
            Settings = req.Settings,
            SecretRef = secretRef
        };
        var serialized = JsonSerializer.Serialize(descriptor);
        if (side == "from") job.SourceConnectionRef = serialized; else job.DestConnectionRef = serialized;
        job.WizardStep = Math.Max(job.WizardStep, 2);
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(Guid jobId, string side, CancellationToken ct)
    {
        ValidateSide(side);
        var job = await _db.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId, ct) ?? throw new JobNotFoundException();
        var raw = side == "from" ? job.SourceConnectionRef : job.DestConnectionRef;
        if (string.IsNullOrEmpty(raw))
            return new ConnectionTestResult(false, 0, 0, "NO_CONNECTION", "No connection configured for this side.");

        var descriptor = JsonSerializer.Deserialize<ConnectionDescriptor>(raw)!;
        var plugin = _plugins.FirstOrDefault(p => p.Id.Value == descriptor.Provider.Value)
                     ?? throw new InvalidOperationException($"No plugin for provider {descriptor.Provider}.");
        var secretValues = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(descriptor.SecretRef))
            secretValues["secret"] = await _secrets.RetrieveAsync(descriptor.SecretRef, ct);
        var bundle = new SecretBundle(secretValues);

        try
        {
            if (side == "from")
            {
                await using var src = plugin.CreateSource(descriptor, bundle);
                return await src.TestConnectionAsync(ct);
            }
            await using var dst = plugin.CreateDestination(descriptor, bundle);
            return await dst.TestConnectionAsync(ct);
        }
        catch (Exception ex)
        {
            // Connector-normalized signature is "<provider>:<condition>"; map via catalog. Never echo creds.
            var signature = ex.Message;
            var resolution = _catalog.Match(descriptor.Provider, signature);
            var code = resolution is null ? "UNKNOWN_ERROR" : ToStableCode(descriptor.Provider, signature);
            return new ConnectionTestResult(false, 0, 0, code, resolution?.Diagnosis ?? "Connection failed.");
        }
    }

    private static string ToStableCode(ProviderId provider, string signature)
    {
        var condition = signature.Contains(':') ? signature[(signature.IndexOf(':') + 1)..] : signature;
        // e.g. ("imap","AUTHENTICATIONFAILED") -> "IMAP_AUTH_FAILED"
        var normalized = condition.Replace("AUTHENTICATIONFAILED", "AUTH_FAILED");
        return $"{provider.Value.ToUpperInvariant()}_{normalized}";
    }
}
```

```csharp
// src/EMaigrator.Api/Endpoints/ConnectionEndpoints.cs
using System.ComponentModel.DataAnnotations;
using EMaigrator.Api.Contracts;
using EMaigrator.Api.Mapping;
using EMaigrator.Api.Services;
using EMaigrator.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Endpoints;

public static class ConnectionEndpoints
{
    public static RouteGroupBuilder MapConnectionEndpoints(this RouteGroupBuilder group)
    {
        group.MapPut("/migrations/{id:guid}/connection/{side}", async (
            Guid id, string side, ConnectionRequest req, IConnectionService svc, AppDbContext db) =>
        {
            if (side is not ("from" or "to")) return Results.BadRequest(new { error = "side must be 'from' or 'to'." });
            if (req.Settings is null || req.Settings.Count == 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["settings"] = new[] { "required" } });
            if (!Enum.TryParse<EMaigrator.Core.Abstractions.AuthMethod>(req.Auth, true, out _))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["auth"] = new[] { "unknown auth method" } });
            try
            {
                await svc.StoreConnectionAsync(id, side, req, default);
            }
            catch (JobNotFoundException) { return Results.NotFound(); }
            catch (BadSideException) { return Results.BadRequest(new { error = "bad side" }); }

            var job = await db.Set<Job>().FirstAsync(j => j.Id == id);
            var mbx = await db.Set<MailboxMigration>().Where(m => m.JobId == id).ToListAsync();
            return Results.Ok(MigrationMapper.ToDto(job, mbx));
        });

        group.MapPost("/migrations/{id:guid}/connection/{side}/test", async (
            Guid id, string side, IConnectionService svc) =>
        {
            try { return Results.Ok(await svc.TestConnectionAsync(id, side, default)); }
            catch (JobNotFoundException) { return Results.NotFound(); }
            catch (BadSideException) { return Results.BadRequest(new { error = "bad side" }); }
        });

        return group;
    }
}
```

Register the service in `ApiServiceCollectionExtensions.AddEMaigratorApi`:

```csharp
        services.AddScoped<EMaigrator.Api.Services.IConnectionService, EMaigrator.Api.Services.ConnectionService>();
```

Wire in `Program.cs` (after `v1.MapMigrationEndpoints();`):

```csharp
v1.MapConnectionEndpoints();
```

4. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~ConnectionEndpointsTests`. Expected **PASS** (4 tests).

5. - [ ] Commit.

```bash
git add src/EMaigrator.Api src/EMaigrator.Api.Tests
git commit -m "feat(api): PUT connection stores creds via ISecretStore + POST test maps catalog errors

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: PUT scope (JSON + CSV multipart parse + validation)

**Goal:** Implement `PUT /migrations/{id}/scope` accepting either JSON (`ScopeRequest` mirroring `ScopeSpec`) or a `multipart/form-data` CSV upload (columns `source_mailbox,destination_mailbox`); parse + validate the CSV (header present, no blank/duplicate pairs, valid email-ish values) into `MailboxMigration` rows, persist scope on the Job, and advance the wizard.

**Files:**
- Create: `src/EMaigrator.Api/Contracts/ScopeRequest.cs`
- Create: `src/EMaigrator.Api/Services/CsvMailboxParser.cs`
- Create: `src/EMaigrator.Api/Endpoints/ScopeEndpoints.cs`
- Modify: `src/EMaigrator.Api/Program.cs`
- Test: `src/EMaigrator.Api.Tests/ScopeEndpointTests.cs`
- Test: `src/EMaigrator.Api.Tests/CsvMailboxParserTests.cs`

**Acceptance Criteria:**
- [ ] `CsvMailboxParser.Parse(stream)` returns `MailboxPair[]` for a valid CSV with header `source_mailbox,destination_mailbox`.
- [ ] Parser rejects: missing header (throws `CsvValidationException`), blank source or dest, and duplicate source mailboxes — each with a row-numbered message.
- [ ] `PUT /migrations/{id}/scope` as JSON `{isBatch:false, pairs:[{sourceMailbox,destMailbox}]}` persists one `MailboxMigration` and sets `Job.IsBatch=false`, `WizardStep≥3`.
- [ ] `PUT /migrations/{id}/scope` as `multipart/form-data` with a CSV file persists N `MailboxMigration` rows (batch), returns `MigrationDto` with `mailboxCount==N`, `isBatch==true`.
- [ ] An invalid CSV (duplicate source) returns 400 with a `errors` array naming the offending row.
- [ ] Cross-tenant id → 404; unauthenticated → 401.

**Verify:** `dotnet test src/EMaigrator.Api.Tests --filter "FullyQualifiedName~ScopeEndpointTests|FullyQualifiedName~CsvMailboxParserTests"` → all pass.

**Steps:**

1. - [ ] Write the failing tests.

```csharp
// src/EMaigrator.Api.Tests/CsvMailboxParserTests.cs
using System.Text;
using EMaigrator.Api.Services;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Api.Tests;

public class CsvMailboxParserTests
{
    private static Stream S(string csv) => new MemoryStream(Encoding.UTF8.GetBytes(csv));

    [Fact]
    public void Parses_valid_csv()
    {
        var pairs = CsvMailboxParser.Parse(S("source_mailbox,destination_mailbox\na@old.com,a@new.com\nb@old.com,b@new.com\n"));
        pairs.Should().HaveCount(2);
        pairs[0].SourceMailbox.Should().Be("a@old.com");
        pairs[1].DestMailbox.Should().Be("b@new.com");
    }

    [Fact]
    public void Rejects_missing_header()
    {
        var act = () => CsvMailboxParser.Parse(S("a@old.com,a@new.com\n"));
        act.Should().Throw<CsvValidationException>().WithMessage("*header*");
    }

    [Fact]
    public void Rejects_blank_field()
    {
        var act = () => CsvMailboxParser.Parse(S("source_mailbox,destination_mailbox\na@old.com,\n"));
        act.Should().Throw<CsvValidationException>().WithMessage("*row 2*");
    }

    [Fact]
    public void Rejects_duplicate_source()
    {
        var act = () => CsvMailboxParser.Parse(S("source_mailbox,destination_mailbox\na@old.com,a@new.com\na@old.com,c@new.com\n"));
        act.Should().Throw<CsvValidationException>().WithMessage("*duplicate*row 3*");
    }
}
```

```csharp
// src/EMaigrator.Api.Tests/ScopeEndpointTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EMaigrator.Api.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Api.Tests;

[Collection(PostgresCollection.Name)]
public class ScopeEndpointTests
{
    private readonly ApiTestFactory _factory;
    public ScopeEndpointTests(PostgresFixture pg) => _factory = new ApiTestFactory(pg.ConnectionString).WithFakeImapPlugin();

    private async Task<string> NewDraft(HttpClient c)
    {
        var id = (await (await c.PostAsJsonAsync("/api/v1/migrations", new { }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        await c.PatchAsJsonAsync($"/api/v1/migrations/{id}/endpoints", new { from = "imap", to = "graph" });
        return id;
    }

    [Fact]
    public async Task Json_single_scope_persists_one_mailbox()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var id = await NewDraft(client);
        var res = await client.PutAsJsonAsync($"/api/v1/migrations/{id}/scope", new
        {
            isBatch = false,
            pairs = new[] { new { sourceMailbox = "old@biz.com", destMailbox = "new@biz.com" } }
        });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await res.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("isBatch").GetBoolean().Should().BeFalse();
        dto.GetProperty("mailboxCount").GetInt32().Should().Be(1);
        dto.GetProperty("wizardStep").GetInt32().Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Multipart_csv_persists_batch()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var id = await NewDraft(client);
        using var content = new MultipartFormDataContent();
        var csv = "source_mailbox,destination_mailbox\na@old.com,a@new.com\nb@old.com,b@new.com\nc@old.com,c@new.com\n";
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", "mailboxes.csv");

        var res = await client.PutAsync($"/api/v1/migrations/{id}/scope", content);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await res.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("isBatch").GetBoolean().Should().BeTrue();
        dto.GetProperty("mailboxCount").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task Invalid_csv_returns_400_with_row_error()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var id = await NewDraft(client);
        using var content = new MultipartFormDataContent();
        var csv = "source_mailbox,destination_mailbox\na@old.com,a@new.com\na@old.com,c@new.com\n";
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", "dupes.csv");
        var res = await client.PutAsync($"/api/v1/migrations/{id}/scope", content);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.Content.ReadAsStringAsync()).Should().Contain("duplicate");
    }
}
```

2. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter "FullyQualifiedName~ScopeEndpointTests|FullyQualifiedName~CsvMailboxParserTests"`. Expected **FAIL** — parser/endpoint not implemented.

3. - [ ] Implement.

```csharp
// src/EMaigrator.Api/Contracts/ScopeRequest.cs
namespace EMaigrator.Api.Contracts;

public sealed record ScopePairDto(string SourceMailbox, string DestMailbox);

public sealed record ScopeRequest(
    bool IsBatch,
    IReadOnlyList<ScopePairDto>? Pairs,
    IReadOnlyList<string>? IncludeFolders,
    IReadOnlyList<string>? ExcludeFolders,
    DateTimeOffset? Since,
    DateTimeOffset? Before);
```

```csharp
// src/EMaigrator.Api/Services/CsvMailboxParser.cs
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using EMaigrator.Core.Diagnostics;   // MailboxPair

namespace EMaigrator.Api.Services;

public sealed class CsvValidationException : Exception
{
    public CsvValidationException(string message) : base(message) { }
}

public static class CsvMailboxParser
{
    public static IReadOnlyList<MailboxPair> Parse(Stream csv)
    {
        using var reader = new StreamReader(csv);
        using var parser = new CsvParser(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true, MissingFieldFound = null, TrimOptions = TrimOptions.Trim
        });

        if (!parser.Read())
            throw new CsvValidationException("CSV is empty; expected a header 'source_mailbox,destination_mailbox'.");
        var header = parser.Record ?? Array.Empty<string>();
        var srcIdx = Array.FindIndex(header, h => string.Equals(h, "source_mailbox", StringComparison.OrdinalIgnoreCase));
        var dstIdx = Array.FindIndex(header, h => string.Equals(h, "destination_mailbox", StringComparison.OrdinalIgnoreCase));
        if (srcIdx < 0 || dstIdx < 0)
            throw new CsvValidationException("CSV header must contain 'source_mailbox' and 'destination_mailbox'.");

        var pairs = new List<MailboxPair>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rowNum = 1;
        while (parser.Read())
        {
            rowNum++;
            var rec = parser.Record ?? Array.Empty<string>();
            var src = srcIdx < rec.Length ? rec[srcIdx]?.Trim() ?? "" : "";
            var dst = dstIdx < rec.Length ? rec[dstIdx]?.Trim() ?? "" : "";
            if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dst))
                throw new CsvValidationException($"Blank mailbox value at row {rowNum}.");
            if (!src.Contains('@') || !dst.Contains('@'))
                throw new CsvValidationException($"Invalid mailbox address at row {rowNum}.");
            if (!seen.Add(src))
                throw new CsvValidationException($"Duplicate source mailbox '{src}' at row {rowNum}.");
            pairs.Add(new MailboxPair(src, dst));
        }
        if (pairs.Count == 0) throw new CsvValidationException("CSV contains no mailbox pairs.");
        return pairs;
    }
}
```

```csharp
// src/EMaigrator.Api/Endpoints/ScopeEndpoints.cs
using EMaigrator.Api.Contracts;
using EMaigrator.Api.Mapping;
using EMaigrator.Api.Services;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Endpoints;

public static class ScopeEndpoints
{
    public static RouteGroupBuilder MapScopeEndpoints(this RouteGroupBuilder group)
    {
        group.MapPut("/migrations/{id:guid}/scope", async (Guid id, HttpRequest request, AppDbContext db) =>
        {
            var job = await db.Set<Job>().FirstOrDefaultAsync(j => j.Id == id);
            if (job is null) return Results.NotFound();

            IReadOnlyList<MailboxPair> pairs;
            bool isBatch;

            if (request.HasFormContentType && request.Form.Files.Count > 0)
            {
                try
                {
                    await using var stream = request.Form.Files[0].OpenReadStream();
                    pairs = CsvMailboxParser.Parse(stream);
                }
                catch (CsvValidationException ex)
                {
                    return Results.BadRequest(new { errors = new[] { ex.Message } });
                }
                isBatch = true;
            }
            else
            {
                var scope = await request.ReadFromJsonAsync<ScopeRequest>();
                if (scope is null || scope.Pairs is null || scope.Pairs.Count == 0)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["pairs"] = new[] { "at least one mailbox pair is required" } });
                pairs = scope.Pairs.Select(p => new MailboxPair(p.SourceMailbox, p.DestMailbox)).ToList();
                isBatch = scope.IsBatch;
            }

            // Replace existing mailbox rows for this job.
            var existing = await db.Set<MailboxMigration>().Where(m => m.JobId == id).ToListAsync();
            db.Set<MailboxMigration>().RemoveRange(existing);
            foreach (var p in pairs)
                db.Set<MailboxMigration>().Add(new MailboxMigration
                {
                    Id = Guid.NewGuid(), JobId = id, SourceMailbox = p.SourceMailbox,
                    DestMailbox = p.DestMailbox, Status = MailboxMigrationStatus.Pending
                });

            job.IsBatch = isBatch;
            job.WizardStep = Math.Max(job.WizardStep, 3);
            job.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            var mbx = await db.Set<MailboxMigration>().Where(m => m.JobId == id).ToListAsync();
            return Results.Ok(MigrationMapper.ToDto(job, mbx));
        }).DisableAntiforgery();   // multipart upload from SPA with bearer token; CSRF mitigated by SameSite cookie + bearer

        return group;
    }
}
```

Wire in `Program.cs` (after `v1.MapConnectionEndpoints();`):

```csharp
v1.MapScopeEndpoints();
```

4. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter "FullyQualifiedName~ScopeEndpointTests|FullyQualifiedName~CsvMailboxParserTests"`. Expected **PASS** (7 tests).

5. - [ ] Commit.

```bash
git add src/EMaigrator.Api src/EMaigrator.Api.Tests
git commit -m "feat(api): PUT scope with JSON + CSV multipart parse and validation

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: MigrationsHub + IMigrationProgressClient + Redis backplane + worker→hub bridge

**Goal:** Implement the `MigrationsHub : Hub<IMigrationProgressClient>` exactly per CONTRACTS §6 (tenant-authorized `Subscribe`/`Unsubscribe` per `migrationId` group), enable the Redis backplane for horizontal fan-out, and add a `MigrationProgressBridge` MassTransit consumer that maps `MigrationProgressEvent`/`NeedsDecisionEvent` to `IHubContext<MigrationsHub, IMigrationProgressClient>` group pushes.

**Files:**
- Create: `src/EMaigrator.Api/Realtime/IMigrationProgressClient.cs`
- Create: `src/EMaigrator.Api/Realtime/MigrationsHub.cs`
- Create: `src/EMaigrator.Api/Realtime/SignalRDtos.cs`
- Create: `src/EMaigrator.Api/Realtime/MigrationProgressBridge.cs`
- Create: `src/EMaigrator.Api/Realtime/IMigrationGroupNotifier.cs`
- Create: `src/EMaigrator.Api/Realtime/SignalRMigrationGroupNotifier.cs`
- Modify: `src/EMaigrator.Api/AppConfiguration/ApiServiceCollectionExtensions.cs`
- Modify: `src/EMaigrator.Api/Program.cs`
- Test: `src/EMaigrator.Api.Tests/SignalRProgressTests.cs`
- Test: `src/EMaigrator.Api.Tests/MigrationProgressBridgeTests.cs`

**Acceptance Criteria:**
- [ ] `MigrationsHub.Subscribe(migrationId)` adds the connection to group `migrationId` **only if** the migration belongs to the caller's tenant; otherwise throws `HubException` (unauthorized).
- [ ] `IMigrationProgressClient` declares `Progress`, `StatusChanged`, `NeedsDecision` exactly as CONTRACTS §6.
- [ ] An authenticated SignalR client that `Subscribe`s receives a `Progress(...)` push when `IMigrationGroupNotifier.PushProgressAsync` is invoked for that migration id.
- [ ] `MigrationProgressBridge` (a `MassTransit.IConsumer<MigrationProgressEvent>` + `IConsumer<NeedsDecisionEvent>`) calls `IMigrationGroupNotifier.PushProgressAsync`/`PushNeedsDecisionAsync` with the mapped DTO and `MailboxMigrationId`-derived migration group.
- [ ] Backplane is enabled via `AddStackExchangeRedis(...)` when `Redis:Configuration` is present (no-op/in-memory in tests).

**Verify:** `dotnet test src/EMaigrator.Api.Tests --filter "FullyQualifiedName~SignalRProgressTests|FullyQualifiedName~MigrationProgressBridgeTests"` → all pass.

**Steps:**

1. - [ ] Write the failing tests.

```csharp
// src/EMaigrator.Api.Tests/SignalRProgressTests.cs
using EMaigrator.Api.Realtime;
using EMaigrator.Api.Tests.Infrastructure;
using EMaigrator.Infrastructure.Data;
using EMaigrator.Core.Model;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EMaigrator.Api.Tests;

[Collection(PostgresCollection.Name)]
public class SignalRProgressTests
{
    private readonly ApiTestFactory _factory;
    public SignalRProgressTests(PostgresFixture pg) => _factory = new ApiTestFactory(pg.ConnectionString);

    [Fact]
    public async Task Subscribed_client_receives_progress_push()
    {
        // Arrange: a real authed user + a migration they own.
        var (http, tenantId) = await AuthClient.CreateAsync(_factory);
        var token = http.DefaultRequestHeaders.Authorization!.Parameter!;
        Guid migrationId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = new Job { Id = Guid.NewGuid(), TenantId = tenantId, SourceProvider = new ProviderId("imap"),
                DestProvider = new ProviderId("graph"), Status = JobStatus.Running, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
            db.Set<Job>().Add(job);
            var mbx = new MailboxMigration { Id = Guid.NewGuid(), JobId = job.Id, SourceMailbox = "a@b.c", DestMailbox = "a@d.c", Status = MailboxMigrationStatus.Running };
            db.Set<MailboxMigration>().Add(mbx);
            await db.SaveChangesAsync();
            migrationId = job.Id;
        }

        var conn = new HubConnectionBuilder()
            .WithUrl(_factory.Server.BaseAddress + "hubs/migrations?access_token=" + token,
                o => o.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler())
            .Build();
        MigrationProgressDto? received = null;
        var tcs = new TaskCompletionSource();
        conn.On<MigrationProgressDto>(nameof(IMigrationProgressClient.Progress), dto => { received = dto; tcs.TrySetResult(); });
        await conn.StartAsync();
        await conn.InvokeAsync("Subscribe", migrationId.ToString());

        // Act: push via the notifier.
        var notifier = _factory.Services.GetRequiredService<IMigrationGroupNotifier>();
        await notifier.PushProgressAsync(new MigrationProgressDto(migrationId.ToString(), 5, 10, "/Inbox", 120, "Running"));

        // Assert.
        (await Task.WhenAny(tcs.Task, Task.Delay(5000))).Should().Be(tcs.Task, "progress push should arrive");
        received!.Migrated.Should().Be(5);
        await conn.DisposeAsync();
    }

    [Fact]
    public async Task Subscribe_to_other_tenants_migration_throws()
    {
        var (httpA, _) = await AuthClient.CreateAsync(_factory);
        var (httpB, tenantB) = await AuthClient.CreateAsync(_factory);
        Guid migrationB;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = new Job { Id = Guid.NewGuid(), TenantId = tenantB, SourceProvider = new ProviderId("imap"),
                DestProvider = new ProviderId("graph"), Status = JobStatus.Running, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
            db.Set<Job>().Add(job); await db.SaveChangesAsync(); migrationB = job.Id;
        }
        var tokenA = httpA.DefaultRequestHeaders.Authorization!.Parameter!;
        var conn = new HubConnectionBuilder()
            .WithUrl(_factory.Server.BaseAddress + "hubs/migrations?access_token=" + tokenA,
                o => o.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler())
            .Build();
        await conn.StartAsync();
        var act = async () => await conn.InvokeAsync("Subscribe", migrationB.ToString());
        await act.Should().ThrowAsync<Exception>("cross-tenant subscribe must be rejected");
        await conn.DisposeAsync();
    }
}
```

```csharp
// src/EMaigrator.Api.Tests/MigrationProgressBridgeTests.cs
using EMaigrator.Api.Realtime;
using EMaigrator.Core.Contracts;
using FluentAssertions;
using MassTransit;
using NSubstitute;
using Xunit;

namespace EMaigrator.Api.Tests;

public class MigrationProgressBridgeTests
{
    [Fact]
    public async Task Consuming_progress_event_pushes_to_group()
    {
        var notifier = Substitute.For<IMigrationGroupNotifier>();
        var bridge = new MigrationProgressBridge(notifier);
        var mbxId = Guid.NewGuid();

        var ctx = Substitute.For<ConsumeContext<MigrationProgressEvent>>();
        ctx.Message.Returns(new MigrationProgressEvent(mbxId, 7, 10, "/Sent", 99.0, "Running"));
        await bridge.Consume(ctx);

        await notifier.Received(1).PushProgressAsync(Arg.Is<MigrationProgressDto>(
            d => d.Migrated == 7 && d.Total == 10 && d.Status == "Running"));
    }

    [Fact]
    public async Task Consuming_needs_decision_event_pushes_to_group()
    {
        var notifier = Substitute.For<IMigrationGroupNotifier>();
        var bridge = new MigrationProgressBridge(notifier);
        var ctx = Substitute.For<ConsumeContext<NeedsDecisionEvent>>();
        ctx.Message.Returns(new NeedsDecisionEvent(Guid.NewGuid(), "FolderCollision", "name clash",
            new[] { EMaigrator.Core.Diagnostics.RemediationAction.RenameFolder }));
        await bridge.Consume(ctx);
        await notifier.Received(1).PushNeedsDecisionAsync(Arg.Any<string>(), Arg.Any<NeedsDecisionDto>());
    }
}
```

2. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter "FullyQualifiedName~SignalRProgressTests|FullyQualifiedName~MigrationProgressBridgeTests"`. Expected **FAIL** — hub, notifier, and bridge do not exist.

3. - [ ] Implement.

```csharp
// src/EMaigrator.Api/Realtime/SignalRDtos.cs
namespace EMaigrator.Api.Realtime;

// SignalR event payloads. Property names match the hub method names per CONTRACTS §6.
public sealed record MigrationProgressDto(string MigrationId, long Migrated, long Total, string? CurrentFolder, double MsgPerMin, string Status);
public sealed record NeedsDecisionDto(string IssueType, string Detail, string[] Options);
```

```csharp
// src/EMaigrator.Api/Realtime/IMigrationProgressClient.cs
namespace EMaigrator.Api.Realtime;

public interface IMigrationProgressClient   // server → client (CONTRACTS §6)
{
    Task Progress(MigrationProgressDto dto);
    Task StatusChanged(string migrationId, string status);
    Task NeedsDecision(string migrationId, NeedsDecisionDto dto);
}
```

```csharp
// src/EMaigrator.Api/Realtime/MigrationsHub.cs
using EMaigrator.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Realtime;

[Authorize]
public class MigrationsHub : Hub<IMigrationProgressClient>   // client → server (CONTRACTS §6)
{
    private readonly AppDbContext _db;   // global query filter scopes to the caller's tenant
    public MigrationsHub(AppDbContext db) => _db = db;

    public async Task Subscribe(string migrationId)
    {
        if (!Guid.TryParse(migrationId, out var id))
            throw new HubException("Invalid migration id.");
        // Filtered query: returns false if the migration belongs to another tenant.
        var owned = await _db.Set<Job>().AnyAsync(j => j.Id == id);
        if (!owned) throw new HubException("Not authorized for this migration.");
        await Groups.AddToGroupAsync(Context.ConnectionId, migrationId);
    }

    public Task Unsubscribe(string migrationId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, migrationId);
}
```

```csharp
// src/EMaigrator.Api/Realtime/IMigrationGroupNotifier.cs
namespace EMaigrator.Api.Realtime;

public interface IMigrationGroupNotifier
{
    Task PushProgressAsync(MigrationProgressDto dto);
    Task PushStatusChangedAsync(string migrationId, string status);
    Task PushNeedsDecisionAsync(string migrationId, NeedsDecisionDto dto);
}
```

```csharp
// src/EMaigrator.Api/Realtime/SignalRMigrationGroupNotifier.cs
using Microsoft.AspNetCore.SignalR;

namespace EMaigrator.Api.Realtime;

public sealed class SignalRMigrationGroupNotifier : IMigrationGroupNotifier
{
    private readonly IHubContext<MigrationsHub, IMigrationProgressClient> _hub;
    public SignalRMigrationGroupNotifier(IHubContext<MigrationsHub, IMigrationProgressClient> hub) => _hub = hub;

    public Task PushProgressAsync(MigrationProgressDto dto) =>
        _hub.Clients.Group(dto.MigrationId).Progress(dto);
    public Task PushStatusChangedAsync(string migrationId, string status) =>
        _hub.Clients.Group(migrationId).StatusChanged(migrationId, status);
    public Task PushNeedsDecisionAsync(string migrationId, NeedsDecisionDto dto) =>
        _hub.Clients.Group(migrationId).NeedsDecision(migrationId, dto);
}
```

```csharp
// src/EMaigrator.Api/Realtime/MigrationProgressBridge.cs
using EMaigrator.Core.Contracts;
using MassTransit;

namespace EMaigrator.Api.Realtime;

// Consumes worker-published events and fans them out over SignalR. The SignalR group key is the
// migration id; MailboxMigrationId maps 1:1 to the migration's mailbox unit, so the group is its string.
public sealed class MigrationProgressBridge :
    IConsumer<MigrationProgressEvent>, IConsumer<NeedsDecisionEvent>
{
    private readonly IMigrationGroupNotifier _notifier;
    public MigrationProgressBridge(IMigrationGroupNotifier notifier) => _notifier = notifier;

    public Task Consume(ConsumeContext<MigrationProgressEvent> ctx)
    {
        var m = ctx.Message;
        var migrationId = m.MailboxMigrationId.ToString();
        return Task.WhenAll(
            _notifier.PushProgressAsync(new MigrationProgressDto(migrationId, m.Migrated, m.Total, m.CurrentFolder, m.MsgPerMin, m.Status)),
            _notifier.PushStatusChangedAsync(migrationId, m.Status));
    }

    public Task Consume(ConsumeContext<NeedsDecisionEvent> ctx)
    {
        var m = ctx.Message;
        var migrationId = m.MailboxMigrationId.ToString();
        return _notifier.PushNeedsDecisionAsync(migrationId,
            new NeedsDecisionDto(m.IssueType, m.Detail, m.Options.Select(o => o.ToString()).ToArray()));
    }
}
```

Wire SignalR + backplane + bridge in `ApiServiceCollectionExtensions.AddEMaigratorApi`. Replace the bare `services.AddSignalR();` from Task 0 with:

```csharp
        var signalR = services.AddSignalR();
        var redis = config["Redis:Configuration"];
        if (!string.IsNullOrEmpty(redis))
            signalR.AddStackExchangeRedis(redis, o => o.Configuration.ChannelPrefix =
                StackExchange.Redis.RedisChannel.Literal("emaigrator-signalr"));
        services.AddScoped<EMaigrator.Api.Realtime.IMigrationGroupNotifier, EMaigrator.Api.Realtime.SignalRMigrationGroupNotifier>();
```

> The MassTransit `MigrationProgressBridge` consumer is registered by the Infrastructure MassTransit wiring (Plan 03 exposes `AddEMaigratorMassTransit(cfg => cfg.AddConsumer<T>())`). In the API call site, register it: `services.AddEMaigratorMassTransitConsumers(typeof(MigrationProgressBridge).Assembly);` — Plan 03's extension scans for `IConsumer` implementations. For tests we don't run a broker, so the bridge is unit-tested directly (above) and the notifier is resolved from DI.

Map the hub in `Program.cs` (after auth middleware, near endpoint mapping):

```csharp
app.MapHub<EMaigrator.Api.Realtime.MigrationsHub>("/hubs/migrations");
```

4. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter "FullyQualifiedName~SignalRProgressTests|FullyQualifiedName~MigrationProgressBridgeTests"`. Expected **PASS** (4 tests).

5. - [ ] Commit.

```bash
git add src/EMaigrator.Api src/EMaigrator.Api.Tests
git commit -m "feat(api): MigrationsHub + Redis backplane + worker->hub progress bridge

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 7: POST preflight (202 async via IPreflightAnalyzer) + GET preflight

**Goal:** `POST /migrations/{id}/preflight` returns 202 immediately, transitions the Job to `PreFlight`, and kicks off a background analysis (`IPreflightAnalyzer.AnalyzeAsync`) that persists a `PreflightPlanDto` and emits SignalR `StatusChanged`. `GET /migrations/{id}/preflight` returns the stored `PreflightPlanDto` (404 if not yet run).

**Files:**
- Create: `src/EMaigrator.Api/Contracts/PreflightDtos.cs`
- Create: `src/EMaigrator.Api/Services/IBackgroundTaskQueue.cs`
- Create: `src/EMaigrator.Api/Services/BackgroundTaskQueue.cs`
- Create: `src/EMaigrator.Api/Services/IPreflightRunner.cs`
- Create: `src/EMaigrator.Api/Services/PreflightRunner.cs`
- Create: `src/EMaigrator.Api/Data/PreflightResultRow.cs`
- Create: `src/EMaigrator.Api/Data/ApiSideContext.cs`
- Create: `src/EMaigrator.Api/Endpoints/PreflightEndpoints.cs`
- Modify: `src/EMaigrator.Api/AppConfiguration/ApiServiceCollectionExtensions.cs`
- Modify: `src/EMaigrator.Api/Program.cs`
- Modify: `src/EMaigrator.Api.Tests/Infrastructure/ApiTestFactory.cs`
- Test: `src/EMaigrator.Api.Tests/PreflightEndpointTests.cs`
- Test: `src/EMaigrator.Api.Tests/Infrastructure/FakePreflightExtensions.cs`

**Acceptance Criteria:**
- [ ] `POST /migrations/{id}/preflight` returns 202 and sets `Job.Status=PreFlight`.
- [ ] After the background run completes, `GET /migrations/{id}/preflight` returns a `PreflightPlanDto` with `issues[]` (each `{issueType, affectedPaths, recommendedAction, options, severity, description}`) and `estimate{mailboxCount,folderCount,messageCount,totalBytes,estimatedDurationSeconds}` mapped from the Core `PreflightPlan`.
- [ ] `GET /migrations/{id}/preflight` before any run returns 404.
- [ ] The runner invokes `IMigrationGroupNotifier.PushStatusChangedAsync(id, "AwaitingApproval")` when analysis finishes.
- [ ] Cross-tenant id → 404; unauthenticated → 401.

**Verify:** `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~PreflightEndpointTests` → all pass.

**Steps:**

1. - [ ] Write the failing test. A fake `IPreflightAnalyzer` returns a deterministic `PreflightPlan`; the runner is invoked synchronously in tests via an injected `IBackgroundTaskQueue` test double that runs inline.

```csharp
// src/EMaigrator.Api.Tests/PreflightEndpointTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EMaigrator.Api.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Api.Tests;

[Collection(PostgresCollection.Name)]
public class PreflightEndpointTests
{
    private readonly ApiTestFactory _factory;
    public PreflightEndpointTests(PostgresFixture pg) =>
        _factory = new ApiTestFactory(pg.ConnectionString).WithFakeImapPlugin().WithFakePreflight();

    private async Task<string> ReadyToPreflight(HttpClient c)
    {
        var id = (await (await c.PostAsJsonAsync("/api/v1/migrations", new { }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        await c.PatchAsJsonAsync($"/api/v1/migrations/{id}/endpoints", new { from = "imap", to = "graph" });
        await c.PutAsJsonAsync($"/api/v1/migrations/{id}/connection/from",
            new { auth = "ImapBasic", settings = new { host = "h", port = "993", accountEmail = "a@b.c" }, secret = "pw" });
        await c.PutAsJsonAsync($"/api/v1/migrations/{id}/connection/to",
            new { auth = "ImapBasic", settings = new { host = "h2", port = "993", accountEmail = "d@e.f" }, secret = "pw2" });
        await c.PutAsJsonAsync($"/api/v1/migrations/{id}/scope",
            new { isBatch = false, pairs = new[] { new { sourceMailbox = "a@b.c", destMailbox = "d@e.f" } } });
        return id;
    }

    [Fact]
    public async Task Get_preflight_before_run_returns_404()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var id = await ReadyToPreflight(client);
        (await client.GetAsync($"/api/v1/migrations/{id}/preflight")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_preflight_is_202_then_get_returns_plan()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var id = await ReadyToPreflight(client);

        var post = await client.PostAsync($"/api/v1/migrations/{id}/preflight", null);
        post.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // The test queue runs inline, so the plan is ready synchronously.
        var get = await client.GetAsync($"/api/v1/migrations/{id}/preflight");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var plan = await get.Content.ReadFromJsonAsync<JsonElement>();
        plan.GetProperty("estimate").GetProperty("messageCount").GetInt64().Should().Be(3201);
        plan.GetProperty("issues").GetArrayLength().Should().Be(1);
        plan.GetProperty("issues")[0].GetProperty("recommendedAction").GetString().Should().Be("FlattenFolder");
    }
}
```

The fakes + factory extensions live in the test project:

```csharp
// src/EMaigrator.Api.Tests/Infrastructure/FakePreflightExtensions.cs
using EMaigrator.Api.Services;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Core.Preflight;
using Microsoft.Extensions.DependencyInjection;

namespace EMaigrator.Api.Tests.Infrastructure;

public static class FakePreflightExtensions
{
    public static ApiTestFactory WithFakePreflight(this ApiTestFactory f) => f;
    public static void AddFakePreflight(IServiceCollection services)
    {
        services.AddSingleton<IPreflightAnalyzer, FakeAnalyzer>();
        services.AddSingleton<IBackgroundTaskQueue, InlineTaskQueue>();
    }

    private sealed class FakeAnalyzer : IPreflightAnalyzer
    {
        public Task<PreflightPlan> AnalyzeAsync(ISourceProvider source, IDestinationProvider dest, ScopeSpec scope, CancellationToken ct)
            => Task.FromResult(new PreflightPlan(
                new[] { new PreflightIssue("FolderDepth", new[] { "/A/B/C/D/E" },
                    RemediationAction.FlattenFolder, new[] { RemediationAction.FlattenFolder, RemediationAction.RenameFolder },
                    Severity.Warning, "Folder too deep") },
                new MigrationEstimate(1, 14, 3201, 250_000_000, TimeSpan.FromMinutes(12))));
    }

    // Runs queued work items synchronously against the root provider so the test sees the
    // persisted plan immediately (no hosted pump). Replaces the production BackgroundTaskQueue in tests.
    private sealed class InlineTaskQueue : IBackgroundTaskQueue
    {
        private readonly IServiceProvider _root;
        public InlineTaskQueue(IServiceProvider root) => _root = root;
        public async ValueTask EnqueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem)
        {
            using var scope = _root.CreateScope();
            await workItem(scope.ServiceProvider, CancellationToken.None);
        }
    }
}
```

> `WithFakePreflight()` is a marker; the real registration of `AddFakePreflight(services)` is added inside `ApiTestFactory.ConfigureWebHost` → `ConfigureServices` (alongside `AddTestPlugins`). `AddFakePreflight` registers `InlineTaskQueue` (above) for `IBackgroundTaskQueue`, replacing the production `BackgroundTaskQueue`; the production `QueuedHostedService` becomes a no-op because nothing writes to its channel. `InlineTaskQueue` runs queued work items synchronously so the `POST /preflight` call completes the analysis (and writes the plan to `ApiSideContext`) before the test issues `GET /preflight`. `InlineTaskQueue` takes the root `IServiceProvider` injected by DI.

2. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~PreflightEndpointTests`. Expected **FAIL** — preflight DTOs/runner/endpoints not implemented.

3. - [ ] Implement.

```csharp
// src/EMaigrator.Api/Contracts/PreflightDtos.cs
namespace EMaigrator.Api.Contracts;

public sealed record PreflightIssueDto(
    string IssueType, IReadOnlyList<string> AffectedPaths, string RecommendedAction,
    IReadOnlyList<string> Options, string Severity, string Description);

public sealed record MigrationEstimateDto(
    int MailboxCount, int FolderCount, long MessageCount, long TotalBytes, double EstimatedDurationSeconds);

public sealed record PreflightPlanDto(IReadOnlyList<PreflightIssueDto> Issues, MigrationEstimateDto Estimate);
```

```csharp
// src/EMaigrator.Api/Services/IBackgroundTaskQueue.cs
namespace EMaigrator.Api.Services;

public interface IBackgroundTaskQueue
{
    ValueTask EnqueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem);
}
```

```csharp
// src/EMaigrator.Api/Services/BackgroundTaskQueue.cs
using System.Threading.Channels;

namespace EMaigrator.Api.Services;

public sealed class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _channel =
        Channel.CreateUnbounded<Func<IServiceProvider, CancellationToken, Task>>();
    public ValueTask EnqueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem) => _channel.Writer.WriteAsync(workItem);
    public IAsyncEnumerable<Func<IServiceProvider, CancellationToken, Task>> Reader(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

// Hosted service that drains the queue, creating a DI scope per work item. It depends on the
// concrete BackgroundTaskQueue (NOT the IBackgroundTaskQueue abstraction) so that swapping the
// IBackgroundTaskQueue registration in tests (InlineTaskQueue) never breaks this pump.
public sealed class QueuedHostedService : BackgroundService
{
    private readonly BackgroundTaskQueue _queue;
    private readonly IServiceProvider _root;
    public QueuedHostedService(BackgroundTaskQueue queue, IServiceProvider root)
        => (_queue, _root) = (queue, root);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var work in _queue.Reader(stoppingToken))
        {
            using var scope = _root.CreateScope();
            try { await work(scope.ServiceProvider, stoppingToken); } catch { /* logged via OTel; never crash the pump */ }
        }
    }
}
```

```csharp
// src/EMaigrator.Api/Services/IPreflightRunner.cs
namespace EMaigrator.Api.Services;

public interface IPreflightRunner
{
    Task RunAsync(Guid jobId, CancellationToken ct);
}
```

> **Contract note (CONTRACTS §5):** `Job` is frozen and has NO `PreflightPlanJson` column. The serialized plan therefore lives in an API-owned side table `PreflightResultRow` in `ApiSideContext` (a DbContext owned by this plan, sharing the same Npgsql connection, with its own EF migration). The runner reads `Job` from the Infrastructure `AppDbContext` and writes the plan JSON to `ApiSideContext`. The frozen `Job`/`MailboxMigration` shapes are never altered.

```csharp
// src/EMaigrator.Api/Data/PreflightResultRow.cs
namespace EMaigrator.Api.Data;

public sealed class PreflightResultRow
{
    public Guid JobId { get; set; }      // PK
    public string PlanJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
```

```csharp
// src/EMaigrator.Api/Data/ApiSideContext.cs
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Data;

// API-owned presentation/orchestration state. NOT in CONTRACTS §5 (keeps the frozen
// Job/MailboxMigration shapes untouched). Shares the same Npgsql connection; own migration.
public sealed class ApiSideContext : DbContext
{
    public ApiSideContext(DbContextOptions<ApiSideContext> options) : base(options) { }

    public DbSet<PreflightResultRow> PreflightResults => Set<PreflightResultRow>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<PreflightResultRow>().HasKey(r => r.JobId);
    }
}
```

```csharp
// src/EMaigrator.Api/Services/PreflightRunner.cs
using System.Text.Json;
using EMaigrator.Api.Data;
using EMaigrator.Api.Realtime;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Core.Model;
using EMaigrator.Core.Preflight;
using EMaigrator.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Services;

public sealed class PreflightRunner : IPreflightRunner
{
    private readonly AppDbContext _db;
    private readonly ApiSideContext _side;
    private readonly IPreflightAnalyzer _analyzer;
    private readonly ISecretStore _secrets;
    private readonly IEnumerable<IProviderPlugin> _plugins;
    private readonly IMigrationGroupNotifier _notifier;

    public PreflightRunner(AppDbContext db, ApiSideContext side, IPreflightAnalyzer analyzer,
        ISecretStore secrets, IEnumerable<IProviderPlugin> plugins, IMigrationGroupNotifier notifier)
        => (_db, _side, _analyzer, _secrets, _plugins, _notifier) = (db, side, analyzer, secrets, plugins, notifier);

    public async Task RunAsync(Guid jobId, CancellationToken ct)
    {
        // Background scope: query filter is bypassed here intentionally (no HTTP principal); we already
        // authorized ownership at POST time, and we load by primary key only.
        var job = await _db.Set<Job>().IgnoreQueryFilters().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return;
        var mbx = await _db.Set<MailboxMigration>().IgnoreQueryFilters().Where(m => m.JobId == jobId).ToListAsync(ct);

        var srcDesc = JsonSerializer.Deserialize<ConnectionDescriptor>(job.SourceConnectionRef!)!;
        var dstDesc = JsonSerializer.Deserialize<ConnectionDescriptor>(job.DestConnectionRef!)!;
        var srcPlugin = _plugins.First(p => p.Id.Value == srcDesc.Provider.Value);
        var dstPlugin = _plugins.First(p => p.Id.Value == dstDesc.Provider.Value);

        var srcSecret = await Bundle(srcDesc, ct);
        var dstSecret = await Bundle(dstDesc, ct);

        await using var source = srcPlugin.CreateSource(srcDesc, srcSecret);
        await using var dest = dstPlugin.CreateDestination(dstDesc, dstSecret);

        var scope = new ScopeSpec
        {
            IsBatch = job.IsBatch,
            Pairs = mbx.Select(m => new MailboxPair(m.SourceMailbox, m.DestMailbox)).ToList()
        };

        var plan = await _analyzer.AnalyzeAsync(source, dest, scope, ct);

        // Persist the plan to the API-owned side table (Job is frozen — no PreflightPlanJson column).
        var planJson = JsonSerializer.Serialize(plan);
        var existing = await _side.PreflightResults.FirstOrDefaultAsync(r => r.JobId == jobId, ct);
        if (existing is null)
            _side.PreflightResults.Add(new PreflightResultRow { JobId = jobId, PlanJson = planJson, CreatedAt = DateTimeOffset.UtcNow });
        else
            existing.PlanJson = planJson;
        await _side.SaveChangesAsync(ct);

        job.Status = JobStatus.AwaitingApproval;
        job.WizardStep = Math.Max(job.WizardStep, 4);
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _notifier.PushStatusChangedAsync(jobId.ToString(), JobStatus.AwaitingApproval.ToString());
    }

    private async Task<SecretBundle> Bundle(ConnectionDescriptor d, CancellationToken ct)
    {
        var values = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(d.SecretRef)) values["secret"] = await _secrets.RetrieveAsync(d.SecretRef, ct);
        return new SecretBundle(values);
    }
}
```

```csharp
// src/EMaigrator.Api/Endpoints/PreflightEndpoints.cs
using System.Text.Json;
using EMaigrator.Api.Contracts;
using EMaigrator.Api.Data;
using EMaigrator.Api.Services;
using EMaigrator.Core.Preflight;
using EMaigrator.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Endpoints;

public static class PreflightEndpoints
{
    public static RouteGroupBuilder MapPreflightEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/migrations/{id:guid}/preflight", async (
            Guid id, AppDbContext db, IBackgroundTaskQueue queue) =>
        {
            var job = await db.Set<Job>().FirstOrDefaultAsync(j => j.Id == id);
            if (job is null) return Results.NotFound();
            if (string.IsNullOrEmpty(job.SourceConnectionRef) || string.IsNullOrEmpty(job.DestConnectionRef))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["connection"] = new[] { "both connections must be configured first" } });

            job.Status = JobStatus.PreFlight;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            await queue.EnqueueAsync(async (sp, ct) =>
                await sp.GetRequiredService<IPreflightRunner>().RunAsync(id, ct));

            return Results.Accepted($"/api/v1/migrations/{id}/preflight");
        });

        group.MapGet("/migrations/{id:guid}/preflight", async (Guid id, AppDbContext db, ApiSideContext side) =>
        {
            // Ownership check via the filtered Job set, then read the side-stored plan.
            if (!await db.Set<Job>().AnyAsync(j => j.Id == id)) return Results.NotFound();
            var row = await side.PreflightResults.FirstOrDefaultAsync(r => r.JobId == id);
            if (row is null) return Results.NotFound();
            var plan = JsonSerializer.Deserialize<PreflightPlan>(row.PlanJson)!;
            var dto = new PreflightPlanDto(
                plan.Issues.Select(i => new PreflightIssueDto(
                    i.IssueType, i.AffectedPaths, i.RecommendedAction.ToString(),
                    i.Options.Select(o => o.ToString()).ToList(), i.Severity.ToString(), i.Description)).ToList(),
                new MigrationEstimateDto(plan.Estimate.MailboxCount, plan.Estimate.FolderCount,
                    plan.Estimate.MessageCount, plan.Estimate.TotalBytes, plan.Estimate.EstimatedDuration.TotalSeconds));
            return Results.Ok(dto);
        });

        return group;
    }
}
```

Register in `ApiServiceCollectionExtensions.AddEMaigratorApi`:

```csharp
        services.AddSingleton<BackgroundTaskQueue>();
        services.AddSingleton<IBackgroundTaskQueue>(sp => sp.GetRequiredService<BackgroundTaskQueue>());
        services.AddHostedService<QueuedHostedService>();
        services.AddScoped<IPreflightRunner, PreflightRunner>();
        services.AddDbContext<EMaigrator.Api.Data.ApiSideContext>(o =>
            o.UseNpgsql(config.GetConnectionString("Postgres")));
```

> The concrete `BackgroundTaskQueue` is registered as itself (consumed by `QueuedHostedService`) and as the `IBackgroundTaskQueue` abstraction (consumed by endpoints). Tests override only the `IBackgroundTaskQueue` registration with `InlineTaskQueue`; `QueuedHostedService` keeps draining the never-written production channel as a harmless no-op.

Add the EF migration for the new context (run from the repo root):

```bash
dotnet ef migrations add ApiSide -c ApiSideContext -p src/EMaigrator.Api -o Data/Migrations
```

Migrate `ApiSideContext` alongside `AppDbContext` in `ApiTestFactory.ConfigureWebHost` → `ConfigureServices` (extend the existing migrate block to also resolve `ApiSideContext` and call `db.Database.Migrate()`):

```csharp
            var side = scope.ServiceProvider.GetRequiredService<EMaigrator.Api.Data.ApiSideContext>();
            side.Database.Migrate();
```

Wire in `Program.cs` (after `v1.MapScopeEndpoints();`):

```csharp
v1.MapPreflightEndpoints();
```

4. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~PreflightEndpointTests`. Expected **PASS** (3 tests).

5. - [ ] Commit.

```bash
git add src/EMaigrator.Api src/EMaigrator.Api.Tests
git commit -m "feat(api): async preflight (202) via IPreflightAnalyzer + GET preflight plan

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 8: POST approve (persist resolutions, enqueue via IJobOrchestrator) + pause/resume/cancel

**Goal:** `POST /migrations/{id}/approve` persists the per-issue-type `resolutions`, flips the Job to `Running`, and enqueues every `MailboxMigration` via `IJobOrchestrator.EnqueueMigrationAsync`. `POST /.../pause|resume|cancel` call `IJobOrchestrator.RequestPause/Resume/CancelAsync` and update Job status, returning the `MigrationDto`.

**Files:**
- Create: `src/EMaigrator.Api/Contracts/ApproveRequest.cs`
- Create: `src/EMaigrator.Api/Data/ApprovedResolutionRow.cs`
- Create: `src/EMaigrator.Api/Endpoints/RunControlEndpoints.cs`
- Modify: `src/EMaigrator.Api/Data/ApiSideContext.cs`
- Modify: `src/EMaigrator.Api/Program.cs`
- Test: `src/EMaigrator.Api.Tests/RunControlTests.cs`

**Acceptance Criteria:**
- [ ] `POST /migrations/{id}/approve` with `{resolutions:{"FolderDepth":"FlattenFolder"}}` persists the resolution, sets `Job.Status=Running`, `WizardStep≥5`, and calls `IJobOrchestrator.EnqueueMigrationAsync` once per `MailboxMigration`.
- [ ] Approve is rejected with 409 if the Job is not `AwaitingApproval`.
- [ ] `POST /.../pause` sets `Paused` and calls `RequestPauseAsync(jobId)`; `/resume` sets `Running` + `RequestResumeAsync`; `/cancel` sets `Cancelled` + `RequestCancelAsync`.
- [ ] An unknown resolution action value → 400.
- [ ] Cross-tenant id → 404; unauthenticated → 401.

**Verify:** `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~RunControlTests` → all pass.

**Steps:**

1. - [ ] Write the failing test. A substitute `IJobOrchestrator` records calls.

```csharp
// src/EMaigrator.Api.Tests/RunControlTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EMaigrator.Api.Tests.Infrastructure;
using EMaigrator.Core.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace EMaigrator.Api.Tests;

[Collection(PostgresCollection.Name)]
public class RunControlTests
{
    private readonly ApiTestFactory _factory;
    public RunControlTests(PostgresFixture pg) =>
        _factory = new ApiTestFactory(pg.ConnectionString).WithFakeImapPlugin().WithFakePreflight().WithRecordingOrchestrator();

    private async Task<string> ApprovableMigration(HttpClient c)
    {
        var id = (await (await c.PostAsJsonAsync("/api/v1/migrations", new { }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        await c.PatchAsJsonAsync($"/api/v1/migrations/{id}/endpoints", new { from = "imap", to = "graph" });
        await c.PutAsJsonAsync($"/api/v1/migrations/{id}/connection/from", new { auth = "ImapBasic", settings = new { host = "h", port = "993", accountEmail = "a@b.c" }, secret = "pw" });
        await c.PutAsJsonAsync($"/api/v1/migrations/{id}/connection/to", new { auth = "ImapBasic", settings = new { host = "h2", port = "993", accountEmail = "d@e.f" }, secret = "pw2" });
        await c.PutAsJsonAsync($"/api/v1/migrations/{id}/scope", new { isBatch = false, pairs = new[] { new { sourceMailbox = "a@b.c", destMailbox = "d@e.f" } } });
        await c.PostAsync($"/api/v1/migrations/{id}/preflight", null);
        await c.GetAsync($"/api/v1/migrations/{id}/preflight");   // inline queue → AwaitingApproval
        return id;
    }

    [Fact]
    public async Task Approve_enqueues_and_sets_running()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var id = await ApprovableMigration(client);

        var res = await client.PostAsJsonAsync($"/api/v1/migrations/{id}/approve",
            new { resolutions = new Dictionary<string, string> { ["FolderDepth"] = "FlattenFolder" } });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("Running");

        var orch = (RecordingOrchestrator)_factory.Services.GetRequiredService<IJobOrchestrator>();
        orch.Enqueued.Should().HaveCount(1);
    }

    [Fact]
    public async Task Approve_when_not_awaiting_returns_409()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var id = (await (await client.PostAsJsonAsync("/api/v1/migrations", new { }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        var res = await client.PostAsJsonAsync($"/api/v1/migrations/{id}/approve",
            new { resolutions = new Dictionary<string, string>() });
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Pause_resume_cancel_drive_orchestrator()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var id = await ApprovableMigration(client);
        await client.PostAsJsonAsync($"/api/v1/migrations/{id}/approve",
            new { resolutions = new Dictionary<string, string>() });

        (await client.PostAsync($"/api/v1/migrations/{id}/pause", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync($"/api/v1/migrations/{id}/resume", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync($"/api/v1/migrations/{id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var orch = (RecordingOrchestrator)_factory.Services.GetRequiredService<IJobOrchestrator>();
        orch.Paused.Should().Contain(Guid.Parse(id));
        orch.Resumed.Should().Contain(Guid.Parse(id));
        orch.Cancelled.Should().Contain(Guid.Parse(id));
    }
}
```

The recording orchestrator + factory extension:

```csharp
// src/EMaigrator.Api.Tests/Infrastructure/RecordingOrchestrator.cs
using EMaigrator.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EMaigrator.Api.Tests.Infrastructure;

public sealed class RecordingOrchestrator : IJobOrchestrator
{
    public List<Guid> Enqueued { get; } = new();
    public List<Guid> Paused { get; } = new();
    public List<Guid> Resumed { get; } = new();
    public List<Guid> Cancelled { get; } = new();
    public Task EnqueueMigrationAsync(Guid id, CancellationToken ct) { Enqueued.Add(id); return Task.CompletedTask; }
    public Task RequestPauseAsync(Guid id, CancellationToken ct) { Paused.Add(id); return Task.CompletedTask; }
    public Task RequestResumeAsync(Guid id, CancellationToken ct) { Resumed.Add(id); return Task.CompletedTask; }
    public Task RequestCancelAsync(Guid id, CancellationToken ct) { Cancelled.Add(id); return Task.CompletedTask; }
}

public static class RecordingOrchestratorExtensions
{
    public static ApiTestFactory WithRecordingOrchestrator(this ApiTestFactory f) => f;
    public static void AddRecordingOrchestrator(IServiceCollection services)
        => services.AddSingleton<IJobOrchestrator, RecordingOrchestrator>();
}
```

> Register `AddRecordingOrchestrator(services)` inside `ApiTestFactory.ConfigureWebHost` → `ConfigureServices` (this replaces the MassTransit-backed `IJobOrchestrator` from Plan 03 with the recorder for deterministic tests).

2. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~RunControlTests`. Expected **FAIL** — approve/control endpoints not implemented.

3. - [ ] Implement.

```csharp
// src/EMaigrator.Api/Contracts/ApproveRequest.cs
namespace EMaigrator.Api.Contracts;

public sealed record ApproveRequest(IReadOnlyDictionary<string, string> Resolutions);
```

```csharp
// src/EMaigrator.Api/Data/ApprovedResolutionRow.cs
namespace EMaigrator.Api.Data;

public sealed class ApprovedResolutionRow
{
    public long Id { get; set; }
    public Guid JobId { get; set; }
    public string IssueType { get; set; } = "";
    public string Action { get; set; } = "";   // RemediationAction name
}
```

Add to `ApiSideContext`: the property `public DbSet<ApprovedResolutionRow> ApprovedResolutions => Set<ApprovedResolutionRow>();` and, in `OnModelCreating`, `b.Entity<ApprovedResolutionRow>().HasKey(r => r.Id);` (with `r.Id` as a database-generated identity). Regenerate the migration: `dotnet ef migrations add ApiSideResolutions -c ApiSideContext -p src/EMaigrator.Api -o Data/Migrations`.

```csharp
// src/EMaigrator.Api/Endpoints/RunControlEndpoints.cs
using EMaigrator.Api.Contracts;
using EMaigrator.Api.Data;
using EMaigrator.Api.Mapping;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Endpoints;

public static class RunControlEndpoints
{
    public static RouteGroupBuilder MapRunControlEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/migrations/{id:guid}/approve", async (
            Guid id, ApproveRequest req, AppDbContext db, ApiSideContext side, IJobOrchestrator orchestrator) =>
        {
            var job = await db.Set<Job>().FirstOrDefaultAsync(j => j.Id == id);
            if (job is null) return Results.NotFound();
            if (job.Status != JobStatus.AwaitingApproval)
                return Results.Conflict(new { error = "migration is not awaiting approval." });

            foreach (var (issueType, action) in req.Resolutions)
            {
                if (!Enum.TryParse<RemediationAction>(action, out _))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { [issueType] = new[] { $"unknown action '{action}'" } });
            }

            var old = await side.ApprovedResolutions.Where(r => r.JobId == id).ToListAsync();
            side.ApprovedResolutions.RemoveRange(old);
            foreach (var (issueType, action) in req.Resolutions)
                side.ApprovedResolutions.Add(new ApprovedResolutionRow { JobId = id, IssueType = issueType, Action = action });
            await side.SaveChangesAsync();

            var mbx = await db.Set<MailboxMigration>().Where(m => m.JobId == id).ToListAsync();
            job.Status = JobStatus.Running;
            job.WizardStep = Math.Max(job.WizardStep, 5);
            job.UpdatedAt = DateTimeOffset.UtcNow;
            foreach (var m in mbx) m.Status = MailboxMigrationStatus.Pending;
            await db.SaveChangesAsync();

            foreach (var m in mbx) await orchestrator.EnqueueMigrationAsync(m.Id, default);
            return Results.Ok(MigrationMapper.ToDto(job, mbx));
        });

        MapControl(group, "pause", JobStatus.Paused, (o, jobId) => o.RequestPauseAsync(jobId, default));
        MapControl(group, "resume", JobStatus.Running, (o, jobId) => o.RequestResumeAsync(jobId, default));
        MapControl(group, "cancel", JobStatus.Cancelled, (o, jobId) => o.RequestCancelAsync(jobId, default));
        return group;
    }

    private static void MapControl(RouteGroupBuilder group, string verb, JobStatus newStatus,
        Func<IJobOrchestrator, Guid, Task> action)
    {
        group.MapPost($"/migrations/{{id:guid}}/{verb}", async (Guid id, AppDbContext db, IJobOrchestrator orchestrator) =>
        {
            var job = await db.Set<Job>().FirstOrDefaultAsync(j => j.Id == id);
            if (job is null) return Results.NotFound();
            await action(orchestrator, id);
            job.Status = newStatus;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            var mbx = await db.Set<MailboxMigration>().Where(m => m.JobId == id).ToListAsync();
            return Results.Ok(MigrationMapper.ToDto(job, mbx));
        });
    }
}
```

Wire in `Program.cs` (after `v1.MapPreflightEndpoints();`):

```csharp
v1.MapRunControlEndpoints();
```

4. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~RunControlTests`. Expected **PASS** (3 tests).

5. - [ ] Commit.

```bash
git add src/EMaigrator.Api src/EMaigrator.Api.Tests
git commit -m "feat(api): approve persists resolutions + enqueues via IJobOrchestrator; pause/resume/cancel

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 9: GET results (+ source↔dest reconciliation) + GET audit (StoreSubjects-aware) + POST rerun

**Goal:** `GET /migrations/{id}/results` returns counts, a source↔destination reconciliation, and the needs-decision queue. `GET /migrations/{id}/audit` returns `AuditEntryDto[]` from `MigrationLogRow` (omitting `subject` when `Job.StoreSubjects==false`), filterable by `?q=&failuresOnly=`. `POST /migrations/{id}/rerun` re-enqueues not-done items via the orchestrator.

**Files:**
- Create: `src/EMaigrator.Api/Contracts/ResultsDtos.cs`
- Create: `src/EMaigrator.Api/Endpoints/ResultsEndpoints.cs`
- Modify: `src/EMaigrator.Api/Program.cs`
- Test: `src/EMaigrator.Api.Tests/ResultsAuditTests.cs`

**Acceptance Criteria:**
- [ ] `GET /migrations/{id}/results` returns `{ counts:{migrated,skipped,failed}, reconciliation:{sourceCount,destCount,matched}, needsDecision:[] }` aggregated across the job's `MailboxMigration` rows + `LedgerEntry` counts (via `ILedger.GetCountsAsync`).
- [ ] `GET /migrations/{id}/audit` returns `AuditEntryDto[]` mapped from `MigrationLogRow`; when `Job.StoreSubjects==false`, every `subject` field is `null`.
- [ ] `?failuresOnly=true` returns only entries whose status is `Failed`; `?q=` filters by subject/folder substring.
- [ ] `POST /migrations/{id}/rerun` calls `IJobOrchestrator.EnqueueMigrationAsync` for each mailbox (the worker re-scans the ledger for not-done) and returns `MigrationDto`.
- [ ] Cross-tenant id → 404; unauthenticated → 401.

**Verify:** `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~ResultsAuditTests` → all pass.

**Steps:**

1. - [ ] Write the failing test. Seeds `MailboxMigration` + `MigrationLogRow` rows directly; a substitute `ILedger` returns deterministic counts.

```csharp
// src/EMaigrator.Api.Tests/ResultsAuditTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EMaigrator.Api.Tests.Infrastructure;
using EMaigrator.Core.Model;
using EMaigrator.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EMaigrator.Api.Tests;

[Collection(PostgresCollection.Name)]
public class ResultsAuditTests
{
    private readonly ApiTestFactory _factory;
    public ResultsAuditTests(PostgresFixture pg) => _factory = new ApiTestFactory(pg.ConnectionString).WithRecordingOrchestrator();

    private async Task<Guid> SeedCompletedJob(Guid tenantId, bool storeSubjects)
    {
        using var scope = _factory.Services.CreateScope();
        ((TestCurrentTenant)scope.ServiceProvider.GetRequiredService<EMaigrator.Api.Tenancy.ICurrentTenant>()).Current = tenantId;
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = new Job { Id = Guid.NewGuid(), TenantId = tenantId, SourceProvider = new ProviderId("imap"),
            DestProvider = new ProviderId("graph"), Status = JobStatus.Completed, StoreSubjects = storeSubjects,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Set<Job>().Add(job);
        var mbx = new MailboxMigration { Id = Guid.NewGuid(), JobId = job.Id, SourceMailbox = "a@b.c", DestMailbox = "d@e.f",
            Status = MailboxMigrationStatus.Completed, MigratedCount = 3, SkippedCount = 1, FailedCount = 1 };
        db.Set<MailboxMigration>().Add(mbx);
        db.Set<MigrationLogRow>().Add(new MigrationLogRow { MailboxMigrationId = mbx.Id, Subject = "Re: invoice",
            MessageDate = DateTimeOffset.UtcNow, SourceFolder = "/Archive", DestFolder = "/Archive", Status = "Migrated", CreatedAt = DateTimeOffset.UtcNow });
        db.Set<MigrationLogRow>().Add(new MigrationLogRow { MailboxMigrationId = mbx.Id, Subject = "Big file",
            MessageDate = DateTimeOffset.UtcNow, SourceFolder = "/Sent", DestFolder = "/Sent", Status = "Failed", ErrorCode = "SIZE", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return job.Id;
    }

    [Fact]
    public async Task Results_returns_counts_and_reconciliation()
    {
        var (client, tenantId) = await AuthClient.CreateAsync(_factory);
        var id = await SeedCompletedJob(tenantId, storeSubjects: true);
        var res = await client.GetFromJsonAsync<JsonElement>($"/api/v1/migrations/{id}/results");
        res.GetProperty("counts").GetProperty("migrated").GetInt64().Should().Be(3);
        res.GetProperty("reconciliation").TryGetProperty("destCount", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Audit_omits_subject_when_store_subjects_false()
    {
        var (client, tenantId) = await AuthClient.CreateAsync(_factory);
        var id = await SeedCompletedJob(tenantId, storeSubjects: false);
        var arr = await client.GetFromJsonAsync<JsonElement>($"/api/v1/migrations/{id}/audit");
        foreach (var e in arr.EnumerateArray())
            (e.GetProperty("subject").ValueKind == JsonValueKind.Null).Should().BeTrue("privacy toggle hides subjects");
    }

    [Fact]
    public async Task Audit_failures_only_filter()
    {
        var (client, tenantId) = await AuthClient.CreateAsync(_factory);
        var id = await SeedCompletedJob(tenantId, storeSubjects: true);
        var arr = await client.GetFromJsonAsync<JsonElement>($"/api/v1/migrations/{id}/audit?failuresOnly=true");
        arr.GetArrayLength().Should().Be(1);
        arr[0].GetProperty("status").GetString().Should().Be("Failed");
    }

    [Fact]
    public async Task Rerun_reenqueues_mailboxes()
    {
        var (client, tenantId) = await AuthClient.CreateAsync(_factory);
        var id = await SeedCompletedJob(tenantId, storeSubjects: true);
        var res = await client.PostAsync($"/api/v1/migrations/{id}/rerun", null);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var orch = (RecordingOrchestrator)_factory.Services.GetRequiredService<EMaigrator.Core.Abstractions.IJobOrchestrator>();
        orch.Enqueued.Should().HaveCountGreaterThanOrEqualTo(1);
    }
}
```

2. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~ResultsAuditTests`. Expected **FAIL** — results/audit/rerun endpoints not implemented.

3. - [ ] Implement.

```csharp
// src/EMaigrator.Api/Contracts/ResultsDtos.cs
namespace EMaigrator.Api.Contracts;

public sealed record ResultCounts(long Migrated, long Skipped, long Failed);
public sealed record Reconciliation(long SourceCount, long DestCount, bool Matched);
public sealed record NeedsDecisionItemDto(string IssueType, string Detail, IReadOnlyList<string> Options);
public sealed record ResultsDto(ResultCounts Counts, Reconciliation Reconciliation, IReadOnlyList<NeedsDecisionItemDto> NeedsDecision);

public sealed record AuditEntryDto(
    string? Subject, DateTimeOffset Date, string SourceFolder, string DestFolder, string Status, string? ErrorCode);
```

```csharp
// src/EMaigrator.Api/Endpoints/ResultsEndpoints.cs
using EMaigrator.Api.Contracts;
using EMaigrator.Api.Mapping;
using EMaigrator.Core.Abstractions;
using EMaigrator.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Endpoints;

public static class ResultsEndpoints
{
    public static RouteGroupBuilder MapResultsEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/migrations/{id:guid}/results", async (Guid id, AppDbContext db, ILedger ledger) =>
        {
            var job = await db.Set<Job>().FirstOrDefaultAsync(j => j.Id == id);
            if (job is null) return Results.NotFound();
            var mbx = await db.Set<MailboxMigration>().Where(m => m.JobId == id).ToListAsync();

            long migrated = 0, skipped = 0, failed = 0;
            foreach (var m in mbx)
            {
                var counts = await ledger.GetCountsAsync(m.Id, default);
                migrated += counts.Migrated; skipped += counts.Skipped; failed += counts.Failed;
            }
            var sourceCount = migrated + skipped + failed;
            var destCount = migrated;   // dest holds only successfully written messages

            // Needs-decision queue: failed entries are surfaced for resolution (worker also DLQs poison messages).
            var needs = (await db.Set<MigrationLogRow>()
                    .Where(l => mbx.Select(m => m.Id).Contains(l.MailboxMigrationId) && l.Status == "Failed")
                    .ToListAsync())
                .Select(l => new NeedsDecisionItemDto(l.ErrorCode ?? "Unknown",
                    $"{l.SourceFolder} → {l.DestFolder}", new[] { "SkipMessage", "RetryWithBackoff" }))
                .ToList();

            return Results.Ok(new ResultsDto(
                new ResultCounts(migrated, skipped, failed),
                new Reconciliation(sourceCount, destCount, sourceCount == destCount + skipped + failed),
                needs));
        });

        group.MapGet("/migrations/{id:guid}/audit", async (Guid id, string? q, bool? failuresOnly, AppDbContext db) =>
        {
            var job = await db.Set<Job>().FirstOrDefaultAsync(j => j.Id == id);
            if (job is null) return Results.NotFound();
            var mbxIds = await db.Set<MailboxMigration>().Where(m => m.JobId == id).Select(m => m.Id).ToListAsync();

            var query = db.Set<MigrationLogRow>().Where(l => mbxIds.Contains(l.MailboxMigrationId));
            if (failuresOnly == true) query = query.Where(l => l.Status == "Failed");
            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(l => (l.Subject != null && l.Subject.Contains(q)) || l.SourceFolder.Contains(q) || l.DestFolder.Contains(q));

            var rows = await query.OrderByDescending(l => l.MessageDate).ToListAsync();
            var dtos = rows.Select(l => new AuditEntryDto(
                job.StoreSubjects ? l.Subject : null,   // privacy toggle (DESIGN.md §10)
                l.MessageDate, l.SourceFolder, l.DestFolder, l.Status, l.ErrorCode));
            return Results.Ok(dtos);
        });

        group.MapPost("/migrations/{id:guid}/rerun", async (Guid id, AppDbContext db, IJobOrchestrator orchestrator) =>
        {
            var job = await db.Set<Job>().FirstOrDefaultAsync(j => j.Id == id);
            if (job is null) return Results.NotFound();
            var mbx = await db.Set<MailboxMigration>().Where(m => m.JobId == id).ToListAsync();
            job.Status = JobStatus.Running; job.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            foreach (var m in mbx) await orchestrator.EnqueueMigrationAsync(m.Id, default);   // worker re-scans ledger for not-done
            return Results.Ok(MigrationMapper.ToDto(job, mbx));
        });

        return group;
    }
}
```

> The test substitutes `ILedger` with the deterministic `FakeLedger` below (counts 3/1/1), registered in `ApiTestFactory.ConfigureWebHost` → `ConfigureServices` alongside `AddRecordingOrchestrator`, so results tests are deterministic without a worker.

```csharp
// src/EMaigrator.Api.Tests/Infrastructure/FakeLedger.cs
using EMaigrator.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EMaigrator.Api.Tests.Infrastructure;

// Deterministic ledger for results/reconciliation tests: 3 migrated, 1 skipped, 1 failed, 0 pending.
public sealed class FakeLedger : ILedger
{
    public Task<bool> IsDoneAsync(Guid mailboxMigrationId, string identityKey, CancellationToken ct) => Task.FromResult(false);
    public Task MarkAsync(Guid mailboxMigrationId, string identityKey, string sourceFolder, string destFolder,
        LedgerStatus status, string? errorCode, CancellationToken ct) => Task.CompletedTask;
    public async IAsyncEnumerable<LedgerEntry> GetNotDoneAsync(Guid mailboxMigrationId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    { await Task.CompletedTask; yield break; }
    public Task<LedgerCounts> GetCountsAsync(Guid mailboxMigrationId, CancellationToken ct) =>
        Task.FromResult(new LedgerCounts(3, 1, 1, 0));
}

public static class FakeLedgerExtensions
{
    public static void AddFakeLedger(IServiceCollection services)
        => services.AddSingleton<ILedger, FakeLedger>();
}
```

Wire in `Program.cs` (after `v1.MapRunControlEndpoints();`):

```csharp
v1.MapResultsEndpoints();
```

4. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~ResultsAuditTests`. Expected **PASS** (4 tests).

5. - [ ] Commit.

```bash
git add src/EMaigrator.Api src/EMaigrator.Api.Tests
git commit -m "feat(api): results+reconciliation, audit with StoreSubjects toggle, rerun

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 10: GET report (CSV + PDF export)

**Goal:** `GET /migrations/{id}/report?format=csv|pdf` streams a downloadable report (counts, duration, per-folder breakdown, skipped/failed) — the MSP proof-of-work deliverable. CSV via `CsvHelper`, PDF via QuestPDF.

**Files:**
- Create: `src/EMaigrator.Api/Reporting/ReportData.cs`
- Create: `src/EMaigrator.Api/Reporting/IReportBuilder.cs`
- Create: `src/EMaigrator.Api/Reporting/CsvReportBuilder.cs`
- Create: `src/EMaigrator.Api/Reporting/PdfReportBuilder.cs`
- Create: `src/EMaigrator.Api/Endpoints/ReportEndpoints.cs`
- Modify: `src/EMaigrator.Api/AppConfiguration/ApiServiceCollectionExtensions.cs`
- Modify: `src/EMaigrator.Api/Program.cs`
- Test: `src/EMaigrator.Api.Tests/ReportEndpointTests.cs`

**Acceptance Criteria:**
- [ ] `GET /migrations/{id}/report?format=csv` returns 200 with `Content-Type: text/csv`, `Content-Disposition: attachment`, and a body whose header row contains `Folder,Migrated,Skipped,Failed` and a totals row.
- [ ] `GET /migrations/{id}/report?format=pdf` returns 200 with `Content-Type: application/pdf` and a body starting with the `%PDF-` magic bytes.
- [ ] An unsupported `format` → 400.
- [ ] Cross-tenant id → 404; unauthenticated → 401.

**Verify:** `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~ReportEndpointTests` → all pass.

**Steps:**

1. - [ ] Write the failing test.

```csharp
// src/EMaigrator.Api.Tests/ReportEndpointTests.cs
using System.Net;
using System.Text;
using EMaigrator.Api.Tests.Infrastructure;
using EMaigrator.Core.Model;
using EMaigrator.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EMaigrator.Api.Tests;

[Collection(PostgresCollection.Name)]
public class ReportEndpointTests
{
    private readonly ApiTestFactory _factory;
    public ReportEndpointTests(PostgresFixture pg) => _factory = new ApiTestFactory(pg.ConnectionString).WithRecordingOrchestrator();

    private async Task<Guid> Seed(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        ((TestCurrentTenant)scope.ServiceProvider.GetRequiredService<EMaigrator.Api.Tenancy.ICurrentTenant>()).Current = tenantId;
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = new Job { Id = Guid.NewGuid(), TenantId = tenantId, SourceProvider = new ProviderId("imap"),
            DestProvider = new ProviderId("graph"), Status = JobStatus.Completed, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Set<Job>().Add(job);
        var mbx = new MailboxMigration { Id = Guid.NewGuid(), JobId = job.Id, SourceMailbox = "a@b.c", DestMailbox = "d@e.f",
            Status = MailboxMigrationStatus.Completed, MigratedCount = 3180, SkippedCount = 18, FailedCount = 3,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-12), FinishedAt = DateTimeOffset.UtcNow };
        db.Set<MailboxMigration>().Add(mbx);
        db.Set<MigrationLogRow>().Add(new MigrationLogRow { MailboxMigrationId = mbx.Id, MessageDate = DateTimeOffset.UtcNow,
            SourceFolder = "/Archive", DestFolder = "/Archive", Status = "Migrated", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return job.Id;
    }

    [Fact]
    public async Task Csv_report_has_headers_and_attachment_disposition()
    {
        var (client, tenantId) = await AuthClient.CreateAsync(_factory);
        var id = await Seed(tenantId);
        var res = await client.GetAsync($"/api/v1/migrations/{id}/report?format=csv");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        res.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("Folder,Migrated,Skipped,Failed");
    }

    [Fact]
    public async Task Pdf_report_has_pdf_magic()
    {
        var (client, tenantId) = await AuthClient.CreateAsync(_factory);
        var id = await Seed(tenantId);
        var res = await client.GetAsync($"/api/v1/migrations/{id}/report?format=pdf");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        var bytes = await res.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public async Task Unsupported_format_returns_400()
    {
        var (client, tenantId) = await AuthClient.CreateAsync(_factory);
        var id = await Seed(tenantId);
        (await client.GetAsync($"/api/v1/migrations/{id}/report?format=xml")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

2. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~ReportEndpointTests`. Expected **FAIL** — report builders/endpoint not implemented.

3. - [ ] Implement.

```csharp
// src/EMaigrator.Api/Reporting/ReportData.cs
namespace EMaigrator.Api.Reporting;

public sealed record FolderBreakdownRow(string Folder, long Migrated, long Skipped, long Failed);

public sealed record ReportData(
    Guid MigrationId, string From, string To, string Status,
    long Migrated, long Skipped, long Failed, TimeSpan Duration,
    IReadOnlyList<FolderBreakdownRow> Folders);
```

```csharp
// src/EMaigrator.Api/Reporting/IReportBuilder.cs
namespace EMaigrator.Api.Reporting;

public interface IReportBuilder
{
    string Format { get; }            // "csv" | "pdf"
    string ContentType { get; }
    string FileName(Guid migrationId);
    byte[] Build(ReportData data);
}
```

```csharp
// src/EMaigrator.Api/Reporting/CsvReportBuilder.cs
using System.Globalization;
using System.Text;
using CsvHelper;

namespace EMaigrator.Api.Reporting;

public sealed class CsvReportBuilder : IReportBuilder
{
    public string Format => "csv";
    public string ContentType => "text/csv";
    public string FileName(Guid id) => $"emaigrator-report-{id}.csv";

    public byte[] Build(ReportData data)
    {
        using var sw = new StringWriter();
        using (var csv = new CsvWriter(sw, CultureInfo.InvariantCulture))
        {
            csv.WriteField("Migration"); csv.WriteField(data.MigrationId.ToString()); csv.NextRecord();
            csv.WriteField("From"); csv.WriteField(data.From); csv.NextRecord();
            csv.WriteField("To"); csv.WriteField(data.To); csv.NextRecord();
            csv.WriteField("Status"); csv.WriteField(data.Status); csv.NextRecord();
            csv.WriteField("Duration (min)"); csv.WriteField(Math.Round(data.Duration.TotalMinutes, 1)); csv.NextRecord();
            csv.NextRecord();
            foreach (var h in new[] { "Folder", "Migrated", "Skipped", "Failed" }) csv.WriteField(h);
            csv.NextRecord();
            foreach (var f in data.Folders)
            { csv.WriteField(f.Folder); csv.WriteField(f.Migrated); csv.WriteField(f.Skipped); csv.WriteField(f.Failed); csv.NextRecord(); }
            csv.WriteField("TOTAL"); csv.WriteField(data.Migrated); csv.WriteField(data.Skipped); csv.WriteField(data.Failed); csv.NextRecord();
        }
        return Encoding.UTF8.GetBytes(sw.ToString());
    }
}
```

```csharp
// src/EMaigrator.Api/Reporting/PdfReportBuilder.cs
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EMaigrator.Api.Reporting;

public sealed class PdfReportBuilder : IReportBuilder
{
    static PdfReportBuilder() => QuestPDF.Settings.License = LicenseType.Community;

    public string Format => "pdf";
    public string ContentType => "application/pdf";
    public string FileName(Guid id) => $"emaigrator-report-{id}.pdf";

    public byte[] Build(ReportData data) => Document.Create(doc =>
    {
        doc.Page(page =>
        {
            page.Margin(40);
            page.Header().Text($"EMaigrator Report — {data.From} → {data.To}").Bold().FontSize(16);
            page.Content().Column(col =>
            {
                col.Item().Text($"Status: {data.Status}   Duration: {Math.Round(data.Duration.TotalMinutes, 1)} min");
                col.Item().Text($"Migrated: {data.Migrated}   Skipped: {data.Skipped}   Failed: {data.Failed}");
                col.Item().PaddingTop(10).Table(t =>
                {
                    t.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); });
                    foreach (var h in new[] { "Folder", "Migrated", "Skipped", "Failed" }) t.Cell().Text(h).Bold();
                    foreach (var f in data.Folders)
                    { t.Cell().Text(f.Folder); t.Cell().Text(f.Migrated.ToString()); t.Cell().Text(f.Skipped.ToString()); t.Cell().Text(f.Failed.ToString()); }
                });
            });
            page.Footer().AlignCenter().Text(x => { x.Span("Migration "); x.Span(data.MigrationId.ToString()); });
        });
    }).GeneratePdf();
}
```

```csharp
// src/EMaigrator.Api/Endpoints/ReportEndpoints.cs
using EMaigrator.Api.Reporting;
using EMaigrator.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Endpoints;

public static class ReportEndpoints
{
    public static RouteGroupBuilder MapReportEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/migrations/{id:guid}/report", async (
            Guid id, string? format, AppDbContext db, IEnumerable<IReportBuilder> builders) =>
        {
            var fmt = (format ?? "csv").ToLowerInvariant();
            var builder = builders.FirstOrDefault(b => b.Format == fmt);
            if (builder is null) return Results.BadRequest(new { error = "format must be csv or pdf." });

            var job = await db.Set<Job>().FirstOrDefaultAsync(j => j.Id == id);
            if (job is null) return Results.NotFound();
            var mbx = await db.Set<MailboxMigration>().Where(m => m.JobId == id).ToListAsync();
            var mbxIds = mbx.Select(m => m.Id).ToList();
            var logs = await db.Set<MigrationLogRow>().Where(l => mbxIds.Contains(l.MailboxMigrationId)).ToListAsync();

            var folders = logs.GroupBy(l => l.DestFolder).Select(g => new FolderBreakdownRow(
                g.Key, g.Count(x => x.Status == "Migrated"), g.Count(x => x.Status == "Skipped"), g.Count(x => x.Status == "Failed"))).ToList();
            var duration = mbx.Where(m => m.StartedAt != null && m.FinishedAt != null)
                .Select(m => m.FinishedAt!.Value - m.StartedAt!.Value).DefaultIfEmpty(TimeSpan.Zero).Max();

            var data = new ReportData(id, job.SourceProvider.Value, job.DestProvider.Value, job.Status.ToString(),
                mbx.Sum(m => m.MigratedCount), mbx.Sum(m => m.SkippedCount), mbx.Sum(m => m.FailedCount), duration, folders);

            var bytes = builder.Build(data);
            return Results.File(bytes, builder.ContentType, builder.FileName(id));
        });
        return group;
    }
}
```

Register builders in `ApiServiceCollectionExtensions.AddEMaigratorApi`:

```csharp
        services.AddSingleton<EMaigrator.Api.Reporting.IReportBuilder, EMaigrator.Api.Reporting.CsvReportBuilder>();
        services.AddSingleton<EMaigrator.Api.Reporting.IReportBuilder, EMaigrator.Api.Reporting.PdfReportBuilder>();
```

Wire in `Program.cs` (after `v1.MapResultsEndpoints();`):

```csharp
v1.MapReportEndpoints();
```

4. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~ReportEndpointTests`. Expected **PASS** (3 tests).

5. - [ ] Commit.

```bash
git add src/EMaigrator.Api src/EMaigrator.Api.Tests
git commit -m "feat(api): GET report exports CSV (CsvHelper) + PDF (QuestPDF)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 11: Email notifications on terminal states (IEmailSender + templates + terminal-state listener)

**Goal:** Send an email on terminal Job states (Completed · Partial · Failed) via an injected `IAppEmailSender` rendering templates; a `TerminalStateNotifier` MassTransit consumer watches `MigrationProgressEvent` with a terminal `Status` and dispatches once per migration (idempotent — guarded by a sent-flag row).

**Files:**
- Create: `src/EMaigrator.Api/Notifications/IAppEmailSender.cs`
- Create: `src/EMaigrator.Api/Notifications/EmailTemplates.cs`
- Create: `src/EMaigrator.Api/Notifications/TerminalStateNotifier.cs`
- Create: `src/EMaigrator.Api/Notifications/DbSentGuard.cs`
- Create: `src/EMaigrator.Api/Notifications/DbNotificationRecipientResolver.cs`
- Create: `src/EMaigrator.Api/Notifications/LoggingEmailSender.cs`
- Create: `src/EMaigrator.Api/Data/NotificationSentRow.cs`
- Modify: `src/EMaigrator.Api/Data/ApiSideContext.cs`
- Modify: `src/EMaigrator.Api/AppConfiguration/ApiServiceCollectionExtensions.cs`
- Test: `src/EMaigrator.Api.Tests/TerminalStateNotifierTests.cs`

**Acceptance Criteria:**
- [ ] `EmailTemplates.Render(status, MigrationDto-ish summary)` returns a `(subject, htmlBody)` whose subject reflects the terminal state ("completed"/"needs your decision"/"failed") and contains the From→To text.
- [ ] `TerminalStateNotifier` (a `MassTransit.IConsumer<MigrationProgressEvent>`) sends exactly one email for a terminal status and does NOT send for a non-terminal `Running` status.
- [ ] A second terminal event for the same migration does NOT resend (idempotent via `NotificationSentRow`).
- [ ] The email body contains NO secret/credential text (asserted) and is addressed to the owning user's email.

**Verify:** `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~TerminalStateNotifierTests` → all pass.

**Steps:**

1. - [ ] Write the failing test.

```csharp
// src/EMaigrator.Api.Tests/TerminalStateNotifierTests.cs
using EMaigrator.Api.Notifications;
using EMaigrator.Core.Contracts;
using FluentAssertions;
using MassTransit;
using NSubstitute;
using Xunit;

namespace EMaigrator.Api.Tests;

public class TerminalStateNotifierTests
{
    [Fact]
    public void Template_reflects_terminal_state_and_endpoints()
    {
        var (subject, body) = EmailTemplates.Render("Completed", "WorkMail", "Microsoft 365", 3180, 18, 3);
        subject.ToLowerInvariant().Should().Contain("complete");
        body.Should().Contain("WorkMail").And.Contain("Microsoft 365");
    }

    [Fact]
    public void Partial_template_says_needs_decision()
    {
        var (subject, _) = EmailTemplates.Render("Partial", "WorkMail", "Google", 10, 0, 2);
        subject.ToLowerInvariant().Should().Contain("decision");
    }

    [Fact]
    public async Task Running_status_does_not_send()
    {
        var email = Substitute.For<IAppEmailSender>();
        var resolver = Substitute.For<INotificationRecipientResolver>();
        var notifier = new TerminalStateNotifier(email, resolver, new InMemorySentGuard());
        var ctx = Substitute.For<ConsumeContext<MigrationProgressEvent>>();
        ctx.Message.Returns(new MigrationProgressEvent(Guid.NewGuid(), 1, 10, "/Inbox", 1, "Running"));
        await notifier.Consume(ctx);
        await email.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Terminal_status_sends_once_only()
    {
        var email = Substitute.For<IAppEmailSender>();
        var resolver = Substitute.For<INotificationRecipientResolver>();
        var mbxId = Guid.NewGuid();
        resolver.ResolveAsync(mbxId, Arg.Any<CancellationToken>())
            .Returns(new NotificationContext("owner@biz.com", "WorkMail", "Microsoft 365"));
        var guard = new InMemorySentGuard();
        var notifier = new TerminalStateNotifier(email, resolver, guard);

        var ctx = Substitute.For<ConsumeContext<MigrationProgressEvent>>();
        ctx.Message.Returns(new MigrationProgressEvent(mbxId, 3180, 3201, null, 0, "Completed"));
        await notifier.Consume(ctx);
        await notifier.Consume(ctx);   // duplicate event

        await email.Received(1).SendAsync("owner@biz.com", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}

// Test-only in-memory sent guard.
file sealed class InMemorySentGuard : ISentGuard
{
    private readonly HashSet<Guid> _sent = new();
    public Task<bool> TryMarkSentAsync(Guid migrationId, CancellationToken ct) => Task.FromResult(_sent.Add(migrationId));
}
```

2. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~TerminalStateNotifierTests`. Expected **FAIL** — notifier/templates/abstractions not implemented.

3. - [ ] Implement.

```csharp
// src/EMaigrator.Api/Notifications/IAppEmailSender.cs
namespace EMaigrator.Api.Notifications;

public interface IAppEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct);
}

public sealed record NotificationContext(string ToEmail, string From, string To);

public interface INotificationRecipientResolver
{
    // Resolves the owning user's email + endpoint labels from the mailbox-migration id.
    Task<NotificationContext?> ResolveAsync(Guid mailboxMigrationId, CancellationToken ct);
}

public interface ISentGuard
{
    Task<bool> TryMarkSentAsync(Guid migrationId, CancellationToken ct);   // true = first time (send), false = already sent
}
```

```csharp
// src/EMaigrator.Api/Notifications/EmailTemplates.cs
namespace EMaigrator.Api.Notifications;

public static class EmailTemplates
{
    public static (string Subject, string HtmlBody) Render(string status, string from, string to,
        long migrated, long skipped, long failed)
    {
        var (subject, headline) = status switch
        {
            "Completed" => ($"Your {from} → {to} migration is complete", "Migration complete"),
            "Partial"   => ($"Your {from} → {to} migration needs your decision", "Migration finished — some items need your decision"),
            "Failed"    => ($"Your {from} → {to} migration failed", "Migration failed"),
            "Cancelled" => ($"Your {from} → {to} migration was cancelled", "Migration cancelled"),
            _           => ($"Your {from} → {to} migration update", "Migration update")
        };
        var body =
            $"<h2>{headline}</h2>" +
            $"<p>Moving mail from <strong>{from}</strong> to <strong>{to}</strong>.</p>" +
            $"<ul><li>{migrated} migrated</li><li>{skipped} skipped</li><li>{failed} failed</li></ul>" +
            "<p>Sign in to EMaigrator to view the full results and audit log.</p>";
        return (subject, body);
    }
}
```

```csharp
// src/EMaigrator.Api/Notifications/TerminalStateNotifier.cs
using EMaigrator.Core.Contracts;
using MassTransit;

namespace EMaigrator.Api.Notifications;

public sealed class TerminalStateNotifier : IConsumer<MigrationProgressEvent>
{
    private static readonly HashSet<string> Terminal = new() { "Completed", "Partial", "Failed", "Cancelled" };
    private readonly IAppEmailSender _email;
    private readonly INotificationRecipientResolver _resolver;
    private readonly ISentGuard _guard;

    public TerminalStateNotifier(IAppEmailSender email, INotificationRecipientResolver resolver, ISentGuard guard)
        => (_email, _resolver, _guard) = (email, resolver, guard);

    public async Task Consume(ConsumeContext<MigrationProgressEvent> ctx)
    {
        var m = ctx.Message;
        if (!Terminal.Contains(m.Status)) return;
        if (!await _guard.TryMarkSentAsync(m.MailboxMigrationId, ctx.CancellationToken)) return;   // already sent

        var recipient = await _resolver.ResolveAsync(m.MailboxMigrationId, ctx.CancellationToken);
        if (recipient is null) return;

        var (subject, body) = EmailTemplates.Render(m.Status, recipient.From, recipient.To, m.Migrated, 0, m.Total - m.Migrated);
        await _email.SendAsync(recipient.ToEmail, subject, body, ctx.CancellationToken);
    }
}
```

```csharp
// src/EMaigrator.Api/Data/NotificationSentRow.cs
namespace EMaigrator.Api.Data;

public sealed class NotificationSentRow
{
    public Guid MailboxMigrationId { get; set; }   // PK; presence = already notified
    public DateTimeOffset SentAt { get; set; }
}
```

Add to `ApiSideContext`: in `OnModelCreating` add `b.Entity<NotificationSentRow>().HasKey(r => r.MailboxMigrationId);` and the property `public DbSet<NotificationSentRow> NotificationsSent => Set<NotificationSentRow>();`. Then add the production guard, recipient resolver, and a default logging email sender:

```csharp
// src/EMaigrator.Api/Notifications/DbSentGuard.cs
using EMaigrator.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Notifications;

// Inserts a row keyed on MailboxMigrationId; a unique-violation (concurrent terminal events)
// means another instance already claimed it, so this caller must NOT send.
public sealed class DbSentGuard : ISentGuard
{
    private readonly ApiSideContext _side;
    public DbSentGuard(ApiSideContext side) => _side = side;

    public async Task<bool> TryMarkSentAsync(Guid migrationId, CancellationToken ct)
    {
        if (await _side.NotificationsSent.AnyAsync(r => r.MailboxMigrationId == migrationId, ct))
            return false;
        _side.NotificationsSent.Add(new NotificationSentRow { MailboxMigrationId = migrationId, SentAt = DateTimeOffset.UtcNow });
        try
        {
            await _side.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)   // unique-violation: a concurrent consumer already inserted
        {
            return false;
        }
    }
}
```

```csharp
// src/EMaigrator.Api/Notifications/DbNotificationRecipientResolver.cs
using EMaigrator.Api.Identity;
using EMaigrator.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Notifications;

// Joins MailboxMigration -> Job -> the owning tenant's first user; maps provider ids to display labels.
// Runs in a background scope (no HTTP principal), so it bypasses the tenant query filter and loads by key.
public sealed class DbNotificationRecipientResolver : INotificationRecipientResolver
{
    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        ["imap"] = "WorkMail", ["graph"] = "Microsoft 365", ["gmail"] = "Google"
    };

    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    public DbNotificationRecipientResolver(AppDbContext db, UserManager<ApplicationUser> users)
        => (_db, _users) = (db, users);

    public async Task<NotificationContext?> ResolveAsync(Guid mailboxMigrationId, CancellationToken ct)
    {
        var row = await _db.Set<MailboxMigration>().IgnoreQueryFilters()
            .Where(m => m.Id == mailboxMigrationId)
            .Join(_db.Set<Job>().IgnoreQueryFilters(), m => m.JobId, j => j.Id,
                (m, j) => new { j.TenantId, j.SourceProvider, j.DestProvider })
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;

        var user = await _users.Users.Where(u => u.TenantId == row.TenantId)
            .OrderBy(u => u.Email).FirstOrDefaultAsync(ct);
        if (user?.Email is null) return null;

        var from = Labels.GetValueOrDefault(row.SourceProvider.Value, row.SourceProvider.Value);
        var to = Labels.GetValueOrDefault(row.DestProvider.Value, row.DestProvider.Value);
        return new NotificationContext(user.Email, from, to);
    }
}
```

```csharp
// src/EMaigrator.Api/Notifications/LoggingEmailSender.cs
using Microsoft.Extensions.Logging;

namespace EMaigrator.Api.Notifications;

// Default self-host implementation: logs the email (no credentials in the body — asserted by tests).
// Hosted deployments swap this for an SMTP/provider-backed IAppEmailSender via DI.
public sealed class LoggingEmailSender : IAppEmailSender
{
    private readonly ILogger<LoggingEmailSender> _log;
    public LoggingEmailSender(ILogger<LoggingEmailSender> log) => _log = log;
    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct)
    {
        _log.LogInformation("Migration email to {To}: {Subject}", toEmail, subject);
        return Task.CompletedTask;
    }
}
```

Register all four in `ApiServiceCollectionExtensions.AddEMaigratorApi`:

```csharp
        services.AddScoped<ISentGuard, DbSentGuard>();
        services.AddScoped<INotificationRecipientResolver, DbNotificationRecipientResolver>();
        services.AddSingleton<IAppEmailSender, LoggingEmailSender>();
        // TerminalStateNotifier is an IConsumer; registered by the Plan 03 MassTransit consumer scan
        // (services.AddEMaigratorMassTransitConsumers(typeof(TerminalStateNotifier).Assembly);).
```

Regenerate the migration: `dotnet ef migrations add ApiSideNotifications -c ApiSideContext -p src/EMaigrator.Api -o Data/Migrations`.

4. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~TerminalStateNotifierTests`. Expected **PASS** (4 tests).

5. - [ ] Commit.

```bash
git add src/EMaigrator.Api src/EMaigrator.Api.Tests
git commit -m "feat(api): terminal-state email notifications (templates + idempotent consumer)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 12: CORS lock-down + security headers + auth-endpoint rate limiting

**Goal:** Lock CORS to configured origins, add a security-headers middleware (CSP, HSTS, X-Content-Type-Options, X-Frame-Options, Referrer-Policy), and apply a per-IP fixed-window rate limiter to the auth endpoints (brute-force guard).

**Files:**
- Create: `src/EMaigrator.Api/Security/SecurityHeadersMiddleware.cs`
- Create: `src/EMaigrator.Api/Security/RateLimitPolicies.cs`
- Modify: `src/EMaigrator.Api/AppConfiguration/ApiServiceCollectionExtensions.cs`
- Modify: `src/EMaigrator.Api/Program.cs`
- Modify: `src/EMaigrator.Api/Endpoints/AuthEndpoints.cs`
- Test: `src/EMaigrator.Api.Tests/SecurityHardeningTests.cs`

**Acceptance Criteria:**
- [ ] Every response carries `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, and a `Content-Security-Policy` header; HTTPS responses carry `Strict-Transport-Security`.
- [ ] CORS allows the configured origin (`http://localhost:5173`) and rejects an unconfigured origin (no `Access-Control-Allow-Origin` for a disallowed origin preflight).
- [ ] The auth rate-limit policy is named `"auth"` and is attached to `/auth/register` and `/auth/login`; exceeding the window returns 429.
- [ ] Normal API routes are unaffected by the auth limiter.

**Verify:** `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~SecurityHardeningTests` → all pass.

**Steps:**

1. - [ ] Write the failing test.

```csharp
// src/EMaigrator.Api.Tests/SecurityHardeningTests.cs
using System.Net;
using System.Net.Http.Json;
using EMaigrator.Api.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Api.Tests;

[Collection(PostgresCollection.Name)]
public class SecurityHardeningTests
{
    private readonly ApiTestFactory _factory;
    public SecurityHardeningTests(PostgresFixture pg) => _factory = new ApiTestFactory(pg.ConnectionString);

    [Fact]
    public async Task Security_headers_present_on_every_response()
    {
        using var client = _factory.CreateClient();
        var res = await client.GetAsync("/health");
        res.Headers.Contains("X-Content-Type-Options").Should().BeTrue();
        res.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        res.Headers.Contains("X-Frame-Options").Should().BeTrue();
        res.Headers.Contains("Referrer-Policy").Should().BeTrue();
        res.Headers.Contains("Content-Security-Policy").Should().BeTrue();
    }

    [Fact]
    public async Task Cors_allows_configured_origin_only()
    {
        using var client = _factory.CreateClient();
        var allowed = new HttpRequestMessage(HttpMethod.Options, "/api/v1/migrations");
        allowed.Headers.Add("Origin", "http://localhost:5173");
        allowed.Headers.Add("Access-Control-Request-Method", "GET");
        var ok = await client.SendAsync(allowed);
        ok.Headers.Contains("Access-Control-Allow-Origin").Should().BeTrue();

        var denied = new HttpRequestMessage(HttpMethod.Options, "/api/v1/migrations");
        denied.Headers.Add("Origin", "https://evil.example.com");
        denied.Headers.Add("Access-Control-Request-Method", "GET");
        var no = await client.SendAsync(denied);
        no.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Auth_login_is_rate_limited()
    {
        using var client = _factory.CreateClient();
        // Fixed bucket so this test trips 429 deterministically without contaminating other auth tests.
        client.DefaultRequestHeaders.Add("X-Client-Id", "ratelimit-task12");
        HttpResponseMessage? last = null;
        for (var i = 0; i < 25; i++)
            last = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "x@y.z", password = "nope-nope-nope" });
        // The fixed window (configured at 10/min) must trip 429 within 25 rapid attempts.
        last!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
```

2. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~SecurityHardeningTests`. Expected **FAIL** — headers/CORS/rate-limit not wired.

3. - [ ] Implement.

```csharp
// src/EMaigrator.Api/Security/SecurityHeadersMiddleware.cs
namespace EMaigrator.Api.Security;

public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext ctx)
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        h["Content-Security-Policy"] =
            "default-src 'self'; frame-ancestors 'none'; object-src 'none'; base-uri 'self'";
        if (ctx.Request.IsHttps)
            h["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        await _next(ctx);
    }
}
```

```csharp
// src/EMaigrator.Api/Security/RateLimitPolicies.cs
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace EMaigrator.Api.Security;

public static class RateLimitPolicies
{
    public const string Auth = "auth";
    // Client partition key header. In production a reverse proxy is not trusted to set this, so the
    // limiter falls back to the connection's remote IP. The in-process test host has no remote IP, so
    // tests set X-Client-Id to isolate buckets (each AuthClient gets a unique id; the rate-limit test
    // reuses one fixed id to trip the window deterministically without contaminating other tests).
    public const string ClientIdHeader = "X-Client-Id";

    public static IServiceCollection AddEMaigratorRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(Auth, ctx =>
            {
                var key = ctx.Connection.RemoteIpAddress?.ToString()
                          ?? ctx.Request.Headers[ClientIdHeader].ToString();
                if (string.IsNullOrEmpty(key)) key = "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(key,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
                    });
            });
        });
}
```

> Test isolation: because the `WebApplicationFactory` host exposes no `RemoteIpAddress`, the partition key falls back to the `X-Client-Id` request header. `AuthClient.CreateAsync` sets a fresh GUID `X-Client-Id` on each client so its register+login never collide with another test's auth bucket; `SecurityHardeningTests.Auth_login_is_rate_limited` and the Task 13 re-assertion set one fixed `X-Client-Id` and fire >10 requests to trip 429 deterministically.

Wire in `ApiServiceCollectionExtensions.AddEMaigratorApi`:

```csharp
        services.AddEMaigratorRateLimiting();
        var origins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        services.AddCors(o => o.AddDefaultPolicy(p => p
            .WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
```

In `Program.cs`, add the middleware + CORS + rate-limiter to the pipeline (security headers first, before everything; CORS before auth; rate limiter after routing):

```csharp
app.UseMiddleware<EMaigrator.Api.Security.SecurityHeadersMiddleware>();
app.UseCors();
app.UseRateLimiter();
```

Attach the `auth` policy to the auth endpoints in `AuthEndpoints.MapAuthEndpoints`. In Task 1 the `/auth/register` and `/auth/login` map calls each end with `.AllowAnonymous();`. Change that trailing `.AllowAnonymous();` on BOTH calls to:

```csharp
            .AllowAnonymous()
            .RequireRateLimiting(EMaigrator.Api.Security.RateLimitPolicies.Auth);
```

So the full chain for login becomes (register is identical, just with its own handler from Task 1):

```csharp
        group.MapPost("/auth/login", async (
            LoginRequest req, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
            IJwtTokenIssuer issuer, HttpContext http) =>
        {
            // ...unchanged Task 1 handler body...
            return Results.Ok(new LoginResponse(token, expires));
        })
            .AllowAnonymous()
            .RequireRateLimiting(EMaigrator.Api.Security.RateLimitPolicies.Auth);
```

> The `WebApplicationFactory` test host serves over HTTP, so the HSTS assertion is gated on `IsHttps`; the other four headers are unconditional and are what the test checks. The rate-limit test fires 25 attempts inside one fixed minute window against `PermitLimit=10` → the 11th+ returns 429.

4. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~SecurityHardeningTests`. Expected **PASS** (3 tests).

5. - [ ] Commit.

```bash
git add src/EMaigrator.Api src/EMaigrator.Api.Tests
git commit -m "feat(api): lock CORS to configured origins, add security headers + auth rate limiting

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 13: Security Verification (USER-ORDERED GATE)

**Goal:** Prove the API's security focus from the INDEX per-plan table: tenant isolation enforced; all non-public routes require auth; no secret values in any response; input validation; CORS locked; security headers present; auth endpoints rate-limited.

**USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in acceptanceCriteria has been re-validated independently, with output captured.

**Files:**
- Create: `src/EMaigrator.Api.Tests/Security/RouteAuthSweepTests.cs`
- Create: `src/EMaigrator.Api.Tests/Security/CrossTenantAccessTests.cs`
- Create: `src/EMaigrator.Api.Tests/Security/NoSecretInResponseTests.cs`
- Create: `src/EMaigrator.Api.Tests/Security/InputValidationSweepTests.cs`
- Create: `src/EMaigrator.Api.Tests/Security/HardeningReassertTests.cs`

**Acceptance Criteria:**
- [ ] **Auth sweep:** an automated test enumerates every non-public route (`POST/GET/PATCH/PUT/DELETE` under `/api/v1/migrations*`, and `/hubs/migrations`) and asserts each returns **401** when called with no token. Captured output lists each route + observed 401.
- [ ] **Tenant isolation:** tenant A creates a full migration (draft→scope); tenant B receives **404** on `GET/PATCH/PUT/POST/DELETE` for that id AND a SignalR `Subscribe` to it throws — proving the row-level filter denies cross-tenant access on both REST and realtime.
- [ ] **No secrets in responses:** a stored connection secret string is asserted absent from the bodies of `GET /migrations/{id}`, `PUT .../connection/from` (response), `POST .../connection/from/test`, `GET .../audit`, and `GET .../report?format=csv` (grep-style `.Should().NotContain(secret)` over each body).
- [ ] **Input validation:** malformed bodies (missing required field, bad enum, oversized/blank CSV, unknown `side`) each return **400/422** — never 500; captured output shows status per case.
- [ ] **CORS locked:** preflight from an unconfigured origin yields no `Access-Control-Allow-Origin` (re-asserted independently of Task 12 in `HardeningReassertTests`).
- [ ] **Security headers:** `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Content-Security-Policy` present on an arbitrary route (re-asserted in `HardeningReassertTests`).
- [ ] **Auth rate limit:** rapid repeated `/auth/login` attempts trip **429** (re-asserted in `HardeningReassertTests`).
- [ ] All five security test classes pass and the captured `dotnet test` output is attached to the task close-out.

**Verify:** `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~EMaigrator.Api.Tests.Security` → all pass (auth sweep, cross-tenant, no-secret, input-validation, hardening re-assert).

**Steps:**

1. - [ ] Write the failing security tests.

```csharp
// src/EMaigrator.Api.Tests/Security/RouteAuthSweepTests.cs
using System.Net;
using System.Net.Http.Json;
using EMaigrator.Api.Tests.Infrastructure;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace EMaigrator.Api.Tests.Security;

[Collection(PostgresCollection.Name)]
public class RouteAuthSweepTests
{
    private readonly ApiTestFactory _factory;
    private readonly ITestOutputHelper _out;
    public RouteAuthSweepTests(PostgresFixture pg, ITestOutputHelper o) { _factory = new ApiTestFactory(pg.ConnectionString); _out = o; }

    public static IEnumerable<object[]> ProtectedRoutes() => new[]
    {
        new object[] { "GET",    "/api/v1/migrations" },
        new object[] { "POST",   "/api/v1/migrations" },
        new object[] { "GET",    "/api/v1/migrations/00000000-0000-0000-0000-000000000001" },
        new object[] { "DELETE", "/api/v1/migrations/00000000-0000-0000-0000-000000000001" },
        new object[] { "PATCH",  "/api/v1/migrations/00000000-0000-0000-0000-000000000001/endpoints" },
        new object[] { "PUT",    "/api/v1/migrations/00000000-0000-0000-0000-000000000001/connection/from" },
        new object[] { "POST",   "/api/v1/migrations/00000000-0000-0000-0000-000000000001/connection/from/test" },
        new object[] { "PUT",    "/api/v1/migrations/00000000-0000-0000-0000-000000000001/scope" },
        new object[] { "POST",   "/api/v1/migrations/00000000-0000-0000-0000-000000000001/preflight" },
        new object[] { "GET",    "/api/v1/migrations/00000000-0000-0000-0000-000000000001/preflight" },
        new object[] { "POST",   "/api/v1/migrations/00000000-0000-0000-0000-000000000001/approve" },
        new object[] { "POST",   "/api/v1/migrations/00000000-0000-0000-0000-000000000001/pause" },
        new object[] { "POST",   "/api/v1/migrations/00000000-0000-0000-0000-000000000001/resume" },
        new object[] { "POST",   "/api/v1/migrations/00000000-0000-0000-0000-000000000001/cancel" },
        new object[] { "GET",    "/api/v1/migrations/00000000-0000-0000-0000-000000000001/results" },
        new object[] { "GET",    "/api/v1/migrations/00000000-0000-0000-0000-000000000001/audit" },
        new object[] { "POST",   "/api/v1/migrations/00000000-0000-0000-0000-000000000001/rerun" },
        new object[] { "GET",    "/api/v1/migrations/00000000-0000-0000-0000-000000000001/report" },
    };

    [Theory]
    [MemberData(nameof(ProtectedRoutes))]
    public async Task Protected_route_returns_401_without_token(string method, string path)
    {
        using var client = _factory.CreateClient();
        var req = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT" or "PATCH") req.Content = JsonContent.Create(new { });
        var res = await client.SendAsync(req);
        _out.WriteLine($"{method,-6} {path} -> {(int)res.StatusCode}");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{method} {path} must require auth");
    }

    [Fact]
    public async Task Hub_rejects_unauthenticated_connection()
    {
        using var client = _factory.CreateClient();
        // SignalR negotiate without a token must be rejected (401).
        var res = await client.PostAsync("/hubs/migrations/negotiate?negotiateVersion=1", null);
        _out.WriteLine($"HUB negotiate -> {(int)res.StatusCode}");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

```csharp
// src/EMaigrator.Api.Tests/Security/CrossTenantAccessTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EMaigrator.Api.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace EMaigrator.Api.Tests.Security;

[Collection(PostgresCollection.Name)]
public class CrossTenantAccessTests
{
    private readonly ApiTestFactory _factory;
    public CrossTenantAccessTests(PostgresFixture pg) => _factory = new ApiTestFactory(pg.ConnectionString).WithFakeImapPlugin();

    [Fact]
    public async Task Tenant_B_cannot_touch_tenant_A_migration_over_rest_or_signalr()
    {
        var (a, _) = await AuthClient.CreateAsync(_factory);
        var (b, _) = await AuthClient.CreateAsync(_factory);
        var id = (await (await a.PostAsJsonAsync("/api/v1/migrations", new { }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        await a.PatchAsJsonAsync($"/api/v1/migrations/{id}/endpoints", new { from = "imap", to = "graph" });

        foreach (var (method, path) in new[]
        {
            ("GET", $"/api/v1/migrations/{id}"),
            ("POST", $"/api/v1/migrations/{id}/preflight"),
            ("GET", $"/api/v1/migrations/{id}/results"),
            ("GET", $"/api/v1/migrations/{id}/audit"),
            ("DELETE", $"/api/v1/migrations/{id}"),
        })
        {
            var req = new HttpRequestMessage(new HttpMethod(method), path);
            var res = await b.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.NotFound, $"tenant B must get 404 for {method} {path}");
        }

        var patch = await b.PatchAsJsonAsync($"/api/v1/migrations/{id}/endpoints", new { from = "imap", to = "gmail" });
        patch.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var tokenB = b.DefaultRequestHeaders.Authorization!.Parameter!;
        var conn = new HubConnectionBuilder()
            .WithUrl(_factory.Server.BaseAddress + "hubs/migrations?access_token=" + tokenB,
                o => o.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler())
            .Build();
        await conn.StartAsync();
        var act = async () => await conn.InvokeAsync("Subscribe", id);
        await act.Should().ThrowAsync<Exception>("tenant B must not subscribe to tenant A's migration");
        await conn.DisposeAsync();
    }
}
```

```csharp
// src/EMaigrator.Api.Tests/Security/NoSecretInResponseTests.cs
using System.Net.Http.Json;
using System.Text.Json;
using EMaigrator.Api.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Api.Tests.Security;

[Collection(PostgresCollection.Name)]
public class NoSecretInResponseTests
{
    private readonly ApiTestFactory _factory;
    public NoSecretInResponseTests(PostgresFixture pg) => _factory = new ApiTestFactory(pg.ConnectionString).WithFakeImapPlugin();

    [Fact]
    public async Task Secret_never_appears_in_any_response_body()
    {
        const string secret = "TOPSECRET-app-password-9f3c-DEADBEEF";
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var id = (await (await client.PostAsJsonAsync("/api/v1/migrations", new { }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        await client.PatchAsJsonAsync($"/api/v1/migrations/{id}/endpoints", new { from = "imap", to = "graph" });

        var putBody = await (await client.PutAsJsonAsync($"/api/v1/migrations/{id}/connection/from", new
        {
            auth = "ImapBasic", settings = new { host = "h", port = "993", accountEmail = "a@b.c" }, secret
        })).Content.ReadAsStringAsync();

        var getBody = await (await client.GetAsync($"/api/v1/migrations/{id}")).Content.ReadAsStringAsync();
        var testBody = await (await client.PostAsync($"/api/v1/migrations/{id}/connection/from/test", null)).Content.ReadAsStringAsync();
        var auditBody = await (await client.GetAsync($"/api/v1/migrations/{id}/audit")).Content.ReadAsStringAsync();

        foreach (var body in new[] { putBody, getBody, testBody, auditBody })
            body.Should().NotContain(secret, "no API response may contain a credential value");
    }
}
```

```csharp
// src/EMaigrator.Api.Tests/Security/InputValidationSweepTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EMaigrator.Api.Tests.Infrastructure;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace EMaigrator.Api.Tests.Security;

[Collection(PostgresCollection.Name)]
public class InputValidationSweepTests
{
    private readonly ApiTestFactory _factory;
    private readonly ITestOutputHelper _out;
    public InputValidationSweepTests(PostgresFixture pg, ITestOutputHelper o) { _factory = new ApiTestFactory(pg.ConnectionString).WithFakeImapPlugin(); _out = o; }

    [Fact]
    public async Task Malformed_bodies_return_4xx_never_500()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);
        var id = (await (await client.PostAsJsonAsync("/api/v1/migrations", new { }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        // Bad enum auth + unknown side.
        var badAuth = await client.PutAsJsonAsync($"/api/v1/migrations/{id}/connection/from",
            new { auth = "NotAnAuthMethod", settings = new { host = "h" }, secret = "x" });
        var badSide = await client.PutAsJsonAsync($"/api/v1/migrations/{id}/connection/sideways",
            new { auth = "ImapBasic", settings = new { host = "h" }, secret = "x" });

        // Blank CSV upload.
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("source_mailbox,destination_mailbox\n"));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", "empty.csv");
        await client.PatchAsJsonAsync($"/api/v1/migrations/{id}/endpoints", new { from = "imap", to = "graph" });
        var emptyCsv = await client.PutAsync($"/api/v1/migrations/{id}/scope", content);

        // Missing required register field.
        var badReg = await client.PostAsJsonAsync("/api/v1/auth/register", new { email = "not-an-email", password = "short" });

        foreach (var (name, res) in new[]
        {
            ("badAuth", badAuth), ("badSide", badSide), ("emptyCsv", emptyCsv), ("badReg", badReg)
        })
        {
            _out.WriteLine($"{name} -> {(int)res.StatusCode}");
            ((int)res.StatusCode).Should().BeInRange(400, 422, $"{name} must be a client error, not 500");
            res.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        }
    }
}
```

```csharp
// src/EMaigrator.Api.Tests/Security/HardeningReassertTests.cs
using System.Net;
using System.Net.Http.Json;
using EMaigrator.Api.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Api.Tests.Security;

// Independent re-assertion of the Task 12 hardening (CORS, security headers, auth rate limit)
// so the security gate stands on its own evidence rather than trusting Task 12's class.
[Collection(PostgresCollection.Name)]
public class HardeningReassertTests
{
    private readonly ApiTestFactory _factory;
    public HardeningReassertTests(PostgresFixture pg) => _factory = new ApiTestFactory(pg.ConnectionString);

    [Fact]
    public async Task Security_headers_present()
    {
        using var client = _factory.CreateClient();
        var res = await client.GetAsync("/health");
        res.Headers.Contains("X-Content-Type-Options").Should().BeTrue();
        res.Headers.Contains("X-Frame-Options").Should().BeTrue();
        res.Headers.Contains("Referrer-Policy").Should().BeTrue();
        res.Headers.Contains("Content-Security-Policy").Should().BeTrue();
    }

    [Fact]
    public async Task Cors_rejects_unconfigured_origin()
    {
        using var client = _factory.CreateClient();
        var denied = new HttpRequestMessage(HttpMethod.Options, "/api/v1/migrations");
        denied.Headers.Add("Origin", "https://evil.example.com");
        denied.Headers.Add("Access-Control-Request-Method", "GET");
        var res = await client.SendAsync(denied);
        res.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Auth_login_trips_429()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Client-Id", "ratelimit-gate-task13");
        HttpResponseMessage? last = null;
        for (var i = 0; i < 25; i++)
            last = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "x@y.z", password = "nope-nope-nope" });
        last!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
```

2. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~EMaigrator.Api.Tests.Security`. Expected **FAIL** initially only if any hardening is missing; if Tasks 0–12 are complete, fix any failure surfaced (this is the gate — do not weaken assertions to pass).

3. - [ ] If any assertion fails, remediate the underlying API (e.g., an endpoint missing the fallback auth policy, a body leaking a secret, a 500 on malformed input) — never adjust the test to pass. Re-run until green. Capture the full passing output (including the auth-sweep route list and input-validation status lines) for the close-out.

4. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~EMaigrator.Api.Tests.Security`. Expected **PASS** (auth sweep over ~18 routes + hub, cross-tenant REST+SignalR, no-secret across 4 bodies, input-validation sweep, and hardening re-assert: headers + CORS + 429). Attach captured stdout.

5. - [ ] Commit.

```bash
git add src/EMaigrator.Api.Tests
git commit -m "test(api): security gate — auth sweep, tenant isolation, no-secret, input validation

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 14: Functional Verification — end-to-end wizard happy path against the live API

**Goal:** Prove the subsystem's headline behavior end-to-end: an authenticated operator drives a full migration through the REST surface (create draft → set endpoints → connect both sides → test → scope → preflight → approve → results → report), receiving live SignalR progress and a terminal email — all against the in-process API with a fake provider + recording orchestrator + inline preflight.

**Files:**
- Create: `src/EMaigrator.Api.Tests/Functional/FullWizardFlowTests.cs`

**Acceptance Criteria:**
- [ ] A single test walks: register/login → `POST /migrations` (Draft, step 1) → `PATCH /endpoints` → `PUT /connection/from` + `/to` → `POST /connection/from/test` returns `ok=true` → `PUT /scope` → `POST /preflight` (202) → `GET /preflight` returns the plan → `POST /approve` (→Running, orchestrator enqueued) → after a simulated terminal `MigrationProgressEvent` is bridged, the subscribed SignalR client receives `Progress` + `StatusChanged("Completed")` → `GET /results` reconciles counts → `GET /report?format=pdf` returns a PDF.
- [ ] The orchestrator recorded exactly one enqueue per mailbox.
- [ ] The terminal-state notifier sent exactly one email (asserted via a capturing `IAppEmailSender`).
- [ ] The whole flow runs without a 5xx anywhere (every intermediate response asserted `IsSuccessStatusCode` or the expected 202/200).

**Verify:** `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~FullWizardFlowTests` → all pass.

**Steps:**

1. - [ ] Write the failing end-to-end test.

```csharp
// src/EMaigrator.Api.Tests/Functional/FullWizardFlowTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EMaigrator.Api.Notifications;
using EMaigrator.Api.Realtime;
using EMaigrator.Api.Tests.Infrastructure;
using EMaigrator.Core.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EMaigrator.Api.Tests.Functional;

[Collection(PostgresCollection.Name)]
public class FullWizardFlowTests
{
    private readonly ApiTestFactory _factory;
    public FullWizardFlowTests(PostgresFixture pg) =>
        _factory = new ApiTestFactory(pg.ConnectionString)
            .WithFakeImapPlugin().WithFakePreflight().WithRecordingOrchestrator().WithCapturingEmail();

    [Fact]
    public async Task Operator_drives_full_migration_end_to_end()
    {
        var (client, _) = await AuthClient.CreateAsync(_factory);

        var create = await client.PostAsJsonAsync("/api/v1/migrations", new { });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        (await client.PatchAsJsonAsync($"/api/v1/migrations/{id}/endpoints", new { from = "imap", to = "graph" }))
            .IsSuccessStatusCode.Should().BeTrue();
        (await client.PutAsJsonAsync($"/api/v1/migrations/{id}/connection/from",
            new { auth = "ImapBasic", settings = new { host = "h", port = "993", accountEmail = "a@b.c" }, secret = "pw" }))
            .IsSuccessStatusCode.Should().BeTrue();
        (await client.PutAsJsonAsync($"/api/v1/migrations/{id}/connection/to",
            new { auth = "ImapBasic", settings = new { host = "h2", port = "993", accountEmail = "d@e.f" }, secret = "pw2" }))
            .IsSuccessStatusCode.Should().BeTrue();

        FakeImapPlugin.CurrentMode = FakeImapPlugin.Mode.Ok;
        var test = await client.PostAsync($"/api/v1/migrations/{id}/connection/from/test", null);
        (await test.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ok").GetBoolean().Should().BeTrue();

        (await client.PutAsJsonAsync($"/api/v1/migrations/{id}/scope",
            new { isBatch = false, pairs = new[] { new { sourceMailbox = "a@b.c", destMailbox = "d@e.f" } } }))
            .IsSuccessStatusCode.Should().BeTrue();

        (await client.PostAsync($"/api/v1/migrations/{id}/preflight", null)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await client.GetAsync($"/api/v1/migrations/{id}/preflight")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Subscribe to live progress.
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        var conn = new HubConnectionBuilder()
            .WithUrl(_factory.Server.BaseAddress + "hubs/migrations?access_token=" + token,
                o => o.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler()).Build();
        var statusTcs = new TaskCompletionSource<string>();
        conn.On<MigrationProgressDto>(nameof(IMigrationProgressClient.Progress), _ => { });
        conn.On<string, string>(nameof(IMigrationProgressClient.StatusChanged), (mid, st) => { if (st == "Completed") statusTcs.TrySetResult(st); });
        await conn.StartAsync();
        await conn.InvokeAsync("Subscribe", id);

        var approve = await client.PostAsJsonAsync($"/api/v1/migrations/{id}/approve",
            new { resolutions = new Dictionary<string, string> { ["FolderDepth"] = "FlattenFolder" } });
        (await approve.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("Running");

        var orch = (RecordingOrchestrator)_factory.Services.GetRequiredService<IJobOrchestrator>();
        orch.Enqueued.Should().HaveCount(1);

        // Simulate the worker finishing: bridge a terminal progress event for the mailbox + fire the notifier.
        var mailboxId = orch.Enqueued.Single();
        var notifier = _factory.Services.GetRequiredService<IMigrationGroupNotifier>();
        await notifier.PushProgressAsync(new MigrationProgressDto(id, 3201, 3201, null, 0, "Completed"));
        await notifier.PushStatusChangedAsync(id, "Completed");

        (await Task.WhenAny(statusTcs.Task, Task.Delay(5000))).Should().Be(statusTcs.Task, "completed status should fan out over SignalR");
        await conn.DisposeAsync();

        // Drive the email notifier directly (the worker would publish MigrationProgressEvent in production).
        var emailNotifier = new TerminalStateNotifier(
            _factory.Services.GetRequiredService<IAppEmailSender>(),
            _factory.Services.GetRequiredService<INotificationRecipientResolver>(),
            new OneShotSentGuard());
        var ev = NSubstitute.Substitute.For<MassTransit.ConsumeContext<EMaigrator.Core.Contracts.MigrationProgressEvent>>();
        ev.Message.Returns(new EMaigrator.Core.Contracts.MigrationProgressEvent(mailboxId, 3201, 3201, null, 0, "Completed"));
        await emailNotifier.Consume(ev);
        ((CapturingEmailSender)_factory.Services.GetRequiredService<IAppEmailSender>()).Sent.Should().HaveCount(1);

        var results = await client.GetFromJsonAsync<JsonElement>($"/api/v1/migrations/{id}/results");
        results.GetProperty("counts").TryGetProperty("migrated", out _).Should().BeTrue();

        var report = await client.GetAsync($"/api/v1/migrations/{id}/report?format=pdf");
        report.StatusCode.Should().Be(HttpStatusCode.OK);
        report.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
    }
}

file sealed class OneShotSentGuard : ISentGuard
{
    private bool _done;
    public Task<bool> TryMarkSentAsync(Guid id, CancellationToken ct) { var first = !_done; _done = true; return Task.FromResult(first); }
}
```

`WithCapturingEmail` + `CapturingEmailSender` (registered in `ApiTestFactory`):

```csharp
// src/EMaigrator.Api.Tests/Infrastructure/CapturingEmailSender.cs
using EMaigrator.Api.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace EMaigrator.Api.Tests.Infrastructure;

public sealed class CapturingEmailSender : IAppEmailSender
{
    public List<(string To, string Subject, string Body)> Sent { get; } = new();
    public Task SendAsync(string to, string subject, string body, CancellationToken ct)
    { Sent.Add((to, subject, body)); return Task.CompletedTask; }
}

public static class CapturingEmailExtensions
{
    public static ApiTestFactory WithCapturingEmail(this ApiTestFactory f) => f;
    public static void AddCapturingEmail(IServiceCollection services)
        => services.AddSingleton<IAppEmailSender, CapturingEmailSender>();
}
```

> Register `AddCapturingEmail(services)` and a stub `INotificationRecipientResolver` (returns `new NotificationContext("owner@biz.com","WorkMail","Microsoft 365")`) in `ApiTestFactory.ConfigureWebHost` → `ConfigureServices`.

2. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~FullWizardFlowTests`. Expected **FAIL** if any wiring is incomplete; this test is the integration capstone — fix real wiring until it passes.

3. - [ ] Remediate any genuine gap surfaced by the flow (DI registration, endpoint order, DTO mapping). Do not stub past failures.

4. - [ ] Run it: `dotnet test src/EMaigrator.Api.Tests --filter FullyQualifiedName~FullWizardFlowTests`. Expected **PASS** (1 end-to-end test) — full wizard green including SignalR completion + one email + PDF report.

5. - [ ] Commit.

```bash
git add src/EMaigrator.Api.Tests
git commit -m "test(api): functional end-to-end wizard flow (REST + SignalR + email + report)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Cross-plan integration notes

- **Infrastructure (Plan 03) seams consumed:** `AppDbContext` (with `ITenantProvider` query filter), `ISecretStore`, `ILedger`, `IJobOrchestrator`, `IRateLimiter`, MassTransit registration extension, health checks. The API supplies the per-request tenant value via `ICurrentTenant` → `ITenantProvider`.
- **Core (Plan 02) seams consumed:** `IErrorCatalog`, `IPreflightAnalyzer`, `ConnectionDescriptor`/`ConnectionTestResult`/`SecretBundle`, `ScopeSpec`/`MailboxPair`/`PreflightPlan`, `RemediationAction`, the MassTransit `MigrationProgressEvent`/`NeedsDecisionEvent` contracts. The API maps these to camelCase DTOs verbatim per CONTRACTS §6 — it never redefines them.
- **Workers (Plan 07) interaction:** workers publish `MigrationProgressEvent`/`NeedsDecisionEvent`; the API's `MigrationProgressBridge` + `TerminalStateNotifier` consume them. The API never runs the migration itself — `approve`/`rerun` only enqueue via `IJobOrchestrator`.
- **API-owned tables (NOT in CONTRACTS §5, so kept in `ApiSideContext`):** `PreflightResultRow`, `ApprovedResolutionRow`, `NotificationSentRow`. These are presentation/orchestration state, not engine state, so they live in the API project's own context to keep the frozen `Job`/`MailboxMigration` shapes untouched.

