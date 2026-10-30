import { expect, message, test, type Photos, type Service } from '../fixtures/app';
import { menu, question } from '../fixtures/screens';
import type { Page } from '@playwright/test';

async function oneRemoved(photos: Photos, service: Service, page: Page) {
  photos.picture('IMG_0001.JPG', { seed: 1 });
  photos.copy('IMG_0001.JPG', 'Backup/IMG_0001.JPG');
  await service.scan(photos.root);
  await page.goto('/');
  await page.getByRole('button', { name: 'Delete all duplicates' }).click();
  await question(page).getByRole('button', { name: 'Delete 1 file' }).click();
  await expect(message(page, /^Moved 1 duplicate/)).toBeVisible();
  expect(photos.files('_PhotoSense_Removed')).toHaveLength(1);
}

test.describe('deleting permanently', () => {
  test('what was removed is erased only after a warning that lists it and a second question', async ({ page, photos, service }) => {
    await oneRemoved(photos, service, page);
    await menu(page, 'Delete permanently');
    const warning = page.getByRole('alertdialog', { name: 'Delete removed files permanently?' });
    await expect(warning).toContainText(photos.path('_PhotoSense_Removed'));
    await expect(warning).toContainText('1 file');
    await warning.getByRole('button', { name: 'Delete permanently' }).click();

    const sure = page.getByRole('alertdialog', { name: 'Are you sure?' });
    await expect(sure).toBeVisible();
    expect(photos.files('_PhotoSense_Removed')).toHaveLength(1);
    await sure.getByRole('button', { name: 'Delete permanently' }).click();
    await expect(message(page, /^Deleted 1 file \(\d+ KB\) permanently\.$/)).toBeVisible();
    expect(photos.files('_PhotoSense_Removed')).toEqual([]);
    expect(photos.files()).toHaveLength(1);
  });

  test('the second question can be turned off as it is answered, and on again in Settings', async ({ page, photos, service }) => {
    await oneRemoved(photos, service, page);
    await menu(page, 'Delete permanently');
    await page.getByRole('alertdialog', { name: 'Delete removed files permanently?' }).getByRole('button', { name: 'Delete permanently' }).click();
    const sure = page.getByRole('alertdialog', { name: 'Are you sure?' });
    await sure.getByText('Do not ask me this second time again').click();
    await sure.getByRole('button', { name: 'Delete permanently' }).click();
    await expect(message(page, /^Deleted 1 file/)).toBeVisible();

    // Another copy removed is erased after the warning alone.
    const kept = photos.files()[0];
    photos.copy(kept, 'Second backup/IMG_0001.JPG');
    await service.scan(photos.root);
    await page.reload();
    await page.getByRole('button', { name: 'Delete all duplicates' }).click();
    await question(page).getByRole('button', { name: 'Delete 1 file' }).click();
    await menu(page, 'Delete permanently');
    await page.getByRole('alertdialog', { name: 'Delete removed files permanently?' }).getByRole('button', { name: 'Delete permanently' }).click();
    await expect(message(page, /^Deleted 1 file/).last()).toBeVisible();
    await expect(page.getByRole('alertdialog', { name: 'Are you sure?' })).toBeHidden();
    expect(photos.files()).toHaveLength(1);

    await menu(page, 'Settings');
    const settings = page.getByRole('dialog', { name: 'Settings' });
    const askTwice = settings.getByRole('switch', { name: 'Ask a second time before deleting permanently' });
    await expect(askTwice).not.toBeChecked();
    await settings.getByText('Ask a second time before deleting permanently').click();
    await expect(askTwice).toBeChecked();
    // Kept in this browser.
    await page.reload();
    await menu(page, 'Settings');
    await expect(page.getByRole('dialog', { name: 'Settings' }).getByRole('switch', { name: 'Ask a second time before deleting permanently' })).toBeChecked();
  });

  test('putting the second question away deletes nothing, and asks again next time', async ({ page, photos, service }) => {
    await oneRemoved(photos, service, page);
    for (let i = 0; i < 2; i++) {
      await menu(page, 'Delete permanently');
      await page.getByRole('alertdialog', { name: 'Delete removed files permanently?' }).getByRole('button', { name: 'Delete permanently' }).click();
      const sure = page.getByRole('alertdialog', { name: 'Are you sure?' });
      await expect(sure).toContainText('1 file (23 KB) will be erased for good.');
      await sure.getByText('Do not ask me this second time again').click();
      await sure.getByRole('button', { name: 'Cancel' }).click();
      await expect(sure).toBeHidden();
    }
    expect(photos.files('_PhotoSense_Removed')).toHaveLength(1);
  });

  test('with nothing removed, nothing can be deleted', async ({ page, photos, service }) => {
    photos.picture('IMG_0001.JPG', { seed: 1 });
    await service.scan(photos.root);
    await page.goto('/');
    await menu(page, 'Delete permanently');
    const warning = page.getByRole('alertdialog', { name: 'Delete removed files permanently?' });
    await expect(warning.getByRole('button', { name: 'Delete permanently' })).toBeDisabled();
    await warning.getByRole('button', { name: 'Cancel' }).click();
    await expect(warning).toBeHidden();
  });
});
