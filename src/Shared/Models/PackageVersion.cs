using System.Text.RegularExpressions;

namespace LanForge.Shared.Models;

/// <summary>
/// Versión de paquete/juego, formato semver-lite: N[.N[.N[.N]]][-prerelease].
/// En el alambre viaja como string plano.
/// </summary>
public sealed record PackageVersion : IComparable<PackageVersion>
{
    public const int MaxLength = 64;

    private static readonly Regex Pattern = new(
        @"^(0|[1-9]\d*)(\.(0|[1-9]\d*)){0,3}(-[0-9A-Za-z.-]+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Value { get; }

    public PackageVersion(string value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException($"Versión de paquete inválida: '{value}'.", nameof(value));
        }

        Value = value;
    }

    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= MaxLength && Pattern.IsMatch(value);

    public static bool TryParse(string? value, out PackageVersion? version)
    {
        if (!string.IsNullOrWhiteSpace(value) && IsValid(value))
        {
            version = new PackageVersion(value);
            return true;
        }

        version = null;
        return false;
    }

    public int CompareTo(PackageVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var mainAndPreA = Value.Split('-', 2);
        var mainAndPreB = other.Value.Split('-', 2);

        var mainCompare = CompareMain(mainAndPreA[0], mainAndPreB[0]);
        if (mainCompare != 0)
        {
            return mainCompare;
        }

        // Sin prerelease > con prerelease.
        return (mainAndPreA.Length, mainAndPreB.Length) switch
        {
            (1, 1) => 0,
            (1, 2) => 1,
            (2, 1) => -1,
            _ => string.CompareOrdinal(mainAndPreA[1], mainAndPreB[1]),
        };
    }

    private static int CompareMain(string a, string b)
    {
        var partsA = a.Split('.');
        var partsB = b.Split('.');
        var count = Math.Max(partsA.Length, partsB.Length);
        for (var i = 0; i < count; i++)
        {
            var numA = i < partsA.Length ? int.Parse(partsA[i]) : 0;
            var numB = i < partsB.Length ? int.Parse(partsB[i]) : 0;
            var cmp = numA.CompareTo(numB);
            if (cmp != 0)
            {
                return cmp;
            }
        }

        return 0;
    }

    public static bool operator <(PackageVersion left, PackageVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(PackageVersion left, PackageVersion right) => left.CompareTo(right) > 0;

    public override string ToString() => Value;

    public static implicit operator string(PackageVersion version) => version.Value;
}
