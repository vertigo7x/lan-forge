# Cambios de calidad de LanForge.Server — rama `feat/mejoras-priorizadas-lanforge-server`

> Fecha: 18/11/2026 · Base: `main` (`8937260`) · Rama: `feat/mejoras-priorizadas-lanforge-server`
>
> Este documento recoge las correcciones incorporadas en la rama, el hallazgo de
> [`auditoria-servidor.md`](auditoria-servidor.md) que motiva cada una, el impacto y las
> incompatibilidades que requieren atención al integrar o desplegar.

## Resumen

La rama corrige los ocho hallazgos priorizados P0/P1 detectados en la auditoría del
organizador. La tabla siguiente es la trazabilidad completa:

| ID interno | Hallazgo de auditoría | Cambio | Impacto |
|---|---|---|---|
| A4 | **S1 + S2** — `FilePackageContentStore` y `FileCoverStore` permitsían leer o escribir fuera de su raíz | Se valida el identificador/nombre de fichero antes de resolver la ruta y se comprueba que la ruta final permanezca dentro de la raíz | Cierra la frontera de seguridad de los stores en disco y hace uniforme el comportamiento de todos sus métodos |
| A1 | **S3** — el chat autorizaba por nombre de usuario, no por conexión | `ChatHub.Send` comprueba la pertenencia con `ChatRoomRegistry.IsMember(connectionId, channel)` | Dos jugadores con el mismo nombre ya no pueden escribir desde fuera de un canal |
| A3 | **D1** — publicar un paquete borraba la descripción y la portada | `PublishPackageAsync` hereda `Description` y `CoverFile` de la fila existente y actualiza únicamente el título | Publicar o republicar ya no destruye metadatos ni hace desaparecer la portada del catálogo |
| A2 | **O2** — una descarga abortada se registraba como `Completed` | `DownloadCompletionReporter` usa `RequestAborted` para registrar `Failed`/`download.aborted` cuando el cliente corta la transferencia | La observabilidad del servidor coincide con la del cliente y distingue descargas completas de canceladas |
| M1 | **O1** — el `operationId` del log no era el de la operación ni el del cliente | El identificador se resuelve una sola vez por petición, se guarda en `HttpContext.Items` y se reutiliza en operación, log y respuesta | Una petición queda correlacionada con un único `operationId`, incluso cuando lo propone el cliente |
| M2 | **R1** — `ListCatalogAsync` hacía N+1 consultas | Se cargan todos los juegos una vez y se emparejan con los paquetes en memoria | El coste de `GET /api/v1/catalog` deja de crecer con una consulta extra por paquete |
| M3 | **R2** — la tabla de operaciones no tenía tope y la UI la leía completa cada 2 s | Índice por `started_utc DESC`, lectura con `LIMIT` y poda de operaciones con más de 7 días | La tabla deja de crecer indefinidamente y cada refresco de la UI tiene un coste acotado |
| M4 | Hallazgo derivado de **R2** y de la experiencia de uso: el hashing y la compresión se ejecutaban en el hilo de UI | Hashing y compresión se ejecutan fuera del hilo de UI, con un máximo de 4 hashes concurrentes; la publicación muestra progreso y deshabilita controles | La ventana del organizador deja de congelarse durante la publicación y se puede cancelar sin dejar artefactos parciales |

La numeración `A1`–`M4` es interna a esta entrega. La referencia estable es el
identificador del hallazgo (`S1`, `S2`, `S3`, `O1`, `O2`, `D1`, `R1`, `R2`) en
`docs/auditoria-servidor.md`.

## Detalle de los cambios

### A4 — Confinamiento uniforme de los stores de ficheros

- **Qué cambió**: `FilePackageContentStore` acepta solo identificadores de paquete planos
  (`[A-Za-z0-9_-]+`) y comprueba la ruta resuelta dentro de la raíz en importación, lectura
  de ZIP, lectura de entradas y borrado. `FileCoverStore.SaveAsync` valida el nombre de
  destino con el mismo patrón que usan lectura y borrado.
