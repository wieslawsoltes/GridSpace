import { test, expect } from '@playwright/test';
import fs from 'node:fs/promises';
import { workbenchHeaderCoverage } from './png-probe.mjs';

const root = process.env.GRIDSPACE_URL || 'http://127.0.0.1:4173/GridSpace/';
const state = page => page.evaluate(() => globalThis.gridSpaceDiagnostics);
const failures = new WeakMap();
async function click(page, id) {
  await expect.poll(async () => !!(await state(page))?.controls?.[id], { message: `Control ${id}` }).toBe(true);
  const rect = (await state(page)).controls[id];
  await page.mouse.click(rect.x + rect.width / 2, rect.y + rect.height / 2);
}
async function input(page, id, value) {
  await click(page, id); await page.keyboard.press('Control+A');
  if (value.length) await page.keyboard.type(value); else await page.keyboard.press('Backspace');
  await page.keyboard.press('Enter');
}
async function chart(page) {
  await input(page, 'Namebox', 'B5:F17');
  await click(page, 'RibbonTabInsert'); await click(page, 'Command-chart-column');
  await expect.poll(async () => !!(await state(page)).selectedChart).toBe(true);
  return (await state(page)).selectedChart;
}
const current = async (page, id) => (await state(page)).charts.find(c => c.id === id);
async function link(page, text) {
  await input(page, 'Charttitlereference', text);
  // Rebinding the immediate editor is deferred until after its input event.
  await expect.poll(async () => (await state(page)).controls.Charttitlereference?.text).toBe(text ? text.startsWith('Assumptions') ? "'Assumptions'!$B$3" : "'Revenue'!$B$2" : '');
}
async function grip(page, part, index = -1) {
  await expect.poll(async () => (await state(page)).chartSources?.find(s => s.part === part && s.index === index)?.targets.some(t => t.handle === 'Move')).toBe(true);
  return (await state(page)).chartSources.find(s => s.part === part && s.index === index).targets.find(t => t.handle === 'Move');
}
async function drag(page, target, dx, dy) {
  await page.mouse.move(target.x, target.y); await page.mouse.down();
  await expect.poll(async () => (await state(page)).chartSourceEditing).toBe(true);
  await page.mouse.move(target.x + dx, target.y + dy, { steps: 6 });
}

test.beforeEach(async ({ page }) => {
  const errors = []; failures.set(page, errors); page.on('pageerror', error => errors.push(error.stack || error.message));
  const url = new URL(root); url.searchParams.set('test', '1');
  await page.goto(url.href, { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(() => globalThis.gridSpaceDiagnostics?.controls?.Namebox, null, { timeout: 60000 });
  await expect.poll(async () => workbenchHeaderCoverage(await page.screenshot()), { timeout: 60000 }).toBeGreaterThan(.7);
});
test.afterEach(async ({ page }, info) => {
  await fs.mkdir('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: `artifacts/screenshots/${info.title.replace(/[^a-z0-9]+/gi, '-').toLowerCase()}.png`, fullPage: true });
  await test.info().attach('chart-text-state', { body: JSON.stringify(await state(page), null, 2), contentType: 'application/json' });
  expect(failures.get(page)).toEqual([]);
});

test('links chart titles to formatted cross-sheet cells and freezes them when unlinked', async ({ page }) => {
  const id = await chart(page);
  await link(page, 'Assumptions!B3');
  await expect.poll(async () => (await current(page, id)).displayTitle).toBe('$1,250');
  expect((await current(page, id)).titleReference).toBe("'Assumptions'!$B$3");
  const before = (await state(page)).historyBytes;
  await input(page, 'Charttitlereference', 'Assumptions!B3:B4');
  await expect.poll(async () => (await state(page)).controls.Charttitlereference?.text).toBe("'Assumptions'!$B$3");
  expect((await state(page)).historyBytes).toBe(before);
  await link(page, '');
  await expect.poll(async () => (await current(page, id)).titleReference).toBe(null);
  expect((await current(page, id)).title).toBe('$1,250');
  await click(page, 'Command-chart-source'); await page.keyboard.press('Control+z');
  await expect.poll(async () => (await current(page, id)).titleReference).toBe("'Assumptions'!$B$3");
});

test('inline title editing preserves an unchanged link and unlinks only a committed text change', async ({ page }) => {
  const id = await chart(page); await link(page, 'B2');
  const before = (await state(page)).historyBytes;
  await click(page, 'Command-chart-title');
  await expect.poll(async () => (await state(page)).editing).toBe(true);
  await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).editing).toBe(false);
  expect((await current(page, id)).titleReference).toBe("'Revenue'!$B$2");
  expect((await state(page)).historyBytes).toBe(before);
  await click(page, 'Command-chart-title'); await page.keyboard.press('Control+A'); await page.keyboard.type('Cancelled title');
  await page.keyboard.press('Escape');
  expect((await current(page, id)).displayTitle).toBe('Revenue overview');
  await click(page, 'Command-chart-title'); await page.keyboard.press('Control+A'); await page.keyboard.type('Chart-only caption');
  await page.keyboard.press('Enter');
  await expect.poll(async () => (await current(page, id)).titleReference).toBe(null);
  expect((await current(page, id)).displayTitle).toBe('Chart-only caption');
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await current(page, id)).titleReference).toBe("'Revenue'!$B$2");
  await input(page, 'Namebox', 'B2');
  expect((await state(page)).value).toBe('Revenue overview');
});

