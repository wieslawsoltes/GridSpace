import { defineConfig } from '@playwright/test';
export default defineConfig({
  testDir: './tests/browser', timeout: 90000, expect: { timeout: 15000 }, workers: 1, retries: 0,
  outputDir: 'artifacts/test-results',
  reporter: [['list'], ['html', { outputFolder: 'artifacts/playwright-report', open: 'never' }], ['junit', { outputFile: 'artifacts/browser-results.xml' }]],
  use: { browserName: 'chromium', headless: true, viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1, trace: 'retain-on-failure', screenshot: 'only-on-failure', acceptDownloads: true, launchOptions: { args: ['--disable-dev-shm-usage'] } }
});
