using LanForge.Server.Application;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Pruebas de la validacion de cliente de los campos que el organizador elige con
/// los dialogos del sistema operativo: la carpeta origen tiene que existir y el
/// ejecutable tiene que existir y ser un .exe.
/// </summary>
public sealed class PackageCreationValidatorPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"lanforge-validator-{Guid.NewGuid():N}");

    public PackageCreationValidatorPathsTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "game"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Valida_una_carpeta_y_un_ejecutable_que_existen()
    {
        var sourceDirectory = Path.Combine(_root, "game");
        var executable = Path.Combine(sourceDirectory, "game.exe");
        File.WriteAllText(executable, "MZ");

        var errors = PackageCreationValidator.Validate(BuildRequest(sourceDirectory, executable));

        Assert.Empty(errors);
    }

    [Fact]
    public void Rechaza_una_carpeta_origen_que_no_existe()
    {
        var missing = Path.Combine(_root, "no-existe");
        var executable = Path.Combine(missing, "game.exe");

        var errors = PackageCreationValidator.Validate(BuildRequest(missing, executable));

        Assert.True(errors.ContainsKey(PackageCreationFields.SourceDirectory));
        Assert.Contains("no existe", errors[PackageCreationFields.SourceDirectory], StringComparison.Ordinal);
    }

    [Fact]
    public void Rechaza_un_ejecutable_que_no_existe()
    {
        var sourceDirectory = Path.Combine(_root, "game");
        var missing = Path.Combine(sourceDirectory, "no-existe.exe");

        var errors = PackageCreationValidator.Validate(BuildRequest(sourceDirectory, missing));

        Assert.True(errors.ContainsKey(PackageCreationFields.EntryPoint));
        Assert.Contains("no existe", errors[PackageCreationFields.EntryPoint], StringComparison.Ordinal);
    }

    [Fact]
    public void Rechaza_un_ejecutable_que_no_termina_en_exe()
    {
        var sourceDirectory = Path.Combine(_root, "game");
        var dll = Path.Combine(sourceDirectory, "game.dll");
        File.WriteAllText(dll, "MZ");

        var errors = PackageCreationValidator.Validate(BuildRequest(sourceDirectory, dll));

        Assert.True(errors.ContainsKey(PackageCreationFields.EntryPoint));
        Assert.Contains(".exe", errors[PackageCreationFields.EntryPoint], StringComparison.Ordinal);
    }

    private static PackageCreationRequest BuildRequest(string sourceDirectory, string entryPoint) =>
        new("neon-drift", "Neon Drift", "1.2.3", sourceDirectory, entryPoint, "--fullscreen");
}
