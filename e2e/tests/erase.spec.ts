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

test.describe('Move to Recycle Bin', () => {
  test('what was removed goes only after a question that lists it, saying a drive with no Recycle Bin erases it for good, and a second question', async ({ page, photos, service }) => {
    await oneRemoved(photos, service, page);
    await menu(page, 'Move to Recycle Bin…');
    const warning = page.getByRole('alertdialog', { name: 'Move removed files to the Recycle Bin?' });
    await expect(warning).toContainText(photos.path('_PhotoSense_Removed'));
    await expect(warning).toContainText('1 file');
    // The program is run here with no Recycle Bin, so the question says the file is erased for good, naming the folder.
    await expect(warning).toContainText(/1 file taking \d+ KB is erased for good\./);
    await expect(warning).toContainText('This folder is on a drive with no Recycle Bin');
    await warning.getByRole('button', { name: 'Move to Recycle Bin' }).click();

    const sure = page.getByRole('alertdialog', { name: 'Are you sure?' });
    await expect(sure).toBeVisible();
    expect(photos.files('_PhotoSense_Removed')).toHaveLength(1);
    await sure.getByRole('button', { name: 'Move to Recycle Bin' }).click();
    await expect(message(page, 'Erased 1 file for good, as their drive has no Recycle Bin.')).toBeVisible();
    expect(photos.files('_PhotoSense_Removed')).toEqual([]);
    expect(photos.files()).toHaveLength(1);
  });

  test('the second question can be turned off as it is answered, and on again in Settings', async ({ page, photos, service }) => {
    await oneRemoved(photos, service, page);
    await menu(page, 'Move to Recycle Bin…');
    await page.getByRole('alertdialog', { name: 'Move removed files to the Recycle Bin?' }).getByRole('button', { name: 'Move to Recycle Bin' }).click();
    const sure = page.getByRole('alertdialog', { name: 'Are you sure?' });
    await sure.getByText('Do not ask me this second time again').click();
    await sure.getByRole('button', { name: 'Move to Recycle Bin' }).click();
    await expect(message(page, 'Erased 1 file for good, as their drive has no Recycle Bin.')).toBeVisible();

    // Another copy removed is erased after the warning alone.
    const kept = photos.files()[0];
    photos.copy(kept, 'Second backup/IMG_0001.JPG');
    await service.scan(photos.root);
    await page.reload();
    await page.getByRole('button', { name: 'Delete all duplicates' }).click();
    await question(page).getByRole('button', { name: 'Delete 1 file' }).click();
    await menu(page, 'Move to Recycle Bin…');
    await page.getByRole('alertdialog', { name: 'Move removed files to the Recycle Bin?' }).getByRole('button', { name: 'Move to Recycle Bin' }).click();
    await expect(message(page, 'Erased 1 file for good, as their drive has no Recycle Bin.').last()).toBeVisible();
    await expect(page.getByRole('alertdialog', { name: 'Are you sure?' })).toBeHidden();
    expect(photos.files()).toHaveLength(1);

    await menu(page, 'Settings');
    const settings = page.getByRole('dialog', { name: 'Settings' });
    const askTwice = settings.getByRole('switch', { name: 'Ask a second time before moving to the Recycle Bin' });
    await expect(askTwice).not.toBeChecked();
    await settings.getByText('Ask a second time before moving to the Recycle Bin').click();
    await expect(askTwice).toBeChecked();
    // Kept in this browser.
    await page.reload();
    await menu(page, 'Settings');
    await expect(page.getByRole('dialog', { name: 'Settings' }).getByRole('switch', { name: 'Ask a second time before moving to the Recycle Bin' })).toBeChecked();
  });

  test('putting the second question away deletes nothing, and asks again next time', async ({ page, photos, service }) => {
    await oneRemoved(photos, service, page);
    for (let i = 0; i < 2; i++) {
      await menu(page, 'Move to Recycle Bin…');
      await page.getByRole('alertdialog', { name: 'Move removed files to the Recycle Bin?' }).getByRole('button', { name: 'Move to Recycle Bin' }).click();
      const sure = page.getByRole('alertdialog', { name: 'Are you sure?' });
      await expect(sure).toContainText('1 file taking 23 KB is erased for good.');
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
    await menu(page, 'Move to Recycle Bin…');
    const warning = page.getByRole('alertdialog', { name: 'Move removed files to the Recycle Bin?' });
    await expect(warning.getByRole('button', { name: 'Move to Recycle Bin' })).toBeDisabled();
    await warning.getByRole('button', { name: 'Cancel' }).click();
    await expect(warning).toBeHidden();
  });
});
