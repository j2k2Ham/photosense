import { expect, message, test, type Photos } from '../fixtures/app';
import { PLACES } from '../fixtures/media';
import { drag, gallery, openOrganize, photoFiles, preview, suggestions, tile } from '../fixtures/screens';
import type { Page } from '@playwright/test';

function some(photos: Photos) {
  photos.picture('Trips/IMG_0001.JPG', { seed: 1, at: PLACES.butte });
  photos.picture('Trips/IMG_0002.JPG', { seed: 2, at: PLACES.anaconda });
  photos.picture('Trips/IMG_0003.JPG', { seed: 3 });
}

const folderDialog = (page: Page) => page.getByRole('dialog', { name: /^Put \d+ files? in a folder$|^New folder$/ });
const yours = (page: Page, name: string) => suggestions(page).getByRole('button', { name: `Preview ${name}` });

async function newFolder(page: Page, name: string) {
  await page.getByRole('region', { name: 'Selection' }).getByRole('button', { name: 'New folder from these' }).click();
  await folderDialog(page).getByRole('textbox', { name: 'Name' }).fill(name);
  await folderDialog(page).getByRole('button', { name: 'Create folder' }).click();
  await expect(folderDialog(page)).toBeHidden();
}

test.describe('Organize: folders of your own', () => {
  test('selected files are put in a new folder of their own, previewed and moved there @phone', async ({ page, photos }) => {
    some(photos);
    await openOrganize(page, photos.root);
    await tile(page, 'IMG_0001.JPG').click();
    await tile(page, 'IMG_0003.JPG').click();
    await expect(page.getByRole('region', { name: 'Selection' })).toContainText('2 selected');
    await newFolder(page, 'Family\\Summer');
    await yours(page, 'Family\\Summer').click();
    await expect(preview(page)).toContainText('2 files will move');
    await preview(page).getByRole('button', { name: 'Move 2 files' }).click();
    await expect(message(page, /^Moved 2 files/)).toBeVisible();
    expect(photoFiles(photos)).toEqual(['Family/Summer/IMG_0001.JPG', 'Family/Summer/IMG_0003.JPG', 'Trips/IMG_0002.JPG']);
  });

  test('files are put in a folder that is already there, found by going into the folders inside the one organized', async ({ page, photos }) => {
    some(photos);
    photos.picture('Archive/Old trips/IMG_0100.JPG', { seed: 100 });
    await openOrganize(page, photos.root);
    await tile(page, 'IMG_0002.JPG').click();
    await page.getByRole('region', { name: 'Selection' }).getByRole('button', { name: 'New folder from these' }).click();
    const dialog = folderDialog(page);
    await dialog.getByRole('group', { name: 'Which folder' }).getByRole('button', { name: 'Folder already there' }).click();
    await dialog.getByRole('button', { name: 'Open Archive' }).click();
    await dialog.getByRole('button', { name: /^Old trips/ }).click();
    await dialog.getByRole('button', { name: 'Use Old trips' }).click();
    await expect(dialog).toBeHidden();
    await yours(page, 'Archive\\Old trips').click();
    await preview(page).getByRole('button', { name: 'Move 1 file' }).click();
    await expect(message(page, /^Moved 1 file/)).toBeVisible();
    expect(photoFiles(photos)).toContain('Archive/Old trips/IMG_0002.JPG');
  });

  test('a new folder can be made somewhere else than the folder organized', async ({ page, photos }) => {
    some(photos);
    photos.picture('Elsewhere/keep.JPG', { seed: 50 });
    await openOrganize(page, photos.root);
    await tile(page, 'IMG_0001.JPG').click();
    await page.getByRole('region', { name: 'Selection' }).getByRole('button', { name: 'New folder from these' }).click();
    const dialog = folderDialog(page);
    await dialog.getByRole('textbox', { name: 'Name' }).fill('Picked');
    await dialog.getByRole('button', { name: 'Change' }).click();
    const picker = page.getByRole('dialog', { name: 'Choose where the new folder is made' });
    await picker.getByRole('button', { name: /^Elsewhere/ }).click();
    await picker.getByRole('button', { name: 'Make it here' }).click();
    await expect(dialog).toContainText(photos.path('Elsewhere'));
    await dialog.getByRole('button', { name: 'Create folder' }).click();
    await yours(page, 'Picked').click();
    await preview(page).getByRole('button', { name: 'Move 1 file' }).click();
    await expect(message(page, /^Moved 1 file/)).toBeVisible();
    expect(photoFiles(photos)).toContain('Elsewhere/Picked/IMG_0001.JPG');
  });

  test('files dragged onto a folder of your own, or selected and added to it, go into it', async ({ page, photos }) => {
    some(photos);
    await openOrganize(page, photos.root);
    await tile(page, 'IMG_0001.JPG').click();
    await newFolder(page, 'Best');
    await drag(page, tile(page, 'IMG_0002.JPG'), suggestions(page).getByRole('button', { name: 'Show the files for Best' }));
    await expect(suggestions(page).getByRole('button', { name: 'Show the files for Best' })).toContainText('2 files');
    await tile(page, 'IMG_0003.JPG').click();
    await suggestions(page).getByRole('button', { name: 'Add 1 here' }).click();
    await yours(page, 'Best').click();
    await expect(preview(page)).toContainText('3 files will move');
    await preview(page).getByRole('button', { name: 'Move 3 files' }).click();
    await expect(message(page, /^Moved 3 files/)).toBeVisible();
    expect(photoFiles(photos)).toEqual(['Best/IMG_0001.JPG', 'Best/IMG_0002.JPG', 'Best/IMG_0003.JPG']);
    await expect(gallery(page).getByRole('listitem')).toHaveCount(3);
  });
});
