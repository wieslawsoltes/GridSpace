import { test, expect } from '@playwright/test';
import fs from 'node:fs/promises';
import { workbenchHeaderCoverage } from './png-probe.mjs';

const address = process.env.GRIDSPACE_URL || 'http://127.0.0.1:4173/GridSpace/';
const state = page => page.evaluate(() => globalThis.gridSpaceDiagnostics);
const failures = new WeakMap();

async function click(page, id) {
  await expect.poll(async () => !!(await state(page))?.controls?.[id], { message: `Visible Uno control ${id}` }).toBe(true);
  const control = (await state(page)).controls[id];
  await page.mouse.click(control.x + control.width / 2, control.y + control.height / 2);
}
async function text(page, id, value) {
  await click(page, id); await page.keyboard.press('Control+A'); await page.keyboard.type(value);
}
async function choose(page, id, index) {
  await click(page, id);
  await page.keyboard.press('Home');
  for (let i = 0; i < index; i++) await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).controls[id]?.selectedIndex).toBe(index);
}
async function select(page, range) {
  await text(page, 'Namebox', range); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).selection).toBe(range);
}
async function screenshot(page, name) {
  await fs.mkdir('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: `artifacts/screenshots/${name}.png`, fullPage: true });
}

test.beforeEach(async ({ page }) => {
  const errors = []; failures.set(page, errors);
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => { if (message.type() === 'error') console.log('browser:', message.text()); });
  const url = new URL(address); url.searchParams.set('test', '1');
  await page.goto(url.href, { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(() => globalThis.gridSpaceDiagnostics?.controls?.Namebox, null, { timeout: 60000 });
  await expect.poll(async () => workbenchHeaderCoverage(await page.screenshot()), { timeout: 60000 }).toBeGreaterThan(.7);
});
test.afterEach(async ({ page }, info) => {
  await screenshot(page, info.title.replace(/[^a-z0-9]+/gi, '-').toLowerCase());
  console.log('Parity state:', JSON.stringify(await state(page)));
  expect(failures.get(page)).toEqual([]);
});

test('applies live data bars and restores base formatting through undo', async ({ page }) => {
  await select(page, 'D6:D17'); await click(page, 'RibbonTabData'); await click(page, 'Command-data-bars');
  await expect.poll(async () => (await state(page)).conditionalRules).toBe(1);
  await expect.poll(async () => (await state(page)).dataBar).toBe(true);
  await screenshot(page, 'data-bars-revenue');
  await click(page, 'Quick-undo'); await expect.poll(async () => (await state(page)).conditionalRules).toBe(0);
  await click(page, 'Quick-redo'); await expect.poll(async () => (await state(page)).conditionalRules).toBe(1);
});

test('creates and manages a value-based conditional rule using the editor', async ({ page }) => {
  await select(page, 'D6:D17'); await click(page, 'RibbonTabData'); await click(page, 'Command-conditional-format');
  await text(page, 'Conditionaloperand', '200');
  await screenshot(page, 'conditional-format-rule-editor');
  await click(page, 'PrimaryButton');
  await expect.poll(async () => (await state(page)).conditionalRules).toBe(1);
  await select(page, 'D17'); await expect.poll(async () => (await state(page)).effectiveFill).toBe('#FFC7CE');
  await select(page, 'D6'); await expect.poll(async () => (await state(page)).effectiveFill).toBe('#F0F6F2');
  await click(page, 'Command-conditional-manage');
  await expect.poll(async () => (await state(page)).overlayOpen).toBe(true);
  await screenshot(page, 'conditional-format-rule-manager');
  await click(page, 'CloseButton');
});

test('filters two columns from header buttons and retains manual row hiding', async ({ page }) => {
  // The sample has its filter header in worksheet row 5. This exercises the canvas hit test, not a command hook.
  await page.mouse.click(291, 359);
  await choose(page, 'Filtermode', 1);
  await text(page, 'Filterfirstvalue', 'Americas');
  await screenshot(page, 'column-filter-editor');
  await click(page, 'FilterApply');
  await expect.poll(async () => (await state(page)).filteredRows).toBe(8);
  await page.mouse.click(371, 359);
  await choose(page, 'Filtermode', 1); await choose(page, 'Filterfirstoperator', 2);
  await text(page, 'Filterfirstvalue', '180'); await click(page, 'FilterApply');
  await expect.poll(async () => (await state(page)).filterColumns).toBe(2);
  await expect.poll(async () => (await state(page)).filteredRows).toBe(10);
  await select(page, 'D15'); await click(page, 'RibbonTabView'); await click(page, 'Command-hide-rows');
  await expect.poll(async () => (await state(page)).manuallyHiddenRows).toBe(1);
  await click(page, 'RibbonTabData'); await click(page, 'Command-clear-filter');
  await expect.poll(async () => (await state(page)).filteredRows).toBe(0);
  expect((await state(page)).manuallyHiddenRows).toBe(1);
});

test('sorts by region and descending units with an ordered level editor', async ({ page }) => {
  await select(page, 'B5:I17'); await click(page, 'RibbonTabData'); await click(page, 'Command-custom-sort');
  await choose(page, 'SortColumn0', 1);
  await click(page, 'SortAddLevel'); await choose(page, 'SortColumn1', 2); await choose(page, 'SortDirection1', 1);
  await screenshot(page, 'custom-sort-level-editor'); await click(page, 'PrimaryButton');
  await expect.poll(async () => (await state(page)).sortLevels).toBe(2);
  await select(page, 'C6'); await expect.poll(async () => (await state(page)).value).toBe('Americas');
  await select(page, 'D6'); await expect.poll(async () => (await state(page)).value).toBe('230');
  await click(page, 'Quick-undo'); await select(page, 'D6'); await expect.poll(async () => (await state(page)).value).toBe('126');
});

test('deletes selected rows and restores data and formulas through undo redo', async ({ page }) => {
  await select(page, 'D6:D7');
  // Delete commands are reached through the cell context menu on small ribbon viewports.
  await page.mouse.click(345, 384, { button: 'right' });
  await page.keyboard.press('Escape');
  await click(page, 'Command-delete-row');
  await select(page, 'D6'); await expect.poll(async () => (await state(page)).value).toBe('158');
  await select(page, 'F6'); await expect.poll(async () => (await state(page)).value).toBe('197500');
  await click(page, 'Quick-undo'); await select(page, 'D6'); await expect.poll(async () => (await state(page)).value).toBe('126');
  await click(page, 'Quick-redo'); await select(page, 'D6'); await expect.poll(async () => (await state(page)).value).toBe('158');
});
