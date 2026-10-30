import { expect, message, test, type Photos } from '../fixtures/app';
import { PLACES } from '../fixtures/media';
import { openOrganize, photoFiles, preview, suggestions, tile } from '../fixtures/screens';
import type { Page } from '@playwright/test';

// Two pictures taken in Butte in 2022, one in Anaconda in 2023, and one that says nowhere.
function trip(photos: Photos) {
  photos.picture('Trips/IMG_0001.JPG', { seed: 1, at: PLACES.butte, taken: new Date(2022, 6, 4, 10, 0, 0) });
  photos.picture('Trips/IMG_0002.JPG', { seed: 2, at: PLACES.butte, taken: new Date(2022, 6, 5, 11, 0, 0) });
  photos.picture('Trips/IMG_0003.JPG', { seed: 3, at: PLACES.anaconda, taken: new Date(2023, 1, 2, 9, 0, 0) });
  photos.picture('Trips/IMG_0004.JPG', { seed: 4, taken: new Date(2023, 1, 3, 9, 0, 0) });
}

const card = (page: Page, name: string) => suggestions(page).getByRole('button', { name: `Show the files for ${name}` });

async function undoLatest(page: Page) {
  await suggestions(page).getByRole('button', { name: /^Recently moved/ }).click();
  await suggestions(page).getByRole('button', { name: /^Undo/ }).first().click();
}

