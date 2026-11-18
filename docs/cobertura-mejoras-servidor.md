# Matriz de cobertura — mejoras priorizadas LanForge.Server (rama feat/mejoras-priorizadas-lanforge-server)

Base: `fix/8-1-calidad-del-servidor` (A4, A1) + 4 commits (M2, M3, M4, M5) + A3, A2, M1 en base.

Suite base antes de esta tarea: 212 unit + 13 integracion + 1 E2E = **226 pruebas, 0 fallos**.
Suite despues: **229 unit + 14 integracion + 1 E2E = 244 pruebas, 0 fallos** (+18).

## Cobertura por mejora

| # | Mejora | Commit | Cobertura antes | Cobertura despues |
|---|--------|--------|-----------------|--------------------|
| A4 | Confinamiento uniforme stores ficheros | 5573356 | `FileStorePathTests.cs` | sin cambios (ya cubierta) |
| A1 | Pertenencia chat por connectionId | 5573356 | `ChatHubTests.UnMiembroPorNombre...` | sin cambios (ya cubierta) |
| A3 | Publicar conserva descripcion y portada | 33be525 | `CatalogServiceTests.Publish_ConservaDescripcionYPortada...` | sin cambios (ya cubierta) |
| A2 | Descarga abortada = Failed | 8d46639 | `DownloadCompletionTests` (3 casos) | + `DownloadCompletionReporterTests.cs` (7 casos): estado Completed/Failed, eventos de log, idempotencia del reporte, camino de error |
| M1 | operationId unico por peticion | a6bfd01 | `OperationIdTests.cs` | + `DownloadCompletionTests.Descarga_ElOperationIdEsElMismoEnLaCabeceraLaOperacionYElLog` |
| M2 | Catalogo sin N+1 | 93ae254 | `CatalogServiceTests.ListCatalog_LeeLosJuegosUnaSolaVez...` | sin cambios (ya cubierta) |
| M3 | Operations: indice, limite, poda | 61b9984 | `SqliteCatalogStoreTests` (4 casos) | + 6 casos: poda contra el SQLite real, throttle de 1 minuto, limite por defecto, limite cero, corte de poda |
| M4 | Publicar no congela la UI | 7fd1199 | `AsyncPublishTests` (7 casos) | + `ServerUiTests.cs` (boton deshabilitado durante la publicacion, en headless) + `AsyncPublishTests.Publish_HasheaTodosLosArchivos_EnParalelo` |
| M5 | Confirmacion de borrado sobrevive al refresco | c4e0b32 | `CatalogRowCacheTests` (6 casos) | sin cambios (ya cubierta) |

## Archivos anadidos

- `tests/Unit/Server/DownloadCompletionReporterTests.cs` — 7 casos (A2)
- `tests/Unit/Server/Fakes/RecordingServerLog.cs` — fake de `IServerLog` que guarda las lineas
- `tests/Unit/Server/ServerUiTests.cs` — 3 casos de la UI del organizador en headless (M4, M5)
- `tests/Unit/Server/HeadlessServerFixture.cs` — host real del servidor sobre un directorio temporal
- `tests/Unit/TestInfrastructure/HeadlessAvalonia.cs` — arranque headless compartido por cliente y servidor

## Nota sobre los fallos

Ninguna prueba falla. La suite completa pasa en verde antes y despues: no hay ningun fallo
que diagnosticar ni que justificar como comportamiento modificado.

## Detalle de la infraestructura de UI headless

`HeadlessUnitTestSession.Dispatch` **se traga las excepciones**: una asercion fallida
dentro de la accion despachada pasaria inadvertida (el test verde sin comprobar nada).
Por eso `HeadlessAvalonia.RunOnUiThread` captura la excepcion y la vuelve a lanzar en el
hilo del test, donde xUnit la ve. Sin esto, los tests de UI son vacios.

Ademas, las ventanas de Avalonia solo se pueden construir en el hilo de UI, y ese hilo es
unico por proceso: la UI del cliente y la del servidor comparten la sesion headless y
viven en la misma coleccion de xUnit ("Cliente-UI").

Verificado por mutacion: quitar `SetPublishing(true)` hace fallar el test de la UI, y
quitar el throttle de la poda hace fallar el test de M3.
