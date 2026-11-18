# Pruebas e2e del flujo de creación de paquetes

Pruebas end-to-end del caso de **éxito** del flujo de creación de paquetes: el
formulario real de Avalonia, con sus controles reales, envía el formulario contra
el endpoint real (`POST /api/v1/packages`) de un Kestrel de verdad.

| Fichero | Qué cubre |
|---|---|
| `tests/Unit/Server/PackageCreateSuccessE2ETests.cs` | Caso de éxito completo: rellenar los seis campos, pulsar el botón real, comprobar la confirmación en pantalla, comprobar la petición que llegó al endpoint y comprobar que el paquete quedó publicado. |
| `tests/Unit/TestInfrastructure/SynchronousHttpHandler.cs` | Infraestructura: observa la petición HTTP real y la deja llegar al servidor real. Es el equivalente de `page.route()` / `cy.intercept()` para una app sin navegador. |

## Por qué en `tests/Unit` y no en `e2e/` (Playwright)

La app de LAN Forge es de escritorio (.NET 10 + Avalonia). Su UI es XAML nativo:
no hay DOM, así que Playwright no tiene nada a lo que aplicarse (`page.click()`,
`page.getByRole()`). La convención del repo ya lo tiene resuelto:

| Superficie | Herramienta | Dónde vive |
|---|---|---|
| API HTTP del servidor real | Playwright | `e2e/` |
| **UI de Avalonia** (formulario, botones, errores por campo) | **xUnit + Avalonia headless** | `tests/Unit/Server/` |

Estas pruebas van en `tests/Unit/Server/` porque **pilotan la UI**, que es
justamente lo que Playwright no puede hacer. Lo que sí aportan es que, a
diferencia de `PackageCreateViewComponentTests` (que sustituye la capa de
servicio por un doble), aquí **no hay dobles**: la petición sale por la red y la
atiende el endpoint real.

## Qué comprueba el caso de éxito

1. **Rellenar los seis campos** (`gameId`, `title`, `version`, `sourceDirectory`,
   `entryPoint`, `arguments`) escribiendo en los `TextBox` reales, no en el view
   model: el binding bidireccional es el que propaga el valor, que es el camino
   que recorre el usuario al teclear.
2. **Pulsar el botón real** (`PublishButton`), que dispara el `SubmitCommand`.
3. **La confirmación esperada en pantalla**: el `TextBlock` del formulario muestra
   `Paquete creado: Neon Drift v1.2.3 (2 archivos).` y el formulario queda vacío
   para poder meter el siguiente juego.
4. **La petición al endpoint fue la correcta**: se capturó en la frontera de
   red — `POST /api/v1/packages`, cabecera `X-LanForge-Client-Id:
   organizer-ui` y un cuerpo JSON con los seis campos tal y como se
   escribieron.
5. **El paquete quedó publicado de verdad**: aparece en el catálogo del servidor
   y su manifiesto firmado se sirve con el ejecutable declarado.

### Selectores estables

No hay DOM, así que los equivalentes de `data-testid` son los `x:Name` del XAML:
`GameIdBox`, `TitleBox`, `VersionBox`, `SourceDirectoryBox`, `EntryPointBox`,
`ArgumentsBox` y `PublishButton`. El mensaje de estado se localiza por su
contenido enlazado (`ViewModel.StatusMessage`), no por posición en el árbol ni por
clases de estilo, que en XAML no existen.

### Esperas

No hay `Thread.Sleep` de duración fija para esperar al resultado.
`WaitForStatusMessage` espera **sobre el elemento**: sondea el texto de la
confirmación con un límite de tiempo, y devuelve en cuanto aparece.

## Las cuatro trampas del entorno headless (y cómo se esquivan)

Estas son las condiciones que hacen que esta prueba funcione y que no son
obvias. Están documentadas porque cualquier prueba de UI que await a algo real
vuelve a toparse con ellas.

### 1. El dispatcher headless no bombea el trabajo posteado desde otros hilos

En Avalonia headless, una continuación publicada con
`Dispatcher.UIThread.Post` **no llega a ejecutarse nunca**. Comprobado: ni
`Dispatcher.UIThread.RunJobs()`, ni `DispatcherFrame` + `PushFrame`, ni bombear en
bucle la ejecutan. Por eso una petición HTTP real awaited desde el hilo de UI se
quedaría colgada para siempre: su continuación se postea al dispatcher.

