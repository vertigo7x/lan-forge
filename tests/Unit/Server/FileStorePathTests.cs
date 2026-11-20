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

    [Theory]
    [InlineData(@"..\..\escape")]
    [InlineData("../escape")]
    [InlineData("..")]
    [InlineData("sub/carpeta")]
    [InlineData("")]
    public async Task FilePackageContentStore_no_importa_fuera_de_su_raiz(string packageId)
    {
        // Sandbox privado: el "padre" de la raiz debe ser nuestro, para poder afirmar que no
        // aparecio ningun .zip fuera sin ver ficheros de otros.
        var sandbox = Directory.CreateTempSubdirectory("lanforge-sandbox");
        try
        {
            var root = Path.Combine(sandbox.FullName, "packages");
            Directory.CreateDirectory(root);
            var store = new FilePackageContentStore(root);
            var source = Path.Combine(sandbox.FullName, "src");
            Directory.CreateDirectory(source);
            await File.WriteAllTextAsync(Path.Combine(source, "run.exe"), "binario");

            await Assert.ThrowsAsync<ArgumentException>(() => store.ImportArchiveAsync(packageId, source));

            // Ni fuera de la raiz ni dentro: no se escribe ningun .zip.
            Assert.Empty(Directory.GetFiles(sandbox.FullName, "*.zip"));
            Assert.Empty(Directory.GetFiles(root, "*.zip"));
        }
        finally
        {
            sandbox.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(@"..\..\secreto")]
    [InlineData("../secreto")]
    [InlineData("..")]
    [InlineData("")]
    public async Task FilePackageContentStore_no_abre_fuera_de_su_raiz(string packageId)
    {
        var sandbox = Directory.CreateTempSubdirectory("lanforge-sandbox");
        try
        {
            var root = Path.Combine(sandbox.FullName, "packages");
            Directory.CreateDirectory(root);
            var store = new FilePackageContentStore(root);

            // Fuera de la raiz hay un zip que NO debe poder leerse.
            var outside = Path.Combine(sandbox.FullName, "secreto.zip");
            await File.WriteAllTextAsync(outside, "contenido secreto");

            Assert.Null(await store.OpenArchiveAsync(packageId));
            Assert.Null(await store.OpenReadAsync(packageId, "run.exe"));
        }
        finally
        {
            sandbox.Delete(recursive: true);
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

    [Theory]
    [InlineData(@"..\escape")]
    [InlineData("../escape")]
    [InlineData("sub/carpeta")]
    [InlineData("con.punto")]
    public async Task FileCoverStore_no_guarda_fuera_de_su_raiz(string gameId)
    {
        var sandbox = Directory.CreateTempSubdirectory("lanforge-sandbox");
        try
        {
            var root = Path.Combine(sandbox.FullName, "covers");
            Directory.CreateDirectory(root);
            var store = new FileCoverStore(root);
            var image = Path.Combine(sandbox.FullName, "origen.png");
            await File.WriteAllTextAsync(image, "png");

            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(gameId, image));

            // Ni fuera de la raiz ni dentro: no se guarda ninguna portada con ese identificador.
            Assert.DoesNotContain(
                Directory.GetFiles(sandbox.FullName, "*.png"),
                f => f != image);
            Assert.Empty(Directory.GetFiles(root, "*.png"));
        }
        finally
        {
            sandbox.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task FileCoverStore_guarda_una_portada_que_despues_se_puede_leer()
    {
        var root = Directory.CreateTempSubdirectory("lanforge-covers");
        try
        {
            var store = new FileCoverStore(root.FullName);
            var image = Path.Combine(root.FullName, "origen.PNG");
            await File.WriteAllTextAsync(image, "png");

            var fileName = await store.SaveAsync("quake3", image);

            // El nombre lleva el hash del contenido y conserva la extensión en minúsculas.
            Assert.Matches("^quake3-[a-f0-9]{64}\\.png$", fileName);

            // El flujo se cierra antes de borrar el directorio: en Windows un fichero
            // abierto bloquea el borrado y el test fallaba al limpiar el temporal.
            await using (var read = await store.OpenReadAsync(fileName))
            {
                Assert.NotNull(read);
            }

            Assert.True(await store.DeleteAsync(fileName));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task FileCoverStore_asigna_un_nombre_distinto_si_cambia_el_contenido()
    {
        var root = Directory.CreateTempSubdirectory("lanforge-covers");
        try
        {
            var store = new FileCoverStore(root.FullName);
            var first = Path.Combine(root.FullName, "primera.png");
            var second = Path.Combine(root.FullName, "segunda.png");
            await File.WriteAllTextAsync(first, "primera portada");
            await File.WriteAllTextAsync(second, "segunda portada");

            var firstFileName = await store.SaveAsync("quake3", first);
            var secondFileName = await store.SaveAsync("quake3", second);

            Assert.NotEqual(firstFileName, secondFileName);
            await using var firstStream = await store.OpenReadAsync(firstFileName);
            await using var secondStream = await store.OpenReadAsync(secondFileName);
            Assert.NotNull(firstStream);
            Assert.NotNull(secondStream);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
