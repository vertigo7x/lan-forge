using LanForge.Client.Application;
using LanForge.Client.Domain;

namespace LanForge.UnitTests.Client;

public sealed class GameInstallerTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private (GameInstaller Installer, string InstallRoot) CreateInstaller()
    {
        var root = Path.Combine(_temp.Path, "games");
        return (new GameInstaller(new Fakes.InMemoryClientStore(), root), root);
    }

    private static string CreateStaging(string parent, Dictionary<string, byte[]> files)
    {
        var staging = Path.Combine(parent, "staging-" + Guid.NewGuid().ToString("n"));
        foreach (var (path, content) in files)
        {
            var full = Path.Combine(staging, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, content);
        }

        return staging;
    }

    [Fact]
    public async Task Install_CopiesToGameVersionDirectory()
    {
        var (installer, root) = CreateInstaller();
        var (manifest, _, files) = ManifestTestHelper.Create("quake3", "1.32", ("run.exe", "bin"), ("data/x", "d"));
        var staging = CreateStaging(_temp.Path, files);

        var installed = await installer.InstallAsync(manifest, staging);

        Assert.Equal(Path.Combine(root, "quake3", "1.32"), installed.InstallPath);
        Assert.Equal("run.exe", installed.EntryPoint);
        Assert.Equal("bin", File.ReadAllText(Path.Combine(installed.InstallPath, "run.exe")));
        Assert.Equal("d", File.ReadAllText(Path.Combine(installed.InstallPath, "data", "x")));
    }

    [Fact]
    public async Task Install_NewVersion_RemovesOldVersions()
    {
        var (installer, root) = CreateInstaller();
        var v1 = ManifestTestHelper.Create("game", "1.0", ("a.txt", "v1"));
        await installer.InstallAsync(v1.Manifest, CreateStaging(_temp.Path, v1.Files));

        var v2 = ManifestTestHelper.Create("game", "1.1", ("a.txt", "v2"));
        var installed = await installer.InstallAsync(v2.Manifest, CreateStaging(_temp.Path, v2.Files));

        Assert.False(Directory.Exists(Path.Combine(root, "game", "1.0")));
        Assert.True(Directory.Exists(Path.Combine(root, "game", "1.1")));
        Assert.Equal("v2", File.ReadAllText(Path.Combine(installed.InstallPath, "a.txt")));
    }

    [Fact]
    public async Task Uninstall_RemovesGameDirectory()
    {
        var (installer, root) = CreateInstaller();
        var (manifest, _, files) = ManifestTestHelper.Create();
        await installer.InstallAsync(manifest, CreateStaging(_temp.Path, files));

        await installer.UninstallAsync("game");

        Assert.False(Directory.Exists(Path.Combine(root, "game")));
    }

    [Fact]
    public async Task Install_PathTraversalGameId_Throws()
    {
        var (installer, _) = CreateInstaller();
        var (manifest, _, files) = ManifestTestHelper.Create(gameId: "..");
        var staging = CreateStaging(_temp.Path, files);

        await Assert.ThrowsAsync<PackageValidationException>(() => installer.InstallAsync(manifest, staging));
    }

    [Fact]
    public async Task Launch_StartsProcessWithEntryPointAndWorkingDirectory()
    {
        var starter = new FakeProcessStarter();
        var launcher = new GameLauncher(starter);
        var (installer, _) = CreateInstaller();
        var (manifest, _, files) = ManifestTestHelper.Create(files: [("bin/run.exe", "bin")]);
        var installed = await installer.InstallAsync(manifest, CreateStaging(_temp.Path, files));

        launcher.Launch(installed);

        Assert.Equal(Path.Combine(installed.InstallPath, "bin", "run.exe"), starter.FileName);
        Assert.Equal(installed.InstallPath, starter.WorkingDirectory);
    }

    [Fact]
    public void Launch_MissingEntryPoint_Throws()
    {
        var launcher = new GameLauncher(new FakeProcessStarter());
        var missing = new InstalledGame("g", "g", "1.0", _temp.Path, "nope.exe", null, DateTimeOffset.UtcNow);

        Assert.Throws<LanForgeClientException>(() => launcher.Launch(missing));
    }

    private sealed class FakeProcessStarter : IProcessStarter
    {
        public string? FileName { get; private set; }
        public string? WorkingDirectory { get; private set; }

        public void Start(string fileName, string? arguments, string workingDirectory)
        {
            FileName = fileName;
            WorkingDirectory = workingDirectory;
        }
    }
}