`SynchronousHttpHandler` lo resuelve sin tocar el código de producción: ejecuta la
petición **de verdad** en un hilo de fondo y entrega la respuesta **ya resuelta y
con el cuerpo en memoria** al hilo que llama. Todo el `await` del view model
continúa de forma síncrona en el hilo de interfaz, el binding se actualiza ahí
mismo y la UI muestra el resultado.

> Consecuencia: la petición HTTP sí sale por la red y la responde el endpoint
> real. Lo único que cambia es el momento en que se entrega la respuesta.

### 2. La sesión headless sustituye el `SynchronizationContext` del hilo del test

Al arrancar la sesión de Avalonia, el hilo del test pasa de
`Xunit.Sdk.AsyncTestSyncContext` a `Avalonia.Threading.AvaloniaSynchronizationContext`.
Con ese contexto, **cualquier `await` desde el hilo del test se queda colgado**:
su continuación se postea al dispatcher, que no la ejecuta. Esto afecta incluso a
`await Task.Delay(50)`.

Por eso **la prueba no hace `await`**: es síncrona, y las llamadas HTTP se lanzan
desde un hilo del pool (`Task.Run`) y se esperan de forma síncrona. Igual para la
parada del host: `StopAsync` se lanza desde el pool, porque si se llama desde el
hilo del test su continuación se postea al dispatcher y la parada no termina
nunca.

> **No** se debe "arreglar" esto restaurando el `SynchronizationContext` original
> del test: eso cambia el `Dispatcher` que resuelve el hilo de pruebas y rompe las
> pruebas de la UI del cliente (`ClientUiTests`), que construyen su ventana en el
> hilo del test.

### 3. No anidar despachos dentro de un despacho

`HeadlessAvalonia.RunOnUiThread` / `RunOnUiThreadAndWait` encolan la acción en el
hilo de interfaz. Anidar un despacho dentro de otra acción de UI **se bloquea**:
la acción interna queda encolada detrás de la que ya se está ejecutando. Por eso
`FillForm` toca los seis `TextBox` dentro de una sola acción, y los localizadores
de controles (`TextBox`, `PublishButton`) no despachan por su cuenta: asumen que
ya están en el hilo de UI.

Además, `RunUiJobs()` y `DrainUiQueue()` solo se pueden llamar **desde el hilo de
interfaz**, así que van dentro de una acción de UI.

### 4. La colección de xUnit decide qué hilo es el de la interfaz

La aplicación de Avalonia es un singleton por proceso: hay **dos** colecciones con
fixtures que arrancan la sesión headless (`Cliente-UI` y `Servidor-UI`). Si las
dos arrancan a la vez, gana la que llegue primero y la otra acaba construyendo
sus ventanas en el hilo equivocado ("The calling thread cannot access this
object because a different thread owns it").

Por eso esta prueba está en la colección **`Cliente-UI`**, que es la que declara
las dos fixtures (la del cliente y la del servidor) y la que usa el resto de
pruebas de UI del servidor (`PackageCreateViewComponentTests`). En la colección
`Servidor-UI` la prueba pasa, pero rompe las 8 pruebas de `ClientUiTests`.

## Ejecutar

```bash
dotnet test tests/Unit --configuration Release --filter "FullyQualifiedName~PackageCreateSuccessE2ETests"
```

## Nota sobre el puerto y las conexiones

La prueba arranca el servidor real con `HttpPort: 0`, para que el sistema elija un
puerto libre. Reservar un puerto y soltarlo antes de que Kestrel lo ocupe deja una
carrera: otra prueba en paralelo puede cogerlo entre medias (y el fallo sale como
`Failed to bind to address ... address already in use`, no como un problema de la
prueba). La dirección real se lee después del arranque, de
`IServerAddressesFeature`.

Las peticiones HTTP de la prueba se lanzan con `Connection: close`: si se dejan
abiertas (keep-alive), el servidor sigue con la conexión viva y al parar el host
se queda esperando a que expire el tiempo de inactividad.
