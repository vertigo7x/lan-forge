namespace LanForge.Shared.Protocol;

/// <summary>
/// Contrato de compatibilidad servidor↔cliente. Sin autenticación: la única
/// barrera es que ambos hablen la misma versión de protocolo.
/// </summary>
public static class ProtocolVersion
{
    public const int Current = 1;

    /// <summary>MVP: compatibilidad estricta. Ampliar a rangos cuando haya versiones mayores.</summary>
    public static bool IsCompatible(int serverProtocolVersion) => serverProtocolVersion == Current;
}
