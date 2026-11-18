# Auditoría de LanForge.Server — hallazgos priorizados

> Fecha: 18/11/2026 · Rama: `fix/8-1-calidad-del-servidor` · Alcance: `src/Server` (app del organizador).
> Cada hallazgo se reprodujo contra el código real antes de priorizarse; la evidencia está en la columna "Evidencia".
> Los hallazgos **P0** y **P1** se corrigen en esta rama. Los **P2** quedan documentados y fuera de alcance.

## Criterio de priorización

- **Impacto**: ¿el defecto rompe funcionalidad, pierde datos, o degrada el servidor en uso real de LAN party?
- **Esfuerzo**: coste de corregir con prueba de regresión incluida.
- **Prioridad**: P0 (rompe algo o pierde datos) → P1 (degrada rendimiento u observabilidad) → P2 (higiene, sin efecto observable hoy).

---

## P0 — Corregir en esta rama

### S1. `FilePackageContentStore` permite leer y escribir fuera de su raíz

`GetArchivePath()` concatena `{packageId}.zip` sobre la raíz sin validar, y solo `DeleteArchiveAsync`
comprueba el confinamiento. `ImportArchiveAsync` y `OpenArchiveAsync` no lo hacen.

- **Evidencia**: `ImportArchiveAsync("../escaped", src)` escribe `escaped.zip` fuera de la raíz;
  `OpenArchiveAsync("../secreto")` devuelve el contenido de un fichero exterior.
- **Hoy no es explotable por HTTP** (`CatalogService` valida `GameId` y exige que el paquete exista en la BD),
  pero la clase es la frontera de seguridad que AGENTS.md declara ("confinamiento de rutas") y hoy tres de sus
  cuatro métodos se comportan de forma distinta. Un puerto (`IPackageContentStore`) no puede depender de que
  todos sus llamadores validen.
- **Corrección**: validar el `packageId` por patrón (sin separadores, sin `..`, sin ruta absoluta) en
  `ImportArchiveAsync`, `OpenArchiveAsync` y `OpenReadAsync`, con la misma comprobación de raíz que ya usa el borrado.
- **Prueba**: extender `tests/Unit/Server/FileStorePathTests.cs` con las formas de ruta de Windows y de Linux.

### S2. `FileCoverStore.SaveAsync` escribe fuera de la raíz y crea ficheros ilegibles

`SaveAsync` no valida el nombre: `File.Copy` escribe `{gameId}{ext}` tal cual, mientras que `OpenReadAsync`
y `DeleteAsync` sí exigen `FileNamePattern`. Guardar con un `gameId` con `../` escribe fuera; guardar con un
`gameId` con punto genera un fichero que el propio servidor no puede volver a leer.

- **Evidencia**: `SaveAsync("../portada-fuera", img)` escribe fuera de la raíz de portadas.
- **Corrección**: validar el nombre de destino con el mismo `FileNamePattern` que usan la lectura y el borrado.
- **Prueba**: `tests/Unit/Server/FileStorePathTests.cs`.

### S3. El chat autoriza por nombre de usuario, no por conexión

`ChatHub.Send` comprueba `_rooms.Members(channel).Contains(UserName())`, es decir, compara **nombres**.
El registro guarda la pertenencia por `connectionId`, y `UserName()` devuelve `"jugador"` a cualquiera que no
pase el parámetro `?user=`. Dos clientes anónimos comparten el nombre `"jugador"`, así que **cualquier cliente
puede escribir en un canal en el que no está**, siempre que otro miembro tenga ese nombre. En una sala de juego
(que los jugadores usan para coordinar) el mensaje entra desde fuera.

- **Evidencia**: con un miembro `"jugador"` en `general`, una conexión distinta que nunca se unió pasa la
  comprobación de pertenencia.
- **Corrección**: comprobar la pertenencia por `connectionId` (añadir `IsMember(connectionId, channel)` al registro),
  conservando el nombre solo para mostrarlo.
- **Prueba**: `tests/Integration/ChatHubTests.cs` (un cliente que no entra en la sala no puede escribir en ella).

### O1. El `operationId` del log no es el de la operación ni el del cliente

`LogAsync` lee el id de `context.Response.Headers`, pero `GetOrCreateOperationId` solo escribe esa cabecera
cuando **genera** el id. Si el cliente envía `X-LanForge-Operation-Id`, el log se queda con un GUID nuevo y la
respuesta no devuelve ninguna cabecera de operación. La operación en SQLite sí usa el id del cliente, así que
la fila y la línea de log que describen la misma petición llevan identificadores distintos.

- **Evidencia**: con `X-LanForge-Operation-Id: OP-MIO`, la línea de log `catalog.served` sale con otro id y la
  respuesta no trae `X-LanForge-Operation-Id`.