- **Por qué**: **S1 + S2**. La importación y lectura de paquetes no validaban la ruta,
  mientras que el borrado sí lo hacía; la escritura de portadas aceptaba nombres con
  separadores o que el propio servidor no podía releer.
- **Impacto**: todos los métodos de ambos stores aplican la misma frontera de seguridad.
  Un `packageId` o `gameId` inválido produce un error de argumento o un resultado vacío,
  nunca una lectura/escritura fuera de la raíz.

### A1 — Pertenencia al chat por `connectionId`

- **Qué cambió**: se añadió `ChatRoomRegistry.IsMember(connectionId, channel)` y
  `ChatHub.Send` deja de comparar la lista de nombres.
- **Por qué**: **S3**. Los clientes anónimos comparten el nombre `jugador`; una conexión
  que no había entrado en un canal podía escribir si otra tenía ese nombre.
- **Impacto**: solo puede escribir quien entró en ese canal con esa conexión. El nombre se
  conserva exclusivamente para mostrarlo.

### A3 — Publicar conserva descripción y portada

- **Qué cambió**: al resolver la fila de `Game`, `PublishPackageAsync` parte de la fila
  existente y solo cambia el título. `Description` y `CoverFile` se heredan.
- **Por qué**: **D1**. El upsert anterior creaba una fila de juego vacía y borraba ambos
  metadatos. El título, además, podía divergir del manifiesto firmado.
- **Impacto**: publicar o republicar un juego ya existente conserva sus metadatos y el
  catálogo mantiene el mismo título que el manifiesto. La edición de metadatos sigue
  correspondiendo a `AddOrUpdateGameAsync`.

### A2 — Una descarga abortada se registra como fallida

- **Qué cambió**: `DownloadCompletionReporter` observa `HttpContext.RequestAborted` al
  cerrar el stream. Una descarga cortada se persiste como `Failed` y genera
  `download.aborted` con severidad `Warning`; una completa conserva `Completed` y
  `download.completed`. El reporte es idempotente aunque el stream se cierre dos veces.
- **Por qué**: **O2**. El cierre del stream no distingue entre transferencia terminada y
  cliente desconectado; el servidor declaraba completadas ambas.
- **Impacto**: métricas y diagnóstico reflejan la transferencia real. La cabecera de
  respuesta no cambia; cambia el estado persistido y el evento de log.

### M1 — Un `operationId` único por petición

- **Qué cambió**: `GetOrCreateOperationId` cachea el id en `HttpContext.Items`. El mismo
  valor se usa al crear la operación, al escribir el log y al devolver la cabecera
  `X-LanForge-Operation-Id`.
- **Por qué**: **O1**. Cuando el cliente enviaba el header, la operación usaba el id del
  cliente pero el log generaba otro GUID y la respuesta no devolvía la cabecera.
- **Impacto**: la correlación `clientId`/`operationId` funciona también cuando el cliente
  impone el identificador. La respuesta ahora incluye siempre el id efectivo.

### M2 — Catálogo sin N+1

- **Qué cambió**: `ListCatalogAsync` llama una vez a `ListGamesAsync`, indexa los juegos
  por id y construye las entradas en memoria.
- **Por qué**: **R1**. Antes hacía una consulta a `games` por cada paquete, en el endpoint
  que todos los clientes consultan al sincronizar.
- **Impacto**: el número de consultas deja de crecer con el tamaño del catálogo. No cambia
  el formato de la respuesta.

### M3 — Operaciones indexadas, acotadas y podadas

- **Qué cambió**:
  - `ICatalogStore.ListOperationsAsync` exige un `limit` (200 por defecto).
  - `SqliteCatalogStore` añade el índice `ix_operations_started_utc` y devuelve primero
    las más recientes, con `LIMIT`.
  - Se añadió `ICatalogStore.PruneOperationsAsync`; `CatalogService` elimina operaciones
    con más de siete días, como máximo una vez por minuto.
  - La UI solicita 100 operaciones y ya no carga toda la tabla para descartar el resto.
- **Por qué**: **R2**. Cada sincronización y cada descarga inserta una fila; la tabla
  crecía durante toda la vida del servidor y la UI pagaba el coste completo cada 2 s.
