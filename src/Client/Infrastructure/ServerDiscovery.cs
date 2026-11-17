using LanForge.Shared.Protocol;
using Makaretu.Dns;

namespace LanForge.Client.Infrastructure;

public sealed record DiscoveredServer(
    string ServerId,
    string Name,
    string IpAddress,
    int Port,
    string? Fingerprint);

/// <summary>Descubrimiento de servidores por mDNS durante un intervalo de escaneo.</summary>
public static class ServerDiscovery
{
    public static async Task<IReadOnlyList<DiscoveredServer>> DiscoverAsync(
        TimeSpan scanTime, CancellationToken cancellationToken = default)
    {
        var states = new Dictionary<string, DiscoveryState>(StringComparer.OrdinalIgnoreCase);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(scanTime);

        using var mdns = new MulticastService();
        using var sd = new ServiceDiscovery(mdns);

        mdns.NetworkInterfaceDiscovered += (_, _) => sd.QueryServiceInstances(LanService.ServiceName);

        mdns.AnswerReceived += (_, e) =>
        {
            foreach (var srv in e.Message.Answers.OfType<SRVRecord>())
            {
                if (!srv.Name.ToString().Contains(LanService.ServiceName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var state = GetOrCreate(states, srv.Name.ToString());
                state.Port = srv.Port;
                state.Host = srv.Target.ToString();
            }

            foreach (var txt in e.Message.Answers.OfType<TXTRecord>())
            {
                if (!txt.Name.ToString().Contains(LanService.ServiceName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var properties = TxtProperties.Parse(txt.Strings);
                var state = GetOrCreate(states, txt.Name.ToString());
                if (properties.TryGetValue(LanService.TxtNameKey, out var name))
                {
                    state.Name = name;
                }

                if (properties.TryGetValue(LanService.TxtFingerprintKey, out var fingerprint))
                {
                    state.Fingerprint = fingerprint;
                }
            }

            foreach (var address in e.Message.Answers.OfType<AddressRecord>())
            {
                foreach (var state in states.Values)
                {
                    if (string.Equals(state.Host, address.Name.ToString(), StringComparison.OrdinalIgnoreCase) &&
                        address.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                        state.IpAddress is null)
                    {
                        state.IpAddress = address.Address.ToString();
                    }
                }
            }
        };

        mdns.Start();
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            // Fin del escaneo (nuestro timeout), no es error.
        }

        return states.Values
            .Where(s => s.IpAddress is not null && s.Port > 0)
            .Select(s => new DiscoveredServer(
                ExtractServerId(s.InstanceName),
                s.Name ?? ExtractServerId(s.InstanceName),
                s.IpAddress!,
                s.Port,
                s.Fingerprint))
            .ToArray();
    }

    private static DiscoveryState GetOrCreate(Dictionary<string, DiscoveryState> states, string instanceName)
    {
        if (!states.TryGetValue(instanceName, out var state))
        {
            state = new DiscoveryState { InstanceName = instanceName };
            states[instanceName] = state;
        }

        return state;
    }

    private static string ExtractServerId(string instanceName) =>
        instanceName.Split('.')[0];

    private sealed class DiscoveryState
    {
        public required string InstanceName { get; init; }
        public string? Name { get; set; }
        public string? Fingerprint { get; set; }
        public string? Host { get; set; }
        public string? IpAddress { get; set; }
        public int Port { get; set; }
    }
}
