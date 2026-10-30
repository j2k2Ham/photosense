import { expect, message, test, type Photos } from '../fixtures/app';
import { PLACES } from '../fixtures/media';
import { gallery, namesIn, openOrganize, photoFiles, question, suggestions, tile } from '../fixtures/screens';

function mixed(photos: Photos) {
  photos.picture('Trips/IMG_0001.JPG', { seed: 1, at: PLACES.butte, taken: new Date(2022, 6, 4, 10, 0, 0) });
  photos.picture('Trips/IMG_0002.JPG', { seed: 2, at: PLACES.anaconda, taken: new Date(2023, 1, 2, 9, 0, 0) });
  photos.picture('Trips/IMG_0003.JPG', { seed: 3, taken: new Date(2023, 1, 3, 9, 0, 0) });
  photos.movie('Trips/IMG_0004.MOV', { at: PLACES.butte, taken: new Date(2022, 6, 5, 10, 0, 0) });
}

test.describe('Organize: the files', () => {
  test('every picture and video is shown, and can be narrowed to those with no place, or to photos or videos', async ({ page, photos }) => {
    mixed(photos);
    await openOrganize(page, photos.root);
    await expect(page.getByLabel('Folder being organized')).toContainText('Phone Pictures · 4 files');
    await expect(gallery(page).getByRole('listitem')).toHaveCount(4);
    await page.getByRole('group', { name: 'Show' }).getByRole('button', { name: /^No location/ }).click();
    expect(await namesIn(gallery(page))).toEqual(['IMG_0003.JPG']);
    await page.getByRole('group', { name: 'Show' }).getByRole('button', { name: /^All/ }).click();
    await page.getByRole('group', { name: 'Type' }).getByRole('button', { name: /^Videos/ }).click();
    expect(await namesIn(gallery(page))).toEqual(['IMG_0004.MOV']);
    await page.getByRole('group', { name: 'Type' }).getByRole('button', { name: /^Photos 3/ }).click();
    await expect(gallery(page).getByRole('listitem')).toHaveCount(3);
  });

  test('files are found by name, by place and by month', async ({ page, photos }) => {
    mixed(photos);
    await openOrganize(page, photos.root);
    const search = page.getByRole('textbox', { name: 'Search by name, place or month' });
    await search.fill('IMG_0002');
    expect(await namesIn(gallery(page))).toEqual(['IMG_0002.JPG']);
    await search.fill('Butte');
    expect((await namesIn(gallery(page))).sort()).toEqual(['IMG_0001.JPG', 'IMG_0004.MOV']);
    await search.fill('February');
    expect((await namesIn(gallery(page))).sort()).toEqual(['IMG_0002.JPG', 'IMG_0003.JPG']);
    await search.fill('nowhere at all');
    await expect(page.getByText('No files match “nowhere at all”')).toBeVisible();
  });

  test('pointing at a file shows when and where it was taken', async ({ page, photos }) => {
    mixed(photos);
    await openOrganize(page, photos.root);
    await tile(page, 'IMG_0001.JPG').hover();
    const card = page.getByRole('tooltip');
    await expect(card).toContainText('IMG_0001.JPG');
    await expect(card).toContainText('Butte');
    await expect(card).toContainText('2022');
    await page.mouse.move(0, 0);
    await expect(card).toBeHidden();
  });

  test('all shown are selected at once, or a range with Shift, and Clear lets them go', async ({ page, photos }) => {
    mixed(photos);
    await openOrganize(page, photos.root);
    const bar = page.getByRole('region', { name: 'Selection' });
    const names = await namesIn(gallery(page));
    await tile(page, names[0]).click();
    await tile(page, names[2]).click({ modifiers: ['Shift'] });
    await expect(bar).toContainText('3 selected');
    await bar.getByRole('button', { name: 'Select all 4 shown' }).click();
    await expect(bar).toContainText('4 selected');
    await bar.getByRole('button', { name: 'Clear' }).click();
    await expect(bar).toBeHidden();
  });

  test('selected files are deleted into _PhotoSense_Removed after a question, and Undo in Recently moved puts them back @phone', async ({ page, photos }) => {
    mixed(photos);
    await openOrganize(page, photos.root);
    await tile(page, 'IMG_0003.JPG').click();
    await page.getByRole('region', { name: 'Selection' }).getByRole('button', { name: 'Delete the 1 selected file' }).click();
    await expect(question(page)).toHaveAccessibleName('Delete IMG_0003.JPG?');
    await expect(question(page)).toContainText('_PhotoSense_Removed');
    await question(page).getByRole('button', { name: 'Delete 1 file' }).click();
    await expect(message(page, /IMG_0003\.JPG|1 file/)).toBeVisible();
    expect(photos.files('_PhotoSense_Removed')).toEqual(['_PhotoSense_Removed/Trips/IMG_0003.JPG']);
    await expect(tile(page, 'IMG_0003.JPG')).toBeHidden();

    await suggestions(page).getByRole('button', { name: /^Recently moved/ }).click();
    await suggestions(page).getByRole('button', { name: /^Undo/ }).first().click();
    await expect(message(page, /back to where it was/)).toBeVisible();
    expect(photoFiles(photos)).toContain('Trips/IMG_0003.JPG');
    await expect(tile(page, 'IMG_0003.JPG')).toBeVisible();
  });

  test('another folder is chosen to organize, and remembered', async ({ page, photos }) => {
    mixed(photos);
    photos.picture('Other/IMG_0009.JPG', { seed: 9 });
    await openOrganize(page, photos.path('Trips'));
    await page.getByRole('button', { name: 'Choose root folder' }).click();
    const picker = page.getByRole('dialog', { name: 'Choose a folder to organize' });
    await picker.getByRole('button', { name: /^Up|^Back/ }).first().click();
    await picker.getByRole('button', { name: /^Other/ }).click();
    await picker.getByRole('button', { name: 'Use this folder' }).click();
    await expect(page.getByLabel('Folder being organized')).toContainText('Other · 1 file', { timeout: 60_000 });
    await page.reload();
    await page.getByRole('group', { name: 'Area' }).getByRole('button', { name: 'Organize' }).click();
    await expect(page.getByLabel('Folder being organized')).toContainText('Other');
  });
});
