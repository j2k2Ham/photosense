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

/** Compressing, before any has run. */
const IDLE = { stage: 'idle', quality: 'balanced', total: 0, done: 0, current: null as string | null, progress: 0, results: [] as unknown[], saved: 0, originalsDeleted: null as boolean | null, problem: null };

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
    // Clean up's categories are the program's own, with the phone's storage, which only a phone knows. Deleting from
    // them goes through the program too, into _PhotoSense_Removed, where a phone would use its own bin.
    await page.route(url => url.pathname.endsWith('/api/cleanup/categories'), async route => {
      // The test may have ended while the program was answering.
      let real: object;
      try { real = await (await route.fetch()).json(); } catch { return; }
      await json(route, { ...real, storageTotal: SAFE.storageTotal, storageFree: SAFE.storageFree });
    });
    await page.route(url => url.pathname.includes('/api/swipe'), async route => {
      const what = new URL(route.request().url()).pathname.split('/api/swipe').pop()!.replace(/^\//, '');
      const body = route.request().postData();
      if (route.request().method() === 'POST') this.asked.push(`swipe/${what} ${body ?? ''}`.trim());
      // Today's set is the library's pictures and videos, as the program lists them, drawn the first time it is asked for.
      if (!this.swipe) {
        let listing: { files: unknown[] } = { files: [] };
        if (this.root) {
          const files = new URL(route.request().url());
          files.pathname = '/api/organize/files';
          files.search = `?root=${encodeURIComponent(this.root)}`;
          // A test may end while the library is still being read for it: there is no one left to answer then.
          try { listing = await (await route.fetch({ url: files.toString(), method: 'GET', postData: undefined })).json(); } catch { return; }
        }
        const set = (listing.files as { id: string; sizeBytes: number; livePhoto?: boolean }[]).filter(f => !f.livePhoto)
          .map(f => ({ id: f.id, bytes: f.sizeBytes, onComputer: false, delete: null as boolean | null, spared: false }));
        const week = Array.from({ length: 7 }, (_, i) => ({ date: new Date(Date.now() - (6 - i) * 86_400_000).toISOString().slice(0, 10), swiped: false }));
        this.swipe = { date: week[6].date, set, earlier: [], week, streak: 0, freed: null, unswiped: 0 };
      }
      const today = this.swipe;
      const asked = body ? JSON.parse(body) : {};
      if (what === 'decide') today.set.find(c => c.id === asked.id)!.delete = asked.delete;
      if (what === 'undo') [...today.set].reverse().find(c => c.delete !== null)!.delete = null;
      if (what === 'spare') today.set.find(c => c.id === asked.id)!.spared = asked.spared;
      const decided = today.set.some(c => c.delete !== null);
      today.week[6].swiped = decided;
      today.streak = decided ? 1 : 0;
      if (what === 'delete') {
        const going = today.set.filter(c => c.delete === true && !c.spared);
        this.swipe = { ...today, set: today.set.filter(c => !going.includes(c)), freed: { deleted: going.length, bytes: going.reduce((n, c) => n + c.bytes, 0), left: 0 } };
        return json(route, this.swipe.freed);
      }
      return json(route, this.swipe);
    });
    // Year in review, and the share sheet: only a phone has them.
    await page.route(url => url.pathname.endsWith('/api/year'), route => json(route, this.year));
    await page.route(url => url.pathname.endsWith('/api/share'), async route => {
      const asked = JSON.parse(route.request().postData()!);
      this.shared.push({ name: asked.name, png: Buffer.from(asked.image, 'base64')[0] === 0x89 && Buffer.from(asked.image, 'base64').subarray(1, 4).toString() === 'PNG' });
      return route.fulfill({ status: 204 });
    });
    // Notifications, which only a phone sends: off until turned on, when the phone asks; what one tapped opens, once.
    await page.route(url => url.pathname.endsWith('/api/notices/opened'), async route => {
      const opens = this.opens;
      this.opens = null;
      return json(route, { opens });
    });
    await page.route(url => url.pathname.endsWith('/api/notices'), async route => {
      if (route.request().method() === 'POST') {
        const asked = JSON.parse(route.request().postData()!);
        this.asked.push(`notices ${route.request().postData()}`);
        if ((asked.lowStorage || asked.trialReminder) && !this.notices.allowed) this.notices.allowed = this.allowNotices;
        for (const key of ['lowStorage', 'trialReminder'] as const) if (key in asked) this.notices[key] = asked[key] && this.notices.allowed;
      }
      return json(route, this.notices);
    });
    // Compressing, which only a phone does: started, it has finished by the next time the page asks, the originals gone to the bin.
    await page.route(url => url.pathname.includes('/api/compress'), async route => {
      const what = new URL(route.request().url()).pathname.split('/api/compress').pop()!.replace(/^\//, '');
      if (what === 'videos') return json(route, this.videos);
      if (what === 'start') {
        const asked = JSON.parse(route.request().postData()!);
        this.asked.push(`compress/start ${route.request().postData()}`);
        const going = this.videos.filter(v => asked.ids.includes(v.video.id));
        const results = going.map(v => ({ id: v.video.id, fileName: v.video.fileName, before: v.video.fileSizeBytes, after: v.estimates[asked.quality as 'high' | 'balanced' | 'small']! }));
        this.compression = { ...IDLE, stage: 'running', quality: asked.quality, total: going.length, current: going[0].video.fileName };
        const running = this.compression;
        this.compressed = { ...running, stage: 'done', done: going.length, current: null, progress: 1, results, saved: results.reduce((n, r) => n + r.before - r.after, 0), originalsDeleted: true };
        return json(route, running);
      }
      if (what === 'cancel') return json(route, this.compression);
      // Asked again once started: it has finished.
      const now = this.compression;
      if (this.compressed) { this.compression = this.compressed; this.compressed = undefined; }
      return json(route, now);
    });
  }

  /** Year in review as the phone would say it; none due unless the test gives one. */
  year: Record<string, unknown> = { year: null };
  /** The cards handed to the share sheet: each one's name and whether it is a PNG. */
  readonly shared: { name: string; png: boolean }[] = [];

  /** The phone's notifications, as the person turned them. */
  notices = { lowStorage: false, trialReminder: false, allowed: false };
  /** What the person answers when the phone asks whether PhotoSense may send notifications. */
  allowNotices = true;
  /** What a notification tapped asks to open, next time the page asks. */
  opens: string | null = null;
  /** The phone's videos over 200 MB, with about what each copy takes; none unless the test gives some. */
  videos: { video: { id: string; fileName: string; fileSizeBytes: number } & Record<string, unknown>; estimates: Record<'high' | 'balanced' | 'small', number | null> }[] = [];
  /** How compressing stands, as the page is told next. */
  compression: typeof IDLE & Record<string, unknown> = IDLE;
  private compressed?: typeof IDLE & Record<string, unknown>;

  /** Swipe to clean today, as the stand-in holds it once the page has first asked. */
  swipe?: {
    date: string; set: { id: string; bytes: number; onComputer: boolean; delete: boolean | null; spared: boolean }[]; earlier: unknown[];
    week: { date: string; swiped: boolean }[]; streak: number; freed: { deleted: number; bytes: number; left: number } | null; unswiped: number;
  };
}

