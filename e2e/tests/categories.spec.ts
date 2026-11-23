import { expect, message, test, type Photos, type Service } from '../fixtures/app';
import { photoFiles, question } from '../fixtures/screens';
import type { Page } from '@playwright/test';

const SHOT = new Date(2023, 6, 4, 10, 0, 0);

// Two screenshots, and a burst of three shots taken in the same second by one camera, beside a photo of its own.
async function library(photos: Photos, service: Service, page: Page) {
  photos.png('Screenshots/Screenshot 2024-01-02 at 09.15.00.png', { seed: 5 });
  photos.png('Screenshots/Screenshot 2024-01-03 at 18.40.00.png', { seed: 6 });
  for (const seed of [1, 2, 3]) photos.picture(`Trip/IMG_000${seed}.JPG`, { seed, taken: SHOT });
  photos.picture('Trip/IMG_0009.JPG', { seed: 9, taken: new Date(2023, 6, 5, 12, 0, 0) });
  await service.scan(photos.root);
  await page.goto('/');
  await page.getByRole('tab', { name: /^Categories/ }).click();
}

const categories = (page: Page) => page.getByRole('navigation', { name: 'Categories' });

test.describe('Clean up\'s categories on a computer', () => {
  test('screenshots are deleted into _Trovolve_Removed after a question naming them, and Undo puts them back', async ({ page, photos, service }) => {
    await library(photos, service, page);
    await expect(categories(page).getByRole('button', { name: /^Screenshots/ })).toContainText('2 items');
    await expect(categories(page).getByRole('button', { name: /^Bursts/ })).toContainText('2 extra shots');
    await categories(page).getByRole('button', { name: /^Screenshots/ }).click();

    const pane = page.getByRole('region', { name: 'Screenshots' });
    await expect(pane.getByRole('button', { name: /: to delete$/ })).toHaveCount(2);
    await pane.getByRole('button', { name: 'Delete 2 files' }).click();
    await expect(question(page)).toHaveAccessibleName('Delete 2 files?');
    await expect(question(page)).toContainText(/Screenshot 2024-01-0\d at .+\.png and Screenshot 2024-01-0\d at .+\.png go/);
    await question(page).getByRole('button', { name: 'Delete 2 files' }).click();
    await expect(message(page, /^Moved 2 files \(.+\) to _Trovolve_Removed\.$/)).toBeVisible();
    expect(photos.exists('_Trovolve_Removed/Screenshots/Screenshot 2024-01-02 at 09.15.00.png')).toBe(true);
    expect(photos.exists('Screenshots/Screenshot 2024-01-03 at 18.40.00.png')).toBe(false);
    await expect(pane.getByText('Nothing left to review.')).toBeVisible();

    await page.getByRole('log', { name: 'Messages' }).getByRole('button', { name: 'Undo' }).click();
    await expect(message(page, 'Put 2 files back where they were.')).toBeVisible();
    expect(photos.exists('Screenshots/Screenshot 2024-01-02 at 09.15.00.png')).toBe(true);
    expect(photos.exists('Screenshots/Screenshot 2024-01-03 at 18.40.00.png')).toBe(true);
    await expect(pane.getByRole('button', { name: /: to delete$/ })).toHaveCount(2);
  });

  test('a burst keeps the shot clicked and deletes the rest, saying which stays', async ({ page, photos, service }) => {
    await library(photos, service, page);
    await categories(page).getByRole('button', { name: /^Bursts/ }).click();
    const burst = page.getByRole('region', { name: 'Jul 4, 2023' });
    await expect(burst.getByRole('button')).toHaveCount(3);
    await expect(burst.getByRole('button', { name: /: kept$/ })).toHaveCount(1);
    await burst.getByRole('button', { name: 'IMG_0003.JPG: to delete' }).or(burst.getByRole('button', { name: 'IMG_0003.JPG: kept' })).click();
    await expect(burst.getByRole('button', { name: 'IMG_0003.JPG: kept' })).toBeVisible();

    await page.getByRole('button', { name: 'Delete 2 extra shots' }).click();
    await expect(question(page)).toContainText(/IMG_000[12]\.JPG and IMG_000[12]\.JPG go/);
    await expect(question(page)).toContainText('IMG_0003.JPG stays.');
    await question(page).getByRole('button', { name: 'Delete 2 files' }).click();
    await expect(message(page, /^Moved 2 files \(.+\) to _Trovolve_Removed\. IMG_0003\.JPG stays where it is\.$/)).toBeVisible();
    expect(photoFiles(photos).filter(f => f.startsWith('Trip/'))).toEqual(['Trip/IMG_0003.JPG', 'Trip/IMG_0009.JPG']);
  });
});
