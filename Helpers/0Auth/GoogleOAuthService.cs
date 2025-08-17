
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RegistroCx.Domain;

namespace RegistroCx.Helpers._0Auth;

/// <summary>
/// Implementa el flujo OAuth para Google Calendar (construcción de URL, intercambio y refresh).
/// </summary>
public class GoogleOAuthService : IGoogleOAuthService
{
    private readonly GoogleOAuthOptions _opt;
    private readonly HttpClient _http;
    private readonly Services.Repositories.IUserProfileRepository _repo;

    public GoogleOAuthService(GoogleOAuthOptions opt,
                              HttpClient http,
                              Services.Repositories.IUserProfileRepository repo)
    {
        _opt = opt;
        _http = http;
        _repo = repo;
    }

    public string BuildAuthUrl(long chatId, string userEmail)
    {
        var nonce = Guid.NewGuid().ToString("N");
        // Guardamos el nonce (state)
        _repo.StoreOAuthStateAsync(chatId, nonce, CancellationToken.None)
             .GetAwaiter().GetResult();

        var state = $"{chatId}:{nonce}";
        var scope = Uri.EscapeDataString("https://www.googleapis.com/auth/calendar.events");
        var redirect = Uri.EscapeDataString(_opt.RedirectUri);

        // prompt=consent + access_type=offline garantizan refresh_token la 1ª vez
        var url =
            $"{_opt.AuthBase}?response_type=code" +
            $"&client_id={_opt.ClientId}" +
            $"&redirect_uri={redirect}" +
            $"&scope={scope}" +
            $"&access_type=offline&prompt=consent" +
            $"&state={state}" +
            $"&login_hint={Uri.EscapeDataString(userEmail)}";

        return url;
    }

    public async Task<TokenResponse?> ExchangeCodeAsync(string code, string state, CancellationToken ct)
    {
        // state = "<chatId>:<nonce>"
        var parts = state.Split(':', 2);
        if (parts.Length != 2) return null;
        if (!long.TryParse(parts[0], out var chatId)) return null;
        var nonce = parts[1];

        var profile = await _repo.GetAsync(chatId, ct);
        if (profile == null || profile.OAuthNonce != nonce)
            return null; // state inválido

        var form = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = _opt.ClientId,
            ["client_secret"] = _opt.ClientSecret,
            ["redirect_uri"] = _opt.RedirectUri,
            ["grant_type"] = "authorization_code"
        };

        var res = await _http.PostAsync(_opt.TokenEndpoint, new FormUrlEncodedContent(form), ct);
        if (!res.IsSuccessStatusCode)
        {
            var errBody = await res.Content.ReadAsStringAsync(ct);
            Console.WriteLine("[OAuth] Error intercambio code: " + errBody);
            return null;
        }

        var json = await res.Content.ReadAsStringAsync(ct);
        var root = JsonDocument.Parse(json).RootElement;

        var access = root.GetProperty("access_token").GetString()!;
        var expiresIn = root.TryGetProperty("expires_in", out var expiresEl) ? expiresEl.GetInt32() : 3600;
        var refresh = root.TryGetProperty("refresh_token", out var refreshEl) ? refreshEl.GetString() : null;

        var token = new TokenResponse
        {
            AccessToken = access,
            RefreshToken = refresh,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60),
            RawJson = json
        };

        await _repo.UpdateTokensAsync(chatId, token.AccessToken, token.RefreshToken, token.ExpiresAt?.UtcDateTime, ct);

        // Opcional marcar Ready acá (o dejar que Onboarding lo haga)
        if (profile.State != UserState.Ready)
        {
            profile.GoogleAccessToken = token.AccessToken;
            profile.GoogleRefreshToken = token.RefreshToken ?? profile.GoogleRefreshToken;
            profile.GoogleTokenExpiry = token.ExpiresAt?.UtcDateTime;
            profile.OAuthNonce = null;
            profile.State = UserState.Ready;
            profile.CalendarAutorizado = true; // ✅ Marcar calendario como autorizado
            await _repo.SaveAsync(profile, ct);
        }

        return token;
    }

    public async Task<TokenResponse?> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["refresh_token"] = refreshToken,
            ["client_id"] = _opt.ClientId,
            ["client_secret"] = _opt.ClientSecret,
            ["grant_type"] = "refresh_token"
        };

        var res = await _http.PostAsync(_opt.TokenEndpoint, new FormUrlEncodedContent(form), ct);
        if (!res.IsSuccessStatusCode)
        {
            var errBody = await res.Content.ReadAsStringAsync(ct);
            Console.WriteLine("[OAuth] Error refresh: " + errBody);
            return null;
        }

        var json = await res.Content.ReadAsStringAsync(ct);
        var root = JsonDocument.Parse(json).RootElement;
        var access = root.GetProperty("access_token").GetString()!;
        var expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;

        return new TokenResponse
        {
            AccessToken = access,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60),
            RawJson = json
        };
    }
}
