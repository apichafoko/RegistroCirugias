using System;
using RegistroCx.ProgramServices.Configuration;
using RegistroCx.Services.Repositories;
using RegistroCx.Services.Onboarding;
using RegistroCx.Services.Extraction;
using RegistroCx.Helpers._0Auth;
using RegistroCx.Services;
using Telegram.Bot;
using RegistroCx.ProgramServices.Services.Telegram;

namespace RegistroCx.ProgramServices.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection ConfigureApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Configuración
        services.Configure<TelegramBotOptions>(options =>
        {
            options.Token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN") 
                ?? throw new InvalidOperationException("Falta TELEGRAM_BOT_TOKEN");
            options.Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        });

        services.Configure<OpenAIOptions>(options =>
        {
            options.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "dummy";
            options.AssistantId = Environment.GetEnvironmentVariable("OPENAI_ASSISTANT_ID") ?? "assistant_dummy";
        });

        services.Configure<DatabaseOptions>(options =>
        {
            options.ConnectionString = Environment.GetEnvironmentVariable("DB_CONN") ?? "dummy";
        });

        services.Configure<GoogleOAuthOptions>(options =>
        {
            options.ClientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID")!;
            options.ClientSecret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET")!;
            options.RedirectUri = Environment.GetEnvironmentVariable("GOOGLE_REDIRECT_URI")!;
            options.AuthBase = configuration["GoogleOAuth:AuthBase"]!;
            options.TokenEndpoint = configuration["GoogleOAuth:TokenEndpoint"]!;
        });

        // HttpClients
        services.AddHttpClient<TelegramBotClient>("telegram_bot_client")
            .ConfigurePrimaryHttpMessageHandler<TelegramHttpClientHandler>()
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromMinutes(5);
                client.DefaultRequestHeaders.Add("User-Agent", "RegistroCx-Bot/1.0");
            });

        services.AddHttpClient("general");

        // Servicios principales
        services.AddSingleton<TelegramBotClient>(provider =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<TelegramBotOptions>>();
            var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient("telegram_bot_client");
            return new TelegramBotClient(options.Value.Token, httpClient);
        });
        services.AddSingleton<TelegramHttpClientHandler>();
        
        services.AddScoped<IUserProfileRepository>(provider =>
        {
            var dbOptions = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>();
            return new UserProfileRepository(dbOptions.Value.ConnectionString);
        });
        
        services.AddScoped<RegistroCx.Helpers._0Auth.IGoogleOAuthService>(provider =>
        {
            var googleOptions = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<GoogleOAuthOptions>>().Value;
            var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient("general");
            var userRepo = provider.GetRequiredService<IUserProfileRepository>();
            return new RegistroCx.Helpers._0Auth.GoogleOAuthService(googleOptions, httpClient, userRepo);
        });
        services.AddScoped<IOnboardingService, RegistroCx.Services.Onboarding.OnboardingService>();
        services.AddScoped<LLMOpenAIAssistant>(provider =>
        {
            var openAiOptions = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenAIOptions>>().Value;
            return new LLMOpenAIAssistant(openAiOptions.ApiKey);
        });
        
        // Diccionario compartido para mantener estado entre requests
        services.AddSingleton<Dictionary<long, RegistroCx.Models.Appointment>>();
        services.AddScoped<CirugiaFlowService>();

        // Background services
        services.AddHostedService<TelegramBotService>();

        return services;
    }
}
