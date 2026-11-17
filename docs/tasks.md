# LAN Forge — Desglose de tareas

Basado en `docs/plan.md`. Objetivo: plataforma de escritorio local para gestionar una LAN party en Windows. Dos apps .NET 10 + Avalonia (Servidor y Cliente), compartiendo contratos en `Shared`.

> **Decisiones técnicas propuestas** (marcadas como ⚙️, ajustables):
> - Firma de manifiestos: **Ed25519** (`fingerprint` = hash SHA-256 de la clave pública).
> - mDNS: **Makaretu.Dns** (Zeroconf) para anuncio y descubrimiento.
> - HTTP: **ASP.NET Core minimal APIs** en el servidor.
> - SQLite: **Microsoft.Data.Sqlite** con migraciones propias (sin EF Core para mantener el MVP ligero).
> - Serialización: **System.Text.Json** con esquemas versionados (`schemaVersion`).

---

## Fase 0 — Fundaciones ✅ (completada)

| # | Tarea | Resultado |
|---|-------|-----------|
| 0.1 | ~~Crear solución `LanForge.sln`: `src/Server`, `src/Client`, `src/Shared`, `tests/Unit`, `tests/Integration`, `tests/E2E`~~ ✅ | Estructura Clean/Hexagonal base |
| 0.2 | ~~Configurar `Directory.Packages.props`, `.editorconfig`, `global.json` (SDK .NET 10), Central Package Management~~ ✅ | Builds reproducibles |
| 0.3 | ~~Definir reglas de dependencias entre proyectos (`Shared` no depende de nadie; Server/Client solo hablan con Shared vía contratos)~~ ✅ (ver `AGENTS.md`) | Arquitectura enforceable |
| 0.4 | ~~CI básico: build + tests unitarios en cada push~~ ✅ (`.github/workflows/ci.yml`) | Feedback automático |

## Fase 1 — Shared (contratos) ✅ (completada)

| # | Tarea | Resultado |
|---|-------|-----------|
| 1.1 | ~~Modelos de dominio: `Game`, `Package`, `PackageManifest`, `CatalogEntry`, `SemanticVersion`-propia o `System.Version`~~ ✅ (`GameInfo`, `CatalogEntry`, `PackageVersion`) | Tipos compartidos |
| 1.2 | ~~⚙️ Esquema de manifiesto: `schemaVersion`, id de juego/paquete, versión, lista de archivos con `sha256` y tamaño, fecha, clave pública del firmante~~ ✅ (`PackageManifestPayload` + `SignedPackageManifest`, esquema v1) | Manifiesto tipado y serializable |
| 1.3 | ~~⚙️ Firma Ed25519: generación de par de claves, firma de manifiesto canónico (bytes deterministas), verificación~~ ✅ (`Ed25519` NSec/libsodium, `ManifestSigner`, `ManifestVerifier`) | Firma en Shared |
| 1.4 | ~~Cálculo de `fingerprint` = SHA-256 de la clave pública (formato legible, p. ej. base32/hex agrupado)~~ ✅ (`Fingerprint`, parse/format round-trip) | Identidad de confianza |
| 1.5 | ~~Contrato de compatibilidad de versión de protocolo (versión de API de catálogo/descarga)~~ ✅ (`ProtocolVersion.Current = 1`, `IsCompatible`, `HelloResponse`) | Negociación servidor↔cliente sin auth |
| 1.6 | ~~Modelos de logs: `LogEntry` JSON con `clientId`, `operationId`, timestamps~~ ✅ (`LogEntry` + `LogSeverity`) | Correlación transversal |

**Dependencias:** ninguna. La Fase 1 es crítica: bloquea a Server y Cliente.

## Fase 2 — Servidor / organizador

