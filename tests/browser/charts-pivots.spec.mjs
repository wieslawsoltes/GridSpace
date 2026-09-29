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
async function choose(page, id, index) {
  await click(page, id);
  await expect.poll(async () => (await state(page)).controls[id]?.expanded).toBe(true);
  await page.keyboard.press('Home');
  for (let i = 0; i < index; i++) await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).controls[id]?.selectedIndex).toBe(index);
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

test('moves and resizes chart with one gesture transaction and Escape rollback', async ({ page }) => {
  const id = await insertChart(page);
  const before = (await state(page)).charts.find(c => c.id === id);
  const p = before.panes[0];
  // The title band is a body-move hit target; use coordinates from read-only geometry.
  const x = Math.max(p.clipX + 12, p.x + 45), y = Math.max(p.clipY + 12, p.y + 20);
  await page.mouse.move(x, y); await page.mouse.down();
  await page.mouse.move(x + 55, y + 35, { steps: 8 }); await page.mouse.up();
  await expect.poll(async () => {
    const after = (await state(page)).charts.find(c => c.id === id);
    return after.offsetX !== before.offsetX || after.column !== before.column;
  }).toBe(true);
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await state(page)).charts.find(c => c.id === id)?.offsetX).toBe(before.offsetX);
  const current = (await state(page)).charts.find(c => c.id === id), rect = current.panes[0];
  const right = rect.x + rect.width - 1, bottom = rect.y + rect.height - 1;
  await page.mouse.move(right, bottom); await page.mouse.down();
  await page.mouse.move(right - 40, bottom - 25, { steps: 8 }); await page.mouse.up();
  await expect.poll(async () => (await state(page)).charts.find(c => c.id === id)?.width).toBeLessThan(current.width);
  const saved = (await state(page)).charts.find(c => c.id === id);
  const handle = saved.panes[0];
  await page.mouse.move(handle.x + 40, handle.y + 20); await page.mouse.down();
  await page.mouse.move(handle.x + 75, handle.y + 50, { steps: 4 });
  await page.keyboard.press('Escape'); await page.mouse.up();
  expect((await state(page)).charts.find(c => c.id === id)?.offsetX).toBe(saved.offsetX);
});

test('edits chart title inline and changes type in reusable inspector', async ({ page }) => {
  const id = await insertChart(page);
  await click(page, 'Command-chart-title');
  await expect.poll(async () => !!(await state(page)).controls.Charttitleeditor).toBe(true);
  await page.keyboard.press('Control+A'); await page.keyboard.type('Quarterly performance'); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).charts.find(c => c.id === id)?.title).toBe('Quarterly performance');
  await choose(page, 'Charttype', 1); // Line
  await expect.poll(async () => (await state(page)).charts.find(c => c.id === id)?.kind).toBe('Line');
  await click(page, 'Command-chart-duplicate');
  await expect.poll(async () => (await state(page)).charts.length).toBe(3); // sample + two new
  await click(page, 'Command-chart-delete');
  await expect.poll(async () => (await state(page)).charts.length).toBe(2);
});

test('creates PivotTable and linked chart with editable field list', async ({ page }) => {
  await select(page, 'B5:I17'); await click(page, 'RibbonTabInsert'); await click(page, 'Command-pivot');
  await expect.poll(async () => (await state(page)).pivots.length).toBe(1);
  await expect.poll(async () => !!(await state(page)).controls.PivotFieldList).toBe(true);
  const pivot = (await state(page)).pivots[0];
  expect(pivot.records).toBe(12);
  await click(page, 'PivotChart');
  await expect.poll(async () => (await state(page)).charts.length).toBe(1);
  expect((await state(page)).charts[0].pivotId).toBe(pivot.id);
});

test('loads analytics sample and drills through cached PivotTable values', async ({ page }) => {
  await click(page, 'RibbonTabHelp'); await click(page, 'Command-analytics-sample');
  await expect.poll(async () => (await state(page)).sheet).toBe('Pivot analysis');
  expect((await state(page)).pivots[0].records).toBe(24);
  expect((await state(page)).charts.length).toBe(2);
  await select(page, 'C6');
  await expect.poll(async () => !!(await state(page)).controls.PivotFieldList).toBe(true);
  await click(page, 'Command-pivot-details');
  await expect.poll(async () => (await state(page)).sheet.startsWith('Details')).toBe(true);
  await select(page, 'A2');
  expect((await state(page)).value).toBe('Americas');
});
