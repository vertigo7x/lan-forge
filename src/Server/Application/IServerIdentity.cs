using LanForge.Shared.Security;

namespace LanForge.Server.Application;

/// <summary>Identidad criptográfica del servidor (clave Ed25519 persistente + fingerprint).</summary>
public interface IServerIdentity
{
    string ServerId { get; }
    string ServerName { get; }
    KeyPair KeyPair { get; }
    Fingerprint Fingerprint { get; }
}
