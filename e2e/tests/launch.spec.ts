import { test, expect } from '../fixtures/app';

test.use({ launch: true });

test('the launch comes before everything else when the app first loads, stays, and a reload does not play it again @phone', async ({ page }) => {
  const launch = page.getByRole('img', { name: 'trovolve. Your memories. Evolved.' });
  await page.goto('/');
  await expect(launch).toBeVisible();
  // It stays for LAUNCH_SECONDS (three minutes for now), far longer than a test waits.
  await page.waitForTimeout(3_000);
  await expect(launch).toBeVisible();

  await page.reload();
  // Never painted: hidden from the first moment, and gone once the page wakes.
  await expect(launch).toBeHidden({ timeout: 1 });
  await expect(page.locator('[data-launch]')).toHaveCount(0);
});
