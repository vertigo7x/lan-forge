using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using LANPartyHub.Client.Application.Downloads;
using LANPartyHub.Client.Application.Ports;
using LANPartyHub.Client.Host;
using LANPartyHub.Client.Infrastructure;
using LANPartyHub.Client.Infrastructure.Networking;
using LANPartyHub.Client.Infrastructure.Persistence;
using LANPartyHub.Server.Application.Packages;
using LANPartyHub.Server.Host;
using LANPartyHub.Server.Infrastructure.Persistence;
using LANPartyHub.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ClientProgram = LANPartyHub.Client.Program;

namespace LANPartyHub.IntegrationTests;

/// <summary>
/// Survival of a download of a LARGE package to a real restart of the client
/// (S-5c, AD-11, AD-20, AD-24, SM-2).
///
/// <para>
/// La escala importa: el offset persistido se escribe en el disco cada pocos kiB y el
/// parcial puede medir GIB, así que una reanudación sobre un paquete grande es un caso
/// DISTINTO del de un paquete pequeño. La prueba interrumpe la descarga cuando el
/// parcial ya es grande, reinicia el cliente de verdad y comprueba que el paquete
/// terminado tiene EXACTAMENTE el mismo hash que el original.
/// </para>
/// <para>
/// La prueba NO está en la suite por defecto: 7 GiB de disco y varios minutos de
///Transferencia multiplicarían el coste de cada ejecución. Se ejecuta de forma
/// explícita con la variable <c>LAN_PARTY_HUB_LARGE_PACKAGE_BYTES</c>, que además fija
/// el tamaño del paquete (AD-16).
/// </para>
/// </summary>
[Collection(RealHostCollection.Name)]
public sealed class DownloadLargePackageResumeTests : IDisposable
{
    /// <summary>
    /// Variable that enables the test and fixes the size of the package, in bytes.
    /// </summary>
    public const string SizeVariable = "LAN_PARTY_HUB_LARGE_PACKAGE_BYTES";

    /// <summary>
    /// Smallest size that the scenario admits: below it, the interruption would happen
    /// before any byte and the measurement would say nothing.
    /// </summary>
    private const long MinimumSize = 64L * 1024 * 1024;

    /// <summary>
    /// Biggest size that the server can publish with the current product: the runtime
    /// refuses to read a file of 2 GiB or more into memory, and the publication path
    /// still does exactly that (see the finding in the spike report).
    /// </summary>
    private const long MaximumPublishableSize = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// Reason of the omission when the test is not invoked explicitly.
    /// </summary>
    public const string NotInvokedReason =
        "La prueba de escala no se ejecuta en la suite por defecto: hay que invocarla con "
        + "LAN_PARTY_HUB_LARGE_PACKAGE_BYTES, que fija el tamaño del paquete en bytes.";

    /// <summary>
    /// Identifier of the package of the scenario.
    /// </summary>
    private const string PackageId = "0195f3b2-4c8a-7c1e-9d33-2f6b8a1c5e80";

    /// <summary>
    /// Bytes that the partial has to reach before the download is interrupted, so the
    /// resume starts from an offset that is NOT trivial. It is a quarter of the package,
    /// and never more than 256 MiB.
    /// </summary>
    private const long MaximumInterruptionThreshold = 256L * 1024 * 1024;

    /// <summary>
    /// Size of the block that the content of the package is written in.
    /// </summary>
    private const int BlockSize = 1024 * 1024;

    private readonly List<string> _temporaryFolders = [];

