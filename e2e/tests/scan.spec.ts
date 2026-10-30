import { expect, message, test } from '../fixtures/app';
import { menu } from '../fixtures/screens';

test.describe('scanning', () => {
  test('a first visit asks for a folder, scans it and shows the copies found @phone', async ({ page, photos }) => {
    photos.picture('Trips/IMG_0001.JPG', { seed: 1 });
    photos.copy('Trips/IMG_0001.JPG', 'Backup/IMG_0001.JPG');
    photos.picture('Trips/IMG_0002.JPG', { seed: 2 });
    await page.goto('/');
    await expect(page.getByRole('heading', { name: 'Find duplicate photos and videos' })).toBeVisible();
    await expect(page.getByText('Runs on this device. Nothing is uploaded.')).toBeVisible();

    await page.getByRole('textbox', { name: 'Root folder' }).fill(photos.root);
    await page.getByRole('button', { name: 'Scan' }).click();
    await expect(message(page, /^Scan finished: 1 duplicate group/)).toBeVisible({ timeout: 60_000 });
    await expect(page.getByRole('tab', { name: 'Duplicates 1' })).toBeVisible();
    await expect(page.getByText('3 files · scanned')).toBeVisible();
    // A reload shows the same results, and the folder is remembered for the next scan.
    await page.reload();
    await expect(page.getByRole('tab', { name: 'Duplicates 1' })).toBeVisible();
    await page.getByRole('button', { name: 'Change or rescan' }).click();
    await expect(page.getByRole('textbox', { name: 'Root folder' })).toHaveValue(photos.root);
  });

  test('a folder and its backup are scanned together, and copies between them are found', async ({ page, photos }) => {
    photos.picture('Main/IMG_0001.JPG', { seed: 1 });
    photos.picture('Main/IMG_0002.JPG', { seed: 2 });
    photos.copy('Main/IMG_0002.JPG', 'Elsewhere/Backup/IMG_0002.JPG');
    await page.goto('/');
    await page.getByRole('textbox', { name: 'Root folder' }).fill(photos.path('Main'));
    await page.getByRole('textbox', { name: /^Secondary folder/ }).fill(photos.path('Elsewhere'));
    await page.getByRole('button', { name: 'Scan' }).click();
    await expect(message(page, /^Scan finished/)).toBeVisible({ timeout: 60_000 });
    await expect(page.getByRole('region', { name: 'Groups' }).getByRole('listitem')).toHaveCount(1);
    await expect(page.getByRole('region', { name: 'Review' })).toContainText(photos.path('Elsewhere/Backup'));
  });

  test('without Recursive, the folders inside the folder are left out', async ({ page, photos }) => {
    photos.picture('IMG_0001.JPG', { seed: 1 });
    photos.copy('IMG_0001.JPG', 'Inside/IMG_0001.JPG');
    await page.goto('/');
    await page.getByRole('textbox', { name: 'Root folder' }).fill(photos.root);
    await page.getByRole('checkbox', { name: /^Recursive/ }).uncheck();
    await page.getByRole('button', { name: 'Scan' }).click();
    await expect(message(page, /^Scan finished/)).toBeVisible({ timeout: 60_000 });
    await expect(page.getByText('1 file · scanned')).toBeVisible();
    await expect(page.getByRole('tab', { name: 'Duplicates 0' })).toBeVisible();
  });

  test('a folder that is not there is said so, and nothing is scanned', async ({ page, photos }) => {
    await page.goto('/');
    await page.getByRole('textbox', { name: 'Root folder' }).fill(photos.path('Not there'));
    await page.getByRole('button', { name: 'Scan' }).click();
    await expect(page.getByText(/not found|is not there|does not exist/i).first()).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Find duplicate photos and videos' })).toBeVisible();
  });

  test('the folder picker starts from this computer\'s places, goes into folders and picks one', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('button', { name: 'Browse for the root folder' }).click();
    const picker = page.getByRole('dialog', { name: 'Choose the root folder' });
    await expect(picker.getByRole('navigation', { name: 'Start from' }).getByRole('button').first()).toBeVisible();
    await picker.getByRole('navigation', { name: 'Start from' }).getByRole('button').first().click();
    const here = await picker.getByLabel('Current folder').innerText();
    expect(here).not.toBe('Choose where to start');
    await picker.getByRole('button', { name: 'Use this folder' }).click();
    await expect(picker).toBeHidden();
    await expect(page.getByRole('textbox', { name: 'Root folder' })).toHaveValue(here);
    // Esc puts it away without choosing.
    await page.getByRole('button', { name: 'Browse for the secondary folder' }).click();
    await page.keyboard.press('Escape');
    await expect(page.getByRole('dialog', { name: 'Choose the secondary folder' })).toBeHidden();
    await expect(page.getByRole('textbox', { name: /^Secondary folder/ })).toHaveValue('');
  });

  test('scanning again finds a copy made since the last scan', async ({ page, photos, service }) => {
    photos.picture('IMG_0001.JPG', { seed: 1 });
    await service.scan(photos.root);
    await page.goto('/');
    await expect(page.getByRole('tab', { name: 'Duplicates 0' })).toBeVisible();
    photos.copy('IMG_0001.JPG', 'Copy/IMG_0001.JPG');
    await menu(page, 'Change folders or rescan');
    await page.getByRole('textbox', { name: 'Root folder' }).fill(photos.root);
    await page.getByRole('button', { name: 'Scan' }).click();
    await expect(message(page, /^Scan finished: 1 duplicate group/)).toBeVisible({ timeout: 60_000 });
    await expect(page.getByText('2 files · scanned')).toBeVisible();
  });
});
