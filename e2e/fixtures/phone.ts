// The answers only a phone or another device gives, stood in for in the browser: the program the tests start is a
// computer's, with no phone's photo library and no phone on the network. Everything else is the program's own.
import type { Page, Route } from '@playwright/test';

const json = (route: Route, body: unknown) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });

export interface BackupState {
  stage: 'unpaired' | 'ready' | 'running' | 'paused' | 'failed';
  computer: string | null; folder: string | null; waiting: number; waitingBytes: number; onComputer: number; done: number; total: number; bytes: number; totalBytes: number;
  lastChecked: string | null; lastBackup: string | null; strip: { id: string; done: boolean }[]; problem: string | null;
  settings: { automatic: boolean; onlyWhileCharging: boolean; includeVideos: boolean; keepFavorites: boolean; keepRecent: boolean };
}

/** What Safe to delete could free on the phone stood in for: 2 photos on a 128 GB phone with 16 GB free. */
export const SAFE = {
  computer: 'DESKTOP-1', folder: "Pictures/PhotoSense/Jamie's iPhone", lastChecked: null, checked: { count: 2, bytes: 6e9 }, favorites: { count: 0, bytes: 0 },
  recent: { count: 0, bytes: 0 }, removable: { count: 2, bytes: 6e9 }, storageTotal: 128e9, storageFree: 16e9, cloudPhotos: false,
};

/**
 * A phone, as the page sees it: which program it is, what it may see of the photos, where its library is, and backing
 * up to a computer. It is past its first run unless the test says otherwise.
 */
export class Phone {
  access: 'everything' | 'selected' | 'nothing';
  /** What the person answers when the phone asks what PhotoSense may see. */
  answer: Phone['access'] = 'everything';
  /** The folder the phone's library is shown as, which Scan again reads; none unless the test gives one. */
  root?: string;
  /** Whether this is the phone's first run, with Welcome; otherwise it has been through it already. */
  firstRun = false;
  state: BackupState = {
    stage: 'ready', computer: 'DESKTOP-1', folder: "Pictures/PhotoSense/Jamie's iPhone", waiting: 2, waitingBytes: 6e9, onComputer: 0, done: 0, total: 0, bytes: 0, totalBytes: 0,
    lastChecked: null, lastBackup: null, strip: [], problem: null,
    settings: { automatic: false, onlyWhileCharging: true, includeVideos: true, keepFavorites: true, keepRecent: true },
  };
  /** What the page asked of the phone's backup, but how it stands. */
  readonly asked: string[] = [];

  constructor(access: Phone['access'] = 'everything') { this.access = access; }

  set(changes: Partial<BackupState>) { this.state = { ...this.state, ...changes }; }

  async attach(page: Page) {
    if (!this.firstRun) await page.addInitScript(() => { if (!localStorage.getItem('photosense-first-run')) localStorage.setItem('photosense-first-run', 'done'); });
    await page.route(url => url.pathname.endsWith('/api/folders') && !url.search,
      route => json(route, { path: null, parent: null, folders: this.root ? [{ name: 'Photo Library', path: this.root }] : [] }));
    await page.route('**/api/about', async route => {
      const real = await (await route.fetch()).json();
      await json(route, { ...real, program: 'iphone' });
    });
    await page.route('**/api/library/**', async route => {
      const what = new URL(route.request().url()).pathname.split('/').pop();
      if (what !== 'access') this.asked.push(`library/${what}`);
      if (what === 'choose-more') this.access = 'everything';
      if (what === 'ask') this.access = this.answer;
      if (what === 'open-settings') return route.fulfill({ status: 204 });
      return json(route, { access: this.access });
    });
    await page.route(url => url.pathname.includes('/api/backup'), async route => {
      const what = new URL(route.request().url()).pathname.split('/api/backup').pop()!.replace(/^\//, '');
      const body = route.request().postData();
      if (route.request().method() === 'POST') this.asked.push(`${what} ${body ?? ''}`.trim());
      if (what === 'library') return json(route, { computer: this.state.computer, onComputer: [], waiting: [], lastChecked: null });
      if (what === 'safe') return json(route, { ...SAFE, settings: this.state.settings });
      if (what === 'remove') return json(route, { removed: 2, bytes: 6e9, left: 0, storageTotal: 128e9, storageFreeBefore: 16e9 });
      if (what === 'start') this.set({ stage: 'running', total: 2, totalBytes: 6e9 });
      if (what === 'pause') this.set({ stage: 'paused' });
      if (what === 'settings') this.set({ settings: JSON.parse(body!) });
      return json(route, this.state);
    });
  }
}

