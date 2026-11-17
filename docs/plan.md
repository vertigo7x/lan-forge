LAN Forge es una plataforma de escritorio local para gestionar una LAN party en Windows.
Consta de dos aplicaciones .NET 10 + Avalonia:
- Servidor/organizador: administra el catálogo de juegos, paquetes y manifiestos firmados; publica los paquetes por HTTP, anuncia su presencia por mDNS y muestra clientes/operaciones.
- Cliente/jugador: descubre o configura manualmente el servidor, sincroniza el catálogo, fija fuera de banda el fingerprint de confianza, descarga paquetes por HTTP, valida firma e integridad, instala juegos portables y los lanza.
Principios clave:
- Todo funciona en una LAN, sin depender de Internet.
- Sin autenticación ni autorización de clientes; solo compatibilidad de versión.
- MVP sin P2P: un único camino de descarga HTTP servidor → cliente.
- Paquetes con manifiestos tipados y firmados; la firma y el sha256 se validan antes de instalar.
- Instalación portable confinada al directorio autorizado.
- Persistencia local con SQLite, logs JSON correlacionados por clientId y operationId.
- Arquitectura Clean/Hexagonal: Server, Client, Shared (solo contratos), con pruebas unitarias, integración y E2E.

Puedes seguir estas directrices de diseño:
安装 kimix CLI（https://cdn.kimi.com/agentgw/kimix/latest.json），遵循 kimi-design-usage skill，将该设计稿应用到当前仓库：https://www.kimi.ai/design/project/1a1df8a0-53b2-82a2-8000-0e8dd999bff5?canvasId=lanforge-game-catalog.kui

# Fase 6
## Nuevas funcionalidades 17/11/2026
- Modulo de chat de usuarios, mediante signalR. Los usuarios podrán crear salas para cada juego si quieren. Por defecto, habrá siempre un chat general, que siempre existirá. Desde la app del servidor se podrán ver los canales que se hayan creado. Los canales serán efimeros, si los usuarios salen del canal, este se borrará y no se guardará historial ni nada. El diseño estará basado en el de Kimi Designer.
- La gestión de los juegos de la app server, en lugar de transferir archivos y carpetas, se hará mediante un zip, que se descomprimirá al instalar en el cliente y se borrará al terminar. Tal como está ahora, las transferencias pueden fallar por uno o varios archivos y hacer que los juegos no funcionen.
- Las imágenes a mostrar en la app de cliente, serán las que se albergan en el servidor. Estas imágenes se podrán seleccionar y cargar en el servidor.
# Bugs 17/11/2026
- Los botones del menu lateral, tienen los textos alineados verticalmente arriba, en vez de centrados.
- Los botones de ordenación, tienen los textos alineados verticalmente arriba, en vez de centrados.
# Mejoras 17/11/2026
- Los iconos que se utilizan en el menú lateral no me gustan tanto como los usados en el diseño de Kimi Designer, que son de un solo color. Cambiar esos iconos por los de Kimi Designer.

# Fase 7
## Arreglos y mejoras 17/11/2026
# Bugs
- En la sección catálogo, las imagenes no se muestran en la aplicación cliente
- En la sección de chat, cuando se crea un canal, este muestra el identificador del juego, en lugar del nombre
- En la sección de chat, el área de chat, no muestra comportamiento responsive y el botón de enviar, queda un poco por fuera del área.
- En la sección de ajustes, hay una etiqueta para proporcionar el nombre de usuario, pero este no se puede escribir en ningún lado
- En la sección de ajustes, los botones, restablecer y Guardar cambios, necesitan un poco de padding para que las etiquetas no lleguen hasta los bordes.
# Mejoras
- En la sección catálogo, habría que añadir un botón para actualizar el catálogo del servidor
- En la sección catálogo, hay una etiqueta que pone "Ordenar por nombre", pero no es ni un botón ni tampoco hace ninguna acción
- En la aplicación del servidor, no hay una opción para borrar paquetes de juegos y comienza con su desarrollo
