using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using EMaigrator.Api.Contracts;
using EMaigrator.Api.Identity;
using EMaigrator.Infrastructure.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Endpoints;

/// <summary>
/// The anonymous auth surface (Task 1): register creates a Tenant (engine context) + an
/// ApplicationUser (Api-local Identity store), and login validates the password then issues a
/// tenant-scoped JWT plus an HttpOnly auth cookie. No auth middleware exists yet (Task 2), so these
/// endpoints are explicitly <see cref="OpenApiRouteHandlerBuilderExtensions"/>-anonymous.
/// </summary>
public static class AuthEndpoints
{
    /// <summary>The HttpOnly cookie carrying the access token alongside the JSON response.</summary>
    public const string AuthCookieName = "emaigrator.auth";

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var auth = group.MapGroup("/auth");

        auth.MapPost("/register", RegisterAsync).AllowAnonymous();
        auth.MapPost("/login", LoginAsync).AllowAnonymous();

        return group;
    }

    private static async Task<IResult> RegisterAsync(
        [FromBody] RegisterRequest request,
        [FromServices] IDbContextFactory<EmaigratorDbContext> dbFactory,
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(dbFactory);
        ArgumentNullException.ThrowIfNull(userManager);

        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(request);
        if (!Validator.TryValidateObject(request, context, validationResults, validateAllProperties: true))
        {
            return Results.ValidationProblem(ToErrorDictionary(validationResults));
        }

        var tenantId = Guid.NewGuid();

        // Create the user first: the common failure (duplicate email, etc.) happens here, so we
        // must not write a Tenant row that would then be orphaned.
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = request.Email,
            Email = request.Email,
            TenantId = tenantId,
        };

        var created = await userManager.CreateAsync(user, request.Password).ConfigureAwait(false);
        if (!created.Succeeded)
        {
            var errors = created.Errors
                .GroupBy(e => e.Code, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray(), StringComparer.Ordinal);
            return Results.ValidationProblem(errors);
        }

        // Only after the user exists, write the Tenant row in the engine context (no query filter →
        // writable anonymously). If this throws, best-effort delete the just-created user so we never
        // leave a user without a tenant, then let the exception propagate.
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
            db.Tenants.Add(new Tenant { Id = tenantId, Name = request.OrganizationName });
            await db.SaveChangesAsync().ConfigureAwait(false);
        }
        catch
        {
            await userManager.DeleteAsync(user).ConfigureAwait(false);
            throw;
        }

        return Results.Created($"/api/v1/users/{user.Id}", new { id = user.Id, tenantId });
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest request,
        HttpContext httpContext,
        [FromServices] UserManager<ApplicationUser> userManager,
        [FromServices] SignInManager<ApplicationUser> signInManager,
        [FromServices] IJwtTokenIssuer tokenIssuer)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(signInManager);
        ArgumentNullException.ThrowIfNull(tokenIssuer);

        var user = await userManager.FindByEmailAsync(request.Email).ConfigureAwait(false);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var result = await signInManager
            .CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return Results.Unauthorized();
        }

        var (token, expiresAt) = tokenIssuer.Issue(user);

        httpContext.Response.Cookies.Append(AuthCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = expiresAt,
        });

        return Results.Ok(new LoginResponse(token, expiresAt));
    }

    private static Dictionary<string, string[]> ToErrorDictionary(
        IEnumerable<ValidationResult> validationResults)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var result in validationResults)
        {
            var message = result.ErrorMessage ?? "Invalid value.";
            var members = result.MemberNames.Any() ? result.MemberNames : new[] { "" };
            foreach (var member in members)
            {
                errors[member] = errors.TryGetValue(member, out var existing)
                    ? existing.Append(message).ToArray()
                    : new[] { message };
            }
        }

        return errors;
    }
}
