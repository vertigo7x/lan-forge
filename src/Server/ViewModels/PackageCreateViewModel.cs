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

    private bool _isSubmitting;
    private string? _statusMessage;
    private bool _statusIsError;

    public PackageCreateViewModel(IPackageCreationService service)
    {
        _service = service;
        SubmitCommand = new DelegateCommand(SubmitAsync);
        ResetCommand = new DelegateCommand(() =>
        {
            Reset();
            return Task.CompletedTask;
        });
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
        private set => SetField(ref _isSubmitting, value, nameof(IsSubmitting));
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

    public bool HasStatusMessage => !string.IsNullOrEmpty(_statusMessage);

    public DelegateCommand SubmitCommand { get; }

    public DelegateCommand ResetCommand { get; }

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
        var request = BuildRequest();
        var clientErrors = PackageCreationValidator.Validate(request);
        if (clientErrors.Count > 0)
        {
            ApplyFieldErrors(clientErrors);
            StatusMessage = "Revisa los campos marcados: no se ha creado el paquete.";
            StatusIsError = true;
            return;
        }

        IsSubmitting = true;
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

            ApplyFieldErrors(result.FieldErrors);
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

    private void ResetFields()
    {
        GameId = string.Empty;
        Title = string.Empty;
        Version = string.Empty;
        SourceDirectory = string.Empty;
        EntryPoint = string.Empty;
        Arguments = string.Empty;
    }

    private PackageCreationRequest BuildRequest() =>
        new(
            GameId.Trim(),
            Title.Trim(),
            Version.Trim(),
            SourceDirectory.Trim(),
            EntryPoint.Trim(),
            string.IsNullOrWhiteSpace(Arguments) ? null : Arguments.Trim());

    private void ApplyFieldErrors(IReadOnlyDictionary<string, string> errors)
    {
        GameIdError = errors.GetValueOrDefault(PackageCreationFields.GameId);
        TitleError = errors.GetValueOrDefault(PackageCreationFields.Title);
        VersionError = errors.GetValueOrDefault(PackageCreationFields.Version);
        SourceDirectoryError = errors.GetValueOrDefault(PackageCreationFields.SourceDirectory);
        EntryPointError = errors.GetValueOrDefault(PackageCreationFields.EntryPoint);
        ArgumentsError = errors.GetValueOrDefault(PackageCreationFields.Arguments);
    }

    private void ClearErrors()
    {
        ApplyFieldErrors(new Dictionary<string, string>());
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);

        // Si el campo ya tenía un error, se vuelve a validar al editarlo para que
        // el mensaje refleje el valor actual.
        if (HasErrorFor(propertyName))
        {
            ApplyFieldErrors(PackageCreationValidator.Validate(BuildRequest()));
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
