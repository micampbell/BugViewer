import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { mkdtemp } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

// Run the StyleTests web project first. Playwright can be installed separately;
// PLAYWRIGHT_MODULE_PATH accepts the absolute path to its playwright-core package.
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE_PATH ?? 'playwright');
const browser = await chromium.launch({ headless: true,
    ...(process.platform === 'win32' ? { channel: 'msedge' } : {}),
    args: ['--enable-unsafe-webgpu', '--enable-unsafe-swiftshader'] });
const output = process.env.BUGVIEWER_SCREENSHOTS ?? await mkdtemp(path.join(os.tmpdir(), 'bugviewer-styles-'));
try {
    const page = await browser.newPage({ viewport: { width: 1100, height: 900 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto(process.env.BUGVIEWER_URL ?? 'http://127.0.0.1:5267');
    await page.waitForFunction(() => document.querySelector('#ready-count')?.dataset.count === '3');
    await page.locator('.bugviewer-step-controls').nth(2).waitFor();
    const viewer = page.locator('#default-viewer');
    await viewer.getByTitle('Open Options', { exact: true }).click();
    await viewer.getByRole('button', { name: 'Display', exact: true }).click();
    await viewer.getByText('Show back faces', { exact: true }).waitFor();
    await page.waitForTimeout(250); // Let Fluent's expansion/selection transitions finish before snapshots.

    const selectors = {
        presentation: '#default-viewer .bugviewer-scene-viewer',
        heading: '#default-viewer .bugviewer-scene-viewer-header h2',
        button: '#default-viewer .bugviewer-step-icon-button',
        range: '#default-viewer .bugviewer-step-controls input[type=range]',
        speed: '#default-viewer .bugviewer-step-speed-control select',
        stride: '#default-viewer .bugviewer-step-controls input[type=number]',
        label: '#default-viewer .bugviewer-step-speed-control',
        panel: '#default-viewer .bugviewer-options-popover',
        settingsLabel: '#default-viewer .bugviewer-options-popover label',
        fluentLabel: '#default-viewer .bugviewer-options-popover fluent-field > label',
        fluentSelect: '#default-viewer .bugviewer-options-popover button[slot=control]',
        colorInput: '#default-viewer .bugviewer-options-popover input[type=color]',
        plotButton: '#plot-viewer .bugviewer-step-icon-button',
        plotHeading: '#plot-viewer .bugviewer-scene-viewer-header h2'
    };
    async function styles(selectors) {
        return page.evaluate(selectors => Object.fromEntries(Object.entries(selectors).map(([name, selector]) => {
            const element = document.querySelector(selector);
            if (!element) throw new Error('Missing style-test element: ' + selector);
            const style = getComputedStyle(element);
            const properties = ['display', 'fontFamily', 'fontSize', 'fontWeight', 'lineHeight', 'letterSpacing',
                'textTransform', 'color', 'backgroundColor', 'padding', 'margin', 'borderWidth', 'minWidth', 'height', 'boxSizing'];
            return [name, Object.fromEntries(properties.map(property => [property, style[property]]))];
        })), selectors);
    }
    const baseline = await styles(selectors);
    async function shadowStyles() {
        return page.evaluate(() => {
            const rows = [];
            function visit(root) {
                for (const node of root.children ?? []) {
                    const style = getComputedStyle(node);
                    rows.push(Object.fromEntries(['display', 'fontSize', 'lineHeight', 'letterSpacing',
                        'color', 'backgroundColor', 'padding', 'margin', 'borderWidth'].map(key => [key, style[key]])));
                    if (node.shadowRoot) visit(node.shadowRoot);
                    visit(node);
                }
            }
            visit(document.querySelector('#default-viewer .bugviewer-options-popover'));
            return rows;
        });
    }
    const shadowBaseline = await shadowStyles();
    await viewer.screenshot({ path: path.join(output, 'default.png') });
    await page.evaluate(() => document.body.classList.add('host-conflicts'));
    await page.waitForTimeout(250);
    assert.equal(await page.locator('#host-button').evaluate(node => getComputedStyle(node).fontSize), '32px',
        'The host stylesheet must actually be active');
    const embedded = await styles(selectors);
    const differences = Object.entries(baseline).flatMap(([name, properties]) =>
        Object.entries(properties).filter(([property, value]) => embedded[name][property] !== value)
            .map(([property, value]) => `${name}.${property}: ${value} -> ${embedded[name][property]}`));
    assert.deepEqual(differences, [], 'Host CSS changed control styles');
    assert.deepEqual(await shadowStyles(), shadowBaseline, 'Host CSS changed Fluent controls or their shadow contents');
    await viewer.screenshot({ path: path.join(output, 'conflicting-host.png') });

    const themed = page.locator('#themed-viewer');
    await themed.getByTitle('Open Options', { exact: true }).click();
    assert.equal(await themed.locator('.bugviewer-options-popover').evaluate(node => getComputedStyle(node).width), '420px');
    assert.equal(await themed.locator('.custom-instance').evaluate(node => getComputedStyle(node).fontSize), '16px');
    assert.equal(await themed.locator('.bugviewer-step-icon-button').first().evaluate(node => getComputedStyle(node).height), '38px');
    assert.equal(await themed.locator('.bugviewer-step-controls').evaluate(node => getComputedStyle(node).gap), '10px');
    assert.equal(await themed.locator('.bugviewer-options-popover').evaluate(node => getComputedStyle(node).backgroundColor), 'rgb(234, 244, 255)');
    await themed.screenshot({ path: path.join(output, 'custom-theme.png') });

    // Native save controls share the same baseline, including text entry and headings.
    await page.evaluate(() => document.body.classList.remove('host-conflicts'));
    await viewer.locator('.bugviewer-scene-viewer > button[hidden]:not([data-scene-file-command])').evaluate(node => node.click());
    await viewer.locator('dialog[open]').waitFor();
    const dialogSelectors = { heading: '#default-viewer dialog h2', label: '#default-viewer dialog label',
        input: '#default-viewer dialog input', button: '#default-viewer dialog .bugviewer-scene-save-actions button' };
    const dialogBaseline = await styles(dialogSelectors);
    await page.evaluate(() => document.body.classList.add('host-conflicts'));
    assert.deepEqual(await styles(dialogSelectors), dialogBaseline, 'Host CSS changed the save dialog');
    await viewer.locator('dialog').getByRole('button', { name: 'Cancel', exact: true }).click();

    await viewer.getByTitle('Open Options', { exact: true }).click();
    await viewer.getByRole('button', { name: 'Next step', exact: true }).click();
    await page.waitForFunction(() => document.querySelector('#default-viewer .bugviewer-step-current')?.textContent.trim() === '2');
    assert.equal(await viewer.locator('.bugviewer-step-current').textContent(), '2');
    await viewer.locator('.bugviewer-step-current').click();
    const stepInput = viewer.getByRole('textbox', { name: 'Target step' });
    assert.equal(await stepInput.evaluate(node => getComputedStyle(node).fontSize), '14px');
    await stepInput.fill('3'); await stepInput.press('Enter');
    await viewer.getByRole('button', { name: 'Edit current step', exact: true }).waitFor();
    await page.waitForFunction(() => document.querySelector('#default-viewer .bugviewer-step-current')?.textContent.trim() === '3');
    assert.equal(await viewer.locator('.bugviewer-step-current').textContent(), '3');

    await page.evaluate(() => document.querySelectorAll('.fixture').forEach(node => node.style.width = '320px'));
    assert.ok(await viewer.locator('.bugviewer-step-controls').evaluate(node => node.scrollWidth <= node.clientWidth + 1),
        'Playback controls must wrap without overflowing a narrow viewer');
    assert.deepEqual(errors, [], 'Unexpected browser errors');
    console.log(`PASS: scoped styles resist host CSS; 3D/2D controls, editing, save dialog, narrow layout, and theme variables. Screenshots: ${output}`);
} finally { await browser.close(); }
