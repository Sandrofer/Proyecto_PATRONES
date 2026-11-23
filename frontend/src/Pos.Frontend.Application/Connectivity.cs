using Pos.Shared;

namespace Pos.Frontend.Application;

public enum ConnectionStatus
{
    Unknown,
    Online,
    // The API answers but Odoo does not (503 or status check).
    OdooDown,
    // Odoo answers but there is no open cash register session.
    NoSession,
    // The browser cannot reach the .NET API at all.
    ApiDown
}

// Single place where the frontend knows whether it can charge. It is fed by
// every API answer (HttpPosApi reports 503 and network failures at once) and
// by the periodic status check, and the screens subscribe to Changed.
public sealed class ConnectivityState
{
    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Unknown;
    public string? Message { get; private set; }
    public string? SessionName { get; private set; }
    public DateTime? LastCheckUtc { get; private set; }
    public DateTime? DownSinceUtc { get; private set; }

    // Set when the connection comes back after a failure (cleared by the UI).
    public bool JustRecovered { get; private set; }

    public event Action? Changed;

    public bool IsDown => Status is ConnectionStatus.OdooDown or ConnectionStatus.ApiDown;
    public bool CanCharge => Status is ConnectionStatus.Online or ConnectionStatus.Unknown;

    // Reason shown on the disabled Cobrar button, or null.
    public string? ChargeBlockedReason => Status switch
    {
        ConnectionStatus.OdooDown => "Odoo no está disponible: el cobro se habilitará cuando vuelva.",
        ConnectionStatus.ApiDown => "Sin conexión con el servidor del POS.",
        ConnectionStatus.NoSession => "No hay una sesión de caja abierta en Odoo.",
        _ => null
    };

    public void Apply(SystemStatusDto status) =>
        Set(!status.OdooAvailable ? ConnectionStatus.OdooDown
            : !status.SessionOpen ? ConnectionStatus.NoSession
            : ConnectionStatus.Online,
            status.Message, status.SessionName);

    // Called for every API answer. statusCode null = the request did not reach
    // the API. touchesOdoo = the endpoint needed Odoo, so a success proves it is up.
    public void Observe(int? statusCode, bool failed, bool touchesOdoo)
    {
        if (failed && statusCode is null)
            Set(ConnectionStatus.ApiDown, "No se pudo conectar con el servidor del POS (API).", SessionName);
        else if (statusCode == 503)
            Set(ConnectionStatus.OdooDown, "Odoo no está disponible en este momento.", SessionName);
        else if (!failed && touchesOdoo && IsDown)
            Set(ConnectionStatus.Online, "Odoo conectado.", SessionName);
        else if (!failed && Status == ConnectionStatus.ApiDown)
            Set(ConnectionStatus.Unknown, null, SessionName);
    }

    public void AcknowledgeRecovery()
    {
        JustRecovered = false;
        Changed?.Invoke();
    }

    void Set(ConnectionStatus status, string? message, string? sessionName)
    {
        bool wasDown = IsDown;
        LastCheckUtc = DateTime.UtcNow;
        if (status is ConnectionStatus.OdooDown or ConnectionStatus.ApiDown)
            DownSinceUtc ??= DateTime.UtcNow;
        else
            DownSinceUtc = null;
        if (wasDown && status == ConnectionStatus.Online)
            JustRecovered = true;

        bool changed = status != Status || message != Message || sessionName != SessionName;
        Status = status;
        Message = message;
        SessionName = sessionName;
        if (changed || wasDown)
            Changed?.Invoke();
    }
}

// Periodic check: every 20 s while everything is fine, every 5 s while down.
public sealed class SystemStatusUseCase(IPosApi api, ConnectivityState state)
{
    public static readonly TimeSpan NormalInterval = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan DownInterval = TimeSpan.FromSeconds(5);

    public TimeSpan NextInterval => state.Status == ConnectionStatus.Online ? NormalInterval : DownInterval;

    public async Task CheckAsync()
    {
        var result = await api.GetSystemStatusAsync();
        if (result.IsSuccess)
            state.Apply(result.Value!);
        // Failures were already reported by HttpPosApi through Observe.
    }
}
