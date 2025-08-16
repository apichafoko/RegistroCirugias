using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RegistroCx.Services.Caching
{
    public interface ICacheService
    {
        Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class;
        Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken ct = default) where T : class;
        Task RemoveAsync(string key, CancellationToken ct = default);
        Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null, CancellationToken ct = default) where T : class;
        
        // Métodos específicos para el dominio
        Task<List<string>> GetSurgeonNamesAsync(CancellationToken ct = default);
        Task<List<string>> GetLocationNamesAsync(CancellationToken ct = default);
        Task<List<string>> GetAnesthesiologistNamesAsync(CancellationToken ct = default);
        Task InvalidateSurgeonCacheAsync(CancellationToken ct = default);
        Task InvalidateLocationCacheAsync(CancellationToken ct = default);
        Task InvalidateAnesthesiologistCacheAsync(CancellationToken ct = default);
    }
}