# AGENTS.md — LAN Forge

## Qué es

Plataforma de escritorio local para gestionar una LAN party en Windows. Dos apps .NET 10 + Avalonia:

- **Server** (`src/Server`): organizador. Catálogo de juegos, paquetes firmados, publicación HTTP, anuncio mDNS, vista de clientes/operaciones. Estructura:
  - `Domain/`: `Game` (+ `GameId` slug validado), `Package` (MVP: `PackageId == GameId`), `ServerOperation`, `SeenClient`.
  - `Application/`: puertos (`ICatalogStore`, `IPackageContentStore`, `IServerIdentity`) + `CatalogService` (casos de uso).
  - `Infrastructure/`: `SqliteCatalogStore` (Microsoft.Data.Sqlite, FK activas), `FilePackageContentStore` (`{dataDir}/packages/{packageId}/`, confinamiento de rutas), `FileServerIdentityStore` (`identity.json`, clave Ed25519 estable entre arranques).
  - `Http/`: `ServerOptions` (puerto default 8710), `ApiEndpoints` (`hello`, `catalog`, `manifest`, `download/{**path}`), `ServerWebHost.CreateApp` (fábrica testeable).
  - Publicar paquete: hashear archivos → importar contenido → manifiesto firmado → SQLite. La descarga solo sirve archivos declarados en el manifiesto firmado.
  - mDNS (2.5), logs JSONL (2.6) y UI del organizador (2.7): implementados.
- **Client** (`src/Client`): jugador. Descubre/configura servidor, sincroniza catálogo, fija fingerprint de confianza (TOFU), descarga por HTTP, valida firma + sha256, instala portables y lanza juegos. Estructura:
  - `Domain/`: `InstalledGame`, `ClientOperation`.
  - `Application/`: puertos (`IClientStore`, `IClientLog`, `IServerApi`) + `TrustManager` (TOFU), `CatalogSyncService` (handshake+sync), `PackageDownloader` (Range/reanudación), `PackageValidator` (firma antes de descargar, sha256 después), `GameInstaller` (confinado, limpia versiones), `GameLauncher` (`IProcessStarter`), `ClientSession` (fachada para la UI), excepciones de negocio tipadas.
  - `Infrastructure/`: `SqliteClientStore`, `JsonlClientLog`, `HttpServerApi` (headers `X-LanForge-Client-Id`/`Operation-Id`), `ServerDiscovery` (mDNS), `TxtProperties`.
  - `ClientHost.CreateAsync(options, baseUrl)` — raíz de composición.
  - Datos en `%AppData%\LanForge\Client`: `games/{gameId}/{version}`, `downloads/` (staging, se limpia), `logs/`.
- **Shared** (`src/Shared`): SOLO contratos compartidos (modelos, manifiesto, firma, logs). No depende de nada. Contiene:
  - `Models/`: `GameInfo`, `CatalogEntry`, `PackageVersion` (semver-lite comparable).
  - `Manifest/`: esquema v1, `PackageManifestPayload`, `SignedPackageManifest`, `ManifestSigner`, `ManifestVerifier`.
  - `Security/`: Ed25519 (NSec/libsodium), `KeyPair`, `Fingerprint` (SHA-256 de la clave pública, hex agrupado), `Sha256Hash`.
  - `Protocol/`: `ProtocolVersion.Current = 1`, `HelloResponse`.
  - `Logging/`: `LogEntry`, `LogSeverity`.
  - `SharedJson`: opciones JSON únicas — los bytes canónicos firmados y el formato alambre son el mismo JSON.

Ver `docs/plan.md` (principios) y `docs/tasks.md` (desglose por fases).

## Reglas de arquitectura

- Arquitectura Clean/Hexagonal. `Shared` jamás referencia Server/Client ni librerías de UI/infra.
- Server y Client solo se comunican a través de los contratos de `Shared`.
- Sin autenticación de clientes; solo compatibilidad de versión de protocolo.
- Todo funciona en LAN, sin depender de Internet.
- Manifiestos firmados; firma y sha256 se validan ANTES de instalar.
- Instalación portable confinada al directorio autorizado (rechazar path traversal).
- Persistencia SQLite; logs JSON correlacionados por `clientId`/`operationId`.

## Comandos

```powershell
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
dotnet test tests/Unit --configuration Release
```

## Convenciones

- Central Package Management: versiones SOLO en `Directory.Packages.props`.
- SDK fijado en `global.json` (10.0.403, rollForward latestFeature).
- Estilo en `.editorconfig` (usings fuera de namespace, `_camelCase` para campos privados).
- Tests: xUnit. Unit / Integration en CI; E2E requiere LAN real y no corre en CI.
- CI: `.github/workflows/ci.yml` (Windows, build + tests Unit/Integration).
- mDNS: `Makaretu.Dns.Multicast`; el servicio es `_lanforge._tcp` con TXT `name` y `fingerprint` (constantes en `Shared.Protocol.LanService`). El paquete `Makaretu.Dns` (sin `.Multicast`) NO contiene ServiceDiscovery.
- Los `operation_id` se upsertean (una fila por operación, estado Started→Completed).
- Publicación: `build/publish.ps1` (single-file win-x64 auto-contenido en `artifacts/`).
- Design: `.kimi-design/` (sincronizado con `kimix design sync`; ver skill `kimi-design-usage`). El tema del cliente (`src/Client/Theme.axaml`) usa los tokens del design `lanforge-game-catalog.kui` (fondo oscuro, accent `#A3E635`, MiSans y Font Awesome empaquetadas en `Assets/`). Los estilos con clave son `ControlTheme` (Avalonia 12) y se aplican con `Theme="{StaticResource ...}"`, no `Style=`. Estados activos (nav, chips) por clase `active` (estilo `^.active`); NUNCA valores locales Background/Foreground sobre controles con Theme (rompen el re-render).
- Transferencia de paquetes: el servidor almacena `{dataDir}/packages/{gameId}.zip` (único artefacto); el cliente descarga el zip (`.part` reanudable), lo extrae con `PackageExtractor` (anti-ZipSlip), valida firma+sha256 por archivo e instala; staging borrado al terminar.
- Portadas: `{dataDir}/covers/{gameId}{ext}` servidas en `GET /api/v1/covers/{fileName}` (nombre validado); el catálogo incluye `CatalogEntry.CoverPath` y el cliente cachea en `%AppData%\LanForge\Client\covers` con fallback a las portadas del design.
- Chat SignalR: hub en `/api/v1/chat` (`ChatHub` + `ChatRoomRegistry` singleton). Canales efímeros: `#general` permanente (constantes en `Shared.Chat.ChatHubContract`), salas por gameId borradas al vaciarse; sin historial. UI del cliente: nav "Jugadores"; la app del servidor lista canales activos (pestaña Canales).
