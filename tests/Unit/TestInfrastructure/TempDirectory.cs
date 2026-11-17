namespace LanForge.UnitTests;

/// <summary>Directorio temporal único por test, eliminado al dispose.</summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lanforge-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path);
    }

    public string CreateSourceDirectory(params (string RelativePath, string Content)[] files)
    {
        var source = System.IO.Path.Combine(Path, "src-" + Guid.NewGuid().ToString("n"));
        foreach (var (relativePath, content) in files)
        {
            var full = System.IO.Path.Combine(source, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        return source;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Mejor esfuerzo; el SO limpiará el %TEMP% eventualmente.
        }
    }
}
