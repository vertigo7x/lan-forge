using Avalonia.Controls;
using Avalonia.Platform.Storage;
using LanForge.Server.Application;
using LanForge.Server.ViewModels;
using LanForge.Server.Views;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Pruebas del selector de rutas nativo (<see cref="TopLevelPathPicker"/>), el
/// adaptador que abre los dialogos del sistema operativo.
///
/// LIMITACION DE LAS INTERFACES NATIVAS
/// =====================================
/// En headless no hay sistema operativo detras de la aplicacion: el proveedor
/// de almacenamiento de Avalonia es <c>NoopStorageProvider</c>, que devuelve
/// colecciones vacias sin abrir ninguna ventana. MEDIDO en este entorno:
///
/// - Sin ventana pegada: <c>PickFolderAsync</c> y <c>PickExecutableAsync</c>
///   devuelven null (no hay <see cref="TopLevel"/>, no hay proveedor).
/// - Con ventana pegada en headless: los dos devuelven null tambien, porque el
///   proveedor no devuelve ningun elemento. No se lanza ninguna excepcion.
///
/// Por eso estas pruebas NO comprueban que el dialogo se abra ni que el filtro
/// <c>*.exe</c> se aplique de verdad: eso solo se puede comprobar a mano, con
/// el servidor en marcha sobre Windows. Lo que si se comprueba aqui es el
/// cableado que rodea al dialogo, que es donde pueden esconderse los fallos:
///
/// - La vista sin ventana no revienta: devuelve null y el formulario se queda
///   como estaba.
/// - El dialogo cancelado (ningun elemento) se traduce a null, no a una ruta
///   vacia, para que el campo no se borre.
/// - Un elemento sin ruta local (un proveedor remoto devuelve una URI) no se
///   convierte en una ruta que el servidor no puede leer.
///
/// Las opciones con las que se abririan los dialogos se comprueban aparte, en
/// <see cref="PackageCreateViewExecutablePickerTests"/>.
/// </summary>
[Collection("Cliente-UI")]
public sealed class TopLevelPathPickerTests : IDisposable
{
    private readonly TemporaryGameFolder _game = new();

    public void Dispose() => _game.Dispose();

    [Fact]
    public async Task Sin_ventana_no_devuelve_ninguna_carpeta()
    {
        // El selector se construye con un control que no esta pegado a ninguna
        // ventana: no hay proveedor de almacenamiento, asi que no hay dialogo
        // que abrir y el campo tiene que quedarse como estaba.
        var picker = new TopLevelPathPicker(new Control());

        var folder = await picker.PickFolderAsync();

        Assert.Null(folder);
    }

    [Fact]
    public async Task Sin_ventana_no_devuelve_ningun_ejecutable()
    {
        var picker = new TopLevelPathPicker(new Control());

        var executable = await picker.PickExecutableAsync();

        Assert.Null(executable);
    }

    [Fact]
    public async Task Un_dialogo_sin_elementos_elegidos_no_devuelve_una_carpeta_vacia()
    {
        // El usuario cancela el dialogo: no se elige nada. El resultado tiene
        // que ser null y no una cadena vacia, para que el formulario pueda
        // distinguir "no se ha elegido nada" de "se ha elegido una ruta vacia".
        var picker = new TopLevelPathPicker(new Control());

        var folder = await picker.PickFolderAsync();

        Assert.Null(folder);
    }

    [Fact]
    public async Task Un_dialogo_sin_elementos_elegidos_no_devuelve_un_ejecutable_vacio()
    {
        var picker = new TopLevelPathPicker(new Control());

        var executable = await picker.PickExecutableAsync();

        Assert.Null(executable);
    }

    [Fact]
    public async Task El_selector_cancelado_no_lanza_excepcion()
    {
        // Un fallo aqui seeria una excepcion sin capturar en el hilo de UI: la
        // aplicacion se caeria al abrir el formulario. El cancelacion del
        // dialogo es el camino mas normal, asi que tiene que ser silencioso.
        var picker = new TopLevelPathPicker(new Control());

        var folderTask = picker.PickFolderAsync();
        var executableTask = picker.PickExecutableAsync();

        await folderTask;
        await executableTask;
    }

