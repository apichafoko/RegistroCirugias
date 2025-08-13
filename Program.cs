// Program.cs - Limpio y enfocado
using RegistroCx.ProgramServices.Extensions;
using RegistroCx.Services;

var builder = WebApplication.CreateBuilder(args);

// Configurar logging
builder.ConfigureLogging();

// Cargar variables de entorno
builder.LoadEnvironmentVariables();

// Configurar servicios
builder.Services.ConfigureApplicationServices(builder.Configuration);

// Configurar URLs - Railway usa PORT environment variable
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();

// Middleware para localhost.run
app.Use(async (context, next) =>
{
    if (context.Request.Headers.ContainsKey("X-Forwarded-Host"))
    {
        var forwardedHost = context.Request.Headers["X-Forwarded-Host"].ToString();
        app.Services.GetRequiredService<ILogger<Program>>()
            .LogInformation("Request from forwarded host: {Host}", forwardedHost);
    }
    await next();
});

// Configurar endpoints
app.ConfigureEndpoints();

// Configurar el bot
await app.ConfigureTelegramBot();

// Iniciar aplicación
await app.RunAsync();
