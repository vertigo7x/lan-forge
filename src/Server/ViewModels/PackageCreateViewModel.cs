using System.ComponentModel;
using System.Runtime.CompilerServices;
using LanForge.Server.Application;

namespace LanForge.Server.ViewModels;

/// <summary>
/// Estado del formulario de creación de paquetes. Es un componente independiente:
/// no depende de la ventana principal ni del catálogo, solo del servicio de creación,
/// que se puede sustituir por un cliente HTTP del endpoint.
/// </summary>
public sealed class PackageCreateViewModel : INotifyPropertyChanged
{
    private readonly IPackageCreationService _service;
    private readonly IPathPicker _pathPicker;

    private string _gameId = string.Empty;
    private string _title = string.Empty;
    private string _version = string.Empty;
    private string _sourceDirectory = string.Empty;
    private string _entryPoint = string.Empty;
    private string _arguments = string.Empty;

    private string? _gameIdError;
    private string? _titleError;
    private string? _versionError;
    private string? _sourceDirectoryError;
    private string? _entryPointError;
    private string? _argumentsError;

    /// <summary>
    /// Errores por campo devueltos por el servidor en el ultimo envio. Se guardan
    /// aparte de los errores mostrados para poder conservar el mensaje del servidor
    /// cuando el usuario edita el campo y la validación de cliente no se queja
    /// (por ejemplo, "la carpeta origen no existe" solo lo puede saber el servidor).
    /// </summary>
    private readonly Dictionary<string, string> _serverFieldErrors = new(StringComparer.Ordinal);

    private bool _isSubmitting;
    private bool _canSubmit = true;
    private string? _statusMessage;
    private bool _statusIsError;

    public PackageCreateViewModel(IPackageCreationService service, IPathPicker? pathPicker = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _pathPicker = pathPicker ?? new UnavailablePathPicker();

        SubmitCommand = new DelegateCommand(SubmitAsync);
        ResetCommand = new DelegateCommand(() =>
        {
            Reset();
            return Task.CompletedTask;
        });
        PickSourceDirectoryCommand = new DelegateCommand(PickSourceDirectoryAsync);
        PickEntryPointCommand = new DelegateCommand(PickEntryPointAsync);
    }

    // ---------- Campos del formulario ----------

