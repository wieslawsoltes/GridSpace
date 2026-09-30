import { test, expect } from '@playwright/test';
import fs from 'node:fs/promises';
import { workbenchHeaderCoverage } from './png-probe.mjs';

const root = process.env.GRIDSPACE_URL || 'http://127.0.0.1:4173/GridSpace/';
const state = page => page.evaluate(() => globalThis.gridSpaceDiagnostics);
const failures = new WeakMap();
async function click(page, id) {
  await expect.poll(async () => !!(await state(page))?.controls?.[id]).toBe(true);
  const r = (await state(page)).controls[id];
  await page.mouse.click(r.x + r.width / 2, r.y + r.height / 2);
}
async function select(page, address) {
  await click(page, 'Namebox'); await page.keyboard.press('Control+A');
  await page.keyboard.type(address); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).selection).toBe(address);
}
async function insertChart(page) {
  await select(page, 'B5:F17'); await click(page, 'RibbonTabInsert');
  await click(page, 'Command-chart-column');
  await expect.poll(async () => !!(await state(page)).selectedChart).toBe(true);
  const id = (await state(page)).selectedChart;
  await expect.poll(async () => (await state(page)).controls.ChartInspector?.documentId).toBe(id);
  await expect.poll(async () => (await state(page)).charts.find(c => c.id === id)?.panes.length).toBeGreaterThan(0);
  return id;
}
async function clickInspectorControl(page, id, panelId = 'ChartInspector') {
  await expect.poll(async () => !!(await state(page)).controls[id]).toBe(true);
  for (let attempt = 0; attempt < 24; attempt++) {
    const current = await state(page), target = current.controls[id], panel = current.controls[panelId];
    expect(target, `Control ${id}`).toBeTruthy(); expect(panel, `Panel ${panelId}`).toBeTruthy();
    const top = Math.max(0, panel.y) + 8;
    const bottom = Math.min(page.viewportSize().height, panel.y + panel.height) - 8;
    if (target.y >= top && target.y + target.height <= bottom) {
      await page.mouse.click(target.x + target.width / 2, target.y + target.height / 2); return;
    }
    const oldY = target.y, direction = target.y < top ? -1 : 1;
    await page.mouse.move(panel.x + panel.width / 2, (top + bottom) / 2);
    await page.mouse.wheel(0, direction * 320);
    await expect.poll(async () => {
      const next = (await state(page)).controls[id];
      return !!next && Math.abs(next.y - oldY) > 1;
    }, { message: `Scroll ${id} into the inspector viewport` }).toBe(true);
  }
  throw new Error(`Could not reveal inspector control ${id}`);
}
async function chooseInspectorValue(page, id, index) {
  await clickInspectorControl(page, id);
  await expect.poll(async () => (await state(page)).controls[id]?.expanded).toBe(true);
  await page.keyboard.press('Home');
  for (let i = 0; i < index; i++) await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).controls[id]?.selectedIndex).toBe(index);
}

test.beforeEach(async ({ page }) => {
  const errors = []; failures.set(page, errors); page.on('pageerror', e => errors.push(e.stack || e.message));
  const url = new URL(root); url.searchParams.set('test', '1');
  await page.goto(url.href, { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(() => globalThis.gridSpaceDiagnostics?.controls?.Namebox, null, { timeout: 60000 });
  await expect.poll(async () => workbenchHeaderCoverage(await page.screenshot()), { timeout: 60000 }).toBeGreaterThan(.7);
});
test.afterEach(async ({ page }, info) => {
  await fs.mkdir('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: `artifacts/screenshots/${info.title.replace(/[^a-z0-9]+/gi, '-').toLowerCase()}.png`, fullPage: true });
  await test.info().attach('inspector-state', { body: JSON.stringify(await state(page), null, 2), contentType: 'application/json' });
  expect(failures.get(page)).toEqual([]);
});

test('rebinds Combo controls after a chart inspector type change', async ({ page }) => {
  const id = await insertChart(page);
  await clickInspectorControl(page, 'ChartCustomizeSeries');
  await expect.poll(async () => (await state(page)).charts.find(c => c.id === id)?.seriesCount).toBeGreaterThan(0);
  await chooseInspectorValue(page, 'Charttype', 8);
  await expect.poll(async () => !!(await state(page)).controls.Series0type).toBe(true);
  await expect.poll(async () => !!(await state(page)).controls.Series0secondaryaxis).toBe(true);
  await chooseInspectorValue(page, 'Charttype', 1);
  await expect.poll(async () => !!(await state(page)).controls.Series0type).toBe(false);
});

test('customizes freshly resolved series after changing the chart source', async ({ page }) => {
  const id = await insertChart(page);
  await clickInspectorControl(page, 'Chartsourcerange');
  await page.keyboard.press('Control+A'); await page.keyboard.type('B5:E17'); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).charts.find(c => c.id === id)?.range).toBe('B5:E17');
  await clickInspectorControl(page, 'ChartCustomizeSeries');
  await expect.poll(async () => (await state(page)).charts.find(c => c.id === id)?.seriesCount).toBe(3);
});

test('nudges a chart without rebuilding its inspector or resolving data', async ({ page }) => {
  const id = await insertChart(page);
  const pane = (await state(page)).charts.find(c => c.id === id).panes[0];
  await page.mouse.click(Math.max(pane.clipX + 12, pane.x + 45), Math.max(pane.clipY + 12, pane.y + 20));
  const before = await state(page);
  await page.keyboard.press('ArrowRight');
  await expect.poll(async () => (await state(page)).drawingRevision).toBeGreaterThan(before.drawingRevision);
  await expect.poll(async () => (await state(page)).controls.ChartInspector?.geometrySyncs).toBeGreaterThan(before.controls.ChartInspector.geometrySyncs);
  const after = await state(page);
  expect(after.controls.ChartInspector.visualBuilds).toBe(before.controls.ChartInspector.visualBuilds);
  expect(after.chartDataResolutions).toBe(before.chartDataResolutions);
});

test('rebinds the field list when a copied PivotTable becomes active', async ({ page }) => {
  await click(page, 'RibbonTabHelp'); await click(page, 'Command-analytics-sample');
  await expect.poll(async () => (await state(page)).sheet).toBe('Pivot analysis');
  await select(page, 'B5');
  const original = (await state(page)).pivots[0].id;
  await expect.poll(async () => (await state(page)).controls.PivotFieldList?.documentId).toBe(original);
  await click(page, 'RibbonTabInsert'); await click(page, 'Command-duplicate-sheet');
  await expect.poll(async () => (await state(page)).pivots[0]?.id).not.toBe(original);
  await select(page, 'B5');
  const copied = (await state(page)).pivots[0].id;
  await expect.poll(async () => (await state(page)).controls.PivotFieldList?.documentId).toBe(copied);
  await clickInspectorControl(page, 'Pivotfield1', 'PivotFieldList');
  await expect.poll(async () => (await state(page)).pivots[0]?.rows).toBe(2);
});
