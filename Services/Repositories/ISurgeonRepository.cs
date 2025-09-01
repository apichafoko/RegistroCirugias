using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RegistroCx.Services.Repositories;

public interface ISurgeonRepository
{
    Task<string?> GetEmailByNameAsync(string fullName, CancellationToken ct);
    Task<string?> GetEmailByNicknameAsync(string nickname, CancellationToken ct);
    Task SaveAsync(string nombre, string apellido, string email, CancellationToken ct);
    Task AddNicknameAsync(long surgeonId, string nickname, CancellationToken ct);
    Task<List<string>> GetNamesByEquipoAsync(int equipoId, CancellationToken ct);
}