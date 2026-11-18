# LAN Forge

Plataforma de escritorio local para gestionar una LAN party en Windows. Dos apps **.NET 10 + Avalonia** que funcionan íntegramente en la LAN, sin depender de Internet.

- **Servidor / organizador** (`src/Server`): catálogo de juegos portables, paquetes con manifiestos firmados (Ed25519), publicación por HTTP, anuncio mDNS (`_lanforge._tcp`), vista de clientes y operaciones.
- **Cliente / jugador** (`src/Client`): descubre el servidor (mDNS o manual), fija el fingerprint de confianza fuera de banda (TOFU), sincroniza el catálogo, descarga con reanudación, valida firma + sha256 **antes de instalar**, instala en directorio confinado y lanza los juegos.
- **Shared** (`src/Shared`): contratos comunes (manifiestos, firma, fingerprint, protocolo, logs).

Sin autenticación de clientes: la confianza se basa en el fingerprint TOFU y la compatibilidad de versión de protocolo.

## Requisitos

- .NET SDK 10 (`global.json` fija 10.0.403)
- Windows (Avalonia Desktop)

## Comandos

```powershell
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
```

Ejecutar en desarrollo:

```powershell
dotnet run --project src/Server   # organizador (HTTP :8710 + mDNS + UI)
dotnet run --project src/Client   # jugador
```

## Publicar para la LAN

```powershell
./build/publish.ps1               # artifacts/server y artifacts/client (single-file win-x64)
```

## E2E en LAN real

```powershell
# Con el servidor LAN Forge corriendo en la red:
$env:LANFORGE_E2E_SERVER = "10.0.0.5:8710"
$env:LANFORGE_E2E_DISCOVER = "1"   # opcional: valida también mDNS
dotnet test tests/E2E
```

## Datos en disco

| App | Ubicación |
|-----|-----------|
| Servidor | `%AppData%\LanForge\Server` — `identity.json` (clave Ed25519), `lanforge-server.db`, `packages/{gameId}/`, `logs/*.jsonl` |
| Cliente | `%AppData%\LanForge\Client` — `lanforge-client.db`, `games/{gameId}/{version}/`, `downloads/` (staging temporal), `logs/*.jsonl` |

Documentación de diseño y plan: [`docs/plan.md`](docs/plan.md), [`docs/tasks.md`](docs/tasks.md).

Cambios de calidad del servidor y su trazabilidad con la auditoría:
[`docs/cambios-servidor-8-1.md`](docs/cambios-servidor-8-1.md).
