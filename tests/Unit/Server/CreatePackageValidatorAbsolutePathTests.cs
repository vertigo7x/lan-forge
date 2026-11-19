using LanForge.Server.Application;
using LanForge.Shared.Manifest;
using LanForge.Shared.Models;

namespace LanForge.UnitTests.Server;

/// <summary>
/// El servidor firma el manifiesto, asi que su validacion del punto de entrada
/// es la ultima linea de defensa. Este archivo fija que el servidor rechaza
/// las rutas absolutas con las mismas reglas que Shared, y que el contrato del
/// punto de entrada es el mismo en los dos lados de la API.
/// </summary>
public sealed class CreatePackageValidatorAbsolutePathTests
{
    private static CreatePackageRequest Valid() => new()
    {
        GameId = "quake3",
        Title = "Quake III Arena",
        Version = "1.32",
        SourceDirectory = "/srv/games/quake3",
        EntryPoint = "run.exe",
    };

    [Theory]
    [InlineData("/tmp/game.exe")]
    [InlineData("/tmp/juegos/game.exe")]
    [InlineData("//tmp/game.exe")]
    [InlineData(@"\\servidor\compartido\game.exe")]
    [InlineData("  /tmp/game.exe  ")]
    public void Validate_EntryPoint_absoluto_devuelve_invalid_path(string entryPoint)
    {
        var errors = CreatePackageValidator.Validate(Valid() with { EntryPoint = entryPoint });

        var error = Assert.Single(errors);
        Assert.Equal("entryPoint", error.Field);
        Assert.Equal("invalid_path", error.Code);
    }

    [Fact]
    public void Validate_EntryPoint_absoluto_no_firma_un_manifiesto()
    {
        // El servidor no puede llegar a firmar un manifiesto con un punto de
        // entrada absoluto: la validacion del dominio lo frena antes.
        var entry = Assert.Throws<ArgumentException>(
            () => ManifestFileEntry.Create("/tmp/game.exe", new string('a', 64), 1));

        Assert.Equal("relativePath", entry.ParamName);
    }

    [Fact]
    public void La_ruta_relativa_que_produce_el_cliente_la_acepta_el_servidor()
    {
        // El formulario del organizador elige un .exe con el dialogo del sistema
        // operativo (ruta absoluta en disco) y lo convierte en la ruta relativa
        // que espera el payload. Ese contrato tiene que seguir funcionando: la
        // ruta relativa que produce el cliente la acepta el servidor.
        var sourceDirectory = DirectorioValido();
        var executable = Path.Combine(sourceDirectory, "run.exe");
        File.WriteAllText(executable, "MZ");

        var relativa = PackageSourcePaths.ToPackageRelativePath(sourceDirectory, executable);

        Assert.Equal("run.exe", relativa);
        Assert.Empty(CreatePackageValidator.Validate(Valid() with { EntryPoint = relativa }));
    }

    [Fact]
    public void La_ruta_absoluta_del_cliente_la_rechaza_el_servidor()
    {
        // El caso contrario: la ruta absoluta que el cliente tiene en disco no
        // vale como punto de entrada del paquete. El servidor la rechaza con
        // un unico error, el del campo entryPoint.
        var sourceDirectory = DirectorioValido();
        var executable = Path.Combine(sourceDirectory, "run.exe");
        File.WriteAllText(executable, "MZ");

        var errors = CreatePackageValidator.Validate(Valid() with { EntryPoint = executable });

        var error = Assert.Single(errors);
        Assert.Equal("entryPoint", error.Field);
        Assert.Equal("invalid_path", error.Code);
    }

    [Fact]
    public void Cliente_y_servidor_coinciden_en_el_numero_de_errores_del_punto_de_entrada()
    {
        // El formulario del organizador (cliente) y la API (servidor) pueden
        // recibir la misma ruta absoluta. Los dos tienen que rechazarla, y con
        // el mismo numero de errores, para que el contrato no dependa de donde
        // se mire. La carpeta origen del cliente se deja valida a proposito:
        // si no, el cliente anadiria un error de sistema de ficheros y las
        // cuentas no serian comparables.
        const string rutaAbsoluta = "/tmp/juegos/game.exe";

        var erroresServidor = CreatePackageValidator.Validate(Valid() with { EntryPoint = rutaAbsoluta });
        var erroresCliente = PackageCreationValidator.Validate(new PackageCreationRequest(
            "quake3",
            "Quake III Arena",
            "1.32",
            DirectorioValido(),
            rutaAbsoluta,
            null));

        Assert.Single(erroresServidor);
        Assert.Single(erroresCliente);
        Assert.Contains(erroresServidor, e => e.Field == "entryPoint" && e.Code == "invalid_path");
        Assert.True(erroresCliente.ContainsKey(PackageCreationFields.EntryPoint));
    }

    /// <summary>Carpeta origen que existe, para que el cliente no anada un error de disco.</summary>
    private static string DirectorioValido()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lanforge-abs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
