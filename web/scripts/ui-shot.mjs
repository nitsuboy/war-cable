#!/usr/bin/env node
/**
 * Visual smoke test: drives two headless browsers through the real UI and
 * writes screenshots to /tmp/opencode/duo-ui.
 *
 *   cd web && npm run ui-shot
 */
import { mkdirSync } from 'node:fs';
import puppeteer from 'puppeteer-core';

const URL = process.env.UI_URL ?? 'http://localhost:5173';
const OUT = process.env.UI_OUT ?? '/tmp/opencode/duo-ui';
const CHROME = process.env.CHROME_PATH ?? '/usr/bin/chromium';

mkdirSync(OUT, { recursive: true });

const browser = await puppeteer.launch({
  executablePath: CHROME,
  headless: true,
  args: ['--no-sandbox', '--disable-dev-shm-usage', '--font-render-hinting=none'],
});

async function openPage(label) {
  // A separate browser context gives each player its own localStorage identity.
  const context = await browser.createBrowserContext();
  const page = await context.newPage();
  await page.setViewport({ width: 1280, height: 900, deviceScaleFactor: 1 });
  page.on('pageerror', (error) => console.error(`[${label}] pageerror:`, error.message));
  page.on('console', (message) => {
    if (message.type() === 'error') {
      console.error(`[${label}] console:`, message.text());
    }
  });
  await page.goto(URL, { waitUntil: 'networkidle0' });
  await page.waitForSelector('.lobby', { timeout: 15000 });
  return page;
}

const shot = async (page, name) => {
  await page.screenshot({ path: `${OUT}/${name}.png` });
  console.log(`saved ${name}.png`);
};

try {
  const host = await openPage('host');
  await shot(host, '01-lobby');

  await host.click('.lobby-actions .btn.ghost');
  await host.waitForSelector('.room-code', { timeout: 10000 });
  const code = (await host.$eval('.room-code', (element) => element.textContent ?? '')).trim();
  console.log(`room ${code}`);
  await shot(host, '02-waiting');

  const guest = await openPage('guest');
  await guest.type('#room-code', code);
  await guest.click('.join-form .btn.gold');

  await host.waitForSelector('.table .hand .card', { timeout: 10000 });
  await guest.waitForSelector('.table .hand .card', { timeout: 10000 });
  await new Promise((resolve) => setTimeout(resolve, 400));
  await shot(host, '03-table-host');
  await shot(guest, '04-table-guest');

  // Narrow viewport for the mobile layout (all nine cards still in hand).
  await host.setViewport({ width: 390, height: 844, deviceScaleFactor: 2 });
  await new Promise((resolve) => setTimeout(resolve, 300));
  await shot(host, '05-mobile');
  await host.setViewport({ width: 1280, height: 900, deviceScaleFactor: 1 });
  await new Promise((resolve) => setTimeout(resolve, 300));

  // Host picks a 6, guest picks a 0: guest wins the round.
  const hostCards = await host.$$('.hand .card');
  const guestCards = await guest.$$('.hand .card');
  await hostCards[7].click();
  await guestCards[1].click();

  await host.waitForSelector('.overlay .reveal', { timeout: 10000 });
  await shot(host, '06-reveal-host');
  await shot(guest, '07-reveal-guest');

  // Let the overlay clear and grab the next round.
  await new Promise((resolve) => setTimeout(resolve, 2800));
  await shot(host, '08-round-two');

  await browser.close();
  console.log('UI smoke test passed.');
} catch (error) {
  console.error(error);
  await browser.close();
  process.exit(1);
}
