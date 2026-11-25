import { test, expect } from '../fixtures/app';
import { LAUNCH_SECONDS } from '../../Trovolve.ReactUI/lib/launch';

test.use({ launch: true });

test('the launch comes before everything else when the app first loads, stays its time, goes, and a reload does not play it again @phone', async ({ page }) => {
  // The browser's clock is the test's, so the launch's time is whatever LAUNCH_SECONDS says.
  await page.clock.install();
  const launch = page.getByRole('img', { name: 'trovolve. Your memories. Evolved.' });
  await page.goto('/');
  await expect(launch).toBeVisible();
  await page.clock.fastForward(LAUNCH_SECONDS * 1000 - 1000);
  await expect(launch).toBeVisible();
  await page.clock.fastForward(1500);
  await expect(launch).toBeHidden();
  await expect(page.getByRole('heading', { name: 'Find duplicate photos and videos' })).toBeVisible();

  await page.reload();
  // Never painted: hidden from the first moment, and gone once the page wakes.
  await expect(launch).toBeHidden({ timeout: 1 });
  await expect(page.locator('[data-launch]')).toHaveCount(0);
});
