using LanForge.Server.Application;
using LanForge.Shared.Protocol;
using Makaretu.Dns;

namespace LanForge.Server.Infrastructure;

/// <summary>Anuncia el servicio por mDNS con fingerprint y nombre en TXT.</summary>
public sealed class MdnsAdvertiser : IDisposable
{
    private readonly ServiceDiscovery _discovery;
    private readonly ServiceProfile _profile;

    public MdnsAdvertiser(IServerIdentity identity, int port)
    {
        _profile = new ServiceProfile(identity.ServerId, LanService.ServiceName, (ushort)port)
        {
            HostName = System.Net.Dns.GetHostName() + ".local.",
        };
        _profile.AddProperty(LanService.TxtNameKey, identity.ServerName);
        _profile.AddProperty(LanService.TxtFingerprintKey, identity.Fingerprint.ToString());

        _discovery = new ServiceDiscovery();
        _discovery.Advertise(_profile);
    }

    public void Dispose() => _discovery.Dispose();
}
