# Spike S-5 · Reanudación de paquetes grandes — resultado

**Historia:** 12.3 — S-5 · Reanudación de paquetes grandes, validación del offset persistido
**Decisión que informa:** el mecanismo de reanudación de AD-11 (offset persistido); el spike
informa y no decide sobre el producto.
**Carácter:** **NO BLOQUEANTE** — su resultado es una decisión de fase posterior, no es
requisito previo de ninguna capacidad del MVP y el spike no puede cambiar un artefacto del
MVP (AD-21, R-MVP-5)
**Fecha:** 2026-11-12 (primera medición) · **2026-11-15 (5 interrupciones y escala, en Windows)**
**Resultado:** **POSITIVO CON UNA RESERVA** — la reanudación en cadena está medida y el hash
final coincide; la escala de **7+ GB** no es alcanzable porque **el servidor no puede
publicar paquetes de 2 GiB o más** (hallazgo **S1**, §4), que queda escalado para decisión.

---

## 1. Objetivo y alcance

Validar experimentalmente la reanudación de un paquete grande con reinicio del proceso del
cliente, para que el punto más frágil del MVP (AD-11: el offset persistido) tenga
**evidencia** y no suposición.

Alcance mínimo de la espina: una descarga de **7+ GB** interrumpida y reanudada **varias
veces**, con reinicio del proceso del cliente.

Como S-5 es **NO BLOQUEANTE**, su resultado **no cambia ningún artefacto del MVP** ni
bloquea su cierre: si el spike no se ejecuta o fracasa, el MVP sigue igual (AD-21,
R-MVP-5).

## 2. Configuración del experimento

| Elemento | Valor |
|---|---|
| Mecanismo | Transferencia HTTP con `Range`, offset persistido en SQLite (AD-11) |
| Entorno | Windows 11 25H2 (build 26200), .NET 10.0.403, 108,9 GB libres en `C:` |
| Red | Interfaz de bucle local del propio equipo: el servidor y el cliente hablan HTTP de verdad, pero **no** hay un segundo equipo (límite declarado en §5). El ancho de banda medido es de **disco local**, no de red: el resultado medido a 1 GiB no se puede extrapolar a una LAN |
| Disco | La medición de 1 GiB usa unos 2 GiB de `C:` (el paquete en el servidor y el parcial en el cliente) y, además, 1 GiB de RAM en el servidor, porque la publicación lo lee entero. El caso de 7 GiB necesita unos 7 GiB solo para escribir el fichero |
| Servidor | `TestServer` real, con el contenido servido por HTTP de verdad y por rangos |
| Cliente | arranque **real** del cliente (`ClientProgram.StartClientAsync`): aplica las migraciones, emite la identidad y **reconcilia el progreso persistido contra el parcial del disco** |
| Doble | ninguno en lo observado: el `Range` de la petición se observa en el cliente que la envía, el offset en el SQLite de verdad y el parcial en el disco (AD-16) |

## 3. Criterios y evidencia

### (a) La reanudación parte del offset **persistido**, no de cero — S-5a

**Verificado.** *Evidencia:* `tests/IntegrationTests/DownloadRestartRecoveryTests.cs` →
`A_download_that_was_interrupted_resumes_from_the_persisted_progress_after_a_restart`, más
`tests/IntegrationTests/ClientStartupTests.cs` →
`The_persisted_offset_is_corrected_when_the_partial_file_is_shorter` y
`The_persisted_offset_is_kept_when_the_partial_file_is_longer`, que ejercitan la
reconciliación con una divergencia real entre el registro y el disco.

La observación es de las dos puntas: la petición que llega al servidor empieza en el offset
persistido (`bytes={offset}-`, y nunca `bytes=0-`) y el **cliente** presenta la operación como
«Reanudando desde …» con ese mismo offset (UX-DR-45). ✔

### (b) El reinicio del proceso reconcilia el parcial y el registro **sin divergencia** — S-5b

