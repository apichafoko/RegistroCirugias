using RegistroCx.Models;

namespace RegistroCx.Services.Flow;

public class FlowStateManager
{
    private readonly Dictionary<long, Appointment> _pending;

    public FlowStateManager(Dictionary<long, Appointment> pending)
    {
        _pending = pending;
    }

    public Appointment GetOrCreateAppointment(long chatId)
    {
        if (!_pending.TryGetValue(chatId, out var appt))
        {
            appt = new Appointment { ChatId = chatId };
            _pending[chatId] = appt;
        }
        return appt;
    }

    public void SetAppointment(long chatId, Appointment appointment)
    {
        appointment.ChatId = chatId;
        _pending[chatId] = appointment;
    }

    public void ClearContext(long chatId)
    {
        if (_pending.ContainsKey(chatId))
        {
            _pending.Remove(chatId);
        }
    }

    public int GetActiveConversationsCount()
    {
        return _pending.Count;
    }

    public void CleanOldConversations(TimeSpan tiempoLimite)
    {
        var ahora = DateTime.Now;
        var chatIdsAEliminar = new List<long>();

        foreach (var kvp in _pending)
        {
            // Si no hay inputs recientes, considerar como abandonada
            if (kvp.Value.HistoricoInputs.Count == 0)
            {
                chatIdsAEliminar.Add(kvp.Key);
            }
        }

        foreach (var chatId in chatIdsAEliminar)
        {
            _pending.Remove(chatId);
        }
    }

    public bool IsEditMode(Appointment appt)
    {
        return appt.CampoAEditar == Appointment.CampoPendiente.EsperandoNombreCampo;
    }

    public bool IsWizardMode(Appointment appt)
    {
        return appt.CampoQueFalta != Appointment.CampoPendiente.Ninguno && !appt.ConfirmacionPendiente;
    }

    public bool IsConfirmationPending(Appointment appt)
    {
        return appt.ConfirmacionPendiente;
    }
}