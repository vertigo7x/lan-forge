namespace LanForge.Client.Application;

/// <summary>Errores de negocio del cliente (mensajes aptos para mostrar en UI).</summary>
public class LanForgeClientException : Exception
{
    public LanForgeClientException(string message) : base(message)
    {
    }
}

/// <summary>El servidor aún no tiene fingerprint fijado: el usuario debe confirmarlo fuera de banda.</summary>
public sealed class TrustRequiredException : LanForgeClientException
{
    public string PresentedFingerprint { get; }
    public string ServerName { get; }
    public string BaseUrl { get; }

    public TrustRequiredException(string presentedFingerprint, string serverName, string baseUrl)
        : base($"Servidor nuevo '{serverName}' en {baseUrl}. Confirma su fingerprint fuera de banda: {presentedFingerprint}")
    {
        PresentedFingerprint = presentedFingerprint;
        ServerName = serverName;
        BaseUrl = baseUrl;
    }
}

/// <summary>El servidor presenta un fingerprint distinto al fijado: posible suplantación.</summary>
public sealed class FingerprintMismatchException : LanForgeClientException
{
    public FingerprintMismatchException(string pinnedFingerprint, string presentedFingerprint)
        : base($"El fingerprint del servidor cambió. Fijado: {pinnedFingerprint}. Presentado: {presentedFingerprint}. Rechazado por seguridad.")
    {
    }
}

public sealed class ProtocolMismatchException : LanForgeClientException
{
    public ProtocolMismatchException(int serverVersion, int clientVersion)
        : base($"Versión de protocolo incompatible (servidor: {serverVersion}, cliente: {clientVersion}). Actualiza ambas aplicaciones.")
    {
    }
}

/// <summary>Firma, fingerprint o integridad (sha256) de un paquete no válidos.</summary>
public sealed class PackageValidationException : LanForgeClientException
{
    public PackageValidationException(string message) : base(message)
    {
    }
}
