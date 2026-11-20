namespace LanForge.Server.Application;

/// <summary>Almacena el logotipo global que identifica la LAN party.</summary>
public interface ILanPartyBrandingStore
{
    string? LogoFileName { get; }

    Task<string> SaveLogoAsync(string sourceImagePath, CancellationToken cancellationToken = default);

    Task<Stream?> OpenLogoAsync(string fileName, CancellationToken cancellationToken = default);

    Task ClearLogoAsync(CancellationToken cancellationToken = default);
}
