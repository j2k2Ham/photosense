import { expect, message, test, type Photos, type Service } from '../fixtures/app';
import { photoFiles, question } from '../fixtures/screens';
import type { Page } from '@playwright/test';

const groups = (page: Page) => page.getByRole('region', { name: 'Groups' });
const review = (page: Page) => page.getByRole('region', { name: 'Review' });

// Two pictures with a copy each in a backup folder, and one with no copy.
async function twoCopies(photos: Photos, service: Service, page: Page) {
  photos.picture('Trips/IMG_0001.JPG', { seed: 1 });
  photos.picture('Trips/IMG_0002.JPG', { seed: 2, width: 640, height: 480 });
  photos.picture('Trips/IMG_0003.JPG', { seed: 3 });
  photos.copy('Trips/IMG_0001.JPG', 'Trips/Backup/IMG_0001 (1).JPG');
  photos.copy('Trips/IMG_0002.JPG', 'Trips/Backup/IMG_0002 (1).JPG');
  await service.scan(photos.root);
  await page.goto('/');
  await expect(groups(page).getByRole('listitem')).toHaveCount(2);
}

test.describe('duplicates', () => {
  test('a copy is deleted into _PhotoSense_Removed after a question that names it and the original, and Undo puts it back @phone', async ({ page, photos, service }) => {
    await twoCopies(photos, service, page);
    await groups(page).getByRole('button', { name: /IMG_0001\.JPG/ }).click();
    await expect(review(page).getByRole('heading', { name: 'IMG_0001.JPG', level: 2 })).toBeVisible();
    await review(page).getByRole('button', { name: /^Delete this duplicate/ }).click();
    await expect(question(page)).toHaveAccessibleName('Delete IMG_0001 (1).JPG?');
    await expect(question(page)).toContainText(`The original, IMG_0001.JPG, stays in ${photos.path('Trips')}.`);
    await question(page).getByRole('button', { name: 'Delete 1 file' }).click();

    await expect(message(page, 'Moved IMG_0001 (1).JPG (23 KB) to _PhotoSense_Removed. IMG_0001.JPG stays where it is.')).toBeVisible();
    expect(photos.exists('_PhotoSense_Removed/Trips/Backup/IMG_0001 (1).JPG')).toBe(true);
    expect(photos.exists('Trips/Backup/IMG_0001 (1).JPG')).toBe(false);
    await expect(groups(page).getByRole('listitem')).toHaveCount(1);

    await page.getByRole('log', { name: 'Messages' }).getByRole('button', { name: 'Undo' }).click();
    await expect(message(page, 'Put IMG_0001 (1).JPG back where it was.')).toBeVisible();
    expect(photos.exists('Trips/Backup/IMG_0001 (1).JPG')).toBe(true);
    await expect(groups(page).getByRole('listitem')).toHaveCount(2);
  });

  test('a question put away with Cancel deletes nothing', async ({ page, photos, service }) => {
    await twoCopies(photos, service, page);
    await review(page).getByRole('button', { name: /^Delete this duplicate/ }).click();
    await question(page).getByRole('button', { name: 'Cancel' }).click();
    await expect(question(page)).toBeHidden();
    await review(page).getByRole('button', { name: /^Delete this duplicate/ }).click();
    await page.keyboard.press('Escape');
    await expect(question(page)).toBeHidden();
    expect(photos.files()).toHaveLength(5);
  });

  test('Delete all duplicates deletes every copy and keeps every original, and a copy marked keep is left alone', async ({ page, photos, service }) => {
    await twoCopies(photos, service, page);
    await groups(page).getByRole('button', { name: /IMG_0002\.JPG/ }).click();
    await review(page).getByRole('button', { name: 'Keep this copy' }).click();
    await expect(message(page, 'Keeping IMG_0002 (1).JPG. Bulk deletion will skip it.')).toBeVisible();

    await page.getByRole('button', { name: 'Delete all duplicates' }).click();
    await expect(question(page)).toHaveAccessibleName('Delete all duplicates?');
    await question(page).getByRole('button', { name: 'Delete 1 file' }).click();
    await expect(message(page, 'Moved 1 duplicate (23 KB) to _PhotoSense_Removed')).toBeVisible();
    expect(photoFiles(photos)).toEqual(['Trips/Backup/IMG_0002 (1).JPG', 'Trips/IMG_0001.JPG', 'Trips/IMG_0002.JPG', 'Trips/IMG_0003.JPG']);
  });

  test('ticked groups have their duplicates deleted together', async ({ page, photos, service }) => {
    await twoCopies(photos, service, page);
    await groups(page).getByRole('checkbox', { name: 'Select all' }).check();
    const selected = page.getByRole('region', { name: 'Selected groups' });
    await expect(selected).toContainText('2');
    await selected.getByRole('button', { name: 'Delete their duplicates' }).click();
    await expect(question(page)).toHaveAccessibleName('Delete the duplicates of 2 groups?');
    await question(page).getByRole('button', { name: 'Delete 2 files' }).click();
    await expect(message(page, /^Moved 2 duplicates/)).toBeVisible();
    expect(photoFiles(photos)).toEqual(['Trips/IMG_0001.JPG', 'Trips/IMG_0002.JPG', 'Trips/IMG_0003.JPG']);
    await expect(page.getByText('No duplicates waiting to be removed.')).toBeVisible();
  });

  test('groups are found by a file\'s name or folder, and those whose copies are all kept can be hidden', async ({ page, photos, service }) => {
    await twoCopies(photos, service, page);
    const search = page.getByRole('textbox', { name: 'Search by file name or folder' });
    await search.fill('IMG_0002');
    await expect(groups(page).getByRole('listitem')).toHaveCount(1);
    await search.fill('Backup');
    await expect(groups(page).getByRole('listitem')).toHaveCount(2);
    await search.fill('nothing like it');
    await expect(groups(page).getByRole('listitem')).toHaveCount(0);
    await search.fill('');

    await groups(page).getByRole('button', { name: /IMG_0001\.JPG/ }).click();
    await review(page).getByRole('button', { name: 'Keep this copy' }).click();
    await page.getByText('Hide reviewed', { exact: true }).click();
    await expect(page.getByRole('switch', { name: 'Hide reviewed' })).toBeChecked();
    await expect(groups(page).getByRole('listitem')).toHaveCount(1);
    await expect(groups(page)).toContainText('IMG_0002.JPG');
  });

  test('a Live Photo\'s copy goes with its video and edit file, and comes back with them', async ({ page, photos, service }) => {
    photos.livePhoto('Trips/IMG_0005', { seed: 5 });
    photos.sidecar('Trips/IMG_0005.AAE');
    photos.copy('Trips/IMG_0005.JPG', 'Trips/Backup/IMG_0005.JPG');
    photos.copy('Trips/IMG_0005.MOV', 'Trips/Backup/IMG_0005.MOV');
    photos.copy('Trips/IMG_0005.AAE', 'Trips/Backup/IMG_0005.AAE');
    await service.scan(photos.root);
    await page.goto('/');
    await page.getByRole('button', { name: 'Delete all duplicates' }).click();
    await question(page).getByRole('button', { name: /^Delete \d+ files?$/ }).click();
    await expect(message(page, 'Moved 1 duplicate (23 KB) and 2 linked files to _PhotoSense_Removed')).toBeVisible();
    // One whole Live Photo went, picture, video and edit file together, and the other is left whole.
    const went = photos.files('_PhotoSense_Removed');
    expect(went.map(f => f.replace(/\.\w+$/, ''))).toEqual(Array(3).fill(went[0].replace(/\.\w+$/, '')));
    expect(photoFiles(photos)).toHaveLength(3);

    await page.getByRole('log', { name: 'Messages' }).getByRole('button', { name: 'Undo' }).click();
    await expect(message(page, /back where (it was|they were)/)).toBeVisible();
    expect(photoFiles(photos)).toHaveLength(6);
    expect(photos.files('_PhotoSense_Removed')).toEqual([]);
  });

  test('the same picture saved again in another format is a duplicate, and what differs between them is shown', async ({ page, photos, service }) => {
    photos.picture('IMG_0007.JPG', { seed: 7 });
    photos.png('Exports/IMG_0007.png', { seed: 7 });
    await service.scan(photos.root);
    await page.goto('/');
    await expect(groups(page).getByRole('listitem')).toHaveCount(1);
    const differs = review(page).getByRole('table', { name: 'What differs' });
    await expect(differs.getByRole('row', { name: /^Format/ })).toContainText('PNG');
    await expect(differs.getByRole('row', { name: /^Taken/ })).toBeVisible();
    await expect(review(page)).toContainText('Same picture');
  });
});
