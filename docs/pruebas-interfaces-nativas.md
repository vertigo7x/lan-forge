# Limitaciones de las pruebas de interfaces nativas (servidor)

Este documento explica qué se puede y qué no se puede comprobar de forma
automática en los diálogos nativos del sistema operativo del formulario de
publicación de paquetes, y por qué la suite de pruebas no los abre.

Contexto: el campo «Carpeta origen» y el campo «Ejecutable» se rellenan con
los diálogos del sistema operativo (Avalonia `StorageProvider`). Las reglas de
validación de esas rutas viven en `PackageSourcePaths.Validate` y son puras: se
prueban sin abrir ninguna ventana.

## Lo que sí se prueba automáticamente

| Capa | Dónde | Qué comprueba |
|---|---|---|
| Regla de validación | `PackageSourcePathsValidationTests` | Carpeta existente / inexistente, ejecutable `.exe` en varias grafías, ejecutable inexistente, ejecutable con otra extensión, ejecutable fuera de la carpeta |
| Botón de publicar | `PackageSourcePathsValidationTests`, `PackageCreateViewComponentTests` | `CanPublish` deshabilitado + el motivo visible en la vista real de Avalonia |
| Formulario | `PackageCreateViewModelPickerTests`, `PackageCreateViewComponentTests` | Que un diálogo cancelado o con ruta inválida no guarde la ruta, que el botón se rehabilite con rutas válidas |
| Opciones de los diálogos | `PackageCreateViewExecutablePickerTests` | Filtro `*.exe` del diálogo de ejecutables, `AllowMultiple = false`, títulos de los dos diálogos |
| Cableado del selector nativo | `TopLevelPathPickerTests` | Vista sin ventana → `null`, diálogo sin elementos → `null`, sin excepciones en el hilo de UI, traducción del elemento a ruta local |
| Formato del payload | `PackageCreatePayloadFormatTests` | Cuerpo JSON real de la petición: carpeta absoluta, ejecutable relativo, nombres de campo del contrato |
| Flujo completo | `PackageCreateViewManualFlowTests` | La vista real publica por HTTP contra el endpoint real y el paquete se crea |

## Lo que NO se puede probar automáticamente

**Los diálogos del sistema operativo no se abren en las pruebas.** En CI
(Avalonia `Headless`) no hay sistema operativo detrás de la aplicación, así que
el proveedor de almacenamiento es `NoopStorageProvider`, que devuelve
colecciones vacías sin mostrar ninguna ventana.

Consecuencias medidas en este repositorio:

1. **El diálogo no se puede abrir.** No hay forma de comprobar desde un test
   que aparezca la ventana del explorador de archivos con el filtro `*.exe`, ni
   que el filtro deje elegir un `.dll`.
2. **Un diálogo cancelado y un diálogo que devuelve nada son indistinguibles.**
   Ambos producen `null`. La suite no puede verificar que "cancelar" y "elegir
   un archivo que el sistema operativo filtró" se comporten de forma distinta,
   porque en headless no hay segundo caso.
3. **La navegación real del explorador no se comprueba.** No se puede verificar
   que el diálogo muestre la carpeta actual, que respete la última carpeta
   usada, ni cómo se comporta con rutas de red o con un proveedor remoto.
4. **El diálogo de carpetas no lleva filtro**, pero no es por decisión del
   proyecto: `FolderPickerOpenOptions` de Avalonia no tiene propiedad
   `FileTypeFilter`. La prueba lo fija por reflexión para que un cambio de
   versión de Avalonia no lo pase por alto.

### Cómo se cubre igualmente

El diálogo nativo es una **comodidad de entrada**, no una decisión: la regla que
decide si las rutas sirven vive en `PackageSourcePaths.Validate` y la aplica el
view model después de recibir la ruta. Por eso:

- El filtro `*.exe` del diálogo es el mismo `PackageSourcePaths.ExecutablePattern`
  que usa la regla (`La_extension_se_comprueba_sin_distinguir_mayusculas` ata
  que no pueden divergir).
- Los diálogos se comprueban por sus **opciones** (`ExecutablePickerOptions()` y
  `SourceDirectoryPickerOptions()`), no abriéndolos.
- El **flujo** se comprueba con `IPathPicker` simulado: lo que llega al
  view model es una ruta, y a partir de ahí todo es código normal.

### Lo que queda para la prueba manual

Con el servidor en marcha sobre Windows, conviene comprobar a mano:

1. Que el diálogo del ejecutable muestra solo archivos `.exe` (y que el filtro
   no deja elegir un `.dll`).
2. Que el diálogo de carpetas deja elegir la carpeta del juego portable y que
   la ruta aparece tal cual en el campo, en formato absoluto.
3. Que cancelar cualquiera de los dos diálogos deja el campo como estaba y no
   habilita el botón de publicar.
4. Que al elegir un ejecutable en mayúsculas (`GAME.EXE`) se acepta igual.

## Nota sobre `NoopStorageProvider`

La prueba `Con_una_ventana_en_headless_el_dialogo_no_devuelve_ningun_elemento`
ata el comportamiento real del proveedor headless de Avalonia 12.1.6: con la
vista pegada a una ventana, el diálogo devuelve una colección vacía y el
selector la traduce a `null`. Si una versión futura del proveedor headless
empezara a devolver elementos, esa prueba avisaría y habría que revisar cómo se
traducen al formulario.

## Nota sobre `IStorageItem`

`IStorageItem` de Avalonia **no se puede implementar a mano** (el propio
interface declara que no es implementable por código de usuario). Por eso las
pruebas del selector no usan un doble de almacenamiento: piden el elemento al
proveedor real con `IStorageProvider.TryGetFileFromPathAsync`, que sí funciona
en headless y devuelve un `BclStorageFile` de verdad.

Eso destapó un detalle: para una URI que no es del sistema de ficheros, el
proveedor devuelve **null**, no un elemento con la ruta vacía. `LocalPath` lo
traduce a `null` sin lanzar una excepción (antes el `item.Path` sin comprobación
podía dejar caer el formulario al elegir), que es lo que el formulario
entiende como «no se ha elegido nada».
