import { expect, message, test } from '../fixtures/app';
import { Phone } from '../fixtures/phone';
import { openOrganize, preview, question, suggestions, tile } from '../fixtures/screens';
import { PLACES } from '../fixtures/media';

const sendWindow = (page: import('@playwright/test').Page) => page.getByRole('dialog', { name: 'Send to computer' });

test.describe('on a phone', () => {
  test('a phone that may see only some photos says so, and more are chosen with its own picker @phone', async ({ page }) => {
    const phone = new Phone('selected');
    await phone.attach(page);
    await page.goto('/');
    const note = page.getByRole('note');
    await expect(note).toContainText('PhotoSense can see only the photos you selected.');
    await note.getByRole('button', { name: 'Choose more photos' }).click();
    await expect(message(page, 'PhotoSense can now see all your photos.')).toBeVisible();
    await expect(note).toBeHidden();
    expect(phone.asked).toContain('library/choose-more');
  });

  test('About names the phone app @phone', async ({ page }) => {
    await new Phone().attach(page);
    await page.goto('/');
    await page.getByRole('button', { name: 'Menu' }).click();
    await expect(page.getByRole('menuitem', { name: 'Receive from phone' })).toBeHidden();
    await page.getByRole('menuitem', { name: 'About PhotoSense' }).click();
    await expect(page.getByRole('dialog', { name: 'About PhotoSense' }).getByRole('article')).toContainText(/Version [\d.]+ for iPhone and iPad/);
  });

  test('Organize moves photos and offers no copy, since a phone keeps a photo once @phone', async ({ page, photos }) => {
    await new Phone().attach(page);
    photos.picture('IMG_0001.JPG', { seed: 1, at: PLACES.butte });
    await openOrganize(page, photos.root);
    if (await page.getByRole('group', { name: 'Files or folders' }).isVisible()) await page.getByRole('group', { name: 'Files or folders' }).getByRole('button', { name: /^Folders/ }).click();
    await suggestions(page).getByRole('button', { name: 'Preview Butte' }).click();
    await expect(preview(page).getByRole('group', { name: 'Move or copy' })).toBeHidden();
    await expect(preview(page)).toContainText('Each picture goes into this album and out of the other albums PhotoSense made.');
  });

  test('photos are sent to a computer after both show one number, and Move deletes them from the phone once they are all there @phone', async ({ page, photos }) => {
    const phone = new Phone();
    await phone.attach(page);
    photos.picture('IMG_0001.JPG', { seed: 1 });
    photos.picture('IMG_0002.JPG', { seed: 2 });
    await openOrganize(page, photos.root);
    await tile(page, 'IMG_0001.JPG').click();
    await tile(page, 'IMG_0002.JPG').click();
    await page.getByRole('region', { name: 'Selection' }).getByRole('button', { name: 'Send to computer' }).click();
    await expect(sendWindow(page)).toContainText("Waiting for your computer. On the computer, open PhotoSense and choose Receive from phone in the menu. This phone shows there as Jamie's iPhone.");
    expect(phone.asked[0]).toMatch(/^send\/start \{"files":\["[^"]+","[^"]+"\]\}$/);
    await sendWindow(page).getByRole('group', { name: 'Copy or move' }).getByRole('button', { name: 'Move' }).click();

    phone.set({ stage: 'comparing', code: '482 917', computer: 'DESKTOP-1' });
    await expect(sendWindow(page)).toContainText('Does DESKTOP-1 show this number?482 917');
    await sendWindow(page).getByRole('button', { name: 'They match' }).click();
    expect(phone.asked).toContain('send/confirm {"same":true}');

    phone.set({ stage: 'sending', done: 1, total: 2, bytes: 1_000_000, totalBytes: 2_000_000 });
    await expect(sendWindow(page).getByRole('progressbar', { name: 'Sending to DESKTOP-1' })).toHaveAttribute('aria-valuenow', '50');

    phone.set({ stage: 'sent', done: 2, bytes: 2_000_000, where: 'C:\\Users\\jamie\\Pictures\\From the phone', deletable: ['IMG_0001.JPG', 'IMG_0002.JPG'] });
    await expect(message(page, 'Moved 2 photos to DESKTOP-1. They are in Recently Deleted on this phone for 30 days.')).toBeVisible();
    expect(phone.asked.filter(a => a === 'send/delete-sent')).toHaveLength(1);
    await sendWindow(page).getByRole('button', { name: 'Close' }).click();
    await expect(sendWindow(page)).toBeHidden();
    expect(phone.asked).not.toContain('send/stop');
  });

  test('with Copy, the photos stay on the phone, and are deleted only after a question naming what goes and what stays @phone', async ({ page, photos }) => {
    const phone = new Phone();
    await phone.attach(page);
    photos.picture('IMG_0001.JPG', { seed: 1 });
    await openOrganize(page, photos.root);
    await tile(page, 'IMG_0001.JPG').click();
    await page.getByRole('region', { name: 'Selection' }).getByRole('button', { name: 'Send to computer' }).click();
    await expect(sendWindow(page)).toContainText('The photos stay on this phone as well.');
    phone.set({ stage: 'sent', computer: 'DESKTOP-1', done: 1, total: 1, where: 'C:\\Photos', deletable: ['IMG_0001.JPG'] });
    await expect(sendWindow(page)).toContainText('1 of 1 file is on DESKTOP-1 in C:\\Photos. They are still on this phone too.');
    expect(phone.asked).not.toContain('send/delete-sent');

    await sendWindow(page).getByRole('button', { name: 'Delete 1 photo from this phone' }).click();
    await expect(question(page)).toHaveAccessibleName('Delete IMG_0001.JPG from this phone?');
    await expect(question(page)).toContainText('The copies on DESKTOP-1 in C:\\Photos stay.');
    await question(page).getByRole('button', { name: 'Delete 1 photo' }).click();
    await expect(message(page, 'Deleted 1 photo from this phone. It is in Recently Deleted for 30 days, and the copies on DESKTOP-1 stay.')).toBeVisible();
  });

  test('closing the window while the phone waits stops offering the photos @phone', async ({ page, photos }) => {
    const phone = new Phone();
    await phone.attach(page);
    photos.picture('IMG_0001.JPG', { seed: 1 });
    await openOrganize(page, photos.root);
    await tile(page, 'IMG_0001.JPG').click();
    await page.getByRole('region', { name: 'Selection' }).getByRole('button', { name: 'Send to computer' }).click();
    await expect(sendWindow(page)).toContainText('Waiting for your computer');
    await page.keyboard.press('Escape');
    await expect(sendWindow(page)).toBeHidden();
    await expect.poll(() => phone.asked).toContain('send/stop');
  });
});
