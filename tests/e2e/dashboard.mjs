// End-to-end flow against the Compose stack: sign-in page, the Hangfire dashboard behind it,
// a dashboard action that lands in the audit trail, and two connected workers.
// Usage: BASE_URL=http://localhost:8080 node tests/e2e/dashboard.mjs
import { chromium } from 'playwright';
import { mkdirSync, writeFileSync } from 'node:fs';

const BASE = process.env.BASE_URL ?? 'http://localhost:8080';
const ADMIN = { email: process.env.ADMIN_EMAIL ?? 'admin@example.com', password: process.env.ADMIN_PASSWORD ?? 'admin-password-local' };
const OUT = new URL('./output/', import.meta.url).pathname;
mkdirSync(OUT, { recursive: true });

let failed = false;
const step = async (name, fn) => {
  try { await fn(); console.log(`ok   ${name}`); }
  catch (error) { failed = true; console.log(`FAIL ${name}\n     ${error.message}`); }
};
const expect = (condition, message) => { if (!condition) throw new Error(message); };

const api = async (path, { token, ...init } = {}) => {
  const response = await fetch(`${BASE}/api${path}`, { ...init, headers: { ...(init.headers ?? {}), ...(token ? { Authorization: `Bearer ${token}` } : {}) } });
  const text = await response.text();
  return { status: response.status, body: text ? JSON.parse(text) : null };
};

const login = await api('/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(ADMIN) });
const token = login.body?.accessToken;

const browser = await chromium.launch(process.env.CHROMIUM_PATH ? { executablePath: process.env.CHROMIUM_PATH } : {});
const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
const shot = (name) => page.screenshot({ path: `${OUT}${name}.png`, fullPage: false });

await step('the dashboard sends strangers to the sign-in page', async () => {
  await page.goto(`${BASE}/hangfire`);
  expect(new URL(page.url()).pathname === '/login', `landed on ${page.url()}`);
  await shot('login');
});

await step('a wrong password is refused', async () => {
  await page.fill('#email', ADMIN.email);
  await page.fill('#password', 'not-the-password');
  await page.click('button[type=submit]');
  await page.getByRole('alert').filter({ hasText: 'Email or password is incorrect.' }).waitFor();
  await shot('login-error');
});

await step('an admin signs in and sees the dashboard', async () => {
  await page.fill('#password', ADMIN.password);
  await page.click('button[type=submit]');
  await page.waitForURL(/\/hangfire/);
  await page.getByText('Realtime graph').or(page.getByText('Succeeded')).first().waitFor();
  await shot('dashboard');
});

await step('an uploaded file is imported by a worker', async () => {
  const rows = ['order_no,ordered_at,customer_email,sku,quantity,unit_price'];
  for (let i = 1; i <= 2000; i++) rows.push(`E2E-${Date.now()}-${i},2026-10-${String(1 + (i % 28)).padStart(2, '0')},c${i % 40}@example.com,SKU-${i % 6},${1 + (i % 3)},${(i % 50) + 4.99}`);
  const form = new FormData();
  form.append('file', new Blob([rows.join('\n')], { type: 'text/csv' }), 'october-orders.csv');
  const upload = await api('/imports', { method: 'POST', body: form, token });
  expect(upload.status === 202, `upload answered ${upload.status}`);
  const id = upload.body.run.id;
  for (let i = 0; i < 60; i++) {
    const run = await api(`/jobs/${id}`, { token });
    if (run.body.status === 'Succeeded') return;
    if (run.body.status === 'Failed') throw new Error(run.body.error);
    await new Promise((r) => setTimeout(r, 500));
  }
  throw new Error('the import did not finish in time');
});

await step('both workers are connected', async () => {
  await page.goto(`${BASE}/hangfire/servers`);
  await page.locator('table tbody tr').first().waitFor();
  const servers = await page.locator('table tbody tr').count();
  expect(servers >= 2, `saw ${servers} server(s)`);
  await shot('servers');
});

await step('succeeded jobs are listed with readable names', async () => {
  await page.goto(`${BASE}/hangfire/jobs/succeeded`);
  await page.getByText('Import order file').first().waitFor();
  await shot('succeeded');
});

await step('triggering a recurring job from the dashboard is audited', async () => {
  await page.goto(`${BASE}/hangfire/recurring`);
  await page.getByText('hourly-cleanup').first().waitFor();
  await shot('recurring');
  await page.locator('input[type=checkbox][value="hourly-cleanup"]').check();
  await page.getByRole('button', { name: /Trigger now/ }).click();
  await page.waitForLoadState('networkidle');
  for (let i = 0; i < 20; i++) {
    const audit = await api('/audit?action=dashboard.action', { token });
    if (audit.body.items.some((e) => e.details?.includes('/recurring/trigger') && e.details.includes('hourly-cleanup'))) return;
    await new Promise((r) => setTimeout(r, 500));
  }
  throw new Error('no audit entry for the dashboard action');
});

await step('the API reference loads', async () => {
  await page.goto(`${BASE}/scalar`);
  await page.getByText('Background Jobs').first().waitFor();
  await shot('api-reference');
});

writeFileSync(`${OUT}result.txt`, failed ? 'failed\n' : 'passed\n');
await browser.close();
console.log(failed ? '\nsome steps failed' : '\nall steps passed');
process.exit(failed ? 1 : 0);
