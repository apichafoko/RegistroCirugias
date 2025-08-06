
using Microsoft.Extensions.Options;
using RegistroCx.ProgramServices.Configuration;
using System.Security.Authentication;using System;

namespace RegistroCx.ProgramServices.Services.Telegram;

public class TelegramHttpClientHandler : HttpClientHandler
{
    private readonly ILogger<TelegramHttpClientHandler> _logger;
    private readonly string _environment;

    public TelegramHttpClientHandler(IOptions<TelegramBotOptions> options, ILogger<TelegramHttpClientHandler> logger)
    {
        _logger = logger;
        _environment = options.Value.Environment;
        
        ConfigureHandler();
    }

    private void ConfigureHandler()
    {
        CheckCertificateRevocationList = true;
        SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;

        if (_environment.Equals("Development", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("DESARROLLO: Deshabilitando validación SSL");
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }
        else
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) =>
            {
                if (sslPolicyErrors == System.Net.Security.SslPolicyErrors.None)
                    return true;

                _logger.LogError("Error SSL: {Errors}", sslPolicyErrors);
                return false;
            };
        }
    }
}
