import { expect, message, test } from '../fixtures/app';
import { menu, question } from '../fixtures/screens';

test.describe('the menu', () => {
  test('the light theme is chosen there without closing it, and kept for the next visit @phone', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('button', { name: 'Menu' }).click();
    await page.getByRole('menuitem', { name: /^Theme/ }).click();
    await page.getByRole('menuitemradio', { name: 'Light' }).click();
    await expect(page.getByRole('menuitemradio', { name: 'Light' })).toHaveAttribute('aria-checked', 'true');
    await expect(page.locator('html')).toHaveClass(/\blight\b/);
    await page.reload();
    await expect(page.locator('html')).toHaveClass(/\blight\b/);
    await page.getByRole('button', { name: 'Menu' }).click();
    await page.getByRole('menuitem', { name: /^Theme/ }).click();
    await page.getByRole('menuitemradio', { name: 'Dark' }).click();
    await expect(page.locator('html')).not.toHaveClass(/\blight\b/);
    // Esc puts the menu away.
    await page.keyboard.press('Escape');
    await expect(page.getByRole('menu')).toBeHidden();
  });

  test('the theme follows the device until another is chosen, and Follow device setting goes back to that, changing with the system and kept for the next visit @phone', async ({ page }) => {
    await page.emulateMedia({ colorScheme: 'light' });
    await page.goto('/');
    await expect(page.locator('html')).toHaveClass(/\blight\b/);
    await page.getByRole('button', { name: 'Menu' }).click();
    await expect(page.getByRole('menuitem', { name: /^Theme/ })).toHaveText(/Follow device setting/);
    await page.getByRole('menuitem', { name: /^Theme/ }).click();
    await page.getByRole('menuitemradio', { name: 'Dark' }).click();
    await expect(page.locator('html')).not.toHaveClass(/\blight\b/);
    await page.getByRole('menuitemradio', { name: 'Follow device setting' }).click();
    await expect(page.getByRole('menuitemradio', { name: 'Follow device setting' })).toHaveAttribute('aria-checked', 'true');
    await expect(page.locator('html')).toHaveClass(/\blight\b/);
    await page.emulateMedia({ colorScheme: 'dark' });
    await expect(page.locator('html')).not.toHaveClass(/\blight\b/);
    await page.emulateMedia({ colorScheme: 'light' });
    await page.reload();
    await expect(page.locator('html')).toHaveClass(/\blight\b/);
  });

  test('what went wrong is listed under Errors, counted on the page, until cleared', async ({ page, photos, service }) => {
    photos.picture('IMG_0001.JPG', { seed: 1 });
    photos.copy('IMG_0001.JPG', 'Backup/IMG_0001.JPG');
    await service.scan(photos.root);
    await page.goto('/');
    // A copy deleted behind Trovolve's back can no longer be removed: it says so.
    for (const file of photos.files()) photos.remove(file);
    await page.getByRole('button', { name: /^Delete this duplicate/ }).click();
    await question(page).getByRole('button', { name: 'Delete 1 file' }).click();
    await expect(page.getByRole('log', { name: 'Messages' })).toContainText(/left alone|could not|no longer/i);

    await page.getByRole('button', { name: 'Menu' }).click();
    await page.getByRole('menuitem', { name: /^Support 1 error/ }).click();
    await page.getByRole('menuitem', { name: /^Errors 1/ }).click();
    const errors = page.getByRole('dialog', { name: 'Errors' });
    await expect(errors.getByRole('listitem')).toHaveCount(1);
    await errors.getByRole('button', { name: 'Clear all' }).click();
    await expect(errors).toContainText(/nothing|no errors/i);
  });

  test('Clear results forgets what the scan found, goes to the scan page, and leaves the photos as they are', async ({ page, photos }) => {
    photos.picture('IMG_0001.JPG', { seed: 1 });
    photos.copy('IMG_0001.JPG', 'Backup/IMG_0001.JPG');
    // Scanned from the page, as a person does, which chooses to show the results it found.
    await page.goto('/');
    await page.getByRole('textbox', { name: 'Base folder' }).fill(photos.root);
    await page.getByRole('button', { name: 'Scan' }).click();
    await expect(page.getByRole('tab', { name: 'Duplicates 1' })).toBeVisible({ timeout: 60_000 });
    await menu(page, 'Clear results');
    await expect(question(page)).toHaveAccessibleName('Clear the scan results?');
    await question(page).getByRole('button', { name: 'Clear results' }).click();
    await expect(message(page, 'Cleared the results: 2 files forgotten. Your photos were not touched.')).toBeVisible();
    // The scan page, ready for the next scan.
    await expect(page.getByRole('heading', { name: 'Find duplicate photos and videos' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Scan' })).toBeVisible();
    // With no results left, there is nothing to start over from or go back to.
    await expect(page.getByText('Start over')).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Back to results' })).toHaveCount(0);
    // Nor anything in the menu to change or clear: they show only beside a scan's results.
    await page.getByRole('button', { name: 'Menu' }).click();
    await expect(page.getByRole('menuitem', { name: 'Settings' })).toBeVisible();
    await expect(page.getByRole('menuitem', { name: 'Change folders or rescan' })).toHaveCount(0);
    await expect(page.getByRole('menuitem', { name: 'Clear results' })).toHaveCount(0);
    expect(photos.files()).toEqual(['Backup/IMG_0001.JPG', 'IMG_0001.JPG']);
  });

  test('Support, opened with the right arrow, leads to the support page: an email to support with the version and system, and the common questions @phone', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('button', { name: 'Menu' }).click();
    await page.getByRole('menuitem', { name: /^Support/ }).focus();
    await page.keyboard.press('ArrowRight');
    await expect(page.getByRole('menuitem', { name: /^Errors/ })).toBeFocused();
    await page.getByRole('menuitem', { name: 'Support page' }).click();
    const support = page.getByRole('dialog', { name: 'Support' });
    await expect(support.getByLabel('Details for support')).toContainText(/^Trovolve \S+ for Windows and Mac\nSystem: \w+/);
    await expect(support.getByRole('link', { name: 'Email support' })).toHaveAttribute('href', /^mailto:[^?]+\?subject=Trovolve%20support&body=What%20I%20did/);
    await expect(support.getByRole('article', { name: 'Common questions' })).toContainText('Where do the files I delete go?');
    await support.getByRole('button', { name: 'Read the guide' }).click();
    await expect(page.getByRole('dialog', { name: 'Help' })).toBeVisible();
    await expect(support).toBeHidden();
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

    // The old name still finds the topic it became.
    await help.getByRole('textbox', { name: 'Search the guide' }).fill('delete permanently');
    await expect(help.getByRole('navigation', { name: 'Topics' }).getByRole('button')).toHaveCount(1);
    await expect(reading).toHaveAccessibleName('Removed files and the Recycle Bin');
    await help.getByRole('textbox', { name: 'Search the guide' }).fill('');
    await help.getByRole('navigation', { name: 'Topics' }).getByRole('button', { name: /Backing up a phone to a computer/ }).click();
    await expect(reading).toContainText('over your own Wi-Fi, never through the internet');

    await help.getByRole('navigation', { name: 'Topics' }).getByRole('button', { name: /Scanning for duplicates/ }).click();
    await reading.getByRole('button', { name: 'Go to the scan screen' }).click();
    await expect(help).toBeHidden();
    await expect(page.getByRole('heading', { name: 'Find duplicate photos and videos' })).toBeVisible();
  });
});

test.describe('About', () => {
  test('says which program is running, answers common questions one at a time, and holds the policies and acknowledgements', async ({ page }) => {
    await page.goto('/');
    await menu(page, 'About Trovolve');
    const about = page.getByRole('dialog', { name: 'About Trovolve' });
    await expect(about.getByRole('article')).toContainText(/Version [\d.]+ for Windows and Mac/);

    await about.getByRole('navigation', { name: 'About' }).getByRole('button', { name: 'Support' }).click();
    const faq = about.getByRole('article').locator('details').filter({ hasText: 'Where do the files I delete go?' });
    await expect(faq).not.toHaveAttribute('open', '');
    await faq.locator('summary').click();
    await expect(faq).toHaveAttribute('open', '');
    await expect(faq).toContainText('_Trovolve_Removed');
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
