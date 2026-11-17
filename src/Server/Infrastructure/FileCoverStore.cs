using System.Text.RegularExpressions;
using LanForge.Server.Application;

namespace LanForge.Server.Infrastructure;

/// <summary>Portadas en disco: {root}/{gameId}{ext}. Los nombres servidos por HTTP se validan estrictamente.</summary>
public sealed class FileCoverStore : ICoverStore
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp",
    };

    private static readonly Regex FileNamePattern = new(
        "^[a-zA-Z0-9_-]+\\.(png|jpg|jpeg|webp)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly string _root;

    public FileCoverStore(string root)
    {
        _root = root;
        Directory.CreateDirectory(_root);
    }

    public Task<string> SaveAsync(string gameId, string sourceImagePath, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(sourceImagePath);
        if (!AllowedExtensions.Contains(extension))
        {
            throw new ArgumentException(
                $"Formato de imagen no soportado: '{extension}'. Usa png, jpg, jpeg o webp.",
                nameof(sourceImagePath));
        }

        var fileName = $"{gameId}{extension.ToLowerInvariant()}";
        File.Copy(sourceImagePath, Path.Combine(_root, fileName), overwrite: true);
        return Task.FromResult(fileName);
    }

    public Task<Stream?> OpenReadAsync(string fileName, CancellationToken cancellationToken = default)
    {
        // Solo nombres planos permitidos: sin rutas, sin traversal.
        if (!FileNamePattern.IsMatch(fileName))
        {
            return Task.FromResult<Stream?>(null);
        }

        var path = Path.Combine(_root, fileName);
        if (!File.Exists(path))
        {
            return Task.FromResult<Stream?>(null);
        }

        return Task.FromResult<Stream?>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
    }
}