| # | Tarea | Resultado |
|---|-------|-----------|
| 2.1 | ~~Persistencia SQLite del servidor: tablas de juegos, paquetes, operaciones, clientes vistos~~ ✅ (`SqliteCatalogStore`: schema + FK + lecturas para UI) | Esquema + acceso datos |
| 2.2 | ~~Casos de uso del catálogo: alta/edición de juegos, subida de paquete portable, generación de manifiesto + firma automática~~ ✅ (`CatalogService.PublishPackageAsync`: hash → import → manifiesto firmado → persistir) | Núcleo del organizador |
| 2.3 | ~~Servicio HTTP: `GET /api/v1/catalog`, `GET /api/v1/manifest/{packageId}`, `GET /api/v1/download/{packageId}`~~ ✅ (+ descarga por archivo `/download/{packageId}/{**path}`, solo archivos declarados en el manifiesto firmado) | API de catálogo y descarga |
| 2.4 | ~~Endpoint de compatibilidad `GET /api/v1/hello`~~ ✅ (versión de protocolo + fingerprint + nombre/id del servidor) | Handshake inicial |
| 2.5 | ~~⚙️ mDNS: anuncio `_lanforge._tcp` con puerto y versión de protocolo~~ ✅ (`MdnsAdvertiser`, TXT con name + fingerprint; constantes en `Shared.Protocol.LanService`) | Descubrimiento en LAN |
| 2.6 | ~~Logs JSON de operaciones (subida, publicación, descargas) correlacionados~~ ✅ (`JsonlServerLog` JSONL + operaciones Started→Completed en SQLite vía upsert) | Observabilidad |
| 2.7 | ~~UI Avalonia del organizador: gestión de catálogo/juegos, estado del servidor (IP/puerto, fingerprint), lista de clientes y operaciones en vivo~~ ✅ | App de escritorio |

## Fase 3 — Cliente / jugador ✅ (completada)

| # | Tarea | Resultado |
|---|-------|-----------|
| 3.1 | ~~Persistencia SQLite del cliente: catálogo sincronizado, paquetes instalados, operaciones~~ ✅ (`SqliteClientStore`: clientId, trust, catalog, installed, operations) | Estado local |
| 3.2 | ~~Descubrimiento: mDNS + configuración manual de IP/puerto~~ ✅ (`ServerDiscovery` Makaretu + entrada manual en UI) | Localizar servidor |
| 3.3 | ~~**Pinning de fingerprint fuera de banda**: confirmación TOFU; se persiste y se rechaza cualquier fingerprint distinto~~ ✅ (`TrustManager` + `TrustDialog`) | Confianza (TOFU) |
| 3.4 | ~~Handshake: verificar compatibilidad de versión de protocolo y fingerprint antes de operar~~ ✅ (`CatalogSyncService.SyncAsync`) | Seguridad básica |
| 3.5 | ~~Sincronización de catálogo (`GET /catalog`)~~ ✅ | Catálogo local actualizado |
| 3.6 | ~~Descarga HTTP con reanudación y barra de progreso~~ ✅ (`PackageDownloader`, Range por archivo, `DownloadProgress`) | Descarga robusta |
| 3.7 | ~~Validación **antes de instalar**: firma contra fingerprint fijado + `sha256` por archivo~~ ✅ (`PackageValidator.ValidateSignature` antes de descargar, `ValidateFilesAsync` después) | Integridad garantizada |
| 3.8 | ~~Instalación portable **confinada al directorio autorizado**~~ ✅ (`GameInstaller`, `{dataDir}/games/{gameId}/{version}`, confinamiento + limpieza de versiones antiguas) | Juegos portables |
| 3.9 | ~~Lanzador: ejecutable, argumentos, working directory~~ ✅ (`GameLauncher` + `IProcessStarter`) | Jugar con un clic |
| 3.10 | ~~Logs JSON del cliente correlacionados con el servidor~~ ✅ (`JsonlClientLog`, headers `X-LanForge-Client-Id`/`Operation-Id`) | Trazabilidad |
| 3.11 | ~~UI Avalonia del jugador: descubrir servidor, catálogo, instalar/actualizar/eliminar, jugar~~ ✅ | App de escritorio |

## Fase 4 — Tests ✅ (completada)

