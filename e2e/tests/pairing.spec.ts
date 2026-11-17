import { expect, test } from '../fixtures/app';

test.describe('phones and pairing', () => {
  test('Phones, on a computer, says no phone is paired yet, and Pair a phone shows a QR code and a code to type, and closes', async ({ page }) => {
    await page.goto('/');
    const areas = page.getByRole('group', { name: 'Area' });
    await expect(areas.getByRole('button', { name: /^Phones/ }).getByRole('img', { name: 'New' })).toBeVisible();
    await areas.getByRole('button', { name: /^Phones/ }).click();
    // Asked of the program itself, which keeps the phones' backups.
    const none = page.getByRole('region', { name: 'No phones yet' });
    await expect(none).toContainText('Pair a phone with PhotoSense on it, and it backs up its photos and videos to this computer over Wi-Fi.');
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
});
