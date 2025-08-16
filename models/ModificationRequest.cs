using System;

namespace RegistroCx.models
{
    public class ModificationRequest
    {
        public DateTime? NewDate { get; set; }
        public TimeOnly? NewTime { get; set; }
        public string? NewLocation { get; set; }
        public string? NewSurgeon { get; set; }
        public string? NewAnesthesiologist { get; set; }
        public string? NewSurgeryType { get; set; }
        public int? NewQuantity { get; set; }
        public string? NewNotes { get; set; }

        public bool HasChanges => 
            NewDate.HasValue || 
            NewTime.HasValue || 
            !string.IsNullOrEmpty(NewLocation) ||
            !string.IsNullOrEmpty(NewSurgeon) ||
            !string.IsNullOrEmpty(NewAnesthesiologist) ||
            !string.IsNullOrEmpty(NewSurgeryType) ||
            NewQuantity.HasValue ||
            !string.IsNullOrEmpty(NewNotes);

        public bool AnesthesiologistChanged => !string.IsNullOrEmpty(NewAnesthesiologist);
        public bool DateTimeChanged => NewDate.HasValue || NewTime.HasValue;
        public bool LocationChanged => !string.IsNullOrEmpty(NewLocation);
    }
}