- **Impacto**: el histórico visible conserva siete días y el refresco periódico tiene un
  coste acotado.

### M4 — Publicar sin congelar la UI

- **Qué cambió**: el hashing se ejecuta fuera del hilo de UI, con un máximo de cuatro
  archivos en paralelo; la compresión del ZIP se ejecuta también fuera de ese hilo y
  escribe primero en un `.tmp` que se elimina si falla o se cancela. Mientras publica, el
  botón y los selectores se deshabilitan y se muestra el estado del progreso.
- **Por qué**: el flujo afectado por **R2/M3** también degradaba la usabilidad: publicar un juego
  grande bloqueaba el hilo de UI durante el hashing y la compresión. Además, una
  cancelación podía dejar un ZIP temporal a medias.
- **Impacto**: la ventana del organizador sigue respondiendo, se evita publicar dos veces
  desde el mismo formulario y una publicación cancelada no deja un `.tmp` reutilizable.

## Comportamientos eliminados o alterados

1. **La lectura completa de operaciones se elimina en la UI** (**R2/M3**). Antes
   `RefreshAsync` cargaba toda la tabla y hacía `TakeLast(100)` en memoria. Ahora se pide
   directamente el máximo de 100 filas, ordenadas de más reciente a más antigua. Quien
   consuma `ICatalogStore.ListOperationsAsync` debe indicar explícitamente el límite que
   necesite.
2. **El histórico de operaciones deja de ser ilimitado** (**R2/M3**). La poda elimina
   automáticamente las operaciones iniciadas hace más de siete días. Si se necesita un
   histórico más largo, hay que cambiar `CatalogService.OperationRetention` o implementar
   una exportación antes de la poda.
3. **Publicar ya no borra descripción ni portada** (**D1/A3**). Antes se perdían al
   publicar un `gameId` cuya fila ya existía; ahora se conservan y el título se sincroniza.
4. **La pertenencia al chat deja de depender del nombre** (**S3/A1**). Dos conexiones con el
   mismo nombre de usuario son miembros distintos; una no puede heredar el acceso de la
   otra.
5. **Una descarga cancelada deja de figurar como completada** (**O2/A2**). El detalle
   distingue el motivo (`cancelada por el cliente`) y el log usa `download.aborted`.
6. **La respuesta HTTP incluye siempre `X-LanForge-Operation-Id`** (**O1/M1**), incluso si
   el valor procede del header del cliente. No es un cambio incompatible para clientes que
   ya leían esa cabecera, pero sí observable para clientes que asumían su ausencia.
7. **Los nombres de paquete y portada se validan antes de acceder al disco**
   (**S1/S2/A4**). Los identificadores que contengan separadores, `..`, rutas absolutas u
   otros caracteres no permitidos dejan de aceptarse; antes eran válidos al importar o
   guardar, aunque no weren releíbles o escapaban de la raíz.
8. **La publicación se ejecuta asíncronamente fuera del hilo de UI** (**M4**). El método
   conserva su contrato asíncrono, pero la ejecución real se desplaza a un trabajo en
   segundo plano. La interfaz deshabilita los controles durante la operación.

## Migración y despliegue

- **No hay migración de base de datos**: el índice de `operations` se crea al inicializar
  el esquema SQLite mediante `CREATE INDEX IF NOT EXISTS`. No se cambian tablas, claves
  ni formatos de las filas existentes.
- **No hay migración de datos**: la poda solo elimina operaciones de más de siete días
  cuando el servidor registra una operación nueva. Conviene comprobar el volumen de
  `operations` antes del primer despliegue si se conserva un histórico que deba revisarse.
- **Clientes HTTP**: el cambio del `operationId` es compatible con clientes que ya envían
  `X-LanForge-Operation-Id`; ahora reciben el mismo valor en la respuesta. Los clientes que
  ignoraban la cabecera siguen funcionando.
- **Implementaciones de `ICatalogStore`**: el puerto cambió. Cualquier implementación
  externa o fake debe adaptar `ListOperationsAsync(int limit, ...)` e implementar
  `PruneOperationsAsync(DateTimeOffset olderThan, ...)`.
