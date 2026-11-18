import { defineConfig, devices } from '@playwright/test';

/**
 * Suite e2e de LAN Forge.
 *
 * La app de escritorio (.NET 10 + Avalonia) no tiene superficie web: su UI es XAML
 * nativo y no se puede renderizar en un navegador. Lo que si es verificable
 * end-to-end desde fuera es la superficie HTTP real del servidor (`/api/v1/*`),
 * que es el contrato que consumen el cliente, la UI del organizador y la suite
 * xUnit de integracion. Por eso la suite usa Playwright con `APIRequestContext`
 * (peticiones HTTP reales, sin DOM) contra el servidor Kestrel real arrancado
 * en modo `--headless`.
 *
 * El servidor se lanza con `webServer`: Playwright espera a que `/api/v1/hello`
 * responda antes de ejecutar los tests y lo apaga al terminar.
 */

const PORT = Number(process.env.LANFORGE_E2E_PORT ?? 8711);
const HOST = process.env.LANFORGE_E2E_HOST ?? '127.0.0.1';
const BASE_URL = `http://${HOST}:${PORT}`;

// Directorio de datos propio de la suite: no toca los datos reales del usuario.
const DATA_DIR = process.env.LANFORGE_E2E_DATA_DIR ?? '/tmp/lanforge-e2e-data';

export default defineConfig({
  testDir: './e2e',
  // La superficie HTTP es stateless: una serie es rapida y determinista.
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : [['list'], ['html', { open: 'never' }]],
  timeout: 30_000,
  expect: { timeout: 10_000 },

  use: {
    baseURL: BASE_URL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'off',
  },

  projects: [
    {
      name: 'api',
      testDir: './e2e',
      use: {
        ...devices['Desktop Chrome'],
        // Las pruebas de esta suite hablan HTTP con la API del servidor real.
        baseURL: BASE_URL,
      },
    },
  ],

  webServer: {
    command:
      'dotnet run --project src/Server --configuration Release -- --headless',
    url: `${BASE_URL}/api/v1/hello`,
    // El arranque incluye build y restauracion: el margen es amplio a proposito.
    timeout: 180_000,
    reuseExistingServer: !process.env.CI,
    stdout: 'pipe',
    stderr: 'pipe',
    env: {
      LANFORGE_DATA_DIR: DATA_DIR,
      LANFORGE_HTTP_PORT: String(PORT),
      LANFORGE_LISTEN_ADDRESS: HOST,
    },
  },
});
