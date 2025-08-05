using System;

namespace RegistroCx.Helpers._0Auth;

public class TokenResponse
{
    public string AccessToken { get; set; } = "";
    public string? RefreshToken { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string RawJson { get; set; } = "";
}