- **Descargas cortadas**: las métricas que contaban `Completed` como descarga terminada
  deben tratar `Failed`/`download.aborted` como cancelación. Esto cambia datos históricos
  de operaciones, no el esquema.
- **Nombres de paquete y portada**: los datos ya almacenados no se renombran, pero un
  `packageId` o `gameId` inválido ya no podrá publicarse ni volver a leerse. Conviene
  auditar los ids existentes antes de desplegar.

## Verificación

Desde la raíz del repositorio, con el .NET SDK 10 indicado en `global.json`:

```bash
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
```

La suite completa de esta rama contiene **244 pruebas**: 229 unitarias, 14 de integración
y 1 E2E. Para ejecutar una capa concreta:

```bash
dotnet test tests/Unit --configuration Release
dotnet test tests/Integration --configuration Release
dotnet test tests/E2E --configuration Release
```

La prueba E2E necesita un servidor LAN Forge accesible; se activa con
`LANFORGE_E2E_SERVER=IP:puerto` y, opcionalmente, `LANFORGE_E2E_DISCOVER=1` para validar
también el descubrimiento mDNS. Sin esas variables, se omite.

Pruebas de regresión asociadas a cada cambio:

| Cambio | Cobertura principal |
|---|---|
| A4 | `tests/Unit/Server/FileStorePathTests.cs` |
| A1 | `tests/Integration/ChatHubTests.cs` |
| A3 | `tests/Unit/Server/CatalogServiceTests.cs` |
| A2 | `tests/Integration/DownloadCompletionTests.cs` y `tests/Unit/Server/DownloadCompletionReporterTests.cs` |
| M1 | `tests/Integration/OperationIdTests.cs` y `tests/Integration/DownloadCompletionTests.cs` |
| M2 | `tests/Unit/Server/CatalogServiceTests.cs` y `CountingCatalogStore` |
| M3 | `tests/Unit/Server/SqliteCatalogStoreTests.cs` y `CatalogServiceTests.cs` |
| M4 | `tests/Unit/Server/AsyncPublishTests.cs` y `tests/Unit/Server/ServerUiTests.cs` |
| U1 (M5 en la entrega) | `tests/Unit/Server/CatalogRowCacheTests.cs` |

La cobertura de la parte visible de **M4** (el botón de publicar deshabilitado mientras
dura la publicación) se comprueba en headless: `tests/Unit/Server/ServerUiTests.cs` monta
el host real del servidor sobre un directorio temporal y abre la ventana del organizador
en el hilo de UI de Avalonia. `tests/Unit/TestInfrastructure/HeadlessAvalonia.cs` comparte
ese hilo con las pruebas de la UI del cliente, porque la aplicación Avalonia es un
singleton por proceso.

> La entrega incluye además la corrección de la confirmación de borrado (**U1**): las
> filas `CatalogRow` se reutilizan entre refrescos mediante `CatalogRowCache`, por lo que
> la confirmación de borrado sobrevive al refresco automático de 2 segundos. Se documenta
> aquí por completitud, aunque su implementación y sus pruebas pertenecen al commit base
> `c4e0b32` de esta misma rama.

## Commits

| Commit | Contenido |
|---|---|
| `5573356` | A4 + A1: confinamiento de rutas y pertenencia al chat por conexión |
| `33be525` | A3: publicar conserva descripción y portada |
| `8d46639` | A2: una descarga cortada se registra como fallida |
| `93ae254` | M2: el catálogo lee los juegos una sola vez |
| `61b9984` | M3: operaciones con índice, límite y poda |
| `7fd1199` | M4: publicar un paquete no congela la ventana del organizador |
| `c4e0b32` | U1/M5: la confirmación de borrado sobrevive al refresco del listado |
| `64fae5f` | Documentación de los cambios y su trazabilidad con la auditoría |
| `97cfc84` | Cobertura de los huecos: poda de M3, `DownloadCompletionReporter`, UI del organizador en headless y `operationId` de la descarga |
