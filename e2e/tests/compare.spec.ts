import { expect, test } from '../fixtures/app';
import { PLACES } from '../fixtures/media';

test.describe('looking at a file before deciding', () => {
  test('a picture and its copy are shown whole, side by side, with where it was taken on a map @phone', async ({ page, photos, service }) => {
    photos.picture('IMG_0001.JPG', { seed: 1, at: PLACES.butte, width: 640, height: 480 });
    photos.copy('IMG_0001.JPG', 'Backup/IMG_0001.JPG');
    await service.scan(photos.root);
    await page.goto('/');
    const review = page.getByRole('region', { name: 'Review' });
    await expect(review.getByRole('row', { name: /^Place/ })).toContainText('Butte, Montana, US');
    await expect(review.getByRole('link', { name: 'Show on map' })).toHaveAttribute('href', /openstreetmap\.org\/\?mlat=46\.0038&mlon=-112\.5348/);

    await review.getByRole('button', { name: 'Compare IMG_0001.JPG' }).first().click();
    const compare = page.getByRole('dialog', { name: /^Compare IMG_0001\.JPG/ });
    await expect(compare).toBeVisible();
    // The pictures are the files themselves, not their previews.
    const shown = compare.getByRole('img', { name: 'IMG_0001.JPG' }).first();
    await expect(shown).toBeVisible();
    await expect.poll(() => shown.evaluate((img: HTMLImageElement) => img.complete && img.naturalWidth)).toBe(640);
    await page.keyboard.press('Escape');
    await expect(compare).toBeHidden();
  });

  test('a video PhotoSense cannot play here says so and offers the computer\'s own player', async ({ page, photos, service }) => {
    photos.movie('Clips/IMG_0100.MOV', { seconds: 4 });
    photos.copy('Clips/IMG_0100.MOV', 'Backup/IMG_0100.MOV');
    await service.scan(photos.root);
    await page.goto('/');
    const review = page.getByRole('region', { name: 'Review' });
    await expect(review.getByRole('heading', { name: 'Duplicates of this video (1)' })).toBeVisible();
    await review.getByRole('button', { name: 'Compare IMG_0100.MOV' }).first().click();
    const compare = page.getByRole('dialog', { name: /^Compare IMG_0100\.MOV/ });
    await expect(compare.getByText('This browser cannot play this video. Open it in your default player instead.').first()).toBeVisible();
  });
});
