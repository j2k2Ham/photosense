import { expect, message, test } from '../fixtures/app';
import { photoFiles, question } from '../fixtures/screens';

test.describe('similar shots', () => {
  test('burst frames are offered apart from the duplicates, and one is deleted while the best shot stays', async ({ page, photos, service }) => {
    photos.picture('Burst/IMG_0010.JPG', { seed: 10, taken: new Date(2023, 1, 2, 9, 0, 1) });
    photos.picture('Burst/IMG_0011.JPG', { seed: 10, brighter: 1, taken: new Date(2023, 1, 2, 9, 0, 2) });
    photos.picture('Other/IMG_0020.JPG', { seed: 20 });
    await service.scan(photos.root);
    await page.goto('/');
    await expect(page.getByRole('tab', { name: 'Duplicates 0' })).toBeVisible();
    await page.getByRole('tab', { name: 'Similar 1' }).click();
    const review = page.getByRole('region', { name: 'Review' });
    await expect(review.getByRole('heading', { name: 'Similar shots (1)' })).toBeVisible();
    await expect(review).toContainText('A burst frame or an edited version. Never removed along with the duplicates.');
    const best = await review.getByRole('heading', { level: 2 }).innerText();
    const other = best === 'IMG_0010.JPG' ? 'IMG_0011.JPG' : 'IMG_0010.JPG';

    await review.getByRole('button', { name: /^Delete this copy/ }).click();
    await expect(question(page)).toHaveAccessibleName(`Delete ${other}?`);
    await question(page).getByRole('button', { name: 'Delete 1 file' }).click();
    await expect(message(page, `Moved ${other} to _PhotoSense_Removed. ${best} stays where it is.`)).toBeVisible();
    expect(photoFiles(photos)).toEqual([`Burst/${best}`, 'Other/IMG_0020.JPG']);
    await expect(page.getByRole('tab', { name: 'Similar 0' })).toBeVisible();
  });

  test('ticked groups of similar shots are deleted together, each best shot staying', async ({ page, photos, service }) => {
    for (const [seed, minute] of [[30, 1], [40, 2]]) {
      photos.picture(`Burst/A${seed}.JPG`, { seed, taken: new Date(2023, 1, 2, 9, minute, 1) });
      photos.picture(`Burst/B${seed}.JPG`, { seed, brighter: 1, taken: new Date(2023, 1, 2, 9, minute, 2) });
    }
    await service.scan(photos.root);
    await page.goto('/');
    await page.getByRole('tab', { name: 'Similar 2' }).click();
    await page.getByRole('region', { name: 'Groups' }).getByRole('checkbox', { name: 'Select all' }).check();
    await page.getByRole('region', { name: 'Selected groups' }).getByRole('button', { name: 'Delete their similar shots' }).click();
    await expect(question(page)).toHaveAccessibleName('Delete the similar shots of 2 groups?');
    await question(page).getByRole('button', { name: 'Delete 2 files' }).click();
    await expect(message(page, /^Moved 2 similar shots .* The best shot of each group stays where it is\.$/)).toBeVisible();
    expect(photoFiles(photos)).toHaveLength(2);
  });
});
