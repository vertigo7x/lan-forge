import { expect, test } from '@playwright/test';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';

/**
 * Caso e2e de prevencion del envio duplicado, en la frontera HTTP real.
 *
 * El formulario de la UI es XAML de Avalonia (no HTML): no hay DOM sobre el que
 * hacer `page.route()` ni selectores `data-testid`. Por eso el caso se reparte
 * entre las dos superficies:
 *
 *  - aqui (Playwright) se comprueba la frontera de red: cuantos POST llega al
 *    endpoint de creacion de paquetes y que responde el servidor.
 *  - en `tests/Unit/Server/PackageCreateDuplicateSubmissionE2ETests.cs` se
 *    comprueba la frontera de UI: el formulario real de Avalonia, con su boton
 *    real, al que se le pulsan varias veces, y el endpoint sustituido por un
 *    stub que cuenta las peticiones que llegan.
 *
 * Esta parte cubre el efecto de un envio duplicado cuando llega al servidor:
 * el servidor tiene que rechazar la segunda peticion del mismo juego y version
 * con 409, de modo que un duplicado no puede crear dos paquetes.
 */

test.describe('envio duplicado: el servidor no acepta dos veces el mismo paquete', () => {
  test('la segunda peticion del mismo juego y version se rechaza con 409', async ({ request }) => {
    // Directorio de origen real con un ejecutable: el servidor lo valida, lo
    // hashea, lo importa y lo publica de verdad.
    const sourceDir = fs.mkdtempSync(path.join(os.tmpdir(), 'lanforge-e2e-dup-'));
    fs.writeFileSync(path.join(sourceDir, 'run.exe'), 'contenido-e2e-duplicado');

    try {
      const gameId = `e2e-dup-${Date.now().toString(36)}`;
      const payload = {
        gameId,
        title: 'Juego Duplicado E2E',
        version: '1.0.0',
        sourceDirectory: sourceDir,
        entryPoint: 'run.exe',
      };

      const first = await request.post('/api/v1/packages', {
        headers: { 'Content-Type': 'application/json' },
        data: payload,
      });
      expect(first.status()).toBe(201);

      // La segunda peticion IDENTICA (el efecto de un doble click que llegase a
      // salir): el servidor la rechaza con 409 porque la version ya existe.
      const second = await request.post('/api/v1/packages', {
        headers: { 'Content-Type': 'application/json' },
        data: payload,
      });
      expect(second.status()).toBe(409);

      const body = await second.json();
      expect(body.code).toBeTruthy();
      expect(body.message).toBeTruthy();

      // Y el catalogo solo tiene UNA entrada para ese juego: un envio duplicado
      // no puede haber creado dos paquetes.
      const catalog = await request.get('/api/v1/catalog');
      expect(catalog.status()).toBe(200);
      const entries = await catalog.json();
      const matching = entries.filter((entry: { gameId: string }) => entry.gameId === gameId);
      expect(matching).toHaveLength(1);
    } finally {
      fs.rmSync(sourceDir, { recursive: true, force: true });
    }
  });

  test('dos peticiones identicas simultaneas no crean dos paquetes', async ({ request }) => {
    const sourceDir = fs.mkdtempSync(path.join(os.tmpdir(), 'lanforge-e2e-par-'));
    fs.writeFileSync(path.join(sourceDir, 'run.exe'), 'contenido-e2e-paralelo');

    try {
      const gameId = `e2e-par-${Date.now().toString(36)}`;
      const payload = {
        gameId,
        title: 'Juego Paralelo E2E',
        version: '1.0.0',
        sourceDirectory: sourceDir,
        entryPoint: 'run.exe',
      };

      // Las dos peticiones salen a la vez: es lo que haria un doble click si la
      // UI no lo impidiere.
      //
      // Lo que se comprueba aqui es la garantia que el servidor SI da: un envio
      // duplicado no puede acabar con DOS paquetes publicados para el mismo
      // juego. Cuantas peticiones se acepten y con que codigo de estado es
      // distinto en cada caso (ver el hallazgo de la carrera de importacion que
      // se documenta en el handoff de la tarea), asi que no se fija el codigo.
      const [a, b] = await Promise.all([
        request.post('/api/v1/packages', {
          headers: { 'Content-Type': 'application/json' },
          data: payload,
        }),
        request.post('/api/v1/packages', {
          headers: { 'Content-Type': 'application/json' },
          data: payload,
        }),
      ]);

      // Nunca las dos con 201: eso seria un duplicado aceptado.
      const created = [a.status(), b.status()].filter((status) => status === 201);
      expect(created.length).toBeLessThanOrEqual(1);

      const catalog = await request.get('/api/v1/catalog');
      const entries = await catalog.json();
      const matching = entries.filter((entry: { gameId: string }) => entry.gameId === gameId);
      expect(matching.length).toBeLessThanOrEqual(1);
    } finally {
      fs.rmSync(sourceDir, { recursive: true, force: true });
    }
  });

  test('una version distinta del mismo juego si se publica', async ({ request }) => {
    const sourceDir = fs.mkdtempSync(path.join(os.tmpdir(), 'lanforge-e2e-ver-'));
    fs.writeFileSync(path.join(sourceDir, 'run.exe'), 'contenido-e2e-version');

    try {
      const gameId = `e2e-ver-${Date.now().toString(36)}`;

      const first = await request.post('/api/v1/packages', {
        headers: { 'Content-Type': 'application/json' },
        data: {
          gameId,
          title: 'Juego Version E2E',
          version: '1.0.0',
          sourceDirectory: sourceDir,
          entryPoint: 'run.exe',
        },
      });
      expect(first.status()).toBe(201);

      // Version distinta: no es un duplicado, asi que se publica. La prevention
      // del envio duplicado no puede cerrar el formulario para siempre.
      const second = await request.post('/api/v1/packages', {
        headers: { 'Content-Type': 'application/json' },
        data: {
          gameId,
          title: 'Juego Version E2E',
          version: '1.0.1',
          sourceDirectory: sourceDir,
          entryPoint: 'run.exe',
        },
      });
      expect(second.status()).toBe(201);

      const catalog = await request.get('/api/v1/catalog');
      const entries = await catalog.json();
      const matching = entries.filter((entry: { gameId: string }) => entry.gameId === gameId);
      expect(matching.length).toBeGreaterThanOrEqual(1);
    } finally {
      fs.rmSync(sourceDir, { recursive: true, force: true });
    }
  });
});
