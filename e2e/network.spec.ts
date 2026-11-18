import { expect, test } from '@playwright/test';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';

/**
 * Verificacion de llamadas de red contra el servidor real.
 *
 * En Playwright, `page.route()` intercepta el trafico del navegador. Como esta
 * suite habla con la API mediante `APIRequestContext` (peticiones HTTP reales,
 * sin DOM), la forma equivalente de interceptar y verificar llamadas de red es
 * observar las peticiones y respuestas tal cual las devuelve el servidor y
 * comprobar su codigo de estado, sus cabeceras y su cuerpo. Eso es exactamente
 * lo que se hace aqui: la red se verifica en la frontera real (HTTP), no con
 * dobles de prueba.
 */
test.describe('red: llamadas contra el servidor real', () => {
  test('GET /api/v1/hello devuelve 200 y JSON con la identidad del servidor', async ({ request }) => {
    const response = await request.get('/api/v1/hello');

    expect(response.status()).toBe(200);
    expect(response.headers()['content-type']).toContain('application/json');

    const hello = await response.json();
    expect(hello).toMatchObject({ protocolVersion: 1 });
    // El fingerprint es el SHA-256 de la clave publica Ed25519, en hex minuscula
    // agrupado en bloques de 8 caracteres (8 grupos).
    expect(hello.fingerprint).toMatch(/^[0-9a-f]{8}(-[0-9a-f]{8}){7}$/);
  });

  test('la cabecera X-LanForge-Client-Id se acepta y no rompe la peticion', async ({ request }) => {
    // El cliente real (HttpServerApi) envia esta cabecera; el servidor la usa
    // para registrar el cliente visto.
    const response = await request.get('/api/v1/hello', {
      headers: { 'X-LanForge-Client-Id': 'e2e-playwright-client' },
    });

    expect(response.status()).toBe(200);
  });

  test('la cabecera X-LanForge-Operation-Id del cliente se respeta en la respuesta', async ({ request }) => {
    const operationId = 'e2e0000000000000000000000000000a';
    const response = await request.get('/api/v1/catalog', {
      headers: { 'X-LanForge-Operation-Id': operationId },
    });

    expect(response.status()).toBe(200);
    // El servidor devuelve el identificador efectivo: si el cliente lo propone,
    // se usa el del cliente.
    expect(response.headers()['x-lanforge-operation-id']).toBe(operationId);
  });

  test('el servidor genera un identificador de operacion si el cliente no lo envia', async ({ request }) => {
    const response = await request.get('/api/v1/catalog');

    const operationId = response.headers()['x-lanforge-operation-id'];
    expect(operationId).toBeTruthy();
    expect(operationId).toMatch(/^[0-9a-f]{32}$/);
  });

  test('el servidor rechaza un cuerpo JSON invalido con 400 y codigo de error', async ({ request }) => {
    const response = await request.post('/api/v1/packages', {
      headers: { 'Content-Type': 'application/json' },
      data: '{esto-no-es-json',
    });

    expect(response.status()).toBe(400);

    const body = await response.json();
    expect(body.code).toBe('malformed_body');
    expect(body.message).toBeTruthy();
  });

  test('la validacion del servidor devuelve un error por campo incumplido', async ({ request }) => {
    const response = await request.post('/api/v1/packages', {
      headers: { 'Content-Type': 'application/json' },
      data: {
        gameId: 'slug-invalido!',
        title: '',
        version: 'no-es-una-version',
        sourceDirectory: 'ruta-relativa',
        entryPoint: '../salir.exe',
      },
    });

    expect(response.status()).toBe(400);

    const body = await response.json();
    expect(body.code).toBe('validation_failed');
    expect(Array.isArray(body.errors)).toBe(true);

    // Cada campo incumplido llega con su propio codigo: la UI del organizador
    // los pinta en el campo correspondiente del formulario.
    const byField = new Map<string, string>(
      body.errors.map((e: { field: string; code: string }) => [e.field, e.code]),
    );
    expect(byField.get('gameId')).toBe('invalid_format');
    expect(byField.get('title')).toBe('required');
    expect(byField.get('version')).toBe('invalid_format');
    expect(byField.get('sourceDirectory')).toBe('invalid_path');
    expect(byField.get('entryPoint')).toBe('invalid_path');
  });

  test('un manifiesto inexistente devuelve 404', async ({ request }) => {
    const response = await request.get('/api/v1/manifest/no-existe-e2e');

    expect(response.status()).toBe(404);
  });

  test('publicar un paquete lo hace aparecer en el catalogo y en su manifiesto', async ({ request }) => {
    // Directorio de origen real con un ejecutable: el servidor lo valida, lo
    // hashea, lo importa y lo publica de verdad.
    const sourceDir = fs.mkdtempSync(path.join(os.tmpdir(), 'lanforge-e2e-src-'));
    fs.writeFileSync(path.join(sourceDir, 'run.exe'), 'contenido-e2e');

    try {
      const gameId = `e2e-${Date.now().toString(36)}`;

      const publish = await request.post('/api/v1/packages', {
        headers: { 'Content-Type': 'application/json' },
        data: {
          gameId,
          title: 'Juego E2E',
          version: '1.0.0',
          sourceDirectory: sourceDir,
          entryPoint: 'run.exe',
        },
      });

      expect(publish.status()).toBe(201);

      const created = await publish.json();
      expect(created.gameId).toBe(gameId);
      expect(created.version).toBe('1.0.0');

      // El catalogo ya lo refleja.
      const catalog = await request.get('/api/v1/catalog');
      expect(catalog.status()).toBe(200);
      const entries = await catalog.json();
      expect(entries.some((e: { gameId: string }) => e.gameId === gameId)).toBe(true);

      // El manifiesto se sirve firmado.
      const manifest = await request.get(`/api/v1/manifest/${gameId}`);
      expect(manifest.status()).toBe(200);
      const manifestBody = await manifest.json();
      expect(manifestBody.gameId).toBe(gameId);

      // Y el paquete se puede descargar.
      const download = await request.get(`/api/v1/download/${gameId}`);
      expect(download.status()).toBe(200);
      expect(download.headers()['content-type']).toContain('application/zip');
      expect((await download.body()).length).toBeGreaterThan(0);
    } finally {
      fs.rmSync(sourceDir, { recursive: true, force: true });
    }
  });
});
