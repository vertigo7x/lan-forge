using System.Text.Json;
using LanForge.Server.Application;
using LanForge.Shared.Security;

namespace LanForge.Server.Infrastructure;

/// <summary>
/// Identidad del servidor en <dataDir>/identity.json. Genera el par Ed25519 en el
/// primer arranque y lo reutiliza después — el fingerprint debe ser estable.
/// Nota: clave en texto plano; LAN party sin auth (ver docs/plan.md).
/// </summary>
public sealed class FileServerIdentityStore : IServerIdentity
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public string ServerId { get; }
    public string ServerName { get; }
    public KeyPair KeyPair { get; }
    public Fingerprint Fingerprint { get; }

    public FileServerIdentityStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, "identity.json");

        IdentityData data;
        if (File.Exists(path))
        {
            data = JsonSerializer.Deserialize<IdentityData>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidOperationException($"identity.json corrupto en '{path}'.");
        }
        else
        {
            var keys = Ed25519.GenerateKeyPair();
            data = new IdentityData(
                Guid.NewGuid().ToString("n"),
                Environment.MachineName,
                Convert.ToBase64String(keys.PrivateKey),
                Convert.ToBase64String(keys.PublicKey));
            File.WriteAllText(path, JsonSerializer.Serialize(data, JsonOptions));
        }

        ServerId = data.ServerId;
        ServerName = data.ServerName;
        KeyPair = new KeyPair(Convert.FromBase64String(data.PrivateKey), Convert.FromBase64String(data.PublicKey));
        Fingerprint = Fingerprint.FromPublicKey(KeyPair.PublicKey);
    }

    private sealed record IdentityData(
        string ServerId,
        string ServerName,
        string PrivateKey,
        string PublicKey);
}