- **Impacto**: rompe la trazabilidad que AGENTS.md fija como principio ("logs JSON correlacionados por
  clientId/operationId") justo en el caso interesante, el que el cliente impone su id para correlacionar.
- **Corrección**: resolver el id una sola vez por petición (caché en `HttpContext.Items`), usarlo en la operación,
  en el log y devolverlo siempre en la respuesta.
- **Prueba**: `tests/Integration/ServerApiTests.cs`.

### O2. Una descarga abortada se registra como `Completed`

`NotifyStream` dispara su callback al disponerse, y el stream se cierra tanto si la transferencia terminó como
si el cliente se fue a mitad. El servidor afirma que la descarga se completó y lo escribe en el log
(`download.completed`). El cliente, en cambio, registra la misma descarga como `Failed` / `"cancelado"`: las dos
caras de la LAN party no coinciden.

- **Evidencia**: cortando la descarga a mitad, la operación queda en `Completed`, igual que una descarga íntegra.
- **Corrección**: registrar `Failed` cuando la petición se cancela (`RequestAborted`), distinguiendo el motivo
  en el detalle.
- **Prueba**: `tests/Integration/ServerApiTests.cs`.

---

## P1 — Corregir en esta rama

### D1. Publicar un paquete borra la descripción y la portada del juego

Al publicar por primera vez un `gameId` que ya existe en la BD (por ejemplo porque se le puso portada o
descripción), `PublishPackageAsync` hace upsert de un `Game` nuevo y vacío: `Description` y `CoverFile` pasan a
`null` y la portada desaparece del catálogo. Además, al republicar, el título del juego no se actualiza, así que
el catálogo y el manifiesto firmado pueden discrepar del título.

- **Evidencia**: con descripción y portada puestas, tras `PublishPackageAsync` ambos campos quedan a `null`.
- **Corrección**: al crear la fila del juego, heredar `Description` y `CoverFile` de la fila existente. La edición
  de metadatos del juego sigue siendo de `AddOrUpdateGameAsync`, que es su API.
- **Prueba**: `tests/Unit/Server/CatalogServiceTests.cs`.

### R1. `ListCatalogAsync` hace N+1 consultas

Por cada paquete publicado se hace una consulta extra a la tabla de juegos. Es el endpoint que **todos** los
clientes consultan en cada sincronización, así que el coste crece con el tamaño del catálogo y se multiplica por
el número de jugadores.

- **Evidencia**: 13 paquetes → 14 consultas a SQLite en un solo `GET /catalog`.
- **Corrección**: cargar los juegos una vez y emparejar en memoria.
- **Prueba**: `tests/Unit/Server/CatalogServiceTests.cs`.

### R2. La tabla de operaciones no tiene tope y la UI la lee entera cada 2 segundos

Cada sincronización de catálogo y cada descarga inserta una fila; la tabla crece sin límite durante la party.
`ListOperationsAsync` no tiene `LIMIT` ni índice, y la UI del organizador la carga **completa** en memoria cada
2 segundos para enseguida quedarse con las 100 últimas.

- **Evidencia**: 50 sincronizaciones → 52 filas, todas leídas y ordenadas en cada tick de la UI.
- **Corrección**: índice por `started_utc`, lectura acotada (la UI pide las 100 últimas) y poda de las filas más
  antiguas por encima de un tope generoso.
- **Prueba**: `tests/Unit/Server/SqliteCatalogStoreTests.cs`.

### U1. El refresco automático de la UI destruye la confirmación de borrado

`MainWindow.RefreshAsync` reconstruye `CatalogList.ItemsSource` con objetos `CatalogRow` nuevos cada 2 segundos.
El botón de borrar usa un texto de confirmación en la propia fila (`"Borrar"` → `"¿Seguro?"`), así que el estado
se pierde con el refresco: el usuario tiene que pulsar dos veces **en menos de dos segundos** para que el borrado
llegue a ejecutarse.

- **Corrección**: reutilizar las instancias de `CatalogRow` entre refrescos (actualizarlas en sitio) para que la
  confirmación sobreviva.
- **Prueba**: `tests/Unit/Server/CatalogRowTests.cs`.

---

## P2 — Documentados, fuera de alcance

| # | Hallazgo | Por qué no entra |
|---|---|---|
| H-A | `SqliteCatalogStore` comparte una sola `SqliteConnection` entre lecturas y escrituras; las lecturas no pasan por el cerrojo de escritura. | Con la versión fijada (`Microsoft.Data.Sqlite` 11.0.0) el estrés de 200 operaciones mixtas y 100 lecturas en paralelo con `ReplacePackageAsync` no produjo errores ni lecturas rotas. Es riesgo latente, no defecto reproducido. Corregirlo (conexión por operación) cambia el modelo de concurrencia: merece su propio análisis. |
| H-B | `JsonlServerLog` calcula el nombre del fichero al construirse: un servidor que cruza medianoche sigue escribiendo en el fichero del día anterior. | Afecta solo a la rotación del log; el formato JSONL sigue siendo válido. |
| H-C | `identity.json` corrupto (corte de luz al escribir) produce `ArgumentException: La clave privada debe tener 32 bytes`, sin indicar el fichero. | El mensaje es poco útil pero el fallo es ruidoso y el usuario puede resolverlo borrando el fichero. |
| H-D | `GetLocalIpAddress` devuelve la primera IPv4 del host, que en equipos con adaptadores virtuales (Hyper-V, VPN, Docker) puede no ser la IP de la LAN. | Afecta a la IP que se muestra al organizador; un criterio de selección mejor exige decidir qué es "la IP de la LAN" (heurística, tema de producto). |
| H-E | `ChatHub.OnDisconnectedAsync` no difunde la lista de miembros tras la desconexión (solo la de canales). | Cosmético: la lista de miembros se recalcula al siguiente `Join`/`Leave`. |
| H-F | `Program.Main` no detiene el host web al cerrar la ventana. | Cosmético para el usuario, pero afecta al cierre ordenado de la conexión SQLite. **Queda pendiente**: no se ha corregido en esta rama. |

---

## Trazabilidad

Cada corrección de esta rama lleva su prueba de regresión y su commit. El detalle de qué se cambió y por qué está
en `docs/tasks.md` (Fase 8) y en los mensajes de commit.