test('drags a title cell link without resizing it or recalculating chart value vectors', async ({ page }) => {
  const id = await chart(page); await link(page, 'B2'); await click(page, 'Command-chart-source');
  const before = await state(page);
  await drag(page, await grip(page, 'Title'), 0, 37);
  await expect.poll(async () => (await state(page)).chartSourcePreviewRange).toBe('B3');
  expect((await current(page, id)).titleReference).toBe("'Revenue'!$B$2");
  expect((await state(page)).historyBytes).toBe(before.historyBytes);
  await page.mouse.up();
  await expect.poll(async () => (await current(page, id)).titleReference).toBe("'Revenue'!$B$3");
  expect((await current(page, id)).displayTitle).toContain('Illustrative sales data');
  expect((await state(page)).chartDataResolutions).toBe(before.chartDataResolutions);
  expect((await state(page)).cells).toBe(before.cells);
  expect((await state(page)).layoutRefreshes).toBe(before.layoutRefreshes);
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await current(page, id)).titleReference).toBe("'Revenue'!$B$2");
  await drag(page, await grip(page, 'Title'), 0, 37); await page.keyboard.press('Escape'); await page.mouse.up();
  await expect.poll(async () => (await state(page)).chartSourceEditing).toBe(false);
  expect((await current(page, id)).titleReference).toBe("'Revenue'!$B$2");
});

test('customized series retain live header links with independent caption-only drag undo', async ({ page }) => {
  const id = await chart(page); await click(page, 'Command-chart-customize'); await click(page, 'Command-chart-source');
  await expect.poll(async () => (await state(page)).chartSources?.filter(s => s.part === 'SeriesName').length).toBe(4);
  expect((await current(page, id)).seriesNames[0]).toBe('Region');
  const before = await state(page);
  await drag(page, await grip(page, 'SeriesName', 0), 80, 0);
  await expect.poll(async () => (await state(page)).chartSourcePreviewRange).toBe('D5');
  await page.mouse.up();
  await expect.poll(async () => (await current(page, id)).seriesNames[0]).toBe('Units');
  const after = await state(page);
  expect(after.chartSources.find(s => s.part === 'SeriesValues' && s.index === 0).range).toBe('C6:C17');
  expect(after.chartDataResolutions).toBe(before.chartDataResolutions);
  expect(after.cells).toBe(before.cells);
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await current(page, id)).seriesNames[0]).toBe('Region');
});

test('native save keeps title and automatic-header link identities', async ({ page }) => {
  const id = await chart(page); await link(page, 'B2'); await click(page, 'Command-chart-customize'); await click(page, 'Command-chart-source');
  const pending = page.waitForEvent('download'); await page.keyboard.press('Control+s'); const download = await pending;
  const target = test.info().outputPath('linked-chart.gridspace'); await download.saveAs(target);
  const book = JSON.parse(await fs.readFile(target, 'utf8'));
  const sheet = (book.Sheets ?? book.sheets)[0];
  const savedChart = (sheet.Charts ?? sheet.charts).find(c => (c.Id ?? c.id) === id);
  const title = savedChart.TitleReference ?? savedChart.titleReference;
  expect(title.Sheet ?? title.sheet).toBe('Revenue'); expect(title.Cell ?? title.cell).toBe('B2');
  const series = (savedChart.Series ?? savedChart.series)[0];
  const name = series.NameReference ?? series.nameReference;
  expect(name.Sheet ?? name.sheet).toBe('Revenue'); expect(name.Cell ?? name.cell).toBe('C5');
});
