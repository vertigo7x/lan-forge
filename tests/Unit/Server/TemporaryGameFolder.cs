namespace LanForge.UnitTests.Server;

/// <summary>
/// Carpeta de juego real en el disco, con un ejecutable dentro. Las pruebas del
/// formulario necesitan rutas que existan de verdad: desde que la carpeta origen
/// y el ejecutable se eligen con los dialogos del sistema operativo, la
/// validacion de cliente comprueba que ambos existen en el disco.
///
/// La carpeta se borra al final de la prueba.
/// </summary>
public sealed class TemporaryGameFolder : IDisposable
{
    private readonly string _root;

    public TemporaryGameFolder()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            $"lanforge-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        ExecutablePath = Path.Combine(_root, "game.exe");
        File.WriteAllText(ExecutablePath, "MZ");
    }

    /// <summary>Ruta absoluta de la carpeta que hace de origen del paquete.</summary>
    public string SourceDirectory => _root;

    /// <summary>Ruta absoluta del ejecutable dentro de la carpeta.</summary>
    public string ExecutablePath { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Un fichero abierto puede impedir el borrado: no es motivo para
            // fallar la prueba.
        }
    }
}
