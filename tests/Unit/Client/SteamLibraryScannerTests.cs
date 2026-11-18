using LanForge.Client.Application;
using LanForge.Client.Domain;
using LanForge.Client.Infrastructure;

namespace LanForge.UnitTests.Client;

public sealed class SteamLibraryScannerTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string CreateSteamRoot(string name = "steam")
    {
        var root = Path.Combine(_temp.Path, name);
        Directory.CreateDirectory(Path.Combine(root, "steamapps"));
        return root;
    }

    private static string VdfEscape(string path) => path.Replace("\\", "\\\\");

    private static string LibraryFolders(params string[] libraryPaths)
    {
        var entries = string.Join("\n", libraryPaths.Select((path, index) =>
            "\"" + index + "\"\n{\n	\"path\"		\"" + VdfEscape(path) + "\"\n	\"label\"		\"\"\n}"));
        return "\"libraryfolders\"\n{\n" + entries + "\n}";
    }

    private static string LegacyLibraryFolders(params string[] libraryPaths)
    {
        var entries = string.Join("\n", libraryPaths.Select((path, index) =>
            $"\"{index}\"		\"{VdfEscape(path)}\""));
        return $"\"libraryfolders\"\n{{\n{entries}\n}}";
    }

    private static string AppManifest(int appId, string name, string installDir) =>
        "\"AppState\"\n{\n" +
        "	\"appid\"		\"" + appId + "\"\n" +
        "	\"name\"		\"" + name + "\"\n" +
        "	\"installdir\"		\"" + installDir + "\"\n" +
        "	\"StateFlags\"		\"4\"\n" +
        "}";

    private static void WriteManifest(string library, int appId, string name, string installDir)
    {
        var steamApps = Path.Combine(library, "steamapps");
        Directory.CreateDirectory(steamApps);
        File.WriteAllText(Path.Combine(steamApps, $"appmanifest_{appId}.acf"), AppManifest(appId, name, installDir));
    }

    [Fact]
    public async Task Scan_WithoutSteam_ReturnsEmptyAndDoesNotThrow()
    {
        var scanner = new SteamLibraryScanner([Path.Combine(_temp.Path, "no-existe")]);

        var games = await scanner.ScanAsync();

        Assert.Empty(games);
    }

    [Fact]
    public async Task Scan_ReadsGameFromRootLibrary()
    {
        var root = CreateSteamRoot();
        WriteManifest(root, 440, "Team Fortress 2", "Team Fortress 2");

        var games = await new SteamLibraryScanner([root]).ScanAsync();

        var game = Assert.Single(games);
        Assert.Equal(440, game.AppId);
        Assert.Equal("Team Fortress 2", game.Name);
        Assert.Equal(Path.Combine(root, "steamapps", "common", "Team Fortress 2"), game.InstallPath);
        Assert.Equal("steam://run/440", game.LaunchCommand);
    }

    [Fact]
    public async Task Scan_ReadsAllLibrariesFromLibraryFoldersVdf()
    {
        var root = CreateSteamRoot();
        var secondary = Path.Combine(_temp.Path, "SteamLibrary");
        File.WriteAllText(
            Path.Combine(root, "steamapps", "libraryfolders.vdf"),
            LibraryFolders(root, secondary));
        WriteManifest(root, 440, "Team Fortress 2", "Team Fortress 2");
        WriteManifest(secondary, 730, "Counter-Strike 2", "Counter-Strike Global Offensive");

        var games = await new SteamLibraryScanner([root]).ScanAsync();

        Assert.Equal(2, games.Count);
        Assert.Contains(games, g => g.AppId == 440 && g.Name == "Team Fortress 2");
        Assert.Contains(games, g => g.AppId == 730 &&
            g.InstallPath == Path.Combine(secondary, "steamapps", "common", "Counter-Strike Global Offensive"));
    }

    [Fact]
    public async Task Scan_SupportsLegacyLibraryFoldersFormat()
    {
        var root = CreateSteamRoot();
        var secondary = Path.Combine(_temp.Path, "SteamLibrary");
        File.WriteAllText(
            Path.Combine(root, "steamapps", "libraryfolders.vdf"),
            LegacyLibraryFolders(secondary));
        WriteManifest(secondary, 570, "Dota 2", "dota 2 beta");

        var games = await new SteamLibraryScanner([root]).ScanAsync();

        var game = Assert.Single(games);
        Assert.Equal(570, game.AppId);
    }

    [Fact]
    public async Task Scan_WithoutLibraryFolders_StillScansRootLibrary()
    {
        var root = CreateSteamRoot();
        WriteManifest(root, 4000, "Garry's Mod", "GarrysMod");

        var games = await new SteamLibraryScanner([root]).ScanAsync();

        Assert.Single(games);
    }

    [Fact]
    public async Task Scan_CorruptLibraryFolders_FallsBackToRootLibrary()
    {
        var root = CreateSteamRoot();
        File.WriteAllText(Path.Combine(root, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\" { \"0\" { \"path\"");
        WriteManifest(root, 440, "Team Fortress 2", "Team Fortress 2");

        var games = await new SteamLibraryScanner([root]).ScanAsync();

        Assert.Single(games);
    }

    [Fact]
    public async Task Scan_CorruptManifest_IsSkippedAndOthersAreReturned()
    {
        var root = CreateSteamRoot();
        WriteManifest(root, 440, "Team Fortress 2", "Team Fortress 2");
        File.WriteAllText(Path.Combine(root, "steamapps", "appmanifest_999.acf"), "\"AppState\" { \"appid\" \"no-numero\"");
        File.WriteAllText(Path.Combine(root, "steamapps", "appmanifest_998.acf"), "basura sin formato");

        var games = await new SteamLibraryScanner([root]).ScanAsync();

        var game = Assert.Single(games);
        Assert.Equal(440, game.AppId);
    }

    [Fact]
    public async Task Scan_DuplicateAppIdAcrossLibraries_IsListedOnce()
    {
        var root = CreateSteamRoot();
        var secondary = Path.Combine(_temp.Path, "SteamLibrary");
        File.WriteAllText(
            Path.Combine(root, "steamapps", "libraryfolders.vdf"),
            LibraryFolders(root, secondary));
        WriteManifest(root, 440, "Team Fortress 2", "Team Fortress 2");
        WriteManifest(secondary, 440, "Team Fortress 2", "Team Fortress 2");

        var games = await new SteamLibraryScanner([root]).ScanAsync();

        Assert.Single(games);
    }

    [Fact]
    public async Task Scan_ResultsAreSortedByName()
    {
        var root = CreateSteamRoot();
        WriteManifest(root, 2, "Zulu", "zulu");
        WriteManifest(root, 1, "Alpha", "alpha");

        var games = await new SteamLibraryScanner([root]).ScanAsync();

        Assert.Equal(["Alpha", "Zulu"], games.Select(g => g.Name).ToArray());
    }

    [Fact]
    public async Task Scan_EnvironmentVariable_OverridesDetection()
    {
        var root = CreateSteamRoot();
        WriteManifest(root, 440, "Team Fortress 2", "Team Fortress 2");
        var previous = Environment.GetEnvironmentVariable(SteamLibraryScanner.SteamRootVariable);
        Environment.SetEnvironmentVariable(SteamLibraryScanner.SteamRootVariable, root);
        try
        {
            var games = await new SteamLibraryScanner().ScanAsync();

            var game = Assert.Single(games);
            Assert.Equal(440, game.AppId);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SteamLibraryScanner.SteamRootVariable, previous);
        }
    }

    [Fact]
    public void Launch_SteamGame_StartsSteamRunUri()
    {
        var starter = new FakeProcessStarter();
        var launcher = new GameLauncher(starter);
        var game = SteamGame.Create(440, "Team Fortress 2", Path.Combine(_temp.Path, "tf2"));

        launcher.Launch(game);

        Assert.Equal("steam://run/440", starter.FileName);
    }

    private sealed class FakeProcessStarter : IProcessStarter
    {
        public string? FileName { get; private set; }

        public void Start(string fileName, string? arguments, string workingDirectory)
        {
            FileName = fileName;
        }
    }
}
