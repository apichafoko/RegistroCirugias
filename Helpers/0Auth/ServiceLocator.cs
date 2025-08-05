// ServiceLocator.cs
using System;
using System.Net.Http;
using RegistroCx.Services.Repositories;
using RegistroCx.Helpers.OnBoarding;
using RegistroCx.Helpers._0Auth;

namespace RegistroCx.Helpers._0Auth;

public static class ServiceLocator
{
    public static IUserProfileRepository UserRepo { get; private set; } = null!;
    public static GoogleOAuthOptions GoogleOAuthOptions { get; private set; } = null!;
    public static IGoogleOAuthService GoogleOAuth { get; private set; } = null!;
    public static IOnboardingService Onboarding { get; private set; } = null!;
    public static HttpClient SharedHttp { get; private set; } = null!;

    private static bool _initialized;

    public static void Init(string? connString)
    {
        if (_initialized) return;

        if (string.IsNullOrWhiteSpace(connString))
            throw new InvalidOperationException("DB_CONN / cadena de conexión vacía.");

        UserRepo = new UserProfileRepository(connString);

        var clientId = Env("GOOGLE_CLIENT_ID");
        var clientSecret = Env("GOOGLE_CLIENT_SECRET");
        var redirect = Env("GOOGLE_REDIRECT_URI");

        GoogleOAuthOptions = new GoogleOAuthOptions
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            RedirectUri = redirect
        };

        SharedHttp = new HttpClient();
        GoogleOAuth = new GoogleOAuthService(GoogleOAuthOptions, SharedHttp, UserRepo);
        //Onboarding = new OnboardingService(UserRepo, GoogleOAuth);
        //Onboarding = new OnboardingService(UserRepo);

        _initialized = true;
    }

    private static string Env(string key) =>
        Environment.GetEnvironmentVariable(key)
        ?? throw new InvalidOperationException($"Falta variable de entorno '{key}'.");
}
