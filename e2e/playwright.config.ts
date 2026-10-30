import { defineConfig, devices } from '@playwright/test';

// The Chrome installed on this computer, when PHOTOSENSE_E2E_CHROME is set, instead of Playwright's own download.
const chrome = process.env.PHOTOSENSE_E2E_CHROME ? { channel: 'chrome' } : {};

export default defineConfig({
  testDir: './tests',
  timeout: 90_000,
  expect: { timeout: 15_000 },
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: process.env.CI ? 2 : undefined,
  reporter: process.env.CI ? [['github'], ['list'], ['html', { open: 'never' }]] : [['list'], ['html', { open: 'never' }]],
  use: { trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  projects: [
    // On a computer, in the browser the page is opened in there: every test.
    { name: 'computer', use: { ...devices['Desktop Chrome'], ...chrome, viewport: { width: 1440, height: 900 } } },
    // At a phone's size, in the engine the iPhone app's web view is: the tests marked @phone.
    { name: 'phone', grep: /@phone/, use: { ...devices['iPhone 13'] } },
  ],
});