    /// <summary>
    /// A package of several GiB survives an interruption and a restart of the client, and
    /// the file that it leaves behind is byte by byte the original one.
    /// </summary>
    [Fact]
    public async Task A_large_package_survives_an_interruption_and_the_restart_keeps_its_hash()
    {
        var size = ReadRequestedSize();

        var threshold = Math.Min(MaximumInterruptionThreshold, size / 4);

        var dataFolder = CreateTemporaryFolder();

        await using var host = await TestServer.StartAsync(TestContext.Current.CancellationToken);

        var expectedHash = await WriteLargePackageAsync(host.DataDirectory, size);

        using var serverClient = host.CreateClient();

        await RegisterGameAndPublishAsync(serverClient, host.DataDirectory, size);

        // La primera sesión: la descarga avanza y se INTERRUMPE cuando el parcial ya es
        // grande (AD-11, SM-2).
        var first = await RunClientAsync(dataFolder, host, interrupted: true, threshold, size);

        Assert.True(
            first.PartialLength >= threshold,
            $"La interrupción dejó un parcial de {first.PartialLength} bytes, que no llega al "
            + $"umbral de {threshold} bytes.");

        Assert.True(
            first.PersistedOffset > 0,
            "La interrupción no dejó offset persistido.");

        // La segunda sesión es el REINICIO: el cliente real arranca sobre la MISMA carpeta
        // de datos, recupera la operación y termina el paquete.
        var second = await RunClientAsync(dataFolder, host, interrupted: false, threshold, size);

        Assert.Equal(size, second.PartialLength);

        // La reanudación pide el rango que empieza en el offset persistido y NUNCA desde
        // cero (AC2, AD-20).
        var range = Assert.Single(second.RequestedRanges);

        Assert.Equal($"bytes={first.PersistedOffset}-", range);

        // El hash final es el del paquete original: la reanudación no alteró ni un byte.
        var actualHash = await ComputeHashAsync(second.PartialPath);

        Assert.Equal(expectedHash, actualHash);
    }

    /// <summary>
    /// Reads the size that the operator asked for, or omits the test when it was not
    /// invoked explicitly.
    /// </summary>
    /// <returns>The size of the package, in bytes.</returns>
    private static long ReadRequestedSize()
    {
        var raw = Environment.GetEnvironmentVariable(SizeVariable);

        if (string.IsNullOrWhiteSpace(raw))
        {
            Assert.Skip(NotInvokedReason);
        }

        if (!long.TryParse(raw, out var size) || size <= 0)
        {
            Assert.Fail($"La variable {SizeVariable} tiene que ser un tamaño en bytes mayor que cero: '{raw}'.");
        }

        if (size < MinimumSize)
        {
            Assert.Fail(
                $"La prueba de escala necesita un paquete de al menos {MinimumSize} bytes: '{raw}'.");
        }

        if (size >= MaximumPublishableSize)
        {
            Assert.Fail(
                "El servidor no publica paquetes de 2 GiB o más: la ruta de publicación lee el "
                + "paquete entero en memoria y el runtime rechaza esa lectura. Es el hallazgo "
                + "S1 del informe del spike S-5, no un fallo de la reanudación.");
        }

        return size;
    }