    public string GameId
    {
        get => _gameId;
        set => SetField(ref _gameId, value, nameof(GameId));
    }

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value, nameof(Title));
    }

    public string Version
    {
        get => _version;
        set => SetField(ref _version, value, nameof(Version));
    }

    public string SourceDirectory
    {
        get => _sourceDirectory;
        set => SetField(ref _sourceDirectory, value, nameof(SourceDirectory));
    }

    public string EntryPoint
    {
        get => _entryPoint;
        set => SetField(ref _entryPoint, value, nameof(EntryPoint));
    }

    public string Arguments
    {
        get => _arguments;
        set => SetField(ref _arguments, value, nameof(Arguments));
    }

    // ---------- Errores por campo ----------

    public string? GameIdError
    {
        get => _gameIdError;
        private set => SetField(ref _gameIdError, value, nameof(GameIdError));
    }

    public string? TitleError
    {
        get => _titleError;
        private set => SetField(ref _titleError, value, nameof(TitleError));
    }

    public string? VersionError
    {
        get => _versionError;
        private set => SetField(ref _versionError, value, nameof(VersionError));
    }

    public string? SourceDirectoryError
    {
        get => _sourceDirectoryError;
        private set => SetField(ref _sourceDirectoryError, value, nameof(SourceDirectoryError));
    }

    public string? EntryPointError
    {
        get => _entryPointError;
        private set => SetField(ref _entryPointError, value, nameof(EntryPointError));
    }

    public string? ArgumentsError
    {
        get => _argumentsError;
        private set => SetField(ref _argumentsError, value, nameof(ArgumentsError));
    }

    public bool HasGameIdError => !string.IsNullOrEmpty(_gameIdError);

    public bool HasTitleError => !string.IsNullOrEmpty(_titleError);

    public bool HasVersionError => !string.IsNullOrEmpty(_versionError);

    public bool HasSourceDirectoryError => !string.IsNullOrEmpty(_sourceDirectoryError);

    public bool HasEntryPointError => !string.IsNullOrEmpty(_entryPointError);

    public bool HasArgumentsError => !string.IsNullOrEmpty(_argumentsError);

    // ---------- Estado ----------

    /// <summary>True mientras el paquete se está creando: deshabilita el botón y muestra el progreso.</summary>
    public bool IsSubmitting
    {
        get => _isSubmitting;
        private set
        {
            if (_isSubmitting == value)
            {
                return;
            }

            _isSubmitting = value;
            OnPropertyChanged(nameof(IsSubmitting));
            OnPropertyChanged(nameof(CanPublish));
        }
    }

    /// <summary>Mensaje general del formulario (éxito o error no ligado a un campo).</summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value, nameof(StatusMessage));
    }

    /// <summary>True si el último mensaje es un error (se pinta en color de error).</summary>
    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetField(ref _statusIsError, value, nameof(StatusIsError));
    }

    /// <summary>
    /// Mensaje que se muestra cuando el boton «Examinar…» no puede abrir el
    /// dialogo del sistema operativo. Es accionable: dice que falta el dialogo
    /// y que hay que escribir la ruta a mano.
    /// </summary>
    public const string PathPickerUnavailableMessage =
        "No hay ningún diálogo del sistema operativo disponible: escribe la ruta a mano.";

    public bool HasStatusMessage => !string.IsNullOrEmpty(_statusMessage);

    public DelegateCommand SubmitCommand { get; }

    public DelegateCommand ResetCommand { get; }

    /// <summary>Abre el dialogo de carpetas del sistema operativo.</summary>
    public DelegateCommand PickSourceDirectoryCommand { get; }

    /// <summary>Abre el dialogo de archivos del sistema operativo filtrado a .exe.</summary>
    public DelegateCommand PickEntryPointCommand { get; }

    /// <summary>
    /// Indica si se puede enviar el formulario. Se deshabilita durante el envío.
    ///
    /// El botón de la vista no usa esta propiedad directamente: usa
    /// <see cref="CanPublish"/>, que además comprueba que la carpeta y el
    /// ejecutable elegidos sean utilizables.
    /// </summary>
    public bool CanSubmit
    {
        get => _canSubmit;
        private set => SetField(ref _canSubmit, value, nameof(CanSubmit));
    }

    /// <summary>
    /// Indica si el formulario se puede publicar: no hay ninguna petición en
    /// curso y la carpeta origen y el ejecutable son utilizables.
    ///
    /// La carpeta tiene que existir y el ejecutable tiene que existir y ser un
    /// <c>.exe</c>. Mientras falte cualquiera de las dos, el botón de publicar
    /// queda deshabilitado.
    /// </summary>
    public bool CanPublish => CanSubmit && HasValidSourcePaths;

    /// <summary>
    /// Motivo por el que no se puede publicar todavia, o null si las rutas son
    /// utilizables.
    ///
    /// Es el mensaje que explica el boton deshabilitado: el organizador elige la
    /// carpeta y el ejecutable con los dialogos, pero entre una eleccion y otra
    /// el contenido del disco puede cambiar (el organizador puede borrar la
    /// carpeta o mover el ejecutable). El texto sale de la misma regla que
    /// deshabilita el boton, asi que siempre coinciden.
    /// </summary>
    public string? SourcePathsError => PackageSourcePaths.Validate(SourceDirectory, EntryPoint);

    /// <summary>True cuando hay un motivo por el que no se puede publicar.</summary>
    public bool HasSourcePathsError => !string.IsNullOrEmpty(SourcePathsError);

    /// <summary>
    /// True cuando la carpeta origen y el ejecutable son utilizables. Se
    /// recalcula cada vez que se leen porque el contenido del disco puede cambiar
    /// sin que el formulario se entere (el organizador puede borrar la carpeta
    /// despues de elegirla).
    ///
    /// La regla es la de <see cref="PackageSourcePaths.Validate"/>, la misma que
    /// usan los mensajes de los campos: el boton se deshabilita y el campo dice
    /// por que, sin poder divergir.
    /// </summary>
    private bool HasValidSourcePaths =>
        PackageSourcePaths.Validate(SourceDirectory, EntryPoint) is null;

    // ---------- Acciones ----------

    /// <summary>
    /// Valida en cliente y, si todo está correcto, pide la creación del paquete.
    /// Tras crearse, el formulario se vacía para poder meter el siguiente juego.
    /// </summary>
    public async Task SubmitAsync()
    {
        if (IsSubmitting)
        {
            return;
        }

        ClearErrors();

        // La validacion se hace sobre lo que el organizador ha elegido: la ruta
        // absoluta de la carpeta y la del ejecutable. Son las rutas que se
        // comprueban contra el disco.
        var clientErrors = PackageCreationValidator.Validate(BuildFormRequest());
        if (clientErrors.Count > 0)
        {
            ApplyClientErrors(clientErrors);
            StatusMessage = "Revisa los campos marcados: no se ha creado el paquete.";
            StatusIsError = true;
            return;
        }

        // El payload lleva la ruta del ejecutable relativa a la carpeta origen.
        // Si la conversion falla (el ejecutable esta fuera de la carpeta), el
        // formulario no se envia y el error se muestra en su campo.
        PackageCreationRequest request;
        try
        {
            request = BuildPayloadRequest();
        }
        catch (ArgumentException ex)
        {
            EntryPointError = ex.Message;
            StatusMessage = "Revisa los campos marcados: no se ha creado el paquete.";
            StatusIsError = true;
            return;
        }

        IsSubmitting = true;
        CanSubmit = false;
        try
        {
            var result = await _service.CreateAsync(request);
            if (result.Succeeded)
            {
                var package = result.Package;
                StatusMessage = package is null
                    ? "Paquete creado."
                    : $"Paquete creado: {package.Title} v{package.Version} ({package.FileCount} archivos).";
                StatusIsError = false;
                ResetFields();
                return;
            }

            ApplyServerErrors(result.FieldErrors);
            StatusMessage = result.ErrorMessage ?? "No se ha podido crear el paquete.";
            StatusIsError = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al crear el paquete: {ex.Message}";
            StatusIsError = true;
        }
        finally
        {
            IsSubmitting = false;
            CanSubmit = true;
        }
    }

    /// <summary>Vacía el formulario y los mensajes de error.</summary>
    public void Reset()
    {
        ResetFields();
        ClearErrors();
        StatusMessage = null;
        StatusIsError = false;
    }

    /// <summary>
    /// Abre el dialogo de carpetas del sistema operativo y guarda la ruta
    /// absoluta de la carpeta elegida. Si el usuario cancela, el campo se queda
    /// como estaba.
    ///
    /// El dialogo solo deja elegir carpetas que existen, pero se comprueba
    /// aqui tambien: un proveedor remoto puede devolver una ruta sin carpeta
    /// local y la carpeta puede desaparecer entre la eleccion y la pulsacion.
    /// </summary>
    public async Task PickSourceDirectoryAsync()
    {
        if (!PathPickerAvailable())
        {
            return;
        }

        var folder = await _pathPicker.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        var path = folder.Trim();
        if (!Directory.Exists(path))
        {
            SourceDirectoryError = PackageSourcePaths.MissingSourceDirectoryMessage;
            StatusMessage = "La carpeta elegida no existe.";
            StatusIsError = true;
            return;
        }

        SourceDirectory = path;
        StatusMessage = null;
        StatusIsError = false;
    }

    /// <summary>
    /// Abre el dialogo de archivos del sistema operativo filtrado a <c>*.exe</c> y
    /// guarda la ruta absoluta del ejecutable elegido. Si el usuario cancela, el
    /// campo se queda como estaba.
    ///
    /// El filtro del dialogo es solo una comodidad: aqui se comprueba la extension
    /// y la existencia para que el campo no guarde un archivo que no vale como
    /// ejecutable. Las dos reglas son las de
    /// <see cref="PackageSourcePaths.Validate"/>, las mismas que deshabilitan el
    /// boton de publicar.
    /// </summary>
    public async Task PickEntryPointAsync()
    {
        if (!PathPickerAvailable())
        {
            return;
        }

        var executable = await _pathPicker.PickExecutableAsync();
        if (string.IsNullOrWhiteSpace(executable))
        {
            return;
        }

        var path = executable.Trim();

        // La extension se comprueba antes que la existencia: un archivo que no es
        // un .exe no vale como ejecutable aunque exista.
        if (!PackageSourcePaths.IsExecutable(path))
        {
            EntryPointError = PackageSourcePaths.NotExecutableMessage;
            StatusMessage = "El archivo elegido no es un ejecutable de Windows.";
            StatusIsError = true;
            return;
        }

        // El dialogo del sistema operativo solo deja elegir archivos que existen,
        // pero un proveedor puede devolver una ruta que no esta en el disco: esa
        // ruta no se guarda en el campo.
        if (!File.Exists(path))
        {
            EntryPointError = PackageSourcePaths.MissingExecutableMessage;
            StatusMessage = "El ejecutable elegido no existe.";
            StatusIsError = true;
            return;
        }

        EntryPoint = path;
        EntryPointError = null;
        StatusMessage = null;
        StatusIsError = false;
    }

    private void ResetFields()
    {
        GameId = string.Empty;
        Title = string.Empty;
        Version = string.Empty;
        SourceDirectory = string.Empty;
        EntryPoint = string.Empty;
        Arguments = string.Empty;
    }

    /// <summary>
    /// Comprueba que hay un dialogo del sistema operativo disponible para elegir
    /// una ruta. Si no lo hay, avisa al organizador y devuelve false.
    ///
    /// Cancelar un dialogo NO es lo mismo que no tener dialogo: cancelar es la
    /// forma normal de dejar el campo como estaba y se queda en silencio. Pero si
    /// el boton «Examinar…» no puede abrir nada, el organizador veria que no pasa
    /// nada y no tendria ninguna pista de por que.
    /// </summary>
    private bool PathPickerAvailable()
    {
        if (_pathPicker.IsAvailable)
        {
            return true;
        }

        StatusMessage = PathPickerUnavailableMessage;
        StatusIsError = true;
        return false;
    }

    /// <summary>
    /// Monta la peticion con lo que hay escrito en el formulario, sin transformar
    /// las rutas. Es la peticion que se valida en cliente: la carpeta y el
    /// ejecutable se comprueban contra el disco con las rutas absolutas que ha
    /// elegido el organizador.
    /// </summary>
    private PackageCreationRequest BuildFormRequest() =>
        new(
            GameId.Trim(),
            Title.Trim(),
            Version.Trim(),
            SourceDirectory.Trim(),
            EntryPoint.Trim(),
            string.IsNullOrWhiteSpace(Arguments) ? null : Arguments.Trim());

    /// <summary>
    /// Monta la peticion que se envia al servidor.
    ///
    /// La carpeta origen se envia tal cual (ruta absoluta del servidor) y el
    /// ejecutable se convierte a ruta relativa dentro de esa carpeta: es lo que
    /// espera el payload del endpoint y lo que el cliente resuelve contra el
    /// directorio de instalacion del juego.
    ///
    /// Lanza <see cref="ArgumentException"/> si el ejecutable no esta dentro de
    /// la carpeta origen.
    /// </summary>
    private PackageCreationRequest BuildPayloadRequest()
    {
        var sourceDirectory = SourceDirectory.Trim();
        var entryPoint = PackageSourcePaths.ToPackageRelativePath(sourceDirectory, EntryPoint.Trim());

        return new(
            GameId.Trim(),
            Title.Trim(),
            Version.Trim(),
            sourceDirectory,
            entryPoint,
            string.IsNullOrWhiteSpace(Arguments) ? null : Arguments.Trim());
    }

    /// <summary>
    /// Muestra los errores de la validación de cliente. No toca los errores del
    /// servidor: todavia no se ha enviado nada.
    /// </summary>
    private void ApplyClientErrors(IReadOnlyDictionary<string, string> errors)
    {
        GameIdError = errors.GetValueOrDefault(PackageCreationFields.GameId);
        TitleError = errors.GetValueOrDefault(PackageCreationFields.Title);
        VersionError = errors.GetValueOrDefault(PackageCreationFields.Version);
        SourceDirectoryError = errors.GetValueOrDefault(PackageCreationFields.SourceDirectory);
        EntryPointError = errors.GetValueOrDefault(PackageCreationFields.EntryPoint);
        ArgumentsError = errors.GetValueOrDefault(PackageCreationFields.Arguments);
    }

    /// <summary>
    /// Muestra los errores por campo que devuelve el servidor. Se guardan aparte de
    /// los errores mostrados porque son los unicos que la validación de cliente no
    /// puede reproducir (carpeta inexistente, ejecutable que no esta en el paquete).
    /// Si el usuario edita el campo y el cliente ya no se queja, el mensaje del
    /// servidor se conserva hasta el siguiente envío.
    /// </summary>
    private void ApplyServerErrors(IReadOnlyDictionary<string, string> errors)
    {
        _serverFieldErrors.Clear();
        foreach (var error in errors)
        {
            _serverFieldErrors[error.Key] = error.Value;
        }

        ApplyClientErrors(errors);
    }

    private void ClearErrors()
    {
        _serverFieldErrors.Clear();
        ApplyClientErrors(new Dictionary<string, string>());
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);

        // El boton de publicar depende de las rutas elegidas: al cambiarlas hay
        // que avisar de que su estado puede haber cambiado, y tambien del
        // mensaje que explica por que sigue deshabilitado.
        if (propertyName is nameof(SourceDirectory) or nameof(EntryPoint))
        {
            OnPropertyChanged(nameof(CanPublish));
            OnPropertyChanged(nameof(SourcePathsError));
            OnPropertyChanged(nameof(HasSourcePathsError));
        }

        // CanPublish depende de CanSubmit: si el envio arranca o termina, el
        // estado del boton puede cambiar.
        if (propertyName is nameof(CanSubmit))
        {
            OnPropertyChanged(nameof(CanPublish));
        }

        // Si el campo ya tenía un error, se vuelve a validar al editarlo para que
        // el mensaje refleje el valor actual.
        if (HasErrorFor(propertyName))
        {
            RefreshFieldError(propertyName);
        }
    }

    /// <summary>
    /// Recalcula el error de un campo tras editarlo. La validación de cliente manda
    /// cuando se queja; si el cliente ya no ve problema, se conserva el mensaje del
    /// servidor, que sigue siendo el dato válido (por ejemplo, "la carpeta origen no
    /// existe" solo lo puede saber el servidor) hasta el siguiente envío.
    /// </summary>
    private void RefreshFieldError(string? propertyName)
    {
        var clientErrors = PackageCreationValidator.Validate(BuildFormRequest());
        var fieldKey = FieldKeyFor(propertyName);
        if (fieldKey is null)
        {
            return;
        }

        if (clientErrors.TryGetValue(fieldKey, out var clientMessage))
        {
            SetFieldError(fieldKey, clientMessage);
            return;
        }

        // Sin error de cliente: el mensaje del servidor se mantiene si lo habia.
        var serverMessage = _serverFieldErrors.GetValueOrDefault(fieldKey);
        SetFieldError(fieldKey, serverMessage);
    }

    /// <summary>Traduce el nombre de una propiedad del formulario a la clave de campo.</summary>
    private static string? FieldKeyFor(string? propertyName) => propertyName switch
    {
        nameof(GameId) => PackageCreationFields.GameId,
        nameof(Title) => PackageCreationFields.Title,
        nameof(Version) => PackageCreationFields.Version,
        nameof(SourceDirectory) => PackageCreationFields.SourceDirectory,
        nameof(EntryPoint) => PackageCreationFields.EntryPoint,
        nameof(Arguments) => PackageCreationFields.Arguments,
        _ => null,
    };

    /// <summary>Asigna el mensaje de error de un campo concreto.</summary>
    private void SetFieldError(string fieldKey, string? message)
    {
        switch (fieldKey)
        {
            case PackageCreationFields.GameId:
                GameIdError = message;
                break;
            case PackageCreationFields.Title:
                TitleError = message;
                break;
            case PackageCreationFields.Version:
                VersionError = message;
                break;
            case PackageCreationFields.SourceDirectory:
                SourceDirectoryError = message;
                break;
            case PackageCreationFields.EntryPoint:
                EntryPointError = message;
                break;
            case PackageCreationFields.Arguments:
                ArgumentsError = message;
                break;
        }
    }

    private bool HasErrorFor(string? propertyName) => propertyName switch
    {
        nameof(GameId) => HasGameIdError,
        nameof(Title) => HasTitleError,
        nameof(Version) => HasVersionError,
        nameof(SourceDirectory) => HasSourceDirectoryError,
        nameof(EntryPoint) => HasEntryPointError,
        nameof(Arguments) => HasArgumentsError,
        _ => false,
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