**Verificado en su forma básica.** *Evidencia:* la primera prueba citada en (a) —el offset
persistido nunca es mayor que lo que el parcial contiene en disco, y el progreso efectivo es
la **intersección** de lo persistido y de lo que el parcial contiene (AD-11, AD-24), más
`Every_interrupted_download_resumes_from_the_persisted_progress`. La reconciliación con una
divergencia real entre el registro y el disco la cubren las pruebas de arranque del cliente
citadas en (a). ✔

### (c) Con N interrupciones, el hash final **coincide** — S-5c

**Verificado.** *Evidencia:* `tests/IntegrationTests/DownloadRestartRecoveryTests.cs` →
`Five_chained_interruptions_resume_from_each_persisted_offset_and_finish_with_the_same_hash`.

El escenario encadena **N=5 interrupciones**, cada una con reinicio real del cliente sobre la
misma carpeta de datos, y una última sesión que completa el paquete. Lo que se comprueba:

- cada sesión deja un offset persistido **mayor** que el de la sesión anterior (la cadena
  avanza);
- el identificador de la operación **no cambia** entre sesiones: la reanudación es la misma
  operación, no una intención nueva (AC1, AD-11);
- la evidencia es de la **petición** que el cliente envía: cada reanudación pide
  `bytes={offset persistido}-` y nunca `bytes=0-`, y el servidor contesta `206 Partial
  Content` con el `Content-Range` que declara el tramo servido (AC2, AD-20);
- el **hash final** del parcial es el del paquete original, byte a byte (S-5c, AD-24). ✔

**La pregunta que había quedado abierta en la medición anterior queda cerrada.** La pregunta
era —«en una sesión interrumpida la primera petición registrada fue `bytes=0-`»— y era un
artefacto del registro, que anotaba la petición **por descarga** y no **por sesión**. Con el
registro por sesión se ve que la petición `bytes=0-` es la **legítima** de la primera sesión,
la que inicia la descarga, y que todas las sesiones siguientes piden `bytes={offset}-`. No es
una divergencia entre el registro y el parcial: es el comienzo de la cadena.

### (d) La escala de 7+ GB

**MEDIDA A 1 GiB. Los 7+ GB NO SON ALCANZABLES con el producto actual**, y el motivo es un
hallazgo del propio producto (§4).

*Evidencia:* `tests/IntegrationTests/DownloadLargePackageResumeTests.cs` →
`A_large_package_survives_an_interruption_and_the_restart_keeps_its_hash`. La prueba no está
en la suite por defecto —varios gigabytes y varios minutos multiplicarían el coste de cada
ejecución (AD-16)—: se invoca de forma explícita con `LAN_PARTY_HUB_LARGE_PACKAGE_BYTES`, que
además fija el tamaño del paquete.

El escenario es el mismo que el de (c), pero a escala: la descarga se interrumpe cuando el
parcial ya supera los 256 MiB, el cliente se reinicia de verdad, la descarga se completa y el
hash del parcial se compara con el del paquete original.

**Resultado de la medición (2026-11-15, Windows):**

```
LAN_PARTY_HUB_LARGE_PACKAGE_BYTES = 1073741824   (1 GiB)
Correctas LANPartyHub.IntegrationTests.DownloadLargePackageResumeTests.
  A_large_package_survives_an_interruption_and_the_restart_keeps_its_hash [3 m 4 s]
```

El hash del parcial de **1 GiB** coincide con el del paquete original tras la interrupción y
el reinicio, y la reanudación pide `bytes={offset persistido}-`. La cadena de (c) se repite a
escala sin ninguna diferencia de comportamiento.

**Cómo ejecutar la medición:**

```powershell
$env:LAN_PARTY_HUB_LARGE_PACKAGE_BYTES = "1073741824"   # 1 GiB
dotnet test tests/IntegrationTests/LANPartyHub.IntegrationTests.csproj `
  --filter "FullyQualifiedName~DownloadLargePackageResumeTests"
