// Getting to the parts of the page a test is about, as a person would.
import { expect, type Locator, type Page } from '@playwright/test';
import type { Photos } from './app';

/** The page, with Organize already knowing which folder to show, as it does once a folder has been chosen there. */
export async function openOrganize(page: Page, root: string) {
  // Set on the first visit only: a folder chosen later is remembered over it, as it is for a person.
  await page.addInitScript(folder => { if (!localStorage.getItem('photosense-organize-root')) localStorage.setItem('photosense-organize-root', folder); }, root);
  await page.goto('/');
  await page.getByRole('group', { name: 'Area' }).getByRole('button', { name: 'Organize' }).click();
  // Read afresh when no scan has read it: the first time, the program also loads the place names it goes by.
  await expect(page.getByLabel('Folder being organized')).toContainText(/\d+ files?/, { timeout: 60_000 });
}

/**
 * In the phone app, the tab of that name. The page is laid out as a computer's until the program has said it is the
 * phone app, so the tabs are waited for.
 */
export async function phoneTab(page: Page, name: string) {
  await page.getByRole('navigation', { name: 'Tabs' }).getByRole('button', { name, exact: true }).click();
}

/** In the phone app, Settings, from the gear of the screen showing. */
export async function phoneSettings(page: Page) {
  await page.getByRole('navigation', { name: 'Tabs' }).waitFor();
  await page.getByRole('button', { name: 'Settings' }).first().click();
  return page.getByRole('dialog', { name: 'Settings' });
}

export const menu = async (page: Page, item: string | RegExp) => {
  await page.getByRole('button', { name: 'Menu' }).click();
  await page.getByRole('menuitem', { name: item }).click();
};

export const gallery = (page: Page) => page.getByRole('list', { name: 'Files' });

/** A file's tile in the gallery, by its name. */
export const tile = (page: Page, name: string) => gallery(page).getByRole('button', { name: new RegExp(`(^|\\s)${name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}(\\s|$)`) });

export const suggestions = (page: Page) => page.getByRole('complementary', { name: 'Suggested folders' });

/** Drags one thing onto another as a browser does with the page's own drag and drop, which a moving mouse alone does not start here. */
export async function drag(page: Page, from: Locator, onto: Locator) {
  const carried = await page.evaluateHandle(() => new DataTransfer());
  await from.dispatchEvent('dragstart', { dataTransfer: carried });
  for (const kind of ['dragenter', 'dragover', 'drop']) await onto.dispatchEvent(kind, { dataTransfer: carried });
  await from.dispatchEvent('dragend', { dataTransfer: carried });
}

export const preview = (page: Page) => page.getByRole('dialog', { name: 'Preview' });

export const question = (page: Page) => page.getByRole('alertdialog');

/** The names in a list of files, in order. */
export async function namesIn(list: Locator): Promise<string[]> {
  return (await list.getByRole('listitem').allInnerTexts()).map(text => text.split('\n').find(line => /\.\w{3,4}$/.test(line.trim()))?.trim() ?? text);
}

/** What a test's photos folder holds now, but what PhotoSense keeps for itself. */
export const photoFiles = (photos: Photos) => photos.files().filter(f => !f.includes('_PhotoSense_Removed'));

/** On a narrow screen Organize shows its files or its folders, switched by these buttons; on a wide one it shows both, and this does nothing. */
export async function pane(page: Page, which: 'Files' | 'Folders') {
  const toggle = page.getByRole('group', { name: 'Files or folders' });
  if (await toggle.isVisible()) await toggle.getByRole('button', { name: new RegExp(`^${which}`) }).click();
}

/** On a phone Clean up shows the groups or one group's review; this goes back to the groups, as the person would. */
export async function allGroups(page: Page, isMobile: boolean) {
  if (isMobile) await page.getByRole('button', { name: 'All groups' }).click();
}
