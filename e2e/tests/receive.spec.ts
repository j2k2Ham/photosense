import { expect, test } from '../fixtures/app';
import { PhoneNearby } from '../fixtures/phone';
import { menu } from '../fixtures/screens';

const receiving = (page: import('@playwright/test').Page) => page.getByRole('dialog', { name: 'Receive from phone' });

test.describe('receiving from a phone', () => {
  test('the computer looks for phones on its networks and its cable, and says how to offer photos when none does', async ({ page, photos }) => {
    await page.addInitScript(root => localStorage.setItem('photosense-organize-root', root), photos.root);
    await page.goto('/');
    await menu(page, 'Receive from phone');
    // Asked of the program itself: no phone here offers any.
    await expect(receiving(page)).toContainText('No phone is offering photos yet.', { timeout: 20_000 });
    await expect(receiving(page)).toContainText('choose Send to computer');
    await expect(receiving(page).getByRole('button', { name: 'Receive into Phone Pictures' })).toBeDisabled();
    await receiving(page).getByRole('button', { name: 'Cancel' }).click();
    await expect(receiving(page)).toBeHidden();
  });

  test('a phone found is received from into the folder chosen, after both show one number, and what arrived is said', async ({ page, photos }) => {
    const phone = new PhoneNearby();
    await phone.attach(page);
    photos.picture('Arrived/keep.JPG', { seed: 1 });
    await page.addInitScript(root => localStorage.setItem('photosense-organize-root', root), photos.root);
    await page.goto('/');
    await menu(page, 'Receive from phone');
    await expect(receiving(page).getByRole('radio', { name: /Jamie's iPhone/ })).toBeChecked();
    // Another folder, chosen in the computer's own folders.
    await receiving(page).getByRole('button', { name: 'Change' }).click();
    const picker = page.getByRole('dialog', { name: 'Choose where the photos go' });
    await picker.getByRole('button', { name: /^Arrived/ }).click();
    await picker.getByRole('button', { name: 'Put them here' }).click();
    await receiving(page).getByRole('button', { name: 'Receive into Arrived' }).click();
    expect(phone.asked[0]).toBe(`receive/start ${JSON.stringify({ phone: 'wifi:phone-1', folder: photos.path('Arrived') })}`);
    await expect(receiving(page)).toContainText("Connecting to Jamie's iPhone…");

    phone.set({ stage: 'comparing', code: '482 917' });
    await expect(receiving(page)).toContainText("Does Jamie's iPhone show this number?482 917");
    await receiving(page).getByRole('button', { name: 'They match' }).click();
    expect(phone.asked).toContain('receive/confirm {"same":true}');
    phone.set({ stage: 'receiving', done: 1, total: 4, bytes: 500, totalBytes: 2000 });
    await expect(receiving(page).getByRole('progressbar', { name: "Receiving from Jamie's iPhone" })).toHaveAttribute('aria-valuenow', '25');
    phone.set({ stage: 'done', done: 4, total: 4, bytes: 2000, alreadyThere: 1 });
    await expect(receiving(page)).toContainText(`3 files arrived in ${photos.path('Arrived')}. 1 file was there already, so it was not copied again.`);
    await receiving(page).getByRole('button', { name: 'Close' }).click();
    await expect(receiving(page)).toBeHidden();
    expect(phone.asked).not.toContain('receive/stop');
  });

  test('numbers that do not match stop it, and it can be tried again', async ({ page, photos }) => {
    const phone = new PhoneNearby();
    await phone.attach(page);
    await page.addInitScript(root => localStorage.setItem('photosense-organize-root', root), photos.root);
    photos.picture('IMG_0001.JPG', { seed: 1 });
    await page.goto('/');
    await menu(page, 'Receive from phone');
    await receiving(page).getByRole('button', { name: 'Receive into Phone Pictures' }).click();
    phone.set({ stage: 'comparing', code: '111 222' });
    await receiving(page).getByRole('button', { name: 'They don’t match' }).click();
    expect(phone.asked).toContain('receive/confirm {"same":false}');
    phone.set({ stage: 'failed', problem: 'The numbers did not match, so nothing was sent. Try again, and look that both show the same number.' });
    await expect(receiving(page).getByRole('alert')).toContainText('The numbers did not match');
    await receiving(page).getByRole('button', { name: 'Try again' }).click();
    await expect(receiving(page).getByRole('button', { name: 'Receive into Phone Pictures' })).toBeEnabled();
  });
});