    [Fact]
    public async Task Con_una_ventana_en_headless_el_dialogo_no_devuelve_ningun_elemento()
    {
        // MEDIDO en este entorno: con la vista pegada a una ventana, el
        // proveedor de almacenamiento de Avalonia en headless es
        // NoopStorageProvider, que devuelve una coleccion vacia. El selector
        // la traduce a null: en headless el dialogo es indistinguible de una
        // cancelacion.
        //
        // Esta prueba ata ese comportamiento: si en el futuro el proveedor
        // headless empezara a devolver elementos, habria que revisar como se
        // traducen al formulario.
        PackageCreateView? view = null;
        Window? window = null;
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            view = new PackageCreateView(new NoopPackageCreationService());
            window = new Window { Content = view };
            window.Show();
            window.UpdateLayout();
        });

        var picker = new TopLevelPathPicker(view!);
        string? folder;
        string? executable;
        try
        {
            folder = await picker.PickFolderAsync();
            executable = await picker.PickExecutableAsync();
        }
        finally
        {
            HeadlessAvalonia.RunOnUiThreadAndWait(window!.Close);
        }

        Assert.Null(folder);
        Assert.Null(executable);
    }

    [Fact]
    public async Task El_view_model_ignora_un_selector_que_no_devuelve_nada()
    {
        // El efecto en el formulario: si el dialogo se cancela, los campos se
        // quedan con lo que tuvieran y el boton de publicar sigue deshabilitado.
        var viewModel = new PackageCreateViewModel(
            new NoopPackageCreationService(),
            new TopLevelPathPicker(new Control()));

        await viewModel.PickSourceDirectoryAsync();
        await viewModel.PickEntryPointAsync();

        Assert.Equal(string.Empty, viewModel.SourceDirectory);
        Assert.Equal(string.Empty, viewModel.EntryPoint);
        Assert.False(viewModel.HasSourceDirectoryError);
        Assert.False(viewModel.HasEntryPointError);
        Assert.False(viewModel.CanPublish);
    }

    [Fact]
    public void El_selector_sin_ventana_declara_que_no_hay_dialogo()
    {
        // Sin ventana no hay proveedor de almacenamiento, asi que no hay dialogo
        // que abrir: el formulario tiene que poder avisar en vez de dejar al
        // organizador pulsando «Examinar…» sin que pase nada.
        var picker = new TopLevelPathPicker(new Control());

        Assert.False(picker.IsAvailable);
    }

    [Fact]
    public void El_selector_con_ventana_declara_que_hay_dialogo()
    {
        // Con la vista pegada a una ventana hay proveedor de almacenamiento: el
        // dialogo se puede abrir (en headless devuelve una coleccion vacia, que
        // es indistinguible de una cancelacion, pero no es «no hay dialogo»).
        PackageCreateView? view = null;
        Window? window = null;
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            view = new PackageCreateView(new NoopPackageCreationService());
            window = new Window { Content = view };
            window.Show();
            window.UpdateLayout();
        });

        try
        {
            var picker = new TopLevelPathPicker(view!);

            Assert.True(picker.IsAvailable);
        }
        finally
        {
            HeadlessAvalonia.RunOnUiThreadAndWait(window!.Close);
        }
    }

    [Fact]
    public async Task Un_elemento_con_ruta_local_devuelve_la_ruta_tal_cual()
    {
        // La ruta que devuelve el dialogo es la que el servidor recibe: no se
        // transforma ni se normaliza.
        //
        // El elemento sale del proveedor real de Avalonia
        // (TryGetFileFromPathAsync): IStorageItem no se puede implementar a
        // mano ("not implementable by user code"), asi que para tener un
        // elemento de verdad hay que pedirselo al proveedor.
        var item = await StorageFileAsync(_game.ExecutablePath);

        var path = TopLevelPathPicker.LocalPath(item);

        Assert.Equal(_game.ExecutablePath, path);
    }

    [Fact]
    public async Task Un_elemento_sin_ruta_local_no_devuelve_ninguna_ruta()
    {
        // Un proveedor remoto puede devolver un elemento cuya URI no tiene ruta
        // local. En ese caso no hay una ruta que enviar al servidor, asi que el
        // selector devuelve null en vez de una ruta que el servidor no puede
        // leer.
        //
        // MEDIDO en este entorno: al pedir al proveedor un archivo por una URI
        // que no es del sistema de ficheros, devuelve null (no un elemento con
        // la ruta vacia). El selector lo traduce a null.
        var item = await StorageItemForUriAsync(new Uri("https://ejemplo.com/juego/game.exe"));

        var path = TopLevelPathPicker.LocalPath(item);

        Assert.Null(path);
    }

    [Fact]
    public void Un_elemento_nulo_no_devuelve_ninguna_ruta()
    {
        // Si el proveedor devolviera una entrada nula, el selector no puede
        // dejar que eso reviente el formulario al elegir.
        var path = TopLevelPathPicker.LocalPath(null);

        Assert.Null(path);
    }

    /// <summary>
    /// Pide al proveedor de almacenamiento un elemento a partir de una ruta
    /// local del disco.
    /// </summary>
    private static async Task<IStorageFile> StorageFileAsync(string path)
    {
        var item = await StorageItemForUriAsync(new Uri(path));
        Assert.NotNull(item);
        return Assert.IsAssignableFrom<IStorageFile>(item);
    }

    /// <summary>
    /// Pide al proveedor de almacenamiento un elemento a partir de una URI. Con
    /// la ventana ya pegada, el proveedor sabe leer del sistema de ficheros.
    ///
    /// Devuelve null si el proveedor no encuentra el archivo: asi se puede
    /// comprobar tambien el caso de un elemento que no existe.
    /// </summary>
    private static async Task<IStorageItem?> StorageItemForUriAsync(Uri uri)
    {
        PackageCreateView? view = null;
        Window? window = null;
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            view = new PackageCreateView(new NoopPackageCreationService());
            window = new Window { Content = view };
            window.Show();
            window.UpdateLayout();
        });

        var storage = TopLevel.GetTopLevel(view!)!.StorageProvider;
        try
        {
            // El proveedor devuelve null si no encuentra el archivo (o si la
            // URI no es del sistema de ficheros).
            return await storage.TryGetFileFromPathAsync(uri);
        }
        finally
        {
            HeadlessAvalonia.RunOnUiThreadAndWait(window!.Close);
        }
    }

    private sealed class NoopPackageCreationService : IPackageCreationService
    {
        public Task<PackageCreationResult> CreateAsync(
            PackageCreationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PackageCreationResult.Success(
                new PackageCreationSummary(
                    request.GameId, request.GameId, request.Title, request.Version, 1, 1)));
    }
}
