namespace Stockify.Models.Dtos;

/// <summary>A signed JWT and when it expires.</summary>
public sealed record AccessToken(string Token, DateTime ExpiresAt);
