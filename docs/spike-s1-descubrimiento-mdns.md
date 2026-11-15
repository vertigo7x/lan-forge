# Spike S-1 · Descubrimiento por mDNS — resultado

**Historia:** 12.2 — S-1 · Descubrimiento, validación de mDNS junto a la dirección manual
**Decisión que informa:** política de descubrimiento (R-03); el spike cierra la medición, no el riesgo.
**Carácter:** **NO BLOQUEANTE** — su resultado es una decisión de fase posterior y no es requisito previo de ninguna capacidad del MVP (AD-21, R-MVP-5).
**Fecha:** 2026-11-12 (primera medición) · **2026-11-15 (segunda red dentro de WSL2 sobre el host Windows — S-1c completado)**.
**Resultado del spike:** **S-1c COMPLETO**. Se registró el comportamiento en dos redes con políticas de *multicast* distintas. R-03 sigue **ABIERTO**; la medición acota un caso de ausencia de interfaz apta para *multicast*, no todos los modos en que una red puede filtrar ese tráfico.

---

## 1. Objetivo y alcance

Medir el comportamiento real de mDNS en redes con políticas distintas, para documentar R-03 antes de decidir si el descubrimiento puede ser una dependencia de una fase posterior.

El alcance mínimo del spike es un servidor que anuncia `_lanpartyhub._tcp` y un cliente que descubre o usa una dirección manual. El spike no cambia el producto: la vía manual sigue siendo la alternativa permanente definida por AD-3.

## 2. Configuración del experimento

El experimento se ejecutó en dos redes reales del mismo host Windows: una interfaz Wi-Fi nativa y una interfaz de una distribución WSL2. No se simuló el tráfico.

| Elemento | Red A | Red B |
|---|---|---|
| Red | Wi-Fi, `192.168.1.124/24` (red de prueba; no se verificó como la LAN del evento) | `eth0`, `172.18.22.87/20`, NAT de Hyper-V/WSL2 |
| Política | `supportsMulticast: True` | sin el flag `MULTICAST`; `/sys/class/net/eth0/flags` = `0x3` |
| Anfitrión | Windows 11 25H2, build 26200 (`Environment.OSVersion` informa `10.0.26200`), .NET 10.0.403 | Fedora 44 en WSL2 (`podman-machine-default`), .NET 10.0.404 |
| Mecanismo | mDNS sobre UDP, servicio `_lanpartyhub._tcp` | el mismo producto, sin modificaciones |

La red B es una red NAT real de Hyper-V que ya inicia sin `MULTICAST`. El estado se comprobó antes y después de `wsl --terminate podman-machine-default`. La medición se hizo dentro de ese espacio de nombres porque la interfaz Windows `vEthernet (WSL (Hyper-V firewall))` sí declara `supportsMulticast: True` y no representa la política de `eth0` dentro de WSL2.

## 3. Criterios y evidencia

### (a) Descubrimiento sin dirección — S-1a

En la red A, `MdnsDiscoveryTests.The_client_discovers_the_server_without_the_manual_address` pasó en 10 s: el servidor anunció `_lanpartyhub._tcp` y el cliente encontró `192.168.1.124:47891`. La prueba complementaria `The_discovery_of_a_service_that_is_not_published_returns_nothing` pasó en 3 s, por lo que el descubrimiento no inventa servidores.

En la red B, la misma suite falla como se esperaba al no existir ninguna interfaz apta para *multicast*:

```
System.InvalidOperationException: No multicast-capable network interfaces found.
S-1a RESULTADO: NO DESCUBIERTO
```

Esta medición cubre el caso de una interfaz sin `MULTICAST`. No cubre el caso distinto de una interfaz que declara *multicast* pero cuyo punto de acceso o router lo filtra; esa ruta puede agotar el tiempo y terminar como `Empty` y sigue sin medirse.

### (b) Dirección manual y degradación — S-1b

`ManualConnectionTests` pasó 5 de 5 en cada red. El uso de dirección manual no depende de mDNS, por lo que el transporte sigue disponible cuando el descubrimiento falla.

La sonda temporal ejecutada durante la medición confirmó que `ServerDiscoveryService` traduce la excepción del adaptador a `DiscoveryOutcome.Blocked`, sin propagarla al cliente. También confirmó que una conexión manual llega a `connected: True`; esa comprobación usa `127.0.0.1` y termina en `AnchorUnverified`, porque la sonda no fija el ancla out-of-band. Por tanto, demuestra el transporte y la degradación controlada, pero **no** una instalación o descarga completa ni una conexión manual a través de `eth0`.

### (c) Dos políticas de red registradas — S-1c

| Red | Política | S-1a | Resultado del caso de uso | S-1b |
|---|---|---|---|---|
| A · Wi-Fi `192.168.1.124/24` | con *multicast* | Descubierto | No concluyente: la sonda retiró su anuncio antes de esta lectura y obtuvo `Empty` | 5 de 5 pruebas verdes; la sonda solo ejerció loopback |
| B · WSL2 `eth0` `172.18.22.87/20` | sin `MULTICAST` | No descubierto: excepción explícita | `Blocked`, sin propagar el error | 5 de 5 pruebas verdes; la sonda solo ejerció loopback |

La evidencia se obtuvo con las suites de integración y una sonda de observación temporal, no versionada, bajo `.build-lite/tmp/S1Probe`. El informe conserva los comandos, las redes y los resultados observados; la sonda no forma parte del producto ni se presenta como un artefacto reproducible del repositorio.

## 4. Resultado de la ejecución

| Entorno | Comando o prueba | Resultado |
|---|---|---|
| Red A, Windows | `MdnsDiscoveryTests` | 2 de 2 verdes: descubrimiento real y ausencia de resultados inventados |
| Red A, Windows | `ManualConnectionTests` | 5 de 5 verdes |
| Red B, WSL2 | `MdnsDiscoveryTests` | Fallo esperado: no hay interfaz apta para *multicast* |
| Red B, WSL2 | `ServerDiscoveryTests` | Fallo esperado por la misma precondición de red |
| Red B, WSL2 | `ManualConnectionTests` | 5 de 5 verdes |
| Host Windows | `dotnet test LANPartyHub.sln` | 1337 unitarias y 369 de integración verdes; E2E: 3 verdes y 1 omitida (`Sm1AcceptanceScenarioTests...` requiere una LAN real con dos o más clientes y queda para 13.1) |

## 5. Conclusión y límites declarados

S-1c queda completado: se midieron una red con *multicast* y otra sin una interfaz apta para ello. En la segunda, el adaptador falla de forma explícita, el caso de uso devuelve `Blocked` y el cliente no propaga la excepción. El producto no se modificó.

R-03 sigue **ABIERTO** porque la organización no controla la política de la red del evento. La evidencia acota el caso de interfaz sin `MULTICAST`; no prueba aún el comportamiento de un AP o router que filtra *multicast* mientras la interfaz lo sigue declarando disponible. La decisión de depender del descubrimiento pertenece a una fase posterior (AD-21).

Las pruebas de descubrimiento requieren una interfaz con *multicast* y fallan en la red B por esa precondición. El repositorio ya contiene `NoMulticastReason` en `ServerDiscoveryTests`, pero el patrón para omitir estas pruebas según la red no se cambia en este spike: requiere una historia de seguimiento separada.

**Refs:** AD-3, AD-21, AD-28, R-03, R-MVP-5, UX-DR-30, UX-DR-42.
