import { test, expect } from '@playwright/test';
import fs from 'node:fs/promises';
import { workbenchHeaderCoverage } from './png-probe.mjs';

const root = process.env.GRIDSPACE_URL || 'http://127.0.0.1:4173/GridSpace/';
const state = page => page.evaluate(() => globalThis.gridSpaceDiagnostics);
const failures = new WeakMap();
async function click(page, id) {
  await expect.poll(async () => !!(await state(page))?.controls?.[id]).toBe(true);
  const rect = (await state(page)).controls[id];
  await page.mouse.click(rect.x + rect.width / 2, rect.y + rect.height / 2);
}
async function select(page, address) {
  await click(page, 'Namebox'); await page.keyboard.press('Control+A');
  await page.keyboard.type(address); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).selection).toBe(address);
}
async function choose(page, id, index) {
  await click(page, id);
  await expect.poll(async () => (await state(page)).controls[id]?.expanded).toBe(true);
  await page.keyboard.press('Home');
  for (let i = 0; i < index; i++) await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).controls[id]?.selectedIndex).toBe(index);
}
async function report(page, subtotals = 2) {
  await click(page, 'RibbonTabHelp'); await click(page, 'Command-analytics-sample');
  await expect.poll(async () => (await state(page)).sheet).toBe('Pivot analysis');
  await select(page, 'B5');
  await click(page, 'Pivotfield1'); // Product joins Region in Rows.
  await expect.poll(async () => (await state(page)).pivots[0]?.rows).toBe(2);
  await choose(page, 'Pivotreportlayout', 2); // Compact.
  await choose(page, 'Pivotsubtotals', subtotals);
  await expect.poll(async () => (await state(page)).pivots[0]?.toggles.length).toBeGreaterThan(0);
}
async function toggle(page, expanded = true) {
  const glyph = (await state(page)).pivots[0].toggles.find(t => t.path === 'Americas' && t.expanded === expanded);
  expect(glyph).toBeTruthy();
  await page.mouse.click(glyph.x + glyph.width / 2, glyph.y + glyph.height / 2);
}

test.beforeEach(async ({ page }) => {
  const errors = []; failures.set(page, errors); page.on('pageerror', e => errors.push(e.message));
  const url = new URL(root); url.searchParams.set('test', '1');
  await page.goto(url.href, { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(() => globalThis.gridSpaceDiagnostics?.controls?.Namebox, null, { timeout: 60000 });
  await expect.poll(async () => workbenchHeaderCoverage(await page.screenshot()), { timeout: 60000 }).toBeGreaterThan(.7);
});
test.afterEach(async ({ page }, info) => {
  await fs.mkdir('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: `artifacts/screenshots/${info.title.replace(/[^a-z0-9]+/gi, '-').toLowerCase()}.png`, fullPage: true });
  expect(failures.get(page)).toEqual([]);
});

test('expands and collapses painted hierarchy buttons with undo and redo', async ({ page }) => {
  await report(page);
  const original = (await state(page)).pivots[0].output;
  await toggle(page);
  await expect.poll(async () => (await state(page)).pivots[0].collapsed).toBe(1);
  expect((await state(page)).pivots[0].output).not.toBe(original);
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await state(page)).pivots[0].collapsed).toBe(0);
  expect((await state(page)).pivots[0].output).toBe(original);
  await page.keyboard.press('Control+y');
  await expect.poll(async () => (await state(page)).pivots[0].collapsed).toBe(1);
  await toggle(page, false);
  await expect.poll(async () => (await state(page)).pivots[0].collapsed).toBe(0);
});

test('cancels hierarchy clicks without mutation and supports zoomed targets', async ({ page }) => {
  await report(page);
  const before = (await state(page)).historyBytes;
  const glyph = (await state(page)).pivots[0].toggles[0];
  await page.mouse.move(glyph.x + glyph.width / 2, glyph.y + glyph.height / 2); await page.mouse.down();
  await page.keyboard.press('Escape'); await page.mouse.up();
  expect((await state(page)).pivots[0].collapsed).toBe(0);
  expect((await state(page)).historyBytes).toBe(before);
  await page.mouse.move(450, 330); await page.keyboard.down('Control'); await page.mouse.wheel(0, -120); await page.keyboard.up('Control');
  await expect.poll(async () => (await state(page)).zoom).toBeGreaterThan(1);
  await toggle(page);
  await expect.poll(async () => (await state(page)).pivots[0].collapsed).toBe(1);
});

test('changes hierarchy layout and subtotal placement through the field list', async ({ page }) => {
  await report(page, 1);
  await choose(page, 'Pivotreportlayout', 1); // Outline.
  await expect.poll(async () => (await state(page)).pivots[0].layout).toBe('Outline');
  await click(page, 'Command-pivot-collapse-all');
  await expect.poll(async () => (await state(page)).pivots[0].collapsed).toBe(3);
  await click(page, 'Command-pivot-expand-all');
  await expect.poll(async () => (await state(page)).pivots[0].collapsed).toBe(0);
  await choose(page, 'Pivotreportlayout', 0);
  await choose(page, 'Pivotsubtotals', 0);
  expect((await state(page)).pivots[0].layout).toBe('Tabular');
  expect((await state(page)).pivots[0].subtotals).toBe('None');
});

test('double clicks compact group labels and drills through the collapsed snapshot', async ({ page }) => {
  await report(page);
  const glyph = (await state(page)).pivots[0].toggles[0];
  await page.mouse.dblclick(glyph.x + glyph.width + 24, glyph.y + glyph.height / 2);
  await expect.poll(async () => (await state(page)).pivots[0].collapsed).toBe(1);
  await select(page, 'G6'); // Americas grand total: two products x four quarters.
  const value = (await state(page)).activeCellBounds;
  await page.mouse.dblclick(value.x + value.width / 2, value.y + value.height / 2);
  await expect.poll(async () => (await state(page)).sheet.startsWith('Details')).toBe(true);
  await select(page, 'A9'); expect((await state(page)).value).toBe('Americas');
  await select(page, 'A10'); expect((await state(page)).value).toBe('');
});

test('keyboard expands selected groups and native save retains hierarchy state', async ({ page }) => {
  await report(page); await toggle(page);
  await page.keyboard.press('Control+Alt+ArrowRight');
  await expect.poll(async () => (await state(page)).pivots[0].collapsed).toBe(0);
  await page.keyboard.press('Control+Alt+ArrowLeft');
  await expect.poll(async () => (await state(page)).pivots[0].collapsed).toBe(1);
  const pending = page.waitForEvent('download'); await page.keyboard.press('Control+s');
  const download = await pending; const path = test.info().outputPath('hierarchy.gridspace'); await download.saveAs(path);
  const data = JSON.parse(await fs.readFile(path, 'utf8'));
  const sheets = data.Sheets ?? data.sheets;
  const sheet = sheets.find(s => (s.Name ?? s.name) === 'Pivot analysis');
  const pivot = (sheet.PivotTables ?? sheet.pivotTables)[0];
  expect(pivot.CollapsedRows ?? pivot.collapsedRows).toHaveLength(1);
});
