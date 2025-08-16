using System.Collections.Generic;
using RegistroCx.Models;

namespace RegistroCx.models
{
    public class AppointmentSearchResult
    {
        public List<Appointment> Candidates { get; set; } = new();
        public bool IsAmbiguous => Candidates.Count > 1;
        public bool NotFound => Candidates.Count == 0;
        public Appointment? SingleResult => Candidates.Count == 1 ? Candidates[0] : null;
    }
}