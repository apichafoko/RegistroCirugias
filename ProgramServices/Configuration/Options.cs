using System;

namespace RegistroCx.ProgramServices.Configuration;
public class TelegramBotOptions
{
    public string Token { get; set; } = string.Empty;
    public string Environment { get; set; } = "Production";
}

public class OpenAIOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string AssistantId { get; set; } = string.Empty;
}

public class DatabaseOptions
{
    public string ConnectionString { get; set; } = string.Empty;
}