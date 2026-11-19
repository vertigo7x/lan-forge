# Rutas del manifiesto: por qué `ValidatePath` rechaza las absolutas

Este documento explica el contrato de `ManifestFileEntry.ValidatePath`, el
defecto que corrigió la tarjeta `t_7df14884` (hallazgo B3 de la auditoría
`t_60572d94`) y cómo se comprueba.

Contexto: `ValidatePath` es la función que todo el sistema da por segura
—«ruta relativa segura dentro del paquete»— y su resultado entra en los
bytes canónicos del manifiesto firmado. Un fallo aquí no es un detalle
cosmético: es un contrato que el servidor firma y el cliente instala.

## El contrato

`ValidatePath` acepta **solo rutas relativas**. Rechaza, con
`ArgumentException`:

| Familia | Ejemplos | Por qué |
|---|---|---|
| Raíz de Unix | `/tmp/game.exe`, `//tmp/game.exe` | Absoluta |
| UNC | `\\servidor\compartido\game.exe` | Absoluta |
| Unidad con letra | `C:\Juegos\game.exe` | Absoluta (además lleva `:`) |
| Salto de directorio | `../game.exe`, `bin/../game.exe` | Se sale del paquete |
| Vacía o en blanco | `""`, `"   "` | No es una ruta de archivo |
| Raíz sin nombre | `/` | Se normalizaba a la cadena vacía |

Y acepta, normalizando los separadores a `/`:

| Entrada | Salida |
|---|---|
| `game.exe` | `game.exe` |
| `bin/game.exe` | `bin/game.exe` |
| `bin\game.exe` | `bin/game.exe` |

## El defecto

El guardia de ruta absoluta se evaluaba sobre la cadena **ya normalizada**:

```csharp
var normalized = relativePath.Replace('\\', '/').TrimStart('/');
if (normalized.StartsWith('/') || normalized.Contains(':') || ...)
```

`TrimStart('/')` occurring **antes** del guardia hacía que `StartsWith('/')`
fuera **inútil**: la barra inicial ya se había quitado, así que nunca podía
encontrarse. El resultado medido:

```
ValidatePath("/tmp/game.exe") => "tmp/game.exe"   // sin lanzar
```

Una ruta absoluta Unix se colaba como si fuera la ruta relativa `tmp/game.exe`.

La consecuencia medida en la API: `CreatePackageValidator.Validate` (servidor)
aceptaba `entryPoint = "/tmp/juegos/game.exe"` con **0 errores**, mientras el
formulario del organizador lo rechazaba. La asimetría estaba en `Shared`, no en
el formulario: el servidor firmaba un manifiesto con un punto de entrada
absoluto.

## El arreglo

La comprobación se hace sobre la **cadena original**, con un detector de ruta
absoluta por patrón:

```csharp
var normalized = relativePath.Replace('\\', '/').TrimStart('/');
if (LooksAbsolute(relativePath) || normalized.Contains(':') || ...)
```

`LooksAbsolute` reconoce raíz de Unix, UNC y unidad con letra. Se hace **por
patrón** y no con `Path.IsPathRooted` para que el resultado no dependa del
sistema de ficheros del host: el servidor es Windows, pero el contrato de
«ruta relativa del paquete» no debe depender de dónde corra el proceso. Es el
mismo criterio que ya usa `CreatePackageValidator.LooksAbsolute` para el
directorio origen.

**La normalización no cambia.** Una ruta relativa produce los mismos bytes
canónicos que antes, así que **los manifiestos ya firmados siguen
verificando** en un cliente nuevo: hay una prueba que lo fija
(`Un_manifiesto_ya_firmado_sigue_verificando`).

## Lo que se comprobó

| Capa | Dónde | Qué comprueba |
|---|---|---|
| Regla | `ManifestFileEntryValidatePathTests` | Raíz de Unix, UNC, barra duplicada, raíz `/`, espacios alrededor; las relativas legítimas; `..` y `:` siguen rechazados; la normalización no se rompe; un manifiesto ya firmado sigue verificando |
| Contrato servidor | `CreatePackageValidatorAbsolutePathTests` | Un `entryPoint` absoluto da `invalid_path`; el servidor no llega a firmar el manifiesto; la ruta relativa que produce el cliente la acepta el servidor; cliente y servidor coinciden en el número de errores |

### Prueba roja primero

Con el código original, **14 de las 25 pruebas nuevas fallan**, incluida la
nombrada en el enunciado:

```
ValidatePath_rechaza_una_ruta_absoluta_unix                        [FAIL]
ValidatePath_rechaza_una_ruta_absoluta_unix_con_subdirectorios     [FAIL]
ValidatePath_rechaza_una_ruta_absoluta_unix_con_barra_inicial_doblada [FAIL]
ValidatePath_rechaza_una_ruta_unc_absoluta                         [FAIL]
ValidatePath_rechaza_una_ruta_absoluta_con_espacios_alrededor      [FAIL]
ValidatePath_rechaza_una_raiz_absoluta_vacia                       [FAIL]
Validate_EntryPoint_absoluto_devuelve_invalid_path (x5)            [FAIL]
Validate_EntryPoint_absoluto_no_firma_un_manifiesto                [FAIL]
La_ruta_absoluta_del_cliente_la_rechaza_el_servidor                [FAIL]
Cliente_y_servidor_coinciden_en_el_numero_de_errores_...           [FAIL]
```

### Verificado por mutación

| Mutación | Resultado |
|---|---|
| Guardia sobre la cadena normalizada (el defecto original) | **14 pruebas fallan** — detectada |
| `TrimStart` movido después del guardia | 25 pasan — **las pruebas validan el contrato, no una implementación concreta** |
| `LooksAbsolute` sin `Trim` | **2 pruebas fallan** — detectada |

## Nota: el patrón no está replicado

Se buscó el mismo patrón (`TrimStart` antes del guardia) en todo `src/`: **no
aparece en ningún otro sitio**. El único `TrimStart` del código de producción
es el de `ManifestFileEntry.ValidatePath`.

El `PackageExtractor` del cliente (anti-ZipSlip) no lo usa: resuelve la ruta
con `Path.GetFullPath` y comprueba que quede dentro del directorio destino.
Es la técnica correcta y no le afecta este cambio; ya tiene su propia prueba
(`Extract_ZipSlipEntry_ThrowsAndDoesNotWriteOutside`).

## Lo que NO se puede probar automáticamente

El CI corre en `windows-latest`. El arreglo es lógica de cadena pura (sin
`Path`), así que se comporta igual en Windows y en Linux: la ruta absoluta
Unix se rechaza en los dos sistemas. Lo que no se comprueba aquí es la
interacción con el sistema de ficheros de Windows para rutas como
`\Juegos\game.exe` (raíz de unidad sin letra), que el detector trata como
UNC y por tanto rechaza. Es el mismo criterio que aplica el resto del código
del repositorio.
