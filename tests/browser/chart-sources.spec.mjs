import { test, expect } from '@playwright/test';
import fs from 'node:fs/promises';
import { workbenchHeaderCoverage } from './png-probe.mjs';

const root = process.env.GRIDSPACE_URL || 'http://127.0.0.1:4173/GridSpace/';
const state = page => page.evaluate(() => globalThis.gridSpaceDiagnostics);
const errors = new WeakMap();
async function click(page, id) {
  await expect.poll(async () => !!(await state(page))?.controls?.[id], { message: `Control ${id}` }).toBe(true);
  const r = (await state(page)).controls[id];
  await page.mouse.click(r.x + r.width / 2, r.y + r.height / 2);
}
async function sourceChart(page) {
  await click(page, 'Namebox'); await page.keyboard.press('Control+A');
  await page.keyboard.type('B5:F17'); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).selection).toBe('B5:F17');
  await click(page, 'RibbonTabInsert'); await click(page, 'Command-chart-column');
  await expect.poll(async () => !!(await state(page)).selectedChart).toBe(true);
  await click(page, 'Command-chart-source');
  await expect.poll(async () => (await state(page)).chartSources?.[0]?.targets.some(t => t.handle === 'End')).toBe(true);
  return (await state(page)).selectedChart;
}
async function grip(page, part, index = -1, handle = 'End') {
  await expect.poll(async () => (await state(page)).chartSources?.find(s => s.part === part && s.index === index)?.targets.some(t => t.handle === handle)).toBe(true);
  return (await state(page)).chartSources.find(s => s.part === part && s.index === index).targets.find(t => t.handle === handle);
}
async function start(page, target, dx, dy) {
  await page.mouse.move(target.x, target.y); await page.mouse.down();
  await expect.poll(async () => (await state(page)).chartSourceEditing).toBe(true);
  await page.mouse.move(target.x + dx, target.y + dy, { steps: 6 });
}

test.beforeEach(async ({ page }) => {
  const found = []; errors.set(page, found); page.on('pageerror', e => found.push(e.stack || e.message));
  const url = new URL(root); url.searchParams.set('test', '1');
  await page.goto(url.href, { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(() => globalThis.gridSpaceDiagnostics?.controls?.Namebox, null, { timeout: 60000 });
  await expect.poll(async () => workbenchHeaderCoverage(await page.screenshot()), { timeout: 60000 }).toBeGreaterThan(.7);
});
test.afterEach(async ({ page }, info) => {
  await fs.mkdir('artifacts/screenshots', { recursive: true });
  const name = info.title.replace(/[^a-z0-9]+/gi, '-').toLowerCase();
  await page.screenshot({ path: `artifacts/screenshots/${name}.png`, fullPage: true });
  await test.info().attach('chart-source-state', { body: JSON.stringify(await state(page), null, 2), contentType: 'application/json' });
  expect(errors.get(page)).toEqual([]);
});

test('previews source resize without history then commits one undoable chart edit', async ({ page }) => {
  const id = await sourceChart(page); const before = await state(page);
  const target = await grip(page, 'DataRange');
  await start(page, target, 0, 48);
  await expect.poll(async () => (await state(page)).chartSourcePreviewRange).toBe('B5:F19');
  const preview = await state(page);
  expect(preview.charts.find(c => c.id === id).range).toBe('B5:F17');
  expect(preview.drawingRevision).toBe(before.drawingRevision);
  expect(preview.historyBytes).toBe(before.historyBytes);
  expect(preview.cells).toBe(before.cells);
  await page.mouse.up();
  await expect.poll(async () => (await state(page)).charts.find(c => c.id === id)?.range).toBe('B5:F19');
  expect((await state(page)).layoutRefreshes).toBe(before.layoutRefreshes);
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await state(page)).charts.find(c => c.id === id)?.range).toBe('B5:F17');
  await page.keyboard.press('Control+y');
  await expect.poll(async () => (await state(page)).charts.find(c => c.id === id)?.range).toBe('B5:F19');
});

