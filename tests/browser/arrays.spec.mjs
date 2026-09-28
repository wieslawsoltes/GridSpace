import { test, expect } from '@playwright/test';
import fs from 'node:fs/promises';
import { workbenchHeaderCoverage } from './png-probe.mjs';

const state = page => page.evaluate(() => globalThis.gridSpaceDiagnostics);
const failures = new WeakMap();
async function click(page, id) {
  await expect.poll(async () => !!(await state(page))?.controls?.[id]).toBe(true);
  const b = (await state(page)).controls[id];
  await page.mouse.click(b.x + b.width / 2, b.y + b.height / 2);
}
async function select(page, range) {
  await click(page, 'Namebox'); await page.keyboard.press('Control+A');
  await page.keyboard.type(range); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).selection).toBe(range);
}
async function edit(page, address, value) {
  await select(page, address); await page.keyboard.press('F2');
  await expect.poll(async () => (await state(page)).editing).toBe(true);
  await page.keyboard.press('Control+A'); await page.keyboard.type(value); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).editing).toBe(false);
}
async function value(page, address, expected) {
  await select(page, address); await expect.poll(async () => (await state(page)).value).toBe(expected);
}

test.beforeEach(async ({ page }) => {
  const errors = []; failures.set(page, errors); page.on('pageerror', e => errors.push(e.message));
  const url = new URL(process.env.GRIDSPACE_URL || 'http://127.0.0.1:4173/GridSpace/'); url.searchParams.set('test', '1');
  await page.goto(url.href, { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(() => globalThis.gridSpaceDiagnostics?.controls?.NewSheet, null, { timeout: 60000 });
  await expect.poll(async () => workbenchHeaderCoverage(await page.screenshot()), { timeout: 60000 }).toBeGreaterThan(.7);
  await click(page, 'NewSheet'); await expect.poll(async () => (await state(page)).sheets).toBe(4);
});
test.afterEach(async ({ page }, info) => {
  await fs.mkdir('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: `artifacts/screenshots/${info.title.replace(/[^a-z0-9]+/gi, '-').toLowerCase()}.png` });
  console.log('Array state:', JSON.stringify(await state(page)));
  expect(failures.get(page)).toEqual([]);
});

test('spills arrays with ghost formula protection and delta undo', async ({ page }) => {
  await edit(page, 'A1', '=SEQUENCE(4,3,10,2)');
  await value(page, 'C4', '32');
  expect((await state(page)).cells).toBe(1);
  expect((await state(page)).spillRange).toBe('A1:C4');
  expect((await state(page)).spillFollower).toBe(true);
  expect((await state(page)).controls.Formulabar.readOnly).toBe(true);
  expect((await state(page)).controls.Formulabar.text).toBe('=SEQUENCE(4,3,10,2)');
  await page.keyboard.press('F2'); await expect.poll(async () => (await state(page)).status).toContain('spilled array');
  expect((await state(page)).editing).toBe(false);
  await page.keyboard.press('Delete'); expect((await state(page)).cells).toBe(1);
  await edit(page, 'A1', '=SEQUENCE(2,2)'); await value(page, 'B2', '4'); await value(page, 'C4', '');
  await click(page, 'Quick-undo'); await value(page, 'C4', '32');
  await click(page, 'Quick-redo'); await value(page, 'B2', '4');
});

test('blocked spill recovers and downstream FILTER LET and references recalculate', async ({ page }) => {
  await edit(page, 'B2', 'blocked'); await edit(page, 'A1', '=SEQUENCE(4,2)');
  await value(page, 'A1', '#SPILL!'); await select(page, 'B2'); await page.keyboard.press('Delete');
  await value(page, 'B4', '8');
  await edit(page, 'D1', '=LET(data,A1#,FILTER(data,CHOOSECOLS(data,1)>3))');
  await value(page, 'E2', '8'); expect((await state(page)).spillRange).toBe('D1:E2');
  await edit(page, 'G1', '=SUM(D1#)'); await value(page, 'G1', '26');
  await edit(page, 'A1', '=SEQUENCE(3,2)'); await value(page, 'G1', '11');
  await value(page, 'E2', ''); await value(page, 'D1', '5');
});

test('selection scrolling and cell input retain sparse geometry indexes', async ({ page }) => {
  const baseline = (await state(page)).layoutRefreshes;
  for (const address of ['D50', 'A1', 'Z100', 'B3']) await select(page, address);
  await page.mouse.move(500, 400); await page.mouse.wheel(0, 360);
  await expect.poll(async () => (await state(page)).scrollY).toBeGreaterThan(0);
  await edit(page, 'A1', '7'); await edit(page, 'B1', '=A1*2');
  await value(page, 'B1', '14');
  expect((await state(page)).layoutRefreshes).toBe(baseline);
  await click(page, 'Quick-undo'); await value(page, 'B1', '');
  expect((await state(page)).layoutRefreshes).toBe(baseline);
  await click(page, 'RibbonTabView'); await click(page, 'Command-freeze-top');
  await expect.poll(async () => (await state(page)).layoutRefreshes).toBeGreaterThan(baseline);
});
