namespace LanForge.Shared.Protocol;

/// <summary>Constantes compartidas del descubrimiento mDNS/DNS-SD.</summary>
public static class LanService
{
    public const string ServiceName = "_lanforge._tcp";
    public const string TxtNameKey = "name";
    public const string TxtFingerprintKey = "fingerprint";
}
