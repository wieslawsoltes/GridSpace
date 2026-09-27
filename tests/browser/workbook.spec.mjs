import { test, expect } from '@playwright/test';
import fs from 'node:fs/promises';

const address = process.env.GRIDSPACE_URL || 'http://127.0.0.1:4173/GridSpace/';
const state = page => page.evaluate(() => globalThis.gridSpaceDiagnostics);
const errors = new WeakMap();

async function goToCell(page, cell) {
  // Physical input goes through Uno's actual controls; diagnostics are read-only.
  await page.mouse.click(48, 204);
  await page.keyboard.press('Control+A');
  await page.keyboard.type(cell);
  await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page))?.active).toBe(cell);
}
async function edit(page, text) {
  await page.keyboard.press('F2');
  await page.keyboard.press('Control+A');
  await page.keyboard.type(text);
  await page.keyboard.press('Enter');
}

test.beforeEach(async ({ page }) => {
  const failures = []; errors.set(page, failures);
  page.on('pageerror', error => failures.push(error.message));
  page.on('console', message => { if (message.type() === 'error') console.log('browser:', message.text()); });
  const url = new URL(address); url.searchParams.set('test', '1');
  await page.goto(url.href, { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(() => globalThis.gridSpaceDiagnostics?.ready && globalThis.gridSpaceDiagnostics.width > 500, { timeout: 60000 });
  await expect(page.locator('canvas').first()).toBeVisible();
});
test.afterEach(async ({ page }, info) => {
  await fs.mkdir('artifacts/screenshots', { recursive: true });
  const name = info.title.replace(/[^a-z0-9]+/gi, '-').toLowerCase();
  await page.screenshot({ path: `artifacts/screenshots/${name}.png`, fullPage: true });
  console.log('GridSpace state:', JSON.stringify(await state(page)));
  expect(errors.get(page)).toEqual([]);
});

test('renders the real Uno workbook shell', async ({ page }) => {
  const current = await state(page);
  expect(current.sheet).toBe('Revenue'); expect(current.sheets).toBe(3); expect(current.cells).toBeGreaterThan(100);
  expect(current.height).toBeGreaterThan(400);
  await goToCell(page, 'F6');
  await expect.poll(async () => (await state(page)).value).toBe('157500');
});

test('edits and recalculates formulas with undo and redo', async ({ page }) => {
  await goToCell(page, 'D6'); await edit(page, '200');
  await goToCell(page, 'F6'); await expect.poll(async () => (await state(page)).value).toBe('250000');
  await page.keyboard.press('Control+z');
  await goToCell(page, 'D6'); await expect.poll(async () => (await state(page)).value).toBe('126');
  await page.keyboard.press('Control+y');
  await goToCell(page, 'D6'); await expect.poll(async () => (await state(page)).value).toBe('200');
  await page.keyboard.press('Control+b'); await expect.poll(async () => (await state(page)).bold).toBe(true);
});

test('creates worksheets and downloads native workbook', async ({ page }) => {
  await page.mouse.click(1418, 857);
  await expect.poll(async () => (await state(page)).sheets).toBe(4);
  await goToCell(page, 'A1'); await edit(page, 'GridSpace browser acceptance');
  await goToCell(page, 'A1');
  const pending = page.waitForEvent('download'); await page.keyboard.press('Control+s'); const download = await pending;
  expect(download.suggestedFilename()).toMatch(/\.gridspace$/);
  const target = test.info().outputPath('saved.gridspace'); await download.saveAs(target);
  const json = await fs.readFile(target, 'utf8'); expect(json).toContain('GridSpace browser acceptance'); expect(() => JSON.parse(json)).not.toThrow();
});

test('scrolls to worksheet limits without allocating the full grid', async ({ page }) => {
  await goToCell(page, 'XFD1048576');
  const far = await state(page); expect(far.scrollX).toBeGreaterThan(1000000); expect(far.scrollY).toBeGreaterThan(20000000);
  await page.keyboard.press('Control+Home'); await expect.poll(async () => (await state(page)).active).toBe('A1');
  await page.mouse.move(500, 400); await page.mouse.wheel(0, 480);
  await expect.poll(async () => (await state(page)).scrollY).toBeGreaterThan(0);
});
