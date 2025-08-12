using System;
using RegistroCx.ProgramServices.Configuration;
using RegistroCx.Services.Repositories;
using RegistroCx.Services.Onboarding;
using RegistroCx.Services.Extraction;
using RegistroCx.Helpers._0Auth;
using RegistroCx.Services;
using RegistroCx.Services.Reports;
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
        
        services.AddScoped<IAppointmentRepository>(provider =>
        {
            var dbOptions = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>();
            return new AppointmentRepository(dbOptions.Value.ConnectionString);
        });
        
        services.AddScoped<IAnesthesiologistRepository>(provider =>
        {
            var dbOptions = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>();
            return new AnesthesiologistRepository(dbOptions.Value.ConnectionString);
        });
        
        services.AddScoped<IUsuarioTelegramRepository>(provider =>
        {
            var dbOptions = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>();
            return new UsuarioTelegramRepository(dbOptions.Value.ConnectionString);
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
        services.AddScoped<RegistroCx.Services.Extraction.LLMOpenAIAssistant>(provider =>
        {
            var openAiOptions = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenAIOptions>>().Value;
            return new RegistroCx.Services.Extraction.LLMOpenAIAssistant(openAiOptions.ApiKey);
        });
        
        services.AddScoped<IAnesthesiologistSearchService>(provider =>
        {
            var openAiOptions = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenAIOptions>>().Value;
            return new AnesthesiologistSearchService(openAiOptions.ApiKey);
        });
        
        // Diccionarios compartidos para mantener estado entre requests
        services.AddSingleton<Dictionary<long, RegistroCx.Models.Appointment>>();
        services.AddSingleton<Dictionary<long, RegistroCx.Services.Reports.ReportService.ReportCommandState>>();
        services.AddScoped<CalendarSyncService>();
        services.AddScoped<CirugiaFlowService>(provider =>
        {
            var llm = provider.GetRequiredService<LLMOpenAIAssistant>();
            var pending = provider.GetRequiredService<Dictionary<long, RegistroCx.Models.Appointment>>();
            var confirmationService = provider.GetRequiredService<AppointmentConfirmationService>();
            var oauthService = provider.GetRequiredService<RegistroCx.Helpers._0Auth.IGoogleOAuthService>();
            var userRepo = provider.GetRequiredService<IUserProfileRepository>();
            var calendarSync = provider.GetRequiredService<CalendarSyncService>();
            var appointmentRepo = provider.GetRequiredService<IAppointmentRepository>();
            var multiSurgeryParser = provider.GetRequiredService<MultiSurgeryParser>();
            var reportService = provider.GetRequiredService<IReportService>();
            var anesthesiologistSearchService = provider.GetRequiredService<IAnesthesiologistSearchService>();
            return new CirugiaFlowService(llm, pending, confirmationService, oauthService, userRepo, calendarSync, appointmentRepo, multiSurgeryParser, reportService, anesthesiologistSearchService);
        });
        services.AddScoped<AppointmentConfirmationService>();
        services.AddScoped<AudioTranscriptionService>(provider =>
        {
            var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("general");
            var logger = provider.GetRequiredService<ILogger<AudioTranscriptionService>>();
            return new AudioTranscriptionService(httpClient, logger);
        });
        services.AddScoped<MultiSurgeryParser>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<MultiSurgeryParser>>();
            var llm = provider.GetRequiredService<RegistroCx.Services.Extraction.LLMOpenAIAssistant>();
            return new MultiSurgeryParser(logger, llm);
        });
        services.AddScoped<IGoogleCalendarService>(provider =>
        {
            var userRepo = provider.GetRequiredService<IUserProfileRepository>();
            var oauthService = provider.GetRequiredService<RegistroCx.Helpers._0Auth.IGoogleOAuthService>();
            return new GoogleCalendarService(userRepo, oauthService);
        });

        // Servicios de reportes
        services.AddScoped<ReportDataService>(provider =>
        {
            var appointmentRepo = provider.GetRequiredService<IAppointmentRepository>();
            var userRepo = provider.GetRequiredService<IUserProfileRepository>();
            return new ReportDataService(appointmentRepo, userRepo);
        });
        services.AddScoped<ChartGeneratorService>();
        services.AddScoped<PdfGeneratorService>();
        services.AddScoped<IReportService>(provider =>
        {
            var dataService = provider.GetRequiredService<ReportDataService>();
            var pdfGenerator = provider.GetRequiredService<PdfGeneratorService>();
            var commandStates = provider.GetRequiredService<Dictionary<long, RegistroCx.Services.Reports.ReportService.ReportCommandState>>();
            return new ReportService(dataService, pdfGenerator, commandStates);
        });

        // Background services
        services.AddHostedService<TelegramBotService>();
        services.AddHostedService<AppointmentReminderService>();

        return services;
    }
}