| # | Tarea | Resultado |
|---|-------|-----------|
| 4.1 | ~~Unitarios Shared: serialización determinista, firma/verificación, fingerprint, compatibilidad~~ ✅ (47 tests) | Base confiable |
| 4.2 | ~~Unitarios Server: casos de uso del catálogo, generación de manifiesto~~ ✅ (`CatalogServiceTests`, `SqliteCatalogStoreTests`) | Lógica servidor |
| 4.3 | ~~Unitarios Cliente: pinning, validación de firma/sha256, confinamiento de rutas (rechazar path traversal)~~ ✅ (trust, sync, validator, installer, downloader, launcher, store, sesión) | Lógica cliente |
| 4.4 | ~~Integración: servidor HTTP real + SQLite; cliente descarga y valida contra un paquete real~~ ✅ (`ServerApiTests`, `FullLoopbackTests`: servidor+cliente reales en loopback, incluido rechazo por fingerprint cambiado) | Flujo completo en loopback |
| 4.5 | ~~E2E: servidor y cliente en máquinas/procesos distintos de la LAN real~~ ✅ (`LanPartyE2ETests`, activado con `LANFORGE_E2E_SERVER=IP:puerto`; validación mDNS opcional con `LANFORGE_E2E_DISCOVER=1`) | Validación de la LAN party |

## Fase 5 — Empaquetado y pulido ✅ (completada)

| # | Tarea | Resultado |
|---|-------|-----------|
| 5.1 | ~~Publicación single-file / auto-contenida de Server y Cliente para Windows~~ ✅ (`build/publish.ps1`, win-x64 self-contained, verificado) | Distribución en LAN |
| 5.2 | ~~Manejo de errores UX-friendly (servidor caído, fingerprint cambiado, paquete corrupto)~~ ✅ (errores de negocio con mensajes en UI: `StatusText`, `TrustDialog`, excepciones tipadas) | Robustez |
| 5.3 | ~~Actualización de paquetes ya instalados (nueva versión → nueva carpeta, cleanup)~~ ✅ (`GameInstaller` conserva solo la versión actual) | Ciclo de vida de juegos |

---

## Grafo de dependencias (resumen)

```
0 (fundaciones) → 1 (Shared) ─┬→ 2 (Server) ─┐
                              └→ 3 (Cliente) ├→ 4.4/4.5 (integración/E2E) → 5 (pulido)
                              └→ 4.1 (tests Shared)
```

## Orden recomendado de ejecución

1. **Fase 0 + 1** primero: sin Shared no hay nada más.
2. En paralelo: **2.1–2.4** (servidor núcleo) y **3.1–3.5** (cliente núcleo) usando un servidor HTTP mockeado.
3. **2.5 / 3.2–3.4**: descubrimiento mDNS y confianza.
4. **2.7 / 3.6–3.11**: descarga, validación, instalación, UI.
5. **Fase 4** acompañando cada paso (tests junto al código, no al final).
6. **Fase 5** al cerrar el MVP.

## Fuera de scope (MVP)

- P2P entre clientes.
- Autenticación/autorización de clientes.
- Juegos que requieren instalador (solo portables).
- Soporte fuera de Windows.

## Fase 6 — Actualización 17/11/2026 (plan.md)

### Bugs
- [x] Alineación vertical: textos de nav lateral y chips centrados (`VerticalContentAlignment`).
- [x] Iconos monocromo estilo Kimi Design (Font Awesome) en lugar de emoji.

### Funcionalidades
- [x] 6.1 Transferencia por ZIP: el servidor empaqueta cada juego en un `.zip`; el cliente descarga el zip (reanudable), lo extrae en staging (con protección ZipSlip), valida firma+sha256 por archivo, instala y borra zip/staging.
- [x] 6.2 Imágenes de juegos alojadas en el servidor: subida en la app del organizador, servidas por HTTP (`GET /api/v1/covers/{gameId}`), referenciadas en el catálogo; el cliente las cachea con fallback a las portadas del design.
- [x] 6.3 Chat SignalR: hub en el servidor, canal `#general` permanente + salas efímeras por juego (se borran al quedar vacías, sin historial); la app del servidor lista los canales activos; UI del cliente basada en `screen.players` del design (canales + conectados + mensajes + input).
