import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '../fixtures/app';
import { PLACES } from '../fixtures/media';
import { menu, openOrganize, suggestions } from '../fixtures/screens';
import type { Page } from '@playwright/test';

// Each screen, in both themes, against the WCAG A and AA rules a checker can find for itself: contrast, names,
// roles, landmarks. A person still has to try what no checker can, as with the keyboard and a screen reader.
async function accessible(page: Page, what: string) {
  for (const theme of ['dark', 'light'] as const) {
    await page.evaluate(light => document.documentElement.classList.toggle('light', light), theme === 'light');
    // The theme's colours settle before they are measured.
    await page.waitForTimeout(400);
    const found = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
    const said = found.violations.map(v => `${v.id} (${v.impact}): ${v.help}\n${v.nodes.slice(0, 3).map(n => `  ${n.target.join(' ')}: ${n.failureSummary?.split('\n')[1]?.trim() ?? ''}`).join('\n')}`);
    expect(said, `${what}, ${theme} theme`).toEqual([]);
  }
}

test.describe('every screen can be used with assistive technology and read in either theme', () => {
  test('the scan screen @phone', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByRole('heading', { name: 'Find duplicate photos and videos' })).toBeVisible();
    await accessible(page, 'The scan screen');
  });

  test('the duplicates found, and the window comparing two of them', async ({ page, photos, service }) => {
    photos.picture('IMG_0001.JPG', { seed: 1, at: PLACES.butte });
    photos.copy('IMG_0001.JPG', 'Backup/IMG_0001.JPG');
    await service.scan(photos.root);
    await page.goto('/');
    await expect(page.getByRole('region', { name: 'Review' })).toBeVisible();
    await accessible(page, 'Clean up');
    await page.getByRole('region', { name: 'Review' }).getByRole('button', { name: 'Compare IMG_0001.JPG' }).first().click();
    await expect(page.getByRole('dialog', { name: /^Compare/ })).toBeVisible();
    await accessible(page, 'The compare window');
  });

  test('Organize, its preview and the folder dialog', async ({ page, photos }) => {
    photos.picture('Trips/IMG_0001.JPG', { seed: 1, at: PLACES.butte });
    photos.picture('Trips/IMG_0002.JPG', { seed: 2 });
    await openOrganize(page, photos.root);
    await accessible(page, 'Organize');
    await suggestions(page).getByRole('button', { name: 'Preview Butte' }).click();
    await accessible(page, 'The preview');
    await page.keyboard.press('Escape');
    await page.getByRole('list', { name: 'Files' }).getByRole('button').first().click();
    await page.getByRole('region', { name: 'Selection' }).getByRole('button', { name: 'New folder from these' }).click();
    await accessible(page, 'The folder dialog');
  });

  test('the menu, Settings, Help and About', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('button', { name: 'Menu' }).click();
    await accessible(page, 'The menu');
    await page.keyboard.press('Escape');
    await menu(page, 'Settings');
    await accessible(page, 'Settings');
    await page.keyboard.press('Escape');
    await menu(page, 'Help');
    await accessible(page, 'Help');
    await page.keyboard.press('Escape');
    await menu(page, 'About PhotoSense');
    await accessible(page, 'About');
  });
});
