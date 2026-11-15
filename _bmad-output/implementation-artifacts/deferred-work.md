# Trabajo diferido

- source_spec: `_bmad-output/implementation-artifacts/spec-1-1-estructura-de-solucion-y-contratos-compartidos.md`
  summary: El DTO de `GET /api/v1/server/info` (`{ name, versions, anchorPublicKey, anchorFingerprint }`) no está en la enumeración exhaustiva de AD-18, de modo que `Shared` no tiene hoy el contrato de esa respuesta.
  evidence: La espina define el endpoint en la tabla de operaciones REST y AD-18 declara exhaustiva la lista de contratos de `Shared` sin incluirlo; es un hueco de la espina, no de esta historia. Decidirlo exige enmendar AD-18 (o declarar el DTO fuera de `Shared`), y la historia que lo consuma es la 1.2 o la 4.4.

- source_spec: `_bmad-output/implementation-artifacts/spec-build-lite-plan-schema.md`
  summary: Generalizar la obtención de rutas candidatas para historias posteriores a la 9.2.
  evidence: El planificador de la 9.2 recibe rutas precargadas, pero otras historias mantienen el listado vacío y pueden necesitar explorar su código para producir rutas concretas.
- source_spec: `_bmad-output/implementation-artifacts/spec-build-lite-plan-schema.md`
  summary: Verificar el comportamiento del plan rechazado mientras ejecutan otros workers en el scheduler paralelo.
  evidence: Un DriverError de plan_story podría escapar del scheduler antes de registrar un FAIL por historia; falta una prueba de ejecución paralela que confirme si afecta workers activos. No aplica a --story 9.2.
- source_spec: `_bmad-output/implementation-artifacts/spec-build-lite-plan-schema.md`
  summary: Admitir rutas de escritura bajo nuevos directorios con una validación segura.
  evidence: La validación actual exige que el directorio padre exista; nuevas carpetas de módulos requieren un mecanismo explícito para declararlas sin confundir roles con rutas.

- source_spec: `_bmad-output/implementation-artifacts/spec-9-2-proyeccion-de-instalaciones-y-de-operaciones-en-curso.md`
  summary: El cierre del cliente resuelve `DisposeAsync()` de forma síncrona (`GetAwaiter().GetResult()`) en el camino del hilo de la interfaz.
  evidence: El patrón es PREEXISTENTE en el cierre del cliente y no lo introduce la 9.2, así que queda fuera de esta historia; no se ha demostrado que la continuación capture el hilo de la interfaz. Para confirmarlo hay que observar el hilo que ejecuta la continuación de la disposición asíncrona durante el cierre y comprobar si bloquea ese hilo.

- source_spec: `_bmad-output/planning-artifacts/epics/epics-lan-party-hub-2026-10-26/epics.md`
  summary: Ningún camino del cliente publica `ClientErrorReported` al servidor, así que el registro de errores del organizador solo se llenaría desde las pruebas.
  evidence: La 9.3 deja listo el receptor (método del hub, caso de uso y persistencia con sus tres partes) y la vista que los muestra, pero el AC1 empieza por «Given un cliente que reporta un error» y hoy el contrato no tiene emisor: ni el coordinador de descargas ni el de instalaciones publican el error que catalogan. La 9.4 declara que el registro del cliente «presenta» los errores y no los produce, así que el hueco de la emisión queda sin asignar en el backlog y necesita una historia propia.
  resolution: La 9.4 lo cubre: el cliente registra el error en su SQLite y lo publica como `ClientErrorReported`, con el identificador derivado de forma determinista, así que el registro del organizador de la 9.3 se llena con los errores reales del equipo.

- source_spec: `docs/spike-s5-reanudacion-paquetes.md`
  summary: El servidor no puede publicar un paquete de más de ~2 GiB porque la ruta de publicación lee el paquete entero en memoria para calcular su hash, así que la condición de escala de 7+ GB del spike S-5 es inalcanzable con el producto actual.
  evidence: `src/Server/Infrastructure/Persistence/PackageContentStore.cs` → `ReadBytesAsync` hace `File.ReadAllBytesAsync`, que carga el paquete completo en un `byte[]`; con 7 GiB, `POST /api/v1/packages` responde `500 InternalServerError` (medido en `tests/IntegrationTests/DownloadLargePackageResumeTests.cs` con `LAN_PARTY_HUB_LARGE_PACKAGE_BYTES=7516192768`). El techo no es la memoria de la máquina: el runtime rechaza con «El archivo es demasiado largo. Esta operación está actualmente limitada a archivos con un tamaño inferior a 2 gigabytes» cualquier lectura de 2 GiB o más, así que falla en cualquier equipo. La ruta que SIRVE el contenido (`OpenAsync`) ya lo abre como flujo, con un comentario que dice que un paquete de varios gigabytes no cabe en memoria: la asimetría está en la publicación. Es un hallazgo S1 (docs/plan.md §8) y AD-21 impide arreglarlo dentro del spike 12.3, que es NO BLOQUEANTE y no puede cambiar artefactos del MVP; necesita una historia propia que calcule el hash por flujo en la publicación.
  resolution: pendiente de decisión humana: abrir una historia en la épica 12 (o en la que corresponda al servidor) que calcule el hash del paquete por flujo en la ruta de publicación.

- source_spec: `src/Client/Application/Downloads/DownloadCoordinator.cs`
  summary: El coordinador cierra una operación sin caso de uso de validación con la transición `Downloading → Completed`, que la tabla de estados no permite, así que la transición se rechaza en silencio y la operación se queda en `Downloading` aunque la descarga haya terminado.
  evidence: `DownloadCoordinator.ValidateTransferredBytesAsync` hace `handle.StateMachine.TryTransitionTo(DownloadState.Completed)` cuando `_validation` es `null`, y la tabla de `DownloadStateMachine` solo admite `Downloading → Validating → Completed`. Con el cliente real el validador existe (`Program.cs` entrega el ancla), así que el camino no se alcanza en producción; lo alcanzan las pruebas, que construyen la composición sin ancla. Observado en la 12.3 al esperar el fin de la descarga de 1 GiB. Severidad S2: no pierde datos ni rompe el flujo, pero deja la operación sin estado terminal.
  resolution: pendiente de decisión humana: decidir si la rama sin validador debe pasar por `Validating` o si la tabla debe admitir `Downloading → Completed`, y añadir la aserción del estado terminal en las pruebas de descarga.
