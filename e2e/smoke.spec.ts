import { expect, test } from '@playwright/test';

/**
 * Prueba de humo: la app (servidor) carga y responde.
 *
 * No hay DOM que assertar: la app de LAN Forge es de escritorio y su superficie
 * verificable es la API HTTP. "La app carga" se traduce en "el servidor real
 * arranca, responde en /api/v1/hello y su catalogo es coherente".
 */
test.describe('humo: la app carga', () => {
  test('el servidor real responde en /api/v1/hello', async ({ request }) => {
    const response = await request.get('/api/v1/hello');

    expect(response.status()).toBe(200);
    expect(response.ok()).toBe(true);

    const hello = await response.json();
    // Version de protocolo vigente.
    expect(hello.protocolVersion).toBe(1);
    // Identidad del servidor: sin esto no se puede validar la confianza TOFU.
    expect(hello.fingerprint).toBeTruthy();
    expect(typeof hello.fingerprint).toBe('string');
    expect(hello.serverName).toBeTruthy();
    expect(hello.serverId).toBeTruthy();
  });

  test('el catalogo responde con una coleccion valida', async ({ request }) => {
    const response = await request.get('/api/v1/catalog');

    expect(response.status()).toBe(200);

    const entries = await response.json();
    expect(Array.isArray(entries)).toBe(true);
  });

  test('la API responde con la cabecera de correlacion de operacion', async ({ request }) => {
    const response = await request.get('/api/v1/catalog');

    // El servidor devuelve siempre el identificador efectivo de la operacion,
    // venga del cliente o lo genere el servidor.
    const operationId = response.headers()['x-lanforge-operation-id'];
    expect(operationId).toBeTruthy();
    expect(operationId).toMatch(/^[0-9a-f]{32}$/);
  });

  test('un recurso inexistente responde 404 y no 500', async ({ request }) => {
    const response = await request.get('/api/v1/manifest/no-existe-e2e');

    expect(response.status()).toBe(404);
  });
});
