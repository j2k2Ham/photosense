import { expect, message, test } from '../fixtures/app';
import { Phone } from '../fixtures/phone';
import { phoneSettings, phoneTab, question } from '../fixtures/screens';
import { PLACES } from '../fixtures/media';

const sendScreen = (page: import('@playwright/test').Page) => page.getByRole('region', { name: 'Send to computer' });

/** Chooses the photos named in Library, once it has read them all, and takes them to Send. */
async function sendFromLibrary(page: import('@playwright/test').Page, names: string[]) {
  await page.goto('/');
  await phoneTab(page, 'Library');
  const library = page.getByRole('region', { name: 'Library' });
  await expect(library.getByRole('button', { name: 'Select' })).toBeVisible({ timeout: 60_000 });
  await library.getByRole('button', { name: 'Select' }).click();
  for (const name of names) await library.getByRole('button', { name }).click();
  await page.getByRole('toolbar', { name: 'What is selected' }).getByRole('button', { name: 'Send' }).click();
}

test.describe('on a phone', () => {
  test('the first time, Welcome says what PhotoSense does, and Continue asks for the photos, reads them all and says what it found @phone', async ({ page, photos }) => {
    photos.picture('IMG_0001.JPG', { seed: 1 });
    photos.picture('IMG_0002.JPG', { seed: 2 });
    photos.copy('IMG_0001.JPG', 'Saved again/IMG_0001.JPG');
    const phone = new Phone('nothing');
    Object.assign(phone, { firstRun: true, root: photos.root });
    await phone.attach(page);
    await page.goto('/');
    const welcome = page.getByRole('region', { name: 'Welcome' });
    await expect(welcome.getByRole('heading', { name: 'Welcome to PhotoSense' })).toBeVisible();
    await expect(welcome).toContainText('Suggested albums for each place and year');
    await welcome.getByRole('button', { name: 'Continue' }).click();

    await expect(message(page, 'Found 1 duplicate and no similar shots.')).toBeVisible({ timeout: 60_000 });
    expect(phone.asked).toContain('library/ask');
    const tabs = page.getByRole('navigation', { name: 'Tabs' });
    await expect(tabs.getByRole('button', { name: 'Library' })).toHaveAttribute('aria-current', 'page');
    await page.getByRole('log', { name: 'Messages' }).getByRole('button', { name: 'Review' }).click();
    await expect(tabs.getByRole('button', { name: 'Clean up' })).toHaveAttribute('aria-current', 'page');
    await expect(page.getByRole('list', { name: 'Groups' }).getByRole('listitem')).toHaveCount(1);
  });

  test('refused its photos, it says where access is turned on, and reads them once it is on and the app opens again @phone', async ({ page, photos }) => {
    photos.picture('IMG_0001.JPG', { seed: 1 });
    const phone = new Phone('nothing');
    Object.assign(phone, { firstRun: true, root: photos.root, answer: 'nothing' });
    await phone.attach(page);
    await page.goto('/');
    await page.getByRole('button', { name: 'Continue' }).click();
    const refused = page.getByRole('region', { name: 'Photo access' });
    await expect(refused.getByRole('heading', { name: 'PhotoSense can\'t see your photos' })).toBeVisible();
    await refused.getByRole('button', { name: 'Open Settings' }).click();
    expect(phone.asked).toContain('library/open-settings');

    // iOS starts an app again when its access is changed in Settings.
    phone.access = 'everything';
    await page.reload();
    await expect(message(page, 'Found no duplicates or similar shots.')).toBeVisible({ timeout: 60_000 });
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

  test('About, from Settings, names the phone app, which has no menu and receives nothing @phone', async ({ page }) => {
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
    await settings.getByRole('button', { name: 'Done' }).click();
    await expect(page.getByText(/^Within 5 km · /)).toBeVisible();
  });

  test('photos are sent to a computer after both show one number, and Move deletes them from the phone once they are all there @phone', async ({ page, photos }) => {
    const phone = new Phone();
    await phone.attach(page);
    photos.picture('IMG_0001.JPG', { seed: 1 });
    photos.picture('IMG_0002.JPG', { seed: 2 });
    phone.root = photos.root;
    await sendFromLibrary(page, ['IMG_0001.JPG', 'IMG_0002.JPG']);
    const send = sendScreen(page);
    await expect(send.getByRole('radio', { name: /^What you chose in Library\s*2 items$/ })).toHaveAttribute('aria-checked', 'true');
    await send.getByRole('radiogroup', { name: 'Copy or move' }).getByRole('radio', { name: 'Move' }).click();
    await expect(send).toContainText('Each photo is deleted from this phone once the computer has checked every file of it. It goes to Recently Deleted.');
    await send.getByRole('button', { name: 'Offer 2 items to a computer' }).click();
    await expect(send).toContainText("Jamie's iPhone is offering 2 files. Keep PhotoSense open on this screen.");
    expect(phone.asked[0]).toMatch(/^send\/start \{"files":\["[^"]+","[^"]+"\]\}$/);

    phone.set({ stage: 'comparing', code: '482 917', computer: 'DESKTOP-1' });
    await expect(send.getByRole('heading', { name: 'Does DESKTOP-1 show this number?' })).toBeVisible();
    await expect(send.getByLabel('The number: 482 917')).toBeVisible();
    await send.getByRole('button', { name: "It's the same number" }).click();
    expect(phone.asked).toContain('send/confirm {"same":true}');

    phone.set({ stage: 'sending', done: 1, bytes: 1_000_000, totalBytes: 2_000_000, current: 'IMG_0002.JPG' });
    await expect(send.getByRole('progressbar', { name: 'Sending to DESKTOP-1' })).toHaveAttribute('aria-valuenow', '50');
    await expect(send).toContainText('IMG_0002.JPG');

    phone.set({ stage: 'sent', done: 2, bytes: 2_000_000, current: null, where: 'C:\\Users\\jamie\\Pictures\\From the phone', deletable: ['IMG_0001.JPG', 'IMG_0002.JPG'] });
    await expect(message(page, 'Moved 2 photos to DESKTOP-1. They are in Recently Deleted on this phone for 30 days.')).toBeVisible();
    await expect(send.getByRole('heading', { name: '2 files are on DESKTOP-1' })).toBeVisible();
    await expect(send).toContainText('Deleted from this phone. They wait in Recently Deleted for 30 days.');
    expect(phone.asked.filter(a => a === 'send/delete-sent')).toHaveLength(1);
    await send.getByRole('button', { name: 'Done' }).click();
    await expect(send.getByRole('radiogroup', { name: 'What to send' })).toBeVisible();
    expect(phone.asked).not.toContain('send/stop');
  });

  test('with Copy, the photos stay on the phone, and are deleted only after a question naming what goes and what stays @phone', async ({ page, photos }) => {
    const phone = new Phone();
    await phone.attach(page);
    photos.picture('IMG_0001.JPG', { seed: 1 });
    phone.root = photos.root;
    await sendFromLibrary(page, ['IMG_0001.JPG']);
    const send = sendScreen(page);
    await expect(send).toContainText('The photos stay on this phone. Once the computer has them all, you can delete them here.');
    await send.getByRole('button', { name: 'Offer 1 item to a computer' }).click();
    phone.set({ stage: 'sent', computer: 'DESKTOP-1', done: 1, total: 1, where: 'C:\\Photos', deletable: ['IMG_0001.JPG'] });
    await expect(send.getByRole('heading', { name: '1 file is on DESKTOP-1' })).toBeVisible();
    await expect(send).toContainText('Free up space on this phone');
    expect(phone.asked).not.toContain('send/delete-sent');

    await send.getByRole('button', { name: 'Delete 1 from this phone' }).click();
    const asked = page.getByRole('dialog', { name: 'Delete IMG_0001.JPG from this phone?' });
    await expect(asked).toContainText('IMG_0001.JPG goes to Recently Deleted on this phone, with any Live Photo videos and edits.');
    await expect(asked).toContainText('The copies on DESKTOP-1 stay, in C:\\Photos.');
    await asked.getByRole('button', { name: 'Delete 1 photo' }).click();
    await expect(message(page, 'Deleted 1 photo from this phone. It is in Recently Deleted for 30 days, and the copies on DESKTOP-1 stay.')).toBeVisible();
    await expect(send).toContainText('Deleted from this phone. They wait in Recently Deleted for 30 days.');
  });

  test('numbers said to be different stop it with nothing sent; Try again offers again, and Stop offering stops @phone', async ({ page, photos }) => {
    const phone = new Phone();
    await phone.attach(page);
    photos.picture('IMG_0001.JPG', { seed: 1 });
    phone.root = photos.root;
    await sendFromLibrary(page, ['IMG_0001.JPG']);
    const send = sendScreen(page);
    await send.getByRole('button', { name: 'Offer 1 item to a computer' }).click();
    phone.set({ stage: 'comparing', code: '482 917', computer: 'DESKTOP-1' });
    await send.getByRole('button', { name: "It's different" }).click();
    await expect(send.getByRole('heading', { name: 'Stopped. Nothing was sent.' })).toBeVisible();
    await expect(send.getByRole('alert')).toHaveText('The numbers did not match. Check that you picked this phone on the computer, then try again.');
    await send.getByRole('button', { name: 'Try again' }).click();
    await expect(send.getByRole('heading', { name: 'Waiting for a computer' })).toBeVisible();
    expect(phone.asked.filter(a => a.startsWith('send/start'))).toHaveLength(2);
    await send.getByRole('button', { name: 'Stop offering' }).click();
    await expect(send.getByRole('radiogroup', { name: 'What to send' })).toBeVisible();
    await expect.poll(() => phone.asked).toContain('send/stop');
  });
});