```

## 4. Hallazgo: el servidor no puede publicar un paquete de 2 GiB o más

**Decisión requerida:** el arreglo (calcular el hash del paquete **por flujo** en la ruta de
publicación, que es lo que ya hace la ruta de servicio) **no cabe en esta historia**: S-5 es
**NO BLOQUEANTE** y su carácter normativo prohíbe que el spike cambie un artefacto del MVP
(AD-21, R-MVP-5). El hallazgo necesita **una historia propia** y queda escalado para decisión
(docs/plan.md §8). Esa historia debería traer además una prueba que fije el techo, porque
hoy **ninguna prueba de la suite por defecto** cubre el tamaño del paquete publicado.

**Severidad: S1 — Alta.** Un paquete de varios gigabytes **no se puede publicar** con el
producto actual, así que la condición de escala del spike es inalcanzable y el discurso del
producto sobre paquetes de varios GB no se sostiene en el servidor.

**Evidencia concreta:**

1. `tests/IntegrationTests/DownloadLargePackageResumeTests.cs` con
   `LAN_PARTY_HUB_LARGE_PACKAGE_BYTES=7516192768` (7 GiB) falla al **publicar** el paquete:
   el servidor responde `500 InternalServerError` en `POST /api/v1/packages`.
2. La causa está en `src/Server/Infrastructure/Persistence/PackageContentStore.cs`:
   `ReadBytesAsync` hace `File.ReadAllBytesAsync(path)`, es decir, **lee el paquete entero en
   memoria** para calcular su hash (`src/Server/Application/Packages/PublishPackage.cs`).
3. El techo no es la memoria de la máquina, sino la propia lectura: `File.ReadAllBytes` y
   `File.ReadAllBytesAsync` rechazan los ficheros de **2 GiB o más**, con el mensaje del
   runtime «El archivo es demasiado largo. Esta operación está actualmente limitada a
   archivos con un tamaño inferior a 2 gigabytes». Es decir: el fallo se repite en cualquier
   equipo, con la memoria que tenga.
4. El propio repositorio ya sabe que un paquete de varios gigabytes no cabe en memoria: la
   ruta que **sirve** el contenido (`OpenAsync`) lo abre como **flujo**, y su comentario dice
   que «un paquete de varios gigabytes no cabe en la memoria del servidor y la ruta HTTP con
   Range lo sirve por tramos». La ruta que **publica** no lo hace.

**Consecuencia para la historia:** el criterio de escala de 7+ GB **no se puede medir** con el
producto actual, de modo que 12.3 queda `in-progress` con esta reserva explicada, y el
hallazgo queda escalado para decisión (docs/plan.md §8).

## 5. Conclusión y límites declarados

1. **La reanudación funciona y está medida.** El offset persistido se respeta, el cliente
   vuelve a pedir desde el offset y nunca desde cero, la cadena de N=5 interrupciones
   conserva el identificador de la operación y el hash final coincide byte a byte con el
   original (AD-11, AD-24).
2. **La escala está medida a 1 GiB** con el mismo escenario, y el resultado es el mismo: el
   hash final coincide tras la interrupción y el reinicio. La medición de 1 GiB tardó
   **3 m 4 s**.
3. **Los 7+ GB no son alcanzables** con el producto actual, por el hallazgo de §4: la ruta de
   publicación lee el paquete entero en memoria. Es un **S1 abierto**, escalado para decisión.
4. **Límite del nivel de prueba.** La medición se ha hecho sobre la interfaz de bucle local
   del propio equipo, con el servidor y el cliente en la misma máquina. El nivel que el
   backlog exige para esta historia —**E2E en LAN real**, con dos equipos y el paquete
   completo— **no está cubierto**: queda para la demostración de extremo a extremo (13.1).
5. **Observaciones menores, sin acción en esta historia.** (i) El coordinador intenta cerrar
   una operación sin validador con la transición `Downloading → Completed`, que la tabla de
   estados **no permite** (`Downloading → Validating → Completed`): la transición se rechaza
   en silencio y la operación se queda en `Downloading`. En el cliente real el validador
   existe, así que el camino no se alcanza; el arnés de la prueba sí lo alcanza. (ii) El
   offset se persiste cada 16 KiB, así que un paquete de 1 GiB escribe unas 65 000 veces en el
   SQLite del cliente: es un dato de rendimiento que pertenece al spike 12.4.
6. **El MVP no cambia**: ningún artefacto del producto se ha tocado (AD-21).

**Refs:** AD-11, AD-16, AD-20, AD-21, AD-24, R-MVP-5, UX-DR-45.