test.describe('Organize: suggested folders', () => {
  test('pictures are suggested folders by where they were taken, split by year, and those with no place are set apart', async ({ page, photos }) => {
    trip(photos);
    await openOrganize(page, photos.root);
    await expect(card(page, 'Butte')).toContainText('2 files');
    await expect(card(page, 'Anaconda')).toContainText('1 file');
    await expect(suggestions(page)).toContainText('No location · 1 file');
    await expect(suggestions(page).getByRole('group', { name: 'Map of the suggested folders' }).getByRole('button', { name: /^Butte · 2 files$/ })).toBeVisible();

    // Showing a folder's files narrows the gallery to them.
    await card(page, 'Butte').click();
    await expect(tile(page, 'IMG_0001.JPG')).toBeVisible();
    await expect(tile(page, 'IMG_0003.JPG')).toBeHidden();
    await suggestions(page).getByRole('button', { name: 'Show the files with no location' }).click();
    await expect(tile(page, 'IMG_0004.JPG')).toBeVisible();
    await expect(tile(page, 'IMG_0001.JPG')).toBeHidden();
  });

  test('a suggested folder is previewed and moved, and Undo in Recently moved puts every file back', async ({ page, photos }) => {
    trip(photos);
    await openOrganize(page, photos.root);
    await suggestions(page).getByRole('button', { name: 'Preview Butte' }).click();
    await expect(preview(page)).toContainText('2 files will move');
    await expect(preview(page)).toContainText(photos.path('Butte') + (process.platform === 'win32' ? '\\' : '/'));
    await preview(page).getByRole('button', { name: 'Move 2 files' }).click();
    await expect(preview(page)).toBeHidden();
    await expect(message(page, /^Moved 2 files/)).toBeVisible();
    expect(photoFiles(photos)).toEqual(['Butte/2022/IMG_0001.JPG', 'Butte/2022/IMG_0002.JPG', 'Trips/IMG_0003.JPG', 'Trips/IMG_0004.JPG']);
    await expect(card(page, 'Butte')).toBeHidden();

    await undoLatest(page);
    await expect(message(page, /^Moved 2 files back to where they were/)).toBeVisible();
    expect(photoFiles(photos)).toEqual(['Trips/IMG_0001.JPG', 'Trips/IMG_0002.JPG', 'Trips/IMG_0003.JPG', 'Trips/IMG_0004.JPG']);
  });

  test('grouped by date, the folders are years, and then months', async ({ page, photos }) => {
    trip(photos);
    await openOrganize(page, photos.root);
    await suggestions(page).getByRole('group', { name: 'Group by' }).getByRole('button', { name: 'Date' }).click();
    await expect(card(page, '2022')).toContainText('2 files');
    await expect(card(page, '2023')).toContainText('2 files');
    await suggestions(page).getByRole('button', { name: 'Preview 2023' }).click();
    await preview(page).getByRole('button', { name: 'Move 2 files' }).click();
    await expect(message(page, /^Moved 2 files/)).toBeVisible();
    expect(photoFiles(photos).filter(f => f.startsWith('2023/'))).toHaveLength(2);
  });

  test('a file left out of the preview stays where it is, and the folder can be renamed, with \\ for a folder inside it', async ({ page, photos }) => {
    trip(photos);
    await openOrganize(page, photos.root);
    await suggestions(page).getByRole('button', { name: 'Preview Butte' }).click();
    await preview(page).getByRole('list', { name: 'Files in this folder' }).getByRole('button', { name: /IMG_0002\.JPG/ }).click();
    await expect(preview(page)).toContainText('1 of 2 files will move');
    const name = preview(page).getByRole('textbox', { name: /^Folder name/ });
    await name.fill('Montana\\Butte trip');
    await preview(page).getByRole('button', { name: 'Move 1 file' }).click();
    await expect(message(page, /^Moved 1 file/)).toBeVisible();
    expect(photoFiles(photos)).toContain('Montana/Butte trip/2022/IMG_0001.JPG');
    expect(photoFiles(photos)).toContain('Trips/IMG_0002.JPG');
  });

  test('copying leaves the files where they were, and Undo takes the copies away', async ({ page, photos }) => {
    trip(photos);
    await openOrganize(page, photos.root);
    await suggestions(page).getByRole('button', { name: 'Preview Anaconda' }).click();
    await preview(page).getByRole('group', { name: 'Move or copy' }).getByRole('button', { name: 'Copy' }).click();
    await preview(page).getByRole('button', { name: 'Copy 1 file' }).click();
    await expect(message(page, /^Copied 1 file/)).toBeVisible();
    expect(photoFiles(photos)).toEqual(['Anaconda/2023/IMG_0003.JPG', 'Trips/IMG_0001.JPG', 'Trips/IMG_0002.JPG', 'Trips/IMG_0003.JPG', 'Trips/IMG_0004.JPG']);

    await undoLatest(page);
    await expect(message(page, /^Removed the 1 copy from/)).toBeVisible();
    expect(photoFiles(photos)).toEqual(['Trips/IMG_0001.JPG', 'Trips/IMG_0002.JPG', 'Trips/IMG_0003.JPG', 'Trips/IMG_0004.JPG']);
  });

  test('a file arriving under a name taken there gets a number, and nothing is replaced', async ({ page, photos }) => {
    trip(photos);
    photos.picture('Butte/2022/IMG_0001.JPG', { seed: 99 });
    const there = photos.bytes('Butte/2022/IMG_0001.JPG');
    await openOrganize(page, photos.root);
    await suggestions(page).getByRole('button', { name: 'Preview Butte' }).click();
    await preview(page).getByRole('button', { name: /^Move \d files?$/ }).click();
    await expect(message(page, /renamed so nothing was replaced/)).toBeVisible();
    expect(photoFiles(photos)).toContain('Butte/2022/IMG_0001 (1).JPG');
    expect(photos.bytes('Butte/2022/IMG_0001.JPG').equals(there)).toBe(true);
  });

  test('a Live Photo\'s video and edit file move with its picture', async ({ page, photos }) => {
    photos.livePhoto('Trips/IMG_0005', { seed: 5, at: PLACES.missoula });
    photos.sidecar('Trips/IMG_0005.AAE');
    await openOrganize(page, photos.root);
    await suggestions(page).getByRole('button', { name: /^Preview Missoula/ }).click();
    await preview(page).getByRole('button', { name: /^Move \d files?$/ }).click();
    await expect(message(page, /^Moved/)).toBeVisible();
    expect(photoFiles(photos).filter(f => f.startsWith('Trips/'))).toEqual([]);
    expect(photoFiles(photos).map(f => f.split('/').pop())).toEqual(['IMG_0005.AAE', 'IMG_0005.JPG', 'IMG_0005.MOV']);
  });
});
