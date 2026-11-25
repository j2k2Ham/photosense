import { expect, test } from '../fixtures/app';
import { Phone } from '../fixtures/phone';
import { phoneTab } from '../fixtures/screens';

// The note asking for a backup is the first thing every device shows until it is accepted; these tests leave it to be asked.
test.use({ noteAccepted: false });

const NOTE = 'Keep a backup of photos you care about';

test.describe('the note asking for a backup', () => {
  test('on a computer, reads the terms of use over it, goes only once accepted, and is not asked again', async ({ page, service }) => {
    await page.goto('/');
    const note = page.getByRole('alertdialog', { name: NOTE });
    await expect(note).toContainText('Before you use Trovolve, keep a backup of the photos and videos you care about, and keep it up to date.');
    await expect(note).toContainText('Accepted it on your phone already? Pair the phone with this computer, and Trovolve does not ask here.');
    await expect(service.get<{ accepted: string | null }>('terms')).resolves.toEqual({ accepted: null });
    await page.keyboard.press('Escape');
    await expect(note).toBeVisible();
    await note.getByRole('button', { name: 'Read the terms of use' }).click();
    const about = page.getByRole('dialog', { name: 'About Trovolve' });
    await expect(about.getByRole('button', { name: 'Terms of use' })).toHaveAttribute('aria-current', 'page');
    await expect(about).toContainText('Keep a backup of photos you care about before you use Trovolve');
    await expect(note).toBeHidden();
    await page.keyboard.press('Escape');
    await note.getByRole('button', { name: 'Accept and continue' }).click();
    await expect(note).toBeHidden();
    await expect(page.getByRole('heading', { name: 'Find duplicate photos and videos' })).toBeVisible();
    // The program keeps it, not this browser: another browser is not asked either.
    expect((await service.get<{ accepted: string | null }>('terms')).accepted).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    await page.evaluate(() => localStorage.clear());
    await page.reload();
    await expect(page.getByRole('heading', { name: 'Find duplicate photos and videos' })).toBeVisible();
    await expect(note).toBeHidden();
  });

  test('on a computer, Pair a phone opens pairing over it, and the note waits under it', async ({ page }) => {
    await page.goto('/');
    const note = page.getByRole('alertdialog', { name: NOTE });
    await note.getByRole('button', { name: 'Pair a phone' }).click();
    const pairing = page.getByRole('dialog', { name: 'Pair a phone' });
    await expect(pairing).toBeVisible();
    await expect(note).toBeHidden();
    await page.keyboard.press('Escape');
    await expect(pairing).toBeHidden();
    await expect(note).toBeVisible();
  });

  test('on a phone past its first launch, covers everything at the next launch until accepted @phone', async ({ page }) => {
    await new Phone().attach(page);
    await page.goto('/');
    const note = page.getByRole('region', { name: NOTE });
    await expect(note).toContainText('Using Trovolve means you accept its terms of use.');
    await note.getByRole('button', { name: 'Accept and continue' }).click();
    // Nothing covers the tabs now: one can be tapped.
    await phoneTab(page, 'Clean up');
    await expect(page.getByRole('navigation', { name: 'Tabs' }).getByRole('button', { name: /^Clean up/ })).toHaveAttribute('aria-current', 'page');
    await page.reload();
    await expect(page.getByRole('navigation', { name: 'Tabs' })).toBeVisible();
    await expect(note).toBeHidden();
  });
});
