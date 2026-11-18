# Suite e2e (Playwright)

Suite end-to-end de LAN Forge. Levanta el **servidor real** (Kestrel, el mismo
host HTTP que usa la app de escritorio), espera a que responda y ejecuta las
pruegas contra su API real.

## Por qué Playwright y no Cypress

La app de LAN Forge es de escritorio (.NET 10 + Avalonia): su UI es XAML nativo
y **no existe superficie web** (no hay HTML, ni Razor, ni `wwwroot`). Un
framework de navegador tiene dos opciones, y solo una encaja:

| | ¿Necesita DOM? | ¿Sirve para esta app? |
|---|---|---|
| **Cypress** | Sí. Solo sabe hablar con la aplicación a través del DOM del navegador (`cy.visit`, `cy.get`). Para un sitio sin HTML no hay nada que visitar. | No encaja. |
| **Playwright** | No. `APIRequestContext` emite peticiones HTTP reales, sin DOM. Además `webServer` arranca el proceso del servidor y espera a que esté listo. | **Encaja.** |

Se eligió **Playwright** por dos razones concretas:

1. **`APIRequestContext`**: permite verificar la red en la frontera real
   (código de estado, cabeceras, cuerpo JSON) sin necesidad de un DOM.
2. **`webServer`**: arranca el proceso real del servidor y espera a que
   `/api/v1/hello` responda antes de ejecutar la primera prueba, y lo apaga al
   terminar. La suite depende de un servidor vivo: si no arranca, la suite falla.

## Qué cubre y qué no

- **Cubre**: la superficie HTTP del servidor (`/api/v1/*`), que es el contrato
  que consumen el cliente, la UI del organizador y la suite xUnit de
  integración. Incluye el flujo completo publicar → catálogo → manifiesto →
  descarga.
- **No cubre**: la ventana de Avalonia. Playwright no renderiza XAML. Para la UI
  nativa existen ya las pruebas headless de Avalonia en `tests/Unit`
  (`HeadlessAvalonia`), y la suite E2E en LAN real de `tests/E2E`.

## Requisitos

- .NET SDK 10 (`global.json` fija 10.0.403)
- Node.js 20 o superior

```bash
npm install                  # dependencias de la suite
npm run e2e:install-browsers # solo la primera vez (Chromium)
```

## Ejecutar

```bash
npm run e2e                 # la suite completa contra el servidor real
npm run e2e:ui              # modo interactivo
npm run e2e:headed          # con navegador visible
npm run e2e:report          # abre el informe HTML de la ultima ejecucion
```

El comando levanta el servidor real por su cuenta: no hay que arrancarlo a mano.
El arranque usa `dotnet run --project src/Server -- --headless`, es decir, el
mismo binario y el mismo host HTTP que la app de escritorio, pero **sin** la
ventana de Avalonia ni el anuncio mDNS (no hacen falta para probar la API y la
ventana require un display).

## Configuración

Todo se ajusta por variables de entorno:

| Variable | Por defecto | Para que sirve |
|---|---|---|
| `LANFORGE_E2E_HOST` | `127.0.0.1` | Direccion en la que se levanta el servidor de pruebas. |
| `LANFORGE_E2E_PORT` | `8711` | Puerto del servidor de pruebas. |
| `LANFORGE_E2E_DATA_DIR` | `/tmp/lanforge-e2e-data` | Directorio de datos. Aislado de los datos reales del usuario. |

Variables que lee el propio servidor (las usa el `webServer` de la config):

| Variable | Para que sirve |
|---|---|
| `LANFORGE_DATA_DIR` | Directorio de datos del servidor. |
| `LANFORGE_HTTP_PORT` | Puerto HTTP del servidor. |
| `LANFORGE_LISTEN_ADDRESS` | Direccion de escucha del servidor. |

Ejemplo: levantar la suite en otro puerto y directorio.

```bash
LANFORGE_E2E_PORT=8800 LANFORGE_E2E_DATA_DIR=/tmp/mi-datos npm run e2e
```

## Donde va cada tipo de prueba

Esta distinction es importante, porque las dos superficies se prueban con
herramientas distintas:

| Tipo de prueba | Herramienta | Donde vive |
|---|---|---|
| API HTTP del servidor real (contrato, red, flujo de datos) | **Playwright** | `e2e/` |
| UI de Avalonia (formulario, botones, doble click, errores por campo) | **xUnit + Avalonia headless** | `tests/Unit/Server/` |
| LAN real, cliente contra servidor real | **xUnit** | `tests/E2E/` |

