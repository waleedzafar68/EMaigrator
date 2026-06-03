using System;
using System.ComponentModel.DataAnnotations;

namespace EMaigrator.Api.Contracts;

/// <summary>Registration payload: creates a tenant + the first user in it.</summary>
public sealed record RegisterRequest(
    [property: Required, EmailAddress] string Email,
    [property: Required, MinLength(12)] string Password,
    [property: Required, MinLength(1), MaxLength(256)] string OrganizationName);

/// <summary>Login payload: email + password.</summary>
public sealed record LoginRequest(
    [property: Required, EmailAddress] string Email,
    [property: Required] string Password);

/// <summary>Login result: the access token and its absolute expiry.</summary>
public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt);
