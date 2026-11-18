using System.IO.Compression;
using System.Text.RegularExpressions;
using LanForge.Server.Application;

namespace LanForge.Server.Infrastructure;

/// <summary>Contenido de paquetes en disco: {root}/{packageId}.zip (un zip por paquete).</summary>
public sealed class FilePackageContentStore : IPackageContentStore
{
    /// <summary>
    /// Identificador de paquete admitido: nombre plano, sin separadores ni ruta. El confinement
    /// final se comprueba ademas sobre la ruta resuelta, para que un id invalido nunca escriba
    /// ni lea fuera de la raiz de contenido.
    /// </summary>
    private static readonly Regex PackageIdPattern = new(
        "^[a-zA-Z0-9_-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _root;

    public FilePackageContentStore(string root)
    {
        _root = root;
        Directory.CreateDirectory(_root);
    }

    public Task ImportArchiveAsync(string packageId, string sourceDirectory, CancellationToken cancellationToken = default)
    {
        if (!TryGetArchivePath(packageId, out var target))
        {
            throw new ArgumentException($"Identificador de paquete invalido: '{packageId}'.", nameof(packageId));
        }

        var temp = target + ".tmp";
        if (File.Exists(temp))
        {
            File.Delete(temp);
        }

        ZipFile.CreateFromDirectory(sourceDirectory, temp, CompressionLevel.SmallestSize, includeBaseDirectory: false);
        File.Move(temp, target, overwrite: true);
        return Task.CompletedTask;
    }

    public Task<Stream?> OpenArchiveAsync(string packageId, CancellationToken cancellationToken = default)
    {
        if (!TryGetArchivePath(packageId, out var path) || !File.Exists(path))
        {
            return Task.FromResult<Stream?>(null);
        }

        return Task.FromResult<Stream?>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
    }

    public Task<Stream?> OpenReadAsync(string packageId, string relativePath, CancellationToken cancellationToken = default)
    {
        if (!TryGetArchivePath(packageId, out var path) || !File.Exists(path))
        {
            return Task.FromResult<Stream?>(null);
        }

        var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry(relativePath);
        if (entry is null)
        {
            archive.Dispose();
            return Task.FromResult<Stream?>(null);
        }

        // El stream del zip depende del ZipArchive: envolvemos para que al cerrar el stream
        // se cierre también el archivo zip.
        return Task.FromResult<Stream?>(new ArchiveEntryStream(archive, entry.Open()));
    }

    public Task<bool> DeleteArchiveAsync(string packageId, CancellationToken cancellationToken = default)
    {
        if (!TryGetArchivePath(packageId, out var path) || !File.Exists(path))
        {
            return Task.FromResult(false);
        }

        File.Delete(path);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Resuelve la ruta del .zip de un paquete dentro de la raiz de contenido. Devuelve false si
    /// el identificador no es valido o si la ruta resuelta se sale de la raiz.
    /// </summary>
    private bool TryGetArchivePath(string packageId, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(packageId) || !PackageIdPattern.IsMatch(packageId))
        {
            return false;
        }

        var candidate = Path.GetFullPath(Path.Combine(_root, $"{packageId}.zip"));
        var root = Path.GetFullPath(_root);
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return false;
        }

        path = candidate;
        return true;
    }

    /// <summary>Stream de una entrada zip que dispone el ZipArchive padre al cerrarse.</summary>
    private sealed class ArchiveEntryStream : Stream
    {
        private readonly ZipArchive _archive;
        private readonly Stream _inner;
        private int _disposed;

        public ArchiveEntryStream(ZipArchive archive, Stream inner)
        {
            _archive = archive;
            _inner = inner;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _inner.Dispose();
                _archive.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
