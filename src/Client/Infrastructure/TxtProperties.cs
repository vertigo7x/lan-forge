namespace LanForge.Client.Infrastructure;

/// <summary>Parser de cadenas TXT ("k=v") de DNS-SD. Separado para testearlo sin red.</summary>
public static class TxtProperties
{
    public static IReadOnlyDictionary<string, string> Parse(IEnumerable<string> strings)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in strings)
        {
            var separator = item.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            properties[item[..separator]] = item[(separator + 1)..];
        }

        return properties;
    }
}
