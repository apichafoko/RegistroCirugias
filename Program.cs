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

// Configurar URLs
builder.WebHost.UseUrls("http://0.0.0.0:5002", "https://0.0.0.0:5003");

var app = builder.Build();

// Configurar endpoints
app.ConfigureEndpoints();

// Configurar el bot
await app.ConfigureTelegramBot();

// Iniciar aplicación
await app.RunAsync();