**Playwright no puede tocar la UI de Avalonia.** No hay DOM: `page.click()` y
`page.getByRole()` no tienen a que aplicarse. Si hace falta rellenar un
formulario XAML, hacer doble click en un boton o comprobar que el boton se
deshabilita durante el envio, la prueba va en `tests/Unit` con la infraestructura
headless de Avalonia que ya existe en el repo:

- `[Collection("Servidor-UI")]` + `HeadlessServerFixture` para arrancar Avalonia
  en headless y montar el host web del servidor.
- `HeadlessAvalonia.RunOnUiThread(...)` para ejecutar codigo en el hilo de UI.
- `HeadlessAvalonia.RunUiJobs()` para vaciar la cola del dispatcher.

Ejemplo de referencia: `tests/Unit/Server/PackageCreateViewComponentTests.cs`
(rellena el formulario, envia y comprueba los errores por campo) y
`tests/Unit/Server/PackageCreateViewModelEndpointTests.cs` (la misma vista
contra el endpoint real, sin doblar la capa de servicio).

### Caso de fallo de validacion del formulario

El caso e2e de fallo de validacion esta repartido entre las dos superficies:

- `tests/Unit/Server/PackageCreateValidationFailureE2ETests.cs` — **frontera de
  UI**. Monta el formulario real de Avalonia con el endpoint sustituido por un
  stub HTTP que responde `400 validation_failed` con un error por campo
  (obligatorio vacio, formato incorrecto y ruta fuera de rango). Comprueba que
  la peticion sale, que **cada** mensaje aparece en el control de error de **su**
  campo (`TitleErrorText`, `VersionErrorText`, `SourceDirectoryErrorText`, …: el
  equivalente Avalonia de `data-testid`/`aria-describedby`) y que el usuario no
  ve confirmacion de exito ni es redirigido. Incluye el caso de datos invalidos
  que la validacion local corta antes de salir: los mensajes salen junto a su
  campo y el endpoint no llega a llamarse.
- `e2e/validation-failure.spec.ts` — **frontera de red** contra el servidor
  real: `POST /api/v1/packages` con datos invalidos responde `400` con
  `validation_failed` y un error por campo (`required`, `invalid_format`,
  `invalid_path`, `too_long`), y una peticion invalida no deja ningun paquete en
  el catalogo.

Lo que si aporta la suite de Playwright es la otra mitad del flujo: que el
servidor real acepte la peticion, valide los campos, publique el paquete y lo
sirva en el catalogo. Para "evitar el envio duplicado" hay que combinar ambas
piezas: en la prueba de UI se comprueba que el boton se deshabilita o que la
vista no lanza una segunda peticion, y en la de red se comprueba que el
servidor recibe una sola peticion por envio.

## Como se verifica la red

Como esta suite habla con la API mediante `APIRequestContext` (peticiones HTTP
reales, sin DOM), la verificacion de red se hace **en la frontera real**: se
observa la respuesta que devuelve el servidor y se comprueban su codigo de
estado, sus cabeceras y su cuerpo. No hay dobles de prueba en medio.

Lo que cubre `e2e/network.spec.ts`:

- El codigo de estado y el `content-type` de cada respuesta.
- Las cabeceras de correlacion: `X-LanForge-Client-Id` (la envia el cliente
  real) y `X-LanForge-Operation-Id` (si el cliente la propone, el servidor la
  respeta; si no, el servidor genera una).
- El cuerpo de error de la API: `malformed_body` para JSON invalido y
  `validation_failed` con **un error por campo** (`gameId`, `title`, `version`,
  `sourceDirectory`, `entryPoint`), que es lo que la UI del organizador pinta en
  cada campo del formulario.
- El flujo completo de red: publicar un paquete real y comprobar que aparece en
  el catalogo, que su manifiesto se sirve y que el zip se descarga.

En una app con UI web, el equivalente seria `page.route()` / `page.waitForRequest()`
para interceptar y verificar las llamadas. Aqui el equivalente natural es
`request.get()` / `request.post()` contra `baseURL` y asercionar sobre la
respuesta.

## Estructura

```
e2e/
  smoke.spec.ts              # prueba de humo: la app carga
  network.spec.ts            # verificacion de llamadas de red
  validation-failure.spec.ts  # caso e2e de fallo de validacion (frontera de red)
playwright.config.ts
package.json
tsconfig.json
```

## CI

La suite **no** entra en `.github/workflows/ci.yml` todavia, porque el runner
de CI es `windows-latest` y arranca el servidor con `dotnet run`, mientras que
la suite espera un servidor ya listening. Para integrarla:

1. Anadir un job con `setup-node`, `npm ci`, `npm run e2e:install-browsers` y
   `npx playwright test`.
2. El `webServer` de la config ya se encarga de levantar y apagar el servidor.

Mientras tanto, la suite corre en local con `npm run e2e`.
