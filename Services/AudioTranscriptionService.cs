using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace RegistroCx.Services;

public class AudioTranscriptionService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AudioTranscriptionService> _logger;
    private readonly string _openAiApiKey;

    public AudioTranscriptionService(HttpClient httpClient, ILogger<AudioTranscriptionService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") 
            ?? throw new InvalidOperationException("OPENAI_API_KEY not found");
    }

    public async Task<string?> TranscribeAudioAsync(ITelegramBotClient botClient, Audio audio, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("[AUDIO] Transcribing audio file: {FileId}, Duration: {Duration}s", 
                audio.FileId, audio.Duration);

            // Download audio file from Telegram
            var audioStream = await DownloadAudioFromTelegram(botClient, audio.FileId, ct);
            if (audioStream == null)
            {
                _logger.LogError("[AUDIO] Failed to download audio file");
                return null;
            }

            // Transcribe using OpenAI Whisper
            var transcription = await TranscribeWithWhisper(audioStream, "audio.ogg", ct);
            
            _logger.LogInformation("[AUDIO] ✅ Transcription completed: {Transcription}", 
                transcription?.Length > 50 ? transcription[..50] + "..." : transcription);
            
            return transcription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AUDIO] Error transcribing audio");
            return null;
        }
    }

    public async Task<string?> TranscribeVoiceAsync(ITelegramBotClient botClient, Voice voice, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("[VOICE] Transcribing voice message: {FileId}, Duration: {Duration}s", 
                voice.FileId, voice.Duration);

            // Download voice file from Telegram
            var voiceStream = await DownloadAudioFromTelegram(botClient, voice.FileId, ct);
            if (voiceStream == null)
            {
                _logger.LogError("[VOICE] Failed to download voice file");
                return null;
            }

            // Transcribe using OpenAI Whisper
            var transcription = await TranscribeWithWhisper(voiceStream, "voice.ogg", ct);
            
            _logger.LogInformation("[VOICE] ✅ Transcription completed: {Transcription}", 
                transcription?.Length > 50 ? transcription[..50] + "..." : transcription);
            
            return transcription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[VOICE] Error transcribing voice");
            return null;
        }
    }

    private async Task<Stream?> DownloadAudioFromTelegram(ITelegramBotClient botClient, string fileId, CancellationToken ct)
    {
        try
        {
            var file = await botClient.GetFile(fileId, ct);
            if (file?.FilePath == null)
            {
                _logger.LogError("[AUDIO] No file path received from Telegram API");
                return null;
            }

            var memoryStream = new MemoryStream();
            await botClient.DownloadFile(file.FilePath, memoryStream, ct);
            memoryStream.Position = 0;
            
            _logger.LogInformation("[AUDIO] Downloaded {Size} bytes from Telegram", memoryStream.Length);
            return memoryStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AUDIO] Error downloading file from Telegram");
            return null;
        }
    }

    private async Task<string?> TranscribeWithWhisper(Stream audioStream, string fileName, CancellationToken ct)
    {
        try
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_openAiApiKey}");

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent("whisper-1"), "model");
            form.Add(new StringContent("es"), "language"); // Spanish language
            form.Add(new StringContent("text"), "response_format");
            
            var audioContent = new StreamContent(audioStream);
            audioContent.Headers.Add("Content-Type", "audio/ogg");
            form.Add(audioContent, "file", fileName);

            _logger.LogInformation("[WHISPER] Sending audio to OpenAI Whisper API");
            
            var response = await _httpClient.PostAsync("https://api.openai.com/v1/audio/transcriptions", form, ct);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("[WHISPER] API Error: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return null;
            }

            var textResponse = await response.Content.ReadAsStringAsync(ct);
            _logger.LogDebug("[WHISPER] Raw response: {Response}", textResponse);

            // With response_format "text", the response is plain text, not JSON
            return string.IsNullOrWhiteSpace(textResponse) ? null : textResponse.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WHISPER] Error calling OpenAI API");
            return null;
        }
    }

}