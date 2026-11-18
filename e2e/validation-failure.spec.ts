import { expect, test } from '@playwright/test';

/**
 * Caso e2e de fallo de validacion del flujo de creacion de paquetes, en la
 * frontera HTTP.
 *
 * El formulario de la UI es XAML de Avalonia (no HTML): no hay DOM sobre el que
 * hacer `page.route()` ni selectores `data-testid`. Por eso el caso se reparte
 * entre las dos superficies:
 *
 *  - aqui (Playwright) se comprueba la frontera de red: el endpoint recibe la
 *    peticion, responde con error de validacion y devuelve UN error por campo,
 *    con la clave del campo del formulario (`gameId`, `title`, `version`, ...).
 *    Ese es el contrato que la UI consume para pintar cada mensaje junto a su
 *    campo.
 *  - en `tests/Unit/Server/PackageCreateValidationFailureE2ETests.cs` se
 *    comprueba la frontera de UI: el formulario real de Avalonia, con el
 *    endpoint sustituido por un stub que responde `validation_failed`, pinta
 *    CADA mensaje en el control de error de SU campo y no muestra confirmacion
 *    de exito ni redirige.
 */

interface FieldError {
  field: string;
  code: string;
  message: string;
}

interface ValidationErrorBody {
  code: string;
  message: string;
  errors: FieldError[];
}

test.describe('validacion: el endpoint rechaza los datos invalidos del formulario', () => {
  test('responde 400 validation_failed con un error por campo incumplido', async ({ request }) => {
    // Tres violaciones distintas en la misma peticion:
    //  - campo obligatorio vacio (title),
    //  - formato incorrecto (version y gameId),
    //  - ruta no segura / no absoluta (sourceDirectory, entryPoint).
    const response = await request.post('/api/v1/packages', {
      headers: { 'Content-Type': 'application/json' },
      data: {
        gameId: 'slug invalido!',
        title: '',
        version: 'no-es-una-version',
        sourceDirectory: 'carpeta/relativa',
        entryPoint: '../salir.exe',
      },
    });

    // (a) El endpoint fue llamado y respondio con error de validacion.
    expect(response.status()).toBe(400);
    expect(response.headers()['content-type']).toContain('application/json');

    const body = (await response.json()) as ValidationErrorBody;
    expect(body.code).toBe('validation_failed');
    expect(body.message).toBeTruthy();
    expect(Array.isArray(body.errors)).toBe(true);

    // (b) Cada campo incumplido llega con su propia clave y su codigo: la UI
    // los pinta en el campo correspondiente del formulario.
    const byField = new Map<string, FieldError>(
      body.errors.map((error) => [error.field, error]),
    );

    expect(byField.get('title')?.code).toBe('required');
    expect(byField.get('gameId')?.code).toBe('invalid_format');
    expect(byField.get('version')?.code).toBe('invalid_format');
    expect(byField.get('sourceDirectory')?.code).toBe('invalid_path');
    expect(byField.get('entryPoint')?.code).toBe('invalid_path');

    // Cada error trae un mensaje legible: es el texto que se muestra junto al
    // campo, no un codigo opaco.
    for (const error of body.errors) {
      expect(error.message).toBeTruthy();
      expect(error.message.length).toBeGreaterThan(0);
    }
  });

  test('un formulario con solo el titulo vacio devuelve un unico error en el campo title', async ({ request }) => {
    const response = await request.post('/api/v1/packages', {
      headers: { 'Content-Type': 'application/json' },
      data: {
        gameId: 'neon-drift',
        title: '',
        version: '1.0.0',
        sourceDirectory: '/games/neon',
        entryPoint: 'game.exe',
      },
    });

    expect(response.status()).toBe(400);

    const body = (await response.json()) as ValidationErrorBody;
    expect(body.code).toBe('validation_failed');

    const errors = body.errors;
    expect(errors).toHaveLength(1);
    expect(errors[0].field).toBe('title');
    expect(errors[0].code).toBe('required');
  });

  test('un valor fuera de rango se rechaza con el codigo too_long del campo', async ({ request }) => {
    // El titulo del servidor admite 200 caracteres; 201 lo rebasa.
    const response = await request.post('/api/v1/packages', {
      headers: { 'Content-Type': 'application/json' },
      data: {
        gameId: 'neon-drift',
        title: 't'.repeat(201),
        version: '1.0.0',
        sourceDirectory: '/games/neon',
        entryPoint: 'game.exe',
      },
    });

    expect(response.status()).toBe(400);

    const body = (await response.json()) as ValidationErrorBody;
    expect(body.code).toBe('validation_failed');

    const titleError = body.errors.find((error) => error.field === 'title');
    expect(titleError).toBeTruthy();
    expect(titleError?.code).toBe('too_long');
  });

  test('una peticion invalida no crea ningun paquete en el catalogo', async ({ request }) => {
    const response = await request.post('/api/v1/packages', {
      headers: { 'Content-Type': 'application/json' },
      data: {
        gameId: 'e2e-invalido-no-se-publica',
        title: '',
        version: 'version-invalida',
        sourceDirectory: 'carpeta/relativa',
        entryPoint: '../salir.exe',
      },
    });
    expect(response.status()).toBe(400);

    // El paquete invalido no aparece en el catalogo. No se compara el numero
    // total de entradas: la suite corre en paralelo y otras pruebas publican
    // paquetes a la vez.
    const after = await (await request.get('/api/v1/catalog')).json();
    expect(Array.isArray(after)).toBe(true);
    expect(
      after.some((entry: { gameId: string }) => entry.gameId === 'e2e-invalido-no-se-publica'),
    ).toBe(false);
  });
});
