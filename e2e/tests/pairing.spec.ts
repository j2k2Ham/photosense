import { expect, message, test } from '../fixtures/app';

test.describe('phones and pairing', () => {
  test('Phones, on a computer, says no phone is paired yet, and Pair a phone shows a QR code and a code to type, and closes', async ({ page }) => {
    await page.goto('/');
    const areas = page.getByRole('group', { name: 'Area' });
    await expect(areas.getByRole('button', { name: /^Phones/ }).getByRole('img', { name: 'New' })).toBeVisible();
    await areas.getByRole('button', { name: /^Phones/ }).click();
    // Asked of the program itself, which keeps the phones' backups.
    const none = page.getByRole('region', { name: 'No phones yet' });
    await expect(none).toContainText('Pair a phone with Trovolve on it, and it backs up its photos and videos to this computer over Wi-Fi.');
    await expect(areas.getByRole('img', { name: 'New' })).toBeHidden();
    await none.getByRole('button', { name: 'Pair a phone' }).click();
    const pairing = page.getByRole('dialog', { name: 'Pair a phone' });
    // The program waits for phones as it runs, and makes a code and a QR code.
    await expect(pairing.getByLabel('Code to type on the phone')).toHaveText(/^\d{3} \d{3}$/, { timeout: 20_000 });
    await expect(pairing.getByRole('img', { name: 'QR code for the phone to scan' })).toBeVisible();
    await expect(pairing).toContainText('The phone and this computer need to be on the same Wi-Fi. The code changes every 5 minutes.');
    await pairing.getByRole('button', { name: 'Close' }).click();
    await expect(pairing).toBeHidden();
  });

  test('Rename by date shows the first names now and after, and renames a phone\u2019s copies on this computer', async ({ page }) => {
    let phone = {
      id: 'p', name: "Jamie's iPhone", kind: 'iPhone', folder: "Pictures/Trovolve/Jamie's iPhone", path: '/Pictures/Trovolve/Jamie', paused: false,
      paired: '2026-11-10T09:00:00Z', lastBackup: null, lastChecked: null, held: 2, heldBytes: 2, safe: 0, safeBytes: 0, tripFolders: 0,
      tripsFolder: 'Pictures/Trovolve', tripsPath: '/Pictures/Trovolve', names: 'asOnPhone', arriving: null, events: [],
    };
    const asked: string[] = [];
    // A paired phone, which only a phone could make, stood in for; renaming answered as the program would.
    await page.route(url => url.pathname.endsWith('/api/backup/phones'), route => route.fulfill({ json: [phone] }));
    await page.route(url => url.pathname.endsWith('/api/backup/phones/p/names'), route => {
      if (route.request().method() === 'GET') {
        asked.push(new URL(route.request().url()).search);
        return route.fulfill({ json: { count: 2, rows: [{ now: 'IMG_4821.HEIC', after: '2026-03-02 14.31.07.HEIC' }, { now: 'IMG_4822.HEIC', after: '2026-03-02 14.31.07-2.HEIC' }] } });
      }
      asked.push(route.request().postData()!);
      phone = { ...phone, names: 'date' };
      return route.fulfill({ json: { changed: 2 } });
    });
    await page.goto('/');
    await page.getByRole('group', { name: 'Area' }).getByRole('button', { name: /^Phones/ }).click();
    const files = page.getByRole('region', { name: 'Files from this phone' });
    await expect(files).toContainText('File namesAs on the phone');
    await files.getByRole('button', { name: 'Rename by date…' }).click();
    const window = page.getByRole('dialog', { name: 'Rename files by date' });
    await expect(window.getByRole('row')).toHaveText(['NowAfter', 'IMG_4821.HEIC2026-03-02 14.31.07.HEIC', 'IMG_4822.HEIC2026-03-02 14.31.07-2.HEIC']);
    await window.getByRole('button', { name: 'Rename 2 files' }).click();
    await expect(message(page, 'Renamed 2 files by date. Recently moved, in Organize, can undo it.')).toBeVisible();
    expect(asked).toEqual(['?style=date', '{"kind":"date"}']);
    await expect(files).toContainText('File namesDate and time');
  });

  test('Pair a phone says what phones will call this computer, and Rename names it, kept for the next time it opens', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('group', { name: 'Area' }).getByRole('button', { name: /^Phones/ }).click();
    await page.getByRole('region', { name: 'No phones yet' }).getByRole('button', { name: 'Pair a phone' }).click();
    const pairing = page.getByRole('dialog', { name: 'Pair a phone' });
    // Until named, the program's own machine name.
    await expect(pairing).toContainText(/Phones will call this computer \S+\./, { timeout: 20_000 });
    await pairing.getByRole('button', { name: 'Rename…' }).click();
    const field = pairing.getByRole('textbox', { name: 'Name this computer' });
    await field.fill('Office PC');
    await field.press('Enter');
    await expect(pairing).toContainText('Phones will call this computer Office PC.');
    await pairing.getByRole('button', { name: 'Close' }).click();
    await page.getByRole('region', { name: 'No phones yet' }).getByRole('button', { name: 'Pair a phone' }).click();
    await expect(pairing).toContainText('Phones will call this computer Office PC.');
  });
});