test('cancels source drag with Escape and an outside release without changing data', async ({ page }) => {
  const id = await sourceChart(page); const before = await state(page);
  await start(page, await grip(page, 'DataRange'), 0, 48);
  await page.keyboard.press('Escape'); await page.mouse.up();
  await expect.poll(async () => (await state(page)).chartSourceEditing).toBe(false);
  expect((await state(page)).historyBytes).toBe(before.historyBytes);
  expect((await state(page)).selectedChart).toBe(id);
  await start(page, await grip(page, 'DataRange'), 0, 24);
  await page.mouse.move(10, 5); await page.mouse.up();
  await expect.poll(async () => (await state(page)).chartSourceEditing).toBe(false);
  expect((await state(page)).charts.find(c => c.id === id).range).toBe('B5:F17');
  expect((await state(page)).historyBytes).toBe(before.historyBytes);
});

test('moves a whole source border without changing its dimensions', async ({ page }) => {
  const id = await sourceChart(page);
  await start(page, await grip(page, 'DataRange', -1, 'Move'), 0, 48);
  await page.mouse.up();
  await expect.poll(async () => (await state(page)).charts.find(c => c.id === id)?.range).toBe('B7:F19');
});

test('customizes series and resizes one value vector without changing its neighbors', async ({ page }) => {
  await sourceChart(page); await click(page, 'Command-chart-customize');
  await expect.poll(async () => (await state(page)).chartSources?.length).toBe(5);
  const before = await state(page);
  await start(page, await grip(page, 'SeriesValues', 0), 30, 48);
  await page.mouse.up();
  await expect.poll(async () => (await state(page)).chartSources.find(s => s.part === 'SeriesValues' && s.index === 0)?.range).toBe('C6:C19');
  const after = await state(page);
  expect(after.chartSources.find(s => s.part === 'Categories').range).toBe('B6:B17');
  expect(after.chartSources.find(s => s.part === 'SeriesValues' && s.index === 1).range).toBe('D6:D17');
  expect(after.cells).toBe(before.cells);
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await state(page)).chartSources.find(s => s.part === 'SeriesValues' && s.index === 0)?.range).toBe('C6:C17');
});

test('rejects an invalid automatic shape instead of committing the last valid preview', async ({ page }) => {
  const id = await sourceChart(page); const before = await state(page);
  const end = await grip(page, 'DataRange'); const startGrip = await grip(page, 'DataRange', -1, 'Start');
  await start(page, end, 0, 24);
  await page.mouse.move(startGrip.x + 2, end.y, { steps: 6 });
  await expect.poll(async () => !!(await state(page)).chartSourcePreviewError).toBe(true);
  await page.mouse.up();
  expect((await state(page)).charts.find(c => c.id === id).range).toBe('B5:F17');
  expect((await state(page)).historyBytes).toBe(before.historyBytes);
});

test('keeps PivotChart source ranges report-owned', async ({ page }) => {
  await click(page, 'RibbonTabHelp'); await click(page, 'Command-analytics-sample');
  await expect.poll(async () => (await state(page)).sheet).toBe('Pivot analysis');
  const chart = (await state(page)).charts.find(c => c.pivotId && c.panes.length);
  expect(chart).toBeTruthy(); const p = chart.panes[0];
  await page.mouse.click(Math.max(p.clipX + 12, p.x + 35), Math.max(p.clipY + 12, p.y + 20));
  await expect.poll(async () => (await state(page)).selectedChart).toBe(chart.id);
  await click(page, 'Command-chart-source');
  expect((await state(page)).chartSources).toEqual([]);
  expect((await state(page)).charts.find(c => c.id === chart.id).pivotId).toBe(chart.pivotId);
  expect((await state(page)).status).toContain('PivotTable');
});

test('cancels a source preview when zoom changes before release', async ({ page }) => {
  const id = await sourceChart(page); const before = await state(page);
  await start(page, await grip(page, 'DataRange'), 0, 48);
  await page.keyboard.down('Control'); await page.mouse.wheel(0, -120); await page.keyboard.up('Control');
  await expect.poll(async () => (await state(page)).zoom).toBeGreaterThan(before.zoom);
  await page.mouse.up();
  await expect.poll(async () => (await state(page)).chartSourceEditing).toBe(false);
  expect((await state(page)).charts.find(c => c.id === id).range).toBe('B5:F17');
  expect((await state(page)).historyBytes).toBe(before.historyBytes);
});
