namespace LanForge.E2ETests;

/// <summary>Directorio temporal único por test, eliminado al dispose.</summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lanforge-e2e", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Mejor esfuerzo.
        }
    }
}
