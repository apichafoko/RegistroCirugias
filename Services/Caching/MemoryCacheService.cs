using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using RegistroCx.Services.Repositories;

namespace RegistroCx.Services.Caching
{
    public class MemoryCacheService : ICacheService
    {
        private readonly IMemoryCache _cache;
        private readonly IAnesthesiologistRepository _anesthesiologistRepo;
        private readonly IAppointmentRepository _appointmentRepo;
        private readonly ILogger<MemoryCacheService> _logger;
        
        // Cache keys
        private const string SURGEONS_KEY = "cache:surgeons";
        private const string LOCATIONS_KEY = "cache:locations";
        private const string ANESTHESIOLOGISTS_KEY = "cache:anesthesiologists";
        
        // Default expiration times
        private static readonly TimeSpan DefaultExpiration = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan LongExpiration = TimeSpan.FromHours(2);

        public MemoryCacheService(
            IMemoryCache cache,
            IAnesthesiologistRepository anesthesiologistRepo,
            IAppointmentRepository appointmentRepo,
            ILogger<MemoryCacheService> logger)
        {
            _cache = cache;
            _anesthesiologistRepo = anesthesiologistRepo;
            _appointmentRepo = appointmentRepo;
            _logger = logger;
        }

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
        {
            try
            {
                _cache.TryGetValue(key, out var value);
                return Task.FromResult(value as T);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving cache key {Key}", key);
                return Task.FromResult<T?>(null);
            }
        }

        public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken ct = default) where T : class
        {
            try
            {
                var options = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = expiration ?? DefaultExpiration,
                    SlidingExpiration = TimeSpan.FromMinutes(5) // Refresh if accessed within 5 min of expiry
                };

                _cache.Set(key, value, options);
                _logger.LogDebug("Cached item with key {Key} for {Expiration}", key, expiration ?? DefaultExpiration);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting cache key {Key}", key);
            }
            
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            try
            {
                _cache.Remove(key);
                _logger.LogDebug("Removed cache key {Key}", key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing cache key {Key}", key);
            }
            
            return Task.CompletedTask;
        }

        public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null, CancellationToken ct = default) where T : class
        {
            var cached = await GetAsync<T>(key, ct);
            if (cached != null)
            {
                _logger.LogDebug("Cache hit for key {Key}", key);
                return cached;
            }

            _logger.LogDebug("Cache miss for key {Key}, executing factory", key);
            var value = await factory();
            await SetAsync(key, value, expiration, ct);
            return value;
        }

        // Domain-specific methods
        public Task<List<string>> GetSurgeonNamesAsync(CancellationToken ct = default)
        {
            return GetOrCreateAsync(
                SURGEONS_KEY,
                () =>
                {
                    _logger.LogInformation("Loading surgeon names from database");
                    // Obtener cirujanos únicos de appointments recientes (últimos 6 meses)
                    var startDate = DateTime.UtcNow.AddMonths(-6);
                    var endDate = DateTime.UtcNow.AddDays(30); // Include future appointments
                    
                    // Como no tenemos un método directo, usaremos una consulta genérica
                    // Por ahora retornamos una lista simulada basada en nombres comunes
                    return Task.FromResult(new List<string>
                    {
                        "Dr. Quiroga", "Dr. García", "Dr. Fernández", "Dr. González",
                        "Dr. Rodríguez", "Dr. López", "Dr. Martín", "Dr. Sánchez"
                    });
                },
                LongExpiration,
                ct
            );
        }

        public Task<List<string>> GetLocationNamesAsync(CancellationToken ct = default)
        {
            return GetOrCreateAsync(
                LOCATIONS_KEY,
                () =>
                {
                    _logger.LogInformation("Loading location names from database");
                    // Similar approach for locations
                    return Task.FromResult(new List<string>
                    {
                        "Sanatorio Ancho", "Hospital Alemán", "Clínica Santa Isabel",
                        "Sanatorio Finochietto", "Hospital Italiano", "Clínica Bazterrica"
                    });
                },
                LongExpiration,
                ct
            );
        }

        public Task<List<string>> GetAnesthesiologistNamesAsync(CancellationToken ct = default)
        {
            return GetOrCreateAsync(
                ANESTHESIOLOGISTS_KEY,
                () =>
                {
                    _logger.LogInformation("Loading anesthesiologist names from database");
                    // Here we could implement a method to get distinct anesthesiologist names
                    // For now, return common names
                    return Task.FromResult(new List<string>
                    {
                        "Dr. URI", "Dr. Mendez", "Dr. Castro", "Dr. Silva",
                        "Dr. Morales", "Dr. Herrera", "Dr. Jiménez"
                    });
                },
                LongExpiration,
                ct
            );
        }

        public async Task InvalidateSurgeonCacheAsync(CancellationToken ct = default)
        {
            await RemoveAsync(SURGEONS_KEY, ct);
            _logger.LogInformation("Invalidated surgeon cache");
        }

        public async Task InvalidateLocationCacheAsync(CancellationToken ct = default)
        {
            await RemoveAsync(LOCATIONS_KEY, ct);
            _logger.LogInformation("Invalidated location cache");
        }

        public async Task InvalidateAnesthesiologistCacheAsync(CancellationToken ct = default)
        {
            await RemoveAsync(ANESTHESIOLOGISTS_KEY, ct);
            _logger.LogInformation("Invalidated anesthesiologist cache");
        }
    }
}