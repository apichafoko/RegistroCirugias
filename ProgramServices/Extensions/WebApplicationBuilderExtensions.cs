using System;

namespace RegistroCx.ProgramServices.Extensions;

public static class WebApplicationBuilderExtensions
{
    public static void ConfigureLogging(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
    }

    public static void LoadEnvironmentVariables(this WebApplicationBuilder builder)
    {
        var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("Startup");
        
        try
        {
            DotNetEnv.Env.Load();
            logger.LogInformation("Variables de entorno cargadas correctamente");
        }
        catch (Exception ex)
        {
            logger.LogWarning("No se pudo cargar archivo .env: {Message}", ex.Message);
        }
    }
}
