import { expect, message, test } from '../fixtures/app';
import { menu, question } from '../fixtures/screens';

test.describe('the menu', () => {
  test('the light theme is chosen there without closing it, and kept for the next visit @phone', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('button', { name: 'Menu' }).click();
    await page.getByRole('menuitemradio', { name: 'Light' }).click();
    await expect(page.getByRole('menuitemradio', { name: 'Light' })).toHaveAttribute('aria-checked', 'true');
    await expect(page.locator('html')).toHaveClass(/\blight\b/);
    await page.reload();
    await expect(page.locator('html')).toHaveClass(/\blight\b/);
    await page.getByRole('button', { name: 'Menu' }).click();
    await page.getByRole('menuitemradio', { name: 'Dark' }).click();
    await expect(page.locator('html')).not.toHaveClass(/\blight\b/);
    // Esc puts the menu away.
    await page.keyboard.press('Escape');
    await expect(page.getByRole('menu')).toBeHidden();
  });

  test('what went wrong is listed under Errors, counted on the page, until cleared', async ({ page, photos, service }) => {
    photos.picture('IMG_0001.JPG', { seed: 1 });
    photos.copy('IMG_0001.JPG', 'Backup/IMG_0001.JPG');
    await service.scan(photos.root);
    await page.goto('/');
    // A copy deleted behind PhotoSense's back can no longer be removed: it says so.
    for (const file of photos.files()) photos.remove(file);
    await page.getByRole('button', { name: /^Delete this duplicate/ }).click();
    await question(page).getByRole('button', { name: 'Delete 1 file' }).click();
    await expect(page.getByRole('log', { name: 'Messages' })).toContainText(/left alone|could not|no longer/i);

    await menu(page, /^Errors 1/);
    const errors = page.getByRole('dialog', { name: 'Errors' });
    await expect(errors.getByRole('listitem')).toHaveCount(1);
    await errors.getByRole('button', { name: 'Clear all' }).click();
    await expect(errors).toContainText(/nothing|no errors/i);
  });

  test('Clear results forgets what the scan found and leaves the photos as they are', async ({ page, photos, service }) => {
    photos.picture('IMG_0001.JPG', { seed: 1 });
    photos.copy('IMG_0001.JPG', 'Backup/IMG_0001.JPG');
    await service.scan(photos.root);
    await page.goto('/');
    await menu(page, 'Clear results');
    await expect(question(page)).toHaveAccessibleName('Clear the scan results?');
    await question(page).getByRole('button', { name: 'Clear results' }).click();
    await expect(message(page, 'Cleared the results: 2 files forgotten. Your photos were not touched.')).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Find duplicate photos and videos' })).toBeVisible();
    expect(photos.files()).toEqual(['Backup/IMG_0001.JPG', 'IMG_0001.JPG']);
  });
});

test.describe('Help', () => {
  test('the guide goes from topic to topic, finds topics by their words, and takes you to what a topic describes @phone', async ({ page }) => {
    await page.goto('/');
    await menu(page, 'Help');
    const help = page.getByRole('dialog', { name: 'Help' });
    const reading = help.getByRole('article');
    await expect(reading).toHaveAccessibleName('Getting started');
    await reading.getByRole('button', { name: /^Next/ }).click();
    await expect(reading).toHaveAccessibleName('Scanning for duplicates');

    await help.getByRole('textbox', { name: 'Search the guide' }).fill('recycle');
    await expect(help.getByRole('navigation', { name: 'Topics' }).getByRole('button')).toHaveCount(1);
    await help.getByRole('textbox', { name: 'Search the guide' }).fill('');
    await help.getByRole('navigation', { name: 'Topics' }).getByRole('button', { name: /Sending photos from a phone to a computer/ }).click();
    await expect(reading).toContainText('They go over your own Wi-Fi');

    await help.getByRole('navigation', { name: 'Topics' }).getByRole('button', { name: /Scanning for duplicates/ }).click();
    await reading.getByRole('button', { name: 'Go to the scan screen' }).click();
    await expect(help).toBeHidden();
    await expect(page.getByRole('heading', { name: 'Find duplicate photos and videos' })).toBeVisible();
  });
});

test.describe('About', () => {
  test('says which program is running, answers common questions one at a time, and holds the policies and acknowledgements', async ({ page }) => {
    await page.goto('/');
    await menu(page, 'About PhotoSense');
    const about = page.getByRole('dialog', { name: 'About PhotoSense' });
    await expect(about.getByRole('article')).toContainText(/Version [\d.]+ for Windows and Mac/);

    await about.getByRole('navigation', { name: 'About' }).getByRole('button', { name: 'Support' }).click();
    const faq = about.getByRole('article').locator('details').filter({ hasText: 'Where do the files I delete go?' });
    await expect(faq).not.toHaveAttribute('open', '');
    await faq.locator('summary').click();
    await expect(faq).toHaveAttribute('open', '');
    await expect(faq).toContainText('_PhotoSense_Removed');
    await faq.locator('summary').click();
    await expect(faq).not.toHaveAttribute('open', '');

    await about.getByRole('navigation', { name: 'About' }).getByRole('button', { name: 'Privacy policy' }).click();
    await expect(about.getByRole('article')).toHaveAccessibleName('Privacy policy');
    await expect(about.getByRole('article')).toContainText(/Last updated/);
    await about.getByRole('navigation', { name: 'About' }).getByRole('button', { name: 'Acknowledgements' }).click();
    await expect(about.getByRole('article')).toContainText('LiteDB');
    await page.keyboard.press('Escape');
    await expect(about).toBeHidden();
  });
});
