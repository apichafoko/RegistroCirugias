using System.Threading;
using System.Threading.Tasks;
using RegistroCx.Domain;

namespace RegistroCx.Services.Repositories;

/// <summary>
/// Interfaz para el repositorio de usuarios de Telegram
/// </summary>
public interface IUsuarioTelegramRepository
{
    /// <summary>
    /// Obtiene los datos de Telegram de un usuario por chat_id
    /// </summary>
    Task<UsuarioTelegram?> GetByChatIdAsync(long chatId, CancellationToken ct = default);

    /// <summary>
    /// Obtiene los datos de Telegram de un usuario por telegram_id
    /// </summary>
    Task<UsuarioTelegram?> GetByTelegramIdAsync(long telegramId, CancellationToken ct = default);

    /// <summary>
    /// Obtiene o crea los datos de Telegram de un usuario
    /// </summary>
    Task<UsuarioTelegram> GetOrCreateAsync(long chatId, long? telegramId, CancellationToken ct = default);

    /// <summary>
    /// Actualiza los datos de Telegram del usuario
    /// </summary>
    Task SaveAsync(UsuarioTelegram usuario, CancellationToken ct = default);

    /// <summary>
    /// Actualiza solo los datos específicos de Telegram
    /// </summary>
    Task UpdateTelegramDataAsync(
        long chatId,
        long? telegramId,
        string? nombre,
        string? username,
        string? telefono = null,
        string? email = null,
        CancellationToken ct = default);

    /// <summary>
    /// Actualiza los datos de Telegram buscando primero por teléfono, luego por chat_id
    /// </summary>
    Task UpdateTelegramDataByPhoneAsync(
        long chatId,
        long? telegramId,
        string? nombre,
        string? username,
        string telefono,
        CancellationToken ct = default);
}