    /// <summary>
    /// Writes the content of the package on the disk of the host and returns its hash.
    /// </summary>
    /// <param name="dataDirectory">Folder of the data of the host.</param>
    /// <param name="size">Exact size of the package, in bytes.</param>
    /// <returns>The hexadecimal sha256 of the package.</returns>
    private static async Task<string> WriteLargePackageAsync(string dataDirectory, long size)
    {
        var packages = Path.Combine(dataDirectory, DataDirectory.PackagesFolderName);

        Directory.CreateDirectory(packages);

        var path = Path.Combine(packages, PackageId);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        await using (var file = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: BlockSize,
            useAsync: true))
        {
            var block = new byte[BlockSize];
            long written = 0;

            while (written < size)
            {
                // Cada bloque tiene un contenido PROPIO y reproducible: un bloque
                // desplazado no puede parecerse a otro, así que un hash igual demuestra
                // que el contenido está en su sitio (AD-24).
                var length = (int)Math.Min(BlockSize, size - written);

                RandomNumberGenerator.Fill(block);

                await file.WriteAsync(block.AsMemory(0, length), TestContext.Current.CancellationToken);

                hash.AppendData(block, 0, length);

                written += length;
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>
    /// Computes the sha256 of a file without loading it into memory.
    /// </summary>
    /// <param name="path">Absolute path of the file.</param>
    /// <returns>The hexadecimal sha256 of the file.</returns>
    private static async Task<string> ComputeHashAsync(string path)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: BlockSize,
            useAsync: true);

        var hash = await SHA256.HashDataAsync(stream, TestContext.Current.CancellationToken);

        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Runs one session of the client over the given data folder, which is what a restart
    /// of the team does (AD-11).
    /// </summary>
    /// <param name="dataFolder">Folder of the data of the client.</param>
    /// <param name="host">The real host of the event server.</param>
    /// <param name="interrupted">
    /// <c>true</c> when the download has to be interrupted once the partial is large;
    /// <c>false</c> when the session has to finish the package.
    /// </param>
    /// <param name="threshold">Bytes that the partial has to reach before interrupting.</param>
    /// <param name="size">Exact size of the package, in bytes.</param>
    /// <returns>What the session observed.</returns>
    private async Task<ClientSession> RunClientAsync(
        string dataFolder,
        TestServer host,
        bool interrupted,
        long threshold,
        long size)
    {
        var logFolder = CreateTemporaryFolder();

        using var loggerFactory = ClientProgram.CreateLoggerFactory(logFolder);
        using var correlation = new ClientCorrelationFactory(loggerFactory, "equipo-de-prueba");

        var startup = await ClientProgram.StartClientAsync(
            [],
            correlation,
            loggerFactory,
            dataFolder: dataFolder,
            baseDirectory: logFolder,
            cancellationToken: TestContext.Current.CancellationToken);

        var layout = ClientDataLayout.Create(startup.DataFolder, startup.InstallDirectory);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = startup.StateDatabasePath,
            Pooling = false,
        }.ToString();

        var options = new DbContextOptionsBuilder<ClientStateDbContext>()
            .UseSqlite(
                connectionString,
                sqlite => sqlite.MigrationsAssembly(typeof(ClientStateDbContext).Assembly.GetName().Name))
            .Options;

        await using var context = new ClientStateDbContext(options);

        var stateStore = new ClientStateStore(context);

        var ranges = new List<string>();

        var headers = new StubRequestHeaders(ClientId);

        using var httpClient = new HttpClient(
            new ClientRequestHeadersHandler(headers, new RealClientVersionProvider())
            {
                InnerHandler = new RecordingRangeHandler(new HttpClientHandler(), ranges),
            })
        {
            BaseAddress = new Uri($"http://{host.BoundAddress}:{host.Port}"),
        };

        var engine = new HttpRangeTransferEngine(httpClient);

        using var composition = new DownloadComposition(
            new Uri($"http://{host.BoundAddress}:{host.Port}"),
            headers,
            stateStore,
            layout,
            engine,
            loggerFactory);

        DownloadOperationHandle? handle = null;

        composition.Coordinator.Changed += published => handle ??= published;

        DownloadOperationHandle started;

        var persisted = await stateStore.LoadDownloadOperationsAsync(TestContext.Current.CancellationToken);

        if (persisted.Any(operation => operation.PackageId == PackageId))
        {
            // El REINICIO: la operación persistida se rehidrata y se reanuda (AD-11).
            var recovered = await composition.Coordinator.RecoverAsync(
                composition.ContentUriResolver,
                TestContext.Current.CancellationToken);

            started = Assert.Single(recovered, operation => operation.PackageId == PackageId);
        }
        else
        {
            started = await composition.Coordinator.StartAsync(
                PackageId,
                composition.ContentUriResolver(PackageId),
                TestContext.Current.CancellationToken);
        }

        var partialPath = layout.PartialFilePath(PackageId);

        if (interrupted)
        {
            await WaitUntilAsync(
                () => ReadPartialLength(layout) >= threshold,
                TimeSpan.FromMinutes(30));

            // La interrupción tiene que caer ANTES de que el paquete esté completo: si la
            // descarga se terminó, no se midió ninguna reanudación (S-5c).
            Assert.True(
                ReadPartialLength(layout) < size,
                "La descarga se terminó antes de que la prueba la interrumpiera.");

            await composition.Coordinator.PauseAsync(started);

            await WaitUntilAsync(
                () => started.StateMachine.Current == DownloadState.Paused,
                TimeSpan.FromMinutes(5));

            Assert.Equal(DownloadState.Paused, started.StateMachine.Current);
        }
        else
        {
            // El final de la transferencia se observa por DOS hechos: el parcial tiene
            // el tamaño del paquete y el offset persistido lo alcanza. La composición de
            // esta prueba no valida el manifiesto (el ancla no está fijada), así que el
            // estado de la operación no llega a «Completed» y no es el que se observa.
            var deadline = DateTime.UtcNow.Add(TimeSpan.FromMinutes(30));

            while (true)
            {
                var persistedOffset = await ReadPersistedOffset(stateStore);

                if (ReadPartialLength(layout) >= size && persistedOffset >= size)
                {
                    break;
                }

                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("La descarga del paquete grande no terminó dentro del plazo.");
                }

                await Task.Delay(200, TestContext.Current.CancellationToken);
            }
        }

        return new ClientSession(
            started,
            await ReadPersistedOffset(stateStore),
            ReadPartialLength(layout),
            partialPath,
            ranges.ToArray());
    }

    /// <summary>
    /// Waits until the condition holds, so the test no tiene que dormir un tiempo fijo
    /// (AD-16).
    /// </summary>
    /// <param name="condition">Condition that has to become true.</param>
    /// <param name="timeout">Plazo of the wait.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("La condición no se cumplió dentro del plazo.");
            }

            await Task.Delay(200, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Reads the size of the partial of the package on the disk.
    /// </summary>
    /// <param name="layout">Layout of the data of the client.</param>
    /// <returns>The size of the partial, in bytes.</returns>
    private long ReadPartialLength(ClientDataLayout layout)
    {
        var path = layout.PartialFilePath(PackageId);

        return File.Exists(path) ? new FileInfo(path).Length : 0L;
    }

    /// <summary>
    /// Reads the offset that the client persisted for the operation.
    /// </summary>
    /// <param name="stateStore">Store of the state of the client.</param>
    /// <returns>The persisted offset, in bytes.</returns>
    private static async Task<long> ReadPersistedOffset(ClientStateStore stateStore)
    {
        var operations = await stateStore.LoadDownloadOperationsAsync(TestContext.Current.CancellationToken);

        return operations
            .Where(operation => operation.PackageId == PackageId)
            .Select(operation => operation.BytesPersisted ?? 0L)
            .FirstOrDefault();
    }

    /// <summary>
    /// Registers a videogame and publishes the package, so the server can serve its
    /// content by ranges (AD-7, AD-20).
    /// </summary>
    /// <param name="client">Client of the host.</param>
    /// <param name="dataDirectory">Folder of the data of the host.</param>
    /// <param name="sizeBytes">Exact size of the package.</param>
    private static async Task RegisterGameAndPublishAsync(
        HttpClient client,
        string dataDirectory,
        long sizeBytes)
    {
        var gameRequest = new GameRequest
        {
            SchemaVersion = GamesEndpoint.SupportedSchemaVersion,
            Game = new GameDescriptor(
                string.Empty,
                "Paquete de escala",
                "Paquete de varios gibibytes para medir la reanudación.",
                ["Prueba"],
                ["fps"],
                "cover.png",
                ["captura-1.png"],
                "1.0.0",
                sizeBytes,
                1,
                10,
                "Windows",
                "Servidor",
                "Portable",
                new InstallationManifest.LaunchSettings("game\\game.exe", [], "game"),
                ["DirectX 12"],
                "Propietaria",
                "Solo para el evento"),
        };

        using var gameResponse = await client.PostAsync(
            GamesEndpoint.Route,
            BuildBody(gameRequest),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, gameResponse.StatusCode);

        var gameDescriptor = await ReadDescriptorAsync(gameResponse, TestContext.Current.CancellationToken);

        var packageRequest = new PackageRequest
        {
            SchemaVersion = PackagesEndpoint.SupportedSchemaVersion,
            GameId = gameDescriptor.GameId,
            Package = new PackageDescriptor(
                PackageId,
                gameDescriptor.GameId,
                "1.0.0",
                sizeBytes,
                0,
                string.Empty,
                string.Empty),
            Manifest = new InstallationManifest(
                InstallationManifestSchema.Version,
                PackageId,
                "1.0.0",
                sizeBytes,
                string.Empty,
                new InstallationManifest.LaunchSettings("game\\game.exe", [], "game"),
                [new InstallationManifest.CreateDirectory("game")]),
        };

        using var packageResponse = await client.PostAsync(
            PackagesEndpoint.Route,
            BuildBody(packageRequest),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, packageResponse.StatusCode);

        Assert.NotNull(dataDirectory);
    }

    /// <summary>
    /// Builds the body of a request of the host (AD-5).
    /// </summary>
    /// <typeparam name="T">Type of the request.</typeparam>
    /// <param name="request">Request to build.</param>
    /// <returns>The content of the request.</returns>
    private static StringContent BuildBody<T>(T request) =>
        new(
            JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            System.Text.Encoding.UTF8,
            "application/json");

    /// <summary>
    /// Reads the contract del videojuego de una respuesta (AD-7).
    /// </summary>
    /// <param name="response">Response of the host.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The contract of the videogame.</returns>
    private static async Task<GameDescriptor> ReadDescriptorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        var descriptor = JsonSerializer.Deserialize<GameDescriptor>(
            payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(descriptor);

        return descriptor!;
    }

    private string CreateTemporaryFolder()
    {
        var folder = Directory.CreateTempSubdirectory("lan-party-hub-large-");

        _temporaryFolders.Add(folder.FullName);

        return folder.FullName;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var folder in _temporaryFolders)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // Un fichero que sigue abierto no impide que la prueba termine: la carpeta
                // temporal la limpia el sistema.
            }
            catch (UnauthorizedAccessException)
            {
                // Mismo motivo que antes.
            }
        }
    }

    /// <summary>
    /// Identificador del equipo que usa la prueba.
    /// </summary>
    private const string ClientId = "0195f3b24c8a7c1e9d332f6b8a1c5e75";

    /// <summary>
    /// What a session of the client observed.
    /// </summary>
    /// <param name="Handle">Handle of the operation.</param>
    /// <param name="PersistedOffset">Offset that the client persisted.</param>
    /// <param name="PartialLength">Size of the partial on the disk.</param>
    /// <param name="PartialPath">Path of the partial on the disk.</param>
    /// <param name="RequestedRanges">Ranges that the client asked for.</param>
    private sealed record ClientSession(
        DownloadOperationHandle Handle,
        long PersistedOffset,
        long PartialLength,
        string PartialPath,
        IReadOnlyList<string> RequestedRanges);

    /// <summary>
    /// Headers of the correlation that the composition stamps in every request (AD-15,
    /// AD-23).
    /// </summary>
    private sealed class StubRequestHeaders : IClientRequestHeaders
    {
        public StubRequestHeaders(string? clientId, string? operationId = null)
        {
            ClientId = clientId;
            OperationId = operationId;
        }

        public string? ClientId { get; }

        public string? OperationId { get; }

        public string? MachineName => "equipo-de-prueba";
    }

    /// <summary>
    /// Manejador que RECUERDA el `Range` que el motor pidió y lo deja pasar al servidor
    /// real, así que la observación es del servidor y no del cliente (AD-20).
    /// </summary>
    private sealed class RecordingRangeHandler : DelegatingHandler
    {
        private readonly List<string> _ranges;

        public RecordingRangeHandler(HttpMessageHandler innerHandler, List<string> ranges)
            : base(innerHandler) => _ranges = ranges;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Headers.Range is { } range)
            {
                _ranges.Add(range.ToString());
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
