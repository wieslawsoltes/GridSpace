import { test, expect } from '@playwright/test';
import fs from 'node:fs/promises';
import { workbenchHeaderCoverage } from './png-probe.mjs';

const root = process.env.GRIDSPACE_URL || 'http://127.0.0.1:4173/GridSpace/';
const state = page => page.evaluate(() => globalThis.gridSpaceDiagnostics);
const failures = new WeakMap();

async function click(page, id) {
  await expect.poll(async () => !!(await state(page))?.controls?.[id], { message: `Uno control ${id}` }).toBe(true);
  const rect = (await state(page)).controls[id];
  await page.mouse.click(rect.x + rect.width / 2, rect.y + rect.height / 2);
}
async function select(page, range) {
  await click(page, 'Namebox'); await page.keyboard.press('Control+A');
  await page.keyboard.type(range); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).selection).toBe(range);
}
async function snapshot(page, name) {
  await fs.mkdir('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: `artifacts/screenshots/${name}.png`, fullPage: true });
}
async function insertChart(page) {
  await select(page, 'B5:F17');
  await click(page, 'RibbonTabInsert');
  await click(page, 'Command-chart-column');
  await expect.poll(async () => !!(await state(page)).selectedChart).toBe(true);
  await expect.poll(async () => (await state(page)).width).toBeLessThan(1440);
  const id = (await state(page)).selectedChart;
  await expect.poll(async () => (await state(page)).charts.find(c => c.id === id)?.panes.length).toBeGreaterThan(0);
  return id;
}

test.beforeEach(async ({ page }) => {
  const errors = []; failures.set(page, errors);
  page.on('pageerror', error => errors.push(error.message));
  const url = new URL(root); url.searchParams.set('test', '1');
  await page.goto(url.href, { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(() => globalThis.gridSpaceDiagnostics?.controls?.Namebox, null, { timeout: 60000 });
  await expect.poll(async () => workbenchHeaderCoverage(await page.screenshot()), { timeout: 60000 }).toBeGreaterThan(.7);
});
test.afterEach(async ({ page }, info) => {
  await snapshot(page, info.title.replace(/[^a-z0-9]+/gi, '-').toLowerCase());
  expect(failures.get(page)).toEqual([]);
});


test('reconfigures PivotTable from its cached epoch until explicit Refresh', async ({ page }) => {
  await click(page, 'RibbonTabHelp'); await click(page, 'Command-analytics-sample');
  await expect.poll(async () => (await state(page)).sheet).toBe('Pivot analysis');
  const initial = (await state(page)).pivots[0];
  await select(page, initial.output.split(':').at(-1));
  const total = Number((await state(page)).value);
  expect(total).toBeGreaterThan(0);

  await click(page, 'Sheet-0');
  await expect.poll(async () => (await state(page)).sheet).toBe('Sales');
  await select(page, 'D2');
  const original = Number((await state(page)).value);
  await page.keyboard.press('F2'); await page.keyboard.press('Control+A');
  await page.keyboard.type(String(original + 1000)); await page.keyboard.press('Enter');
  await select(page, 'D2');
  await expect.poll(async () => Number((await state(page)).value)).toBe(original + 1000);

  await click(page, 'Sheet-1');
  await expect.poll(async () => (await state(page)).sheet).toBe('Pivot analysis');
  await select(page, 'C6');
  await click(page, 'Pivotfield1'); // Add Product as a second row dimension.
  await expect.poll(async () => (await state(page)).pivots[0].rows).toBe(2);
  await select(page, (await state(page)).pivots[0].output.split(':').at(-1));
  expect(Number((await state(page)).value)).toBe(total);
  await click(page, 'PivotRefresh');
  await expect.poll(async () => Number((await state(page)).value)).toBe(total + 1000);

  // Undo/redo restore complete output plus the immutable source-cache epoch.
  await click(page, 'Quick-undo');
  await expect.poll(async () => Number((await state(page)).value)).toBe(total);
  await click(page, 'Quick-redo');
  await expect.poll(async () => Number((await state(page)).value)).toBe(total + 1000);
});

test('moves PivotTable fields between areas using actual pointer drag and drop', async ({ page }) => {
  await click(page, 'RibbonTabHelp'); await click(page, 'Command-analytics-sample');
  await expect.poll(async () => (await state(page)).sheet).toBe('Pivot analysis');
  await select(page, 'C6');
  await expect.poll(async () => !!(await state(page)).controls.PivotAreaRows).toBe(true);
  const controls = (await state(page)).controls;
  const source = controls.Pivotfield1, target = controls.PivotAreaRows;
  const x = source.x + source.width / 2, y = source.y + source.height / 2;
  const tx = target.x + target.width / 2, ty = target.y + 15;
  await page.mouse.move(x, y); await page.mouse.down();
  await page.mouse.move(x + 12, y + 12, { steps: 4 });
  await page.mouse.move(tx, ty, { steps: 12 });
  await page.mouse.up();
  await expect.poll(async () => (await state(page)).pivots[0].rows).toBe(2);
  await click(page, 'Quick-undo');
  await expect.poll(async () => (await state(page)).pivots[0].rows).toBe(1);
  await click(page, 'Quick-redo');
  await expect.poll(async () => (await state(page)).pivots[0].rows).toBe(2);

  // Moving Quarter from Columns to Rows must not occur on Escape or an outside drop.
  const after = (await state(page)).controls;
  const quarter = after.Pivotfield2, rows = after.PivotAreaRows;
  const qx = quarter.x + quarter.width / 2, qy = quarter.y + quarter.height / 2;
  await page.mouse.move(qx, qy); await page.mouse.down();
  await page.mouse.move(rows.x + rows.width / 2, rows.y + 15, { steps: 12 });
  await page.keyboard.press('Escape'); await page.mouse.up();
  expect((await state(page)).pivots[0].rows).toBe(2);
  expect((await state(page)).pivots[0].columns).toBe(1);
  await page.mouse.move(qx, qy); await page.mouse.down();
  await page.mouse.move(800, 400, { steps: 12 }); await page.mouse.up();
  expect((await state(page)).pivots[0].rows).toBe(2);
  expect((await state(page)).pivots[0].columns).toBe(1);
});

test('name-box navigation leaves chart editing and reveals the requested cell', async ({ page }) => {
  const id = await insertChart(page);
  await select(page, 'A10000');
  await expect.poll(async () => (await state(page)).selectedChart).toBeNull();
  await expect.poll(async () => (await state(page)).scrollY).toBeGreaterThan(100000);
  await page.keyboard.press('F2');
  await page.keyboard.press('Control+A'); await page.keyboard.type('42'); await page.keyboard.press('Enter');
  await select(page, 'A10000');
  expect((await state(page)).value).toBe('42');
  expect((await state(page)).charts.some(c => c.id === id)).toBe(true);
  expect((await state(page)).charts.find(c => c.id === id).title).not.toBe('42');
});
