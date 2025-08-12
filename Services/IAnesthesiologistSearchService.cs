using System.Collections.Generic;
using System.Threading.Tasks;

namespace RegistroCx.Services;

public interface IAnesthesiologistSearchService
{
    Task<List<AnesthesiologistCandidate>> SearchByPartialNameAsync(string partialName, string teamEmail);
}

public class AnesthesiologistCandidate
{
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Coincidencia { get; set; } = string.Empty;
}