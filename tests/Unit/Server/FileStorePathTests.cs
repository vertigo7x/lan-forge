using LanForge.Server.Infrastructure;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Confinamiento de rutas en disco del servidor. El producto es Windows x64, pero las
/// validaciones son por patron o por comparacion de rutas: se comprueban aqui con formas
/// de ruta de Windows sin depender del host Linux.
/// </summary>
public sealed class FileStorePathTests
{
    [Theory]
    [InlineData(@"..\..\identity.json")]
    [InlineData(@"..\secret.png")]
    [InlineData(@"sub\carpeta.png")]
    [InlineData(@"C:\absoluta.png")]
    [InlineData("..%2Fidentity.json")]
    [InlineData("../escape.png")]
    [InlineData("sin-extension")]
    public async Task FileCoverStore_rechaza_las_formas_de_ruta_de_windows(string fileName)
    {
        var root = Directory.CreateTempSubdirectory("lanforge-covers");
        try
        {
            var store = new FileCoverStore(root.FullName);
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "queda.png"), "png");

            Assert.Null(await store.OpenReadAsync(fileName));
            Assert.False(await store.DeleteAsync(fileName));

            // El archivo de verdad sigue en su sitio: nada se borro ni se leyo fuera.
            Assert.True(File.Exists(Path.Combine(root.FullName, "queda.png")));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task FileCoverStore_borra_la_portada_y_no_las_que_no_existen()
    {
        var root = Directory.CreateTempSubdirectory("lanforge-covers");
        try
        {
            var store = new FileCoverStore(root.FullName);
            var file = Path.Combine(root.FullName, "quake3.png");
            await File.WriteAllTextAsync(file, "png");

            Assert.True(await store.DeleteAsync("quake3.png"));
            Assert.False(File.Exists(file));
            Assert.False(await store.DeleteAsync("quake3.png"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(@"..\..\escape")]
    [InlineData("../escape")]
    [InlineData("..")]
    public async Task FilePackageContentStore_no_borra_fuera_de_su_raiz(string packageId)
    {
        var root = Directory.CreateTempSubdirectory("lanforge-packages");
        try
        {
            var store = new FilePackageContentStore(root.FullName);

            // Fuera de la raiz hay un archivo que NO debe tocarse.
            var outside = Path.Combine(Path.GetDirectoryName(root.FullName)!, "no-tocar.zip");
            await File.WriteAllTextAsync(outside, "contenido");

            Assert.False(await store.DeleteArchiveAsync(packageId));
            Assert.True(File.Exists(outside));

            File.Delete(outside);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task FilePackageContentStore_borra_el_zip_del_paquete()
    {
        var root = Directory.CreateTempSubdirectory("lanforge-packages");
        try
        {
            var store = new FilePackageContentStore(root.FullName);
            var source = Path.Combine(root.FullName, "src");
            Directory.CreateDirectory(source);
            await File.WriteAllTextAsync(Path.Combine(source, "run.exe"), "binario");

            await store.ImportArchiveAsync("quake3", source);
            var archive = Path.Combine(root.FullName, "quake3.zip");
            Assert.True(File.Exists(archive));

            Assert.True(await store.DeleteArchiveAsync("quake3"));
            Assert.False(File.Exists(archive));
            Assert.False(await store.DeleteArchiveAsync("quake3"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
