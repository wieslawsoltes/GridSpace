import { test, expect } from '@playwright/test';
import fs from 'node:fs/promises';
import { workbenchHeaderCoverage } from './png-probe.mjs';

const root = process.env.GRIDSPACE_URL || 'http://127.0.0.1:4173/GridSpace/';
const state = page => page.evaluate(() => globalThis.gridSpaceDiagnostics);
async function click(page, id) {
  await expect.poll(async () => !!(await state(page))?.controls?.[id]).toBe(true);
  const r = (await state(page)).controls[id];
  await page.mouse.click(r.x + r.width / 2, r.y + r.height / 2);
}

test('resizes transposed categories over filter headers without opening a filter', async ({ page }) => {
  const errors = []; page.on('pageerror', e => errors.push(e.stack || e.message));
  const url = new URL(root); url.searchParams.set('test', '1');
  await page.goto(url.href, { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(() => globalThis.gridSpaceDiagnostics?.controls?.Namebox, null, { timeout: 60000 });
  await expect.poll(async () => workbenchHeaderCoverage(await page.screenshot()), { timeout: 60000 }).toBeGreaterThan(.7);
  await click(page, 'Namebox'); await page.keyboard.press('Control+A');
  await page.keyboard.type('B5:F17'); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).selection).toBe('B5:F17');
  await click(page, 'RibbonTabInsert'); await click(page, 'Command-chart-column');
  await expect.poll(async () => !!(await state(page)).selectedChart).toBe(true);
  await click(page, 'Command-chart-switch'); await click(page, 'Command-chart-customize');
  await expect.poll(async () => (await state(page)).chartSources?.find(s => s.part === 'Categories')?.range).toBe('C5:F5');
  const category = (await state(page)).chartSources.find(s => s.part === 'Categories');
  const grip = category.targets.find(t => t.handle === 'End');
  expect(grip).toBeTruthy();
  await page.mouse.move(grip.x, grip.y); await page.mouse.down();
  await expect.poll(async () => (await state(page)).chartSourceEditing).toBe(true);
  expect((await state(page)).overlayOpen).toBe(false);
  await page.mouse.move(grip.x - 130, grip.y + 35, { steps: 8 }); await page.mouse.up();
  await expect.poll(async () => (await state(page)).chartSources.find(s => s.part === 'Categories')?.range).toBe('C5:E5');
  expect((await state(page)).overlayOpen).toBe(false);
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await state(page)).chartSources.find(s => s.part === 'Categories')?.range).toBe('C5:F5');
  await fs.mkdir('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: 'artifacts/screenshots/chart-source-over-filter-header.png', fullPage: true });
  expect(errors).toEqual([]);
});
