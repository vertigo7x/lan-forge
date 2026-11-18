using LanForge.Client.Application;
using LanForge.Client.Domain;
using LanForge.Shared.Logging;

namespace LanForge.Client.Infrastructure;

/// <summary>
/// Detecta los juegos instalados de Steam leyendo las bibliotecas locales
/// (steamapps/libraryfolders.vdf + appmanifest_*.acf). Solo lee: nunca toca
/// la instalación de Steam. Cualquier error (Steam ausente, VDF corrupto,
/// permisos) se registra y se omite: el escaneo nunca rompe el arranque.
/// </summary>
public sealed class SteamLibraryScanner
{
    /// <summary>
    /// Variable de entorno que fija la raíz de Steam a inspeccionar. Sirve para
    /// pruebas y para instalaciones no estándar; admite varias rutas separadas
    /// por ';' (Windows) o ':' (Unix).
    /// </summary>
    public const string SteamRootVariable = "LANFORGE_STEAM_ROOT";

    private readonly IReadOnlyList<string>? _rootOverride;
    private readonly IClientLog? _log;

    /// <summary>Escáner con detección automática de la instalación de Steam del equipo.</summary>
    public SteamLibraryScanner(IClientLog? log = null)
    {
        _log = log;
    }

    /// <summary>Escáner limitado a raíces concretas (pruebas e instalaciones no estándar).</summary>
    public SteamLibraryScanner(IEnumerable<string> steamRoots, IClientLog? log = null)
    {
        _rootOverride = steamRoots.ToList();
        _log = log;
    }

    /// <summary>
    /// Escanea todas las bibliotecas y devuelve los juegos instalados, sin
    /// duplicados por appId y ordenados por nombre. Sin Steam instalado devuelve
    /// una lista vacía.
    /// </summary>
    public async Task<IReadOnlyList<SteamGame>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var roots = _rootOverride ?? GetCandidateRoots();
        var games = new Dictionary<int, SteamGame>();
        var foundAnyRoot = false;

        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(root))
            {
                continue;
            }

            foundAnyRoot = true;
            foreach (var library in await GetLibrariesAsync(root, cancellationToken))
            {
                foreach (var game in await ScanLibraryAsync(library, cancellationToken))
                {
                    games.TryAdd(game.AppId, game);
                }
            }
        }

        if (!foundAnyRoot)
        {
            await LogAsync(LogSeverity.Info, "steam.scan.no_steam", "No se encontró ninguna instalación de Steam.", cancellationToken);
        }

        return games.Values.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Raíces candidatas según el sistema operativo (registro en Windows).</summary>
    public static IReadOnlyList<string> GetCandidateRoots()
    {
        var configured = Environment.GetEnvironmentVariable(SteamRootVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            var roots = new List<string>();
            var fromRegistry = ReadSteamPathFromRegistry();
            if (!string.IsNullOrWhiteSpace(fromRegistry))
            {
                roots.Add(fromRegistry);
            }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                roots.Add(Path.Combine(programFiles, "Steam"));
            }

            return roots;
        }

        if (OperatingSystem.IsMacOS())
        {
            // La app vive en /Applications pero las bibliotecas en Application Support.
            return [Path.Combine(home, "Library", "Application Support", "Steam")];
        }

        return
        [
            Path.Combine(home, ".steam", "steam"),
            Path.Combine(home, ".local", "share", "Steam"),
        ];
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string? ReadSteamPathFromRegistry()
    {
        try
        {
            return Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Bibliotecas configuradas: las de libraryfolders.vdf más la propia raíz
    /// (instalaciones antiguas no siempre la declaran). Sin VDF, solo la raíz.
    /// </summary>
    private async Task<IReadOnlyList<string>> GetLibrariesAsync(string root, CancellationToken cancellationToken)
    {
        var libraries = new List<string> { root };
        var vdfPath = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath))
        {
            return libraries;
        }

        VdfParser.VdfObject? document;
        try
        {
            document = VdfParser.TryParse(await File.ReadAllTextAsync(vdfPath, cancellationToken));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await LogAsync(LogSeverity.Warning, "steam.scan.libraryfolders_unreadable", $"'{vdfPath}': {ex.Message}", cancellationToken);
            return libraries;
        }

        if (document is null)
        {
            await LogAsync(LogSeverity.Warning, "steam.scan.libraryfolders_corrupt", $"'{vdfPath}' no es VDF válido.", cancellationToken);
            return libraries;
        }

        var folders = document.GetSection("libraryfolders") ?? document;
        foreach (var (_, value) in folders)
        {
            // Formato actual: "0" { "path" "D:\\SteamLibrary" ... }
            // Formato antiguo: "0" "D:\\SteamLibrary"
            var path = value switch
            {
                VdfParser.VdfObject section => section.GetString("path"),
                string legacy => legacy,
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(path))
            {
                libraries.Add(path);
            }
        }

        return libraries.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<IReadOnlyList<SteamGame>> ScanLibraryAsync(string library, CancellationToken cancellationToken)
    {
        var steamApps = Path.Combine(library, "steamapps");
        string[] manifests;
        try
        {
            manifests = Directory.Exists(steamApps)
                ? Directory.GetFiles(steamApps, "appmanifest_*.acf")
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await LogAsync(LogSeverity.Warning, "steam.scan.library_unreadable", $"'{steamApps}': {ex.Message}", cancellationToken);
            return [];
        }

        var games = new List<SteamGame>();
        foreach (var manifestPath in manifests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var game = await TryReadManifestAsync(manifestPath, cancellationToken);
            if (game is not null)
            {
                games.Add(game);
            }
        }

        return games;
    }

    private async Task<SteamGame?> TryReadManifestAsync(string manifestPath, CancellationToken cancellationToken)
    {
        VdfParser.VdfObject? document;
        try
        {
            document = VdfParser.TryParse(await File.ReadAllTextAsync(manifestPath, cancellationToken));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await LogAsync(LogSeverity.Warning, "steam.scan.manifest_unreadable", $"'{manifestPath}': {ex.Message}", cancellationToken);
            return null;
        }

        var appState = document?.GetSection("AppState");
        var appIdText = appState?.GetString("appid");
        var name = appState?.GetString("name");
        var installDir = appState?.GetString("installdir");
        if (appState is null || !int.TryParse(appIdText, out var appId) ||
            string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installDir))
        {
            await LogAsync(LogSeverity.Warning, "steam.scan.manifest_corrupt", $"'{manifestPath}' no tiene appid/name/installdir válidos.", cancellationToken);
            return null;
        }

        var library = Directory.GetParent(Path.GetDirectoryName(manifestPath)!)!.FullName;
        var installPath = Path.Combine(library, "steamapps", "common", installDir);
        return SteamGame.Create(appId, name, installPath);
    }

    private async Task LogAsync(LogSeverity severity, string eventName, string message, CancellationToken cancellationToken)
    {
        if (_log is not null)
        {
            await _log.LogAsync(
                new LogEntry(DateTimeOffset.UtcNow, "steam-scanner", Guid.NewGuid().ToString("n"), severity, eventName, message),
                cancellationToken);
        }
    }
}
