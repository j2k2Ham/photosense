import { expect, message, test } from '../fixtures/app';
import { Phone, SAFE } from '../fixtures/phone';
import { phoneSettings, phoneTab, question } from '../fixtures/screens';
import { PLACES } from '../fixtures/media';

const backupScreen = (page: import('@playwright/test').Page) => page.getByRole('region', { name: 'Backup', exact: true });

test.describe('on a phone', () => {
  test('the first time, Welcome leads through photo access and pairing to the first scan, what it found and the offer, then Clean up @phone', async ({ page, photos }) => {
    photos.picture('IMG_0001.JPG', { seed: 1 });
    photos.picture('IMG_0002.JPG', { seed: 2 });
    photos.copy('IMG_0001.JPG', 'Saved again/IMG_0001.JPG');
    const phone = new Phone('nothing');
    Object.assign(phone, { firstRun: true, root: photos.root });
    await phone.attach(page);
    await page.goto('/');
    const welcome = page.getByRole('region', { name: 'Welcome' });
    await expect(welcome.getByRole('heading', { name: 'PhotoSense' })).toBeVisible();
    await expect(welcome).toContainText('Back up to your own computer over Wi-Fi');
    await welcome.getByRole('button', { name: 'Get started' }).click();
    const access = page.getByRole('region', { name: 'Photo access' });
    // An iPhone at a phone's width, an iPad at a computer's.
    await expect(access).toContainText(/Photos are checked on this (iPhone|iPad) and never uploaded\./);
    await access.getByRole('button', { name: 'Continue' }).click();
    expect(phone.asked).toContain('library/ask');

    const pair = page.getByRole('region', { name: 'Connect your computer' });
    await expect(pair).toContainText('Backups and Safe to delete need PhotoSense on your Mac or PC.');
    await pair.getByRole('button', { name: 'Skip for now' }).click();

    const result = page.getByRole('region', { name: 'Scan complete' });
    await expect(result).toContainText(/can be freed from this (iPhone|iPad)/, { timeout: 60_000 });
    await expect(result.locator('dl > div')).toHaveText([/^Duplicates1/]);
    await expect(message(page, 'Found 1 duplicate and no similar shots.')).toBeHidden();
    await result.getByRole('button', { name: 'Continue' }).click();
    // This computer stands in for the phone: it has no store, so the offer has no plans to show.
    const offer = page.getByRole('region', { name: 'PhotoSense Pro' });
    await expect(offer.getByRole('heading', { name: /^Free all .+ with Pro$/ })).toBeVisible();
    await offer.getByRole('button', { name: 'Continue with Free' }).click();

    const tabs = page.getByRole('navigation', { name: 'Tabs' });
    await expect(tabs.getByRole('button', { name: 'Clean up' })).toHaveAttribute('aria-current', 'page');
    await expect(page.getByRole('list', { name: 'Groups' }).getByRole('listitem')).toHaveCount(1);
  });

  test('refused its photos, it says so and where access is turned on, and goes on to pairing once it is on and the app opens again @phone', async ({ page, photos }) => {
    photos.picture('IMG_0001.JPG', { seed: 1 });
    const phone = new Phone('nothing');
    Object.assign(phone, { firstRun: true, root: photos.root, answer: 'nothing' });
    await phone.attach(page);
    await page.goto('/');
    await page.getByRole('button', { name: 'Get started' }).click();
    await page.getByRole('button', { name: 'Continue' }).click();
    const refused = page.getByRole('region', { name: 'Photo access' });
    await expect(message(page, 'PhotoSense needs photo access to work. You can allow it later in Settings.')).toBeVisible();
    await refused.getByRole('button', { name: 'Open Settings' }).click();
    expect(phone.asked).toContain('library/open-settings');

    // iOS starts an app again when its access is changed in Settings.
    phone.access = 'everything';
    await page.reload();
    await expect(page.getByRole('region', { name: 'Connect your computer' })).toBeVisible();
    await expect(refused).toBeHidden();
  });
  test('a phone that may see only some photos says so in Library, and more are chosen with its own picker @phone', async ({ page, photos }) => {
    photos.picture('IMG_0001.JPG', { seed: 1 });
    const phone = new Phone('selected');
    phone.root = photos.root;
    await phone.attach(page);
    await page.goto('/');
    await phoneTab(page, 'Library');
    const note = page.getByRole('note');
    await expect(note).toContainText('PhotoSense can see only the 1 photos you chose.', { timeout: 60_000 });
    await note.getByRole('button', { name: 'Choose more' }).click();
    await expect(message(page, 'PhotoSense can now see all your photos.')).toBeVisible();
    await expect(note).toBeHidden();
    expect(phone.asked).toContain('library/choose-more');
  });

  test('Clean up deletes a copy after a question naming both files by where each is, Undo brings it back, and Delete all duplicates keeps one of each @phone', async ({ page, photos, service }) => {
    photos.picture('IMG_0001.JPG', { seed: 1 });
    photos.picture('IMG_0002.JPG', { seed: 2 });
    photos.copy('IMG_0001.JPG', 'Saved again/IMG_0001.JPG');
    photos.copy('IMG_0002.JPG', 'Saved again/IMG_0002.JPG');
    await service.scan(photos.root);
    const phone = new Phone();
    phone.root = photos.root;
    await phone.attach(page);
    await page.goto('/');
    const groups = page.getByRole('list', { name: 'Groups' });
    await expect(page.getByRole('region', { name: 'Duplicates to delete' })).toContainText('2 duplicates');
    await expect(groups.getByRole('listitem')).toHaveCount(2);
    // Which of the two is kept is PhotoSense's choice: each set keeps one whole.
    const one = () => [photos.exists('IMG_0001.JPG'), photos.exists('Saved again/IMG_0001.JPG')].filter(Boolean).length;

    await groups.getByRole('button', { name: /^IMG_0001\.JPG/ }).click();
    // On a phone the review is pushed over the list, without the tabs; at a tablet's width it is beside the list.
    const review = page.getByRole('main');
    await expect(review.getByText('Identical file')).toBeVisible();
    if (page.viewportSize()!.width < 768) await expect(page.getByRole('navigation', { name: 'Tabs' })).toBeHidden();
    await review.getByRole('button', { name: 'See IMG_0001.JPG full screen' }).first().click();
    const full = page.getByRole('dialog', { name: 'IMG_0001.JPG, full screen' });
    await expect(full.getByRole('radio', { name: 'Original' })).toHaveAttribute('aria-checked', 'true');
    await full.getByRole('button', { name: 'Show the copy' }).click();
    await expect(full.getByRole('radio', { name: 'This copy' })).toHaveAttribute('aria-checked', 'true');
    await full.getByRole('button', { name: 'Close' }).click();

    await review.getByRole('button', { name: 'Delete IMG_0001.JPG' }).click();
    const asked = page.getByRole('dialog', { name: 'Delete IMG_0001.JPG?' });
    await expect(asked).toContainText(/IMG_0001\.JPG stays, in Photo Library/);
    await expect(asked).toContainText(/IMG_0001\.JPG, in Photo Library.*, goes to PhotoSense Removed, where Undo can still bring it back\./);
    await asked.getByRole('button', { name: 'Delete IMG_0001.JPG' }).click();
    await expect(message(page, /^IMG_0001\.JPG, in Photo Library.*, deleted\. The one in Photo Library.* stays\.$/)).toBeVisible();
    expect(one()).toBe(1);
    expect(photos.files('_PhotoSense_Removed').length).toBe(1);
    await expect(groups.getByRole('listitem')).toHaveCount(1);

    await page.getByRole('log', { name: 'Messages' }).getByRole('button', { name: 'Undo' }).click();
    await expect(message(page, 'Put IMG_0001.JPG back where it was.')).toBeVisible();
    expect(one()).toBe(2);
    await expect(groups.getByRole('listitem')).toHaveCount(2);

    await page.getByRole('button', { name: 'Delete all duplicates' }).click();
    const all = page.getByRole('dialog', { name: 'Delete all duplicates?' });
    await expect(all).toContainText('2 copies go to PhotoSense Removed');
    await all.getByRole('button', { name: 'Delete 2 duplicates' }).click();
    await expect(message(page, '2 duplicates deleted. The best copy of each stays.')).toBeVisible();
    expect(one()).toBe(1);
    expect([photos.exists('IMG_0002.JPG'), photos.exists('Saved again/IMG_0002.JPG')].filter(Boolean)).toHaveLength(1);
    // Said under the list, and beside it too at a tablet's width.
    await expect(page.getByText('No duplicates left. What you deleted waits in PhotoSense Removed until you delete it permanently.').first()).toBeVisible();
  });

  test('Library shows every photo by month; chosen ones go into a new album, or are deleted after a question and come back with Undo @phone', async ({ page, photos }) => {
    photos.picture('IMG_0001.JPG', { seed: 1, taken: new Date(2023, 11, 27, 23, 34) });
    photos.picture('IMG_0002.JPG', { seed: 2, taken: new Date(2023, 10, 2, 8, 0) });
    const phone = new Phone();
    phone.root = photos.root;
    await phone.attach(page);
    await page.goto('/');
    await phoneTab(page, 'Library');
    const library = page.getByRole('region', { name: 'Library' });
    await expect(library.getByRole('region', { name: 'December 2023' }).getByRole('button', { name: 'IMG_0001.JPG' })).toBeVisible({ timeout: 60_000 });
    await expect(library.getByRole('group', { name: 'Show' }).getByRole('button', { name: 'Not organized 2' })).toBeVisible();

    await library.getByRole('button', { name: 'IMG_0001.JPG' }).click();
    const viewer = page.getByRole('dialog', { name: 'IMG_0001.JPG' });
    await expect(viewer).toContainText('Dec 27, 2023, 11:34 PM');
    await viewer.getByRole('button', { name: 'Back' }).click();

    const bar = page.getByRole('toolbar', { name: 'What is selected' });
    await library.getByRole('button', { name: 'Select' }).click();
    await library.getByRole('button', { name: 'IMG_0001.JPG' }).click();
    await bar.getByRole('button', { name: 'Add to album' }).click();
    await page.getByRole('dialog', { name: 'Add 1 to an album' }).getByRole('button', { name: '+ New album…' }).click();
    await page.getByRole('dialog', { name: 'New album' }).getByRole('textbox', { name: 'Album name' }).fill('Butte');
    await page.getByRole('dialog', { name: 'New album' }).getByRole('button', { name: 'Create' }).click();
    await expect(message(page, 'Moved 1 item to Butte.')).toBeVisible();
    expect(photos.exists('Butte/IMG_0001.JPG')).toBe(true);
    await expect(library.getByRole('button', { name: 'Not organized 1' })).toBeVisible();

    await library.getByRole('button', { name: 'Select' }).click();
    await library.getByRole('button', { name: 'IMG_0002.JPG' }).click();
    await bar.getByRole('button', { name: 'Delete' }).click();
    const asked = page.getByRole('dialog', { name: 'Delete 1 item?' });
    await expect(asked).toContainText('IMG_0002.JPG goes to PhotoSense Removed.');
    await asked.getByRole('button', { name: 'Delete 1 item' }).click();
    await expect(message(page, '1 item moved to PhotoSense Removed.')).toBeVisible();
    expect(photos.exists('_PhotoSense_Removed/IMG_0002.JPG')).toBe(true);
    await page.getByRole('log', { name: 'Messages' }).getByRole('button', { name: 'Undo' }).click();
    await expect(message(page, 'Moved 1 item back.')).toBeVisible();
    expect(photos.exists('IMG_0002.JPG')).toBe(true);
  });

  test('Settings says what waits in PhotoSense Removed, and Delete permanently erases it after two questions @phone', async ({ page, photos }) => {
    photos.picture('IMG_0001.JPG', { seed: 1 });
    photos.picture('IMG_0002.JPG', { seed: 2 });
    const phone = new Phone();
    phone.root = photos.root;
    await phone.attach(page);
    await page.goto('/');
    await phoneTab(page, 'Library');
    const library = page.getByRole('region', { name: 'Library' });
    await expect(library.getByRole('button', { name: 'Select' })).toBeVisible({ timeout: 60_000 });
    await library.getByRole('button', { name: 'Select' }).click();
    await library.getByRole('button', { name: 'IMG_0002.JPG' }).click();
    await page.getByRole('toolbar', { name: 'What is selected' }).getByRole('button', { name: 'Delete' }).click();
    await page.getByRole('dialog', { name: 'Delete 1 item?' }).getByRole('button', { name: 'Delete 1 item' }).click();
    await expect(message(page, '1 item moved to PhotoSense Removed.')).toBeVisible();

    let settings = await phoneSettings(page);
    await expect(settings.getByText('PhotoSense Removed').locator('xpath=ancestor::li')).toContainText(/1 item · \d+ KB/);
    await expect(settings.getByRole('button', { name: /^Recently moved\s*1$/ })).toBeVisible();
    await expect(settings.getByRole('button', { name: /^Photo access\s*Full access$/ })).toBeVisible();
    await settings.getByRole('button', { name: 'Delete permanently…' }).click();
    const first = page.getByRole('dialog', { name: 'Delete 1 item permanently?' });
    await expect(first).toContainText('Undo can no longer bring these back, and they leave Recently moved.');
    await first.getByRole('button', { name: 'Delete 1 item' }).click();
    const sure = page.getByRole('dialog', { name: 'Are you sure?' });
    await expect(sure).toContainText('The 1 item in PhotoSense Removed goes to Recently Deleted. Undo can no longer bring it back.');
    await sure.getByRole('button', { name: 'Delete 1 item' }).click();
    await expect(message(page, '1 item deleted. They wait in Recently Deleted for 30 days.')).toBeVisible();
    expect(photos.exists('_PhotoSense_Removed/IMG_0002.JPG')).toBe(false);
    expect(photos.exists('IMG_0001.JPG')).toBe(true);

    settings = await phoneSettings(page);
    await expect(settings.getByText('PhotoSense Removed').locator('xpath=ancestor::li')).toContainText('Empty');
    await expect(settings.getByRole('button', { name: 'Delete permanently…' })).toBeDisabled();
    await expect(settings.getByRole('button', { name: /^Recently moved\s*0$/ })).toBeVisible();
  });

  test('About, from Settings, names the phone app, which has no menu @phone', async ({ page }) => {
    await new Phone().attach(page);
    await page.goto('/');
    const settings = await phoneSettings(page);
    await expect(page.getByRole('button', { name: 'Menu' })).toBeHidden();
    await settings.getByRole('button', { name: /^About PhotoSense/ }).click();
    await expect(page.getByRole('dialog', { name: 'About PhotoSense' }).getByRole('article')).toContainText(/Version [\d.]+ for iPhone and iPad/);
  });

  test('Organize suggests an album for each place, moves what is in none yet, numbering a name already taken there, and Undo puts it back @phone', async ({ page, photos }) => {
    photos.picture('IMG_0001.JPG', { seed: 1, at: PLACES.butte, taken: new Date(2023, 6, 4, 10, 0) });
    photos.picture('IMG_0002.JPG', { seed: 2, at: PLACES.butte, taken: new Date(2022, 6, 4, 10, 0) });
    // Already in an album: left out of the suggestions, and its name taken where the first is going.
    photos.picture('Butte/2023/IMG_0001.JPG', { seed: 3, at: PLACES.butte, taken: new Date(2023, 6, 5, 10, 0) });
    const phone = new Phone();
    phone.root = photos.root;
    await phone.attach(page);
    await page.goto('/');
    await phoneTab(page, 'Organize');
    const list = page.getByRole('list', { name: 'Suggestions' });
    await expect(list.getByRole('button', { name: /^Butte/ })).toContainText('2 files', { timeout: 60_000 });
    await list.getByRole('button', { name: /^Butte/ }).click();
    const main = page.getByRole('main');
    await expect(main.getByText('1 name is already taken in Butte › 2023. It gets a number, as in IMG_0001 (1).JPG.')).toBeVisible();
    await main.getByRole('button', { name: 'Review each name' }).click();
    const names = page.getByRole('dialog', { name: 'Name taken' });
    await expect(names).toContainText('Two different files with the same name.');
    await names.getByRole('button', { name: 'Done' }).click();
    await expect(message(page, '1 name reviewed. Nothing moves until you tap Move.')).toBeVisible();

    await main.getByRole('button', { name: 'Move 2 files' }).click();
    await expect(message(page, 'Moved 2 files to Butte.')).toBeVisible();
    expect(photos.exists('Butte/2023/IMG_0001 (1).JPG')).toBe(true);
    expect(photos.exists('Butte/2022/IMG_0002.JPG')).toBe(true);
    expect(photos.exists('Butte/2023/IMG_0001.JPG')).toBe(true);
    await expect(list.getByRole('button', { name: /^Butte/ })).toContainText('Moved');

    await page.getByRole('log', { name: 'Messages' }).getByRole('button', { name: 'Undo' }).click();
    await expect(message(page, 'Put back. Every file is where it was.')).toBeVisible();
    expect(photos.exists('IMG_0001.JPG')).toBe(true);
    expect(photos.exists('IMG_0002.JPG')).toBe(true);
    expect(photos.exists('Butte/2023/IMG_0001 (1).JPG')).toBe(false);

    // Distances follow Settings: miles until kilometers are chosen there.
    await expect(page.getByText(/^Within 5 mi · /)).toBeVisible();
    const settings = await phoneSettings(page);
    await settings.getByRole('radiogroup', { name: 'Distances' }).getByRole('radio', { name: 'Kilometers' }).click();
    await settings.getByRole('button', { name: 'Back' }).click();
    await expect(page.getByText(/^Within 5 km · /)).toBeVisible();
  });

  test('Backup says what waits on the computer, backs up, pauses and carries on, and Send now sends what is chosen in Library @phone', async ({ page, photos }) => {
    const phone = new Phone();
    await phone.attach(page);
    photos.picture('IMG_0001.JPG', { seed: 1 });
    photos.picture('IMG_0002.JPG', { seed: 2 });
    phone.root = photos.root;
    await page.goto('/');
    // The app backs up by itself as it opens, when a backup is due.
    await expect.poll(() => phone.asked).toContain('auto');
    await phoneTab(page, 'Backup');
    const backup = backupScreen(page);
    const card = backup.getByRole('region', { name: 'DESKTOP-1' });
    await expect(card).toContainText('2 new items waiting');
    await expect(card).toContainText('automatic backup is off');
    await card.getByRole('button', { name: 'Back up now' }).click();
    await expect(card.getByRole('progressbar', { name: 'Backing up to DESKTOP-1' })).toBeVisible();
    await card.getByRole('button', { name: 'Pause' }).click();
    await expect(card).toContainText('Paused at 0 of 2');
    await card.getByRole('button', { name: 'Resume' }).click();
    expect(phone.asked.filter(a => a === 'start')).toHaveLength(2);
    expect(phone.asked).toContain('pause');

    // Automatic backup is part of Pro: turning it on opens the Pro sheet, and nothing is kept.
    await backup.getByText('Back up automatically').click();
    await expect(page.getByRole('dialog', { name: 'PhotoSense Pro' })).toContainText('Automatic backup is part of Pro. Sending photos yourself stays free.');
    await page.keyboard.press('Escape');
    expect(phone.asked.filter(a => a.startsWith('settings'))).toHaveLength(0);
    await backup.getByText('Include videos').click();
    await expect.poll(() => phone.asked.filter(a => a.startsWith('settings'))).toEqual([expect.stringContaining('"includeVideos":false')]);

    phone.set({ stage: 'ready', waiting: 2 });
    await backup.getByRole('button', { name: 'Choose photos to send' }).click();
    const library = page.getByRole('region', { name: 'Library' });
    await expect(page.getByRole('toolbar', { name: 'What is selected' })).toBeVisible();
    await library.getByRole('button', { name: 'IMG_0001.JPG' }).click();
    await page.getByRole('toolbar', { name: 'What is selected' }).getByRole('button', { name: 'Send' }).click();
    await expect(message(page, 'Sending 1 item to DESKTOP-1.')).toBeVisible();
    expect(phone.asked.at(-1)).toMatch(/^start \{"files":\["[^"]+"\]\}$/);
    await expect(backupScreen(page)).toBeVisible();
  });

  test('Safe to delete, from Library, removes what the computer checked after saying how much it frees, and says what was freed @phone', async ({ page, photos }) => {
    const phone = new Phone();
    await phone.attach(page);
    photos.picture('IMG_0001.JPG', { seed: 1 });
    phone.root = photos.root;
    await page.goto('/');
    await phoneTab(page, 'Library');
    const banner = page.getByRole('region', { name: 'On DESKTOP-1' });
    await expect(banner).toContainText(/^2 items are on DESKTOP-1Remove them from this (iPhone|iPad) to free up to 6\.0 GB\./);
    await banner.getByRole('button', { name: 'Review' }).click();
    const safe = page.getByRole('dialog', { name: 'Safe to delete' });
    await expect(safe).toContainText(/6\.0 GBcan be freed from this (iPhone|iPad)/);
    await expect(safe.getByRole('region', { name: 'Checked on DESKTOP-1' })).toContainText('2 photos and videosEach one matches the copy on the computer');
    await expect(safe).toContainText('Removed items stay in Recently Deleted for 30 days.');
    await safe.getByRole('button', { name: `Remove ${SAFE.removable.count} items` }).click();
    const freed = page.getByRole('dialog', { name: 'Space freed' });
    await expect(freed.getByRole('heading', { name: '6.0 GB freed' })).toBeVisible();
    await expect(freed).toContainText(/2 items removed from this (iPhone|iPad)\. The copies are on DESKTOP-1 in Pictures\/PhotoSense\/Jamie's iPhone\./);
    await expect(freed).toContainText('Before112 of 128 GB');
    await expect(freed).toContainText('Now106 of 128 GB');
    expect(phone.asked).toContain('remove');
    await freed.getByRole('button', { name: 'Done' }).click();
    await expect(freed).toBeHidden();
    await expect(page.getByRole('region', { name: 'Library' })).toBeVisible();
  });
});
