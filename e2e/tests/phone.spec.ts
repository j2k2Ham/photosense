import path from 'node:path';
import { expect, message, test } from '../fixtures/app';
import { noFix, Phone, RECOVERY_KEY, SAFE } from '../fixtures/phone';
import { leaveSettings, phoneCollection, phoneSettings, phoneTab, question } from '../fixtures/screens';
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
    await expect(welcome.getByRole('heading', { name: 'Trovolve' })).toBeVisible();
    await expect(welcome).toContainText('Back up to your own computer over Wi-Fi');
    await welcome.getByRole('button', { name: 'Get started' }).click();
    const access = page.getByRole('region', { name: 'Photo access' });
    // An iPhone at a phone's width, an iPad at a computer's.
    await expect(access).toContainText(/Photos are checked on this (iPhone|iPad) and never uploaded\./);
    await access.getByRole('button', { name: 'Continue' }).click();
    expect(phone.asked).toContain('library/ask');

    const pair = page.getByRole('region', { name: 'Connect your computer' });
    await expect(pair).toContainText('Backups and Safe to delete need Trovolve on your Mac or PC.');
    await pair.getByRole('button', { name: 'Skip for now' }).click();

    const result = page.getByRole('region', { name: 'Scan complete' });
    await expect(result).toContainText(/can be freed from this (iPhone|iPad)/, { timeout: 60_000 });
    await expect(result.locator('dl > div')).toHaveText([/^Duplicates1/]);
    await expect(message(page, 'Found 1 duplicate and no similar shots.')).toBeHidden();
    await result.getByRole('button', { name: 'Continue' }).click();
    // This computer stands in for the phone: it has no store, so the offer has no plans to show.
    const offer = page.getByRole('region', { name: 'Trovolve Pro' });
    await expect(offer.getByRole('heading', { name: /^Free all .+ with Pro$/ })).toBeVisible();
    await offer.getByRole('button', { name: 'Continue with Free' }).click();

    const tabs = page.getByRole('navigation', { name: 'Tabs' });
    await expect(tabs.getByRole('button', { name: 'Clean up' })).toHaveAttribute('aria-current', 'page');
    await expect(page.getByRole('region', { name: 'Find' }).getByRole('button', { name: /^Duplicates/ })).toContainText('1 item');
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
    await expect(message(page, 'Trovolve needs photo access to work. You can allow it later in Settings.')).toBeVisible();
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
    await expect(note).toContainText('Trovolve can see only the 1 photos you chose.', { timeout: 60_000 });
    await note.getByRole('button', { name: 'Choose more' }).click();
    await expect(message(page, 'Trovolve can now see all your photos.')).toBeVisible();
    await expect(note).toBeHidden();
    expect(phone.asked).toContain('library/choose-more');
  });

  test('Clean up deletes a copy after a question naming both files by where each is, with no Undo, and Delete all duplicates names an original that stays @phone', async ({ page, photos, service }) => {
    photos.picture('IMG_0001.JPG', { seed: 1 });
    photos.picture('IMG_0002.JPG', { seed: 2 });
    photos.copy('IMG_0001.JPG', 'Saved again/IMG_0001.JPG');
    photos.copy('IMG_0002.JPG', 'Saved again/IMG_0002.JPG');
    await service.scan(photos.root);
    const phone = new Phone();
    phone.root = photos.root;
    await phone.attach(page);
    await page.goto('/');
    await page.getByRole('region', { name: 'Find' }).getByRole('button', { name: /^Duplicates/ }).click();
    const groups = page.getByRole('list', { name: 'Groups' });
    await expect(page.getByRole('region', { name: 'Duplicates to delete' })).toContainText('2 duplicates');
    await expect(groups.getByRole('listitem')).toHaveCount(2);
    // Which of the two is kept is Trovolve's choice: each set keeps one whole.
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
    await expect(asked).toContainText(/IMG_0001\.JPG, in Photo Library.*, goes to Recently Deleted for 30 days\./);
    await asked.getByRole('button', { name: 'Delete IMG_0001.JPG' }).click();
    // A phone's Recently Deleted has no Undo of Trovolve's. The program standing in for the phone is a computer's,
    // which holds the copy in _Trovolve_Removed.
    await expect(message(page, /^IMG_0001\.JPG, in Photo Library.*, deleted\. The one in Photo Library.* stays\. The copy waits in Recently Deleted for 30 days\.$/)).toBeVisible();
    await expect(page.getByRole('log', { name: 'Messages' }).getByRole('button', { name: 'Undo' })).toHaveCount(0);
    expect(one()).toBe(1);
    expect(photos.files('_Trovolve_Removed').length).toBe(1);
    await expect(groups.getByRole('listitem')).toHaveCount(1);

    await page.getByRole('button', { name: 'Delete all duplicates' }).click();
    const all = page.getByRole('dialog', { name: 'Delete all duplicates?' });
    await expect(all).toContainText('1 copy goes to Recently Deleted');
    await all.getByRole('button', { name: 'Delete 1 duplicate' }).click();
    await expect(message(page, /^Deleted 1 copy\. The original stays: IMG_0002\.JPG in Photo Library.*\. The copy waits in Recently Deleted for 30 days\.$/)).toBeVisible();
    expect([photos.exists('IMG_0002.JPG'), photos.exists('Saved again/IMG_0002.JPG')].filter(Boolean)).toHaveLength(1);
    // Said under the list, and beside it too at a tablet's width.
    await expect(page.getByText('No duplicates left. What you deleted waits in Recently Deleted for 30 days.').first()).toBeVisible();
  });

  test('Find opens a kind of its own, whose files go in one confirmation, and the storage bar opens Free up space @phone', async ({ page, photos, service, isMobile }) => {
    photos.png('Screenshots/Screenshot 2024-01-02 at 09.15.00.png', { seed: 5 });
    photos.png('Screenshots/Screenshot 2024-01-03 at 18.40.00.png', { seed: 6 });
    photos.picture('IMG_0001.JPG', { seed: 1 });
    await service.scan(photos.root);
    const phone = new Phone();
    phone.root = photos.root;
    await phone.attach(page);
    await page.goto('/');
    const find = page.getByRole('region', { name: 'Find' });
    await expect(find.getByRole('button', { name: /^Screenshots/ })).toContainText('2 items');
    await find.getByRole('button', { name: /^Screenshots/ }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Screenshots' })).toBeVisible();
    await expect(page.getByText('selected from 2 screenshots')).toBeVisible();
    await page.getByRole('button', { name: 'Delete 2 items' }).click();
    await expect(message(page, '2 items deleted. They wait in Recently Deleted for 30 days.')).toBeVisible();
    // The program answering is a computer's, which removes into _Trovolve_Removed where a phone uses its own bin.
    expect(photos.exists('Screenshots/Screenshot 2024-01-02 at 09.15.00.png')).toBe(false);
    await expect(page.getByText('Nothing left to review.')).toBeVisible();
    // Pushed over Clean up on a phone; beside its list on a tablet.
    if (isMobile) await page.getByRole('button', { name: 'Back', exact: true }).click();

    await page.getByRole('button', { name: 'Storage 88% full. Free up space' }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Free up space' })).toBeVisible();
    // An iPad at a computer's width.
    await expect(page.getByText(/^(iPhone|iPad) storage is 88% full$/)).toBeVisible();
    await expect(page.getByRole('list', { name: 'What to delete' }).getByRole('checkbox')).toHaveCount(3);
    if (isMobile) await page.getByRole('button', { name: 'Back', exact: true }).click();
    await expect(find).toBeVisible();
  });

  test('Low-storage alerts are turned on in Clean up, after the phone asks; a large video is compressed on Pro, and the message says what it saved @phone', async ({ page }) => {
    const MB = 1024 ** 2;
    const phone = new Phone();
    phone.videos = [{
      video: {
        id: 'video-1', fileName: 'IMG_0100.MOV', sourcePath: '/Photo Library/IMG_0100.MOV', folder: '/Photo Library', fileSizeBytes: 900 * MB, width: 3840, height: 2160, format: 'MOV',
        isVideo: true, durationSeconds: 300, takenOn: '2023-07-04T10:00:00', placeName: 'Butte, Montana, US', set: 'Primary', kept: false,
      },
      estimates: { high: 400 * MB, balanced: 120 * MB, small: 50 * MB },
    }];
    await phone.attach(page);
    // Compressing is part of Pro: this phone has it.
    await page.route(url => url.pathname.endsWith('/api/plan'), route => route.fulfill({
      json: { tier: 'pro', kind: 'lifetime', started: null, until: null, sharedBy: null, store: 'App Store', removalsPerDay: 50, removedToday: 0, leftToday: null, offers: [] },
    }));
    await page.goto('/');

    const alerts = page.getByRole('switch', { name: /^Low-storage alerts/ });
    await expect(alerts).not.toBeChecked();
    await page.locator('label').filter({ hasText: 'Low-storage alerts' }).click();
    await expect(alerts).toBeChecked();
    expect(phone.asked).toContain('notices {"lowStorage":true}');

    await page.getByRole('region', { name: 'Find' }).getByRole('button', { name: /^Large videos/ }).click();
    await expect(page.getByRole('region', { name: 'Compress all' })).toContainText('Saves about 780.0 MB at Balanced quality');
    await page.getByRole('list', { name: 'Large videos' }).getByRole('button', { name: /^Butte/ }).click();
    await expect(page.getByRole('heading', { level: 1, name: 'Compress video' })).toBeVisible();
    await expect(page.getByText('Saves about 780.0 MB')).toBeVisible();
    await page.getByRole('radio', { name: 'Small' }).click();
    await expect(page.getByText('Saves about 850.0 MB')).toBeVisible();
    await page.getByRole('button', { name: 'Compress', exact: true }).click();
    await expect(message(page, 'Saved 850.0 MB. The original is in Recently Deleted for 30 days.')).toBeVisible();
    expect(phone.asked).toContain('compress/start {"ids":["video-1"],"quality":"small"}');
    await expect(page.getByRole('list', { name: 'Large videos' })).toBeVisible();
  });

  test('Search finds photos by what the phone says, opens one, and the viewer marks it a favorite and shares a copy without its place @phone', async ({ page, photos }) => {
    photos.picture('IMG_0001.JPG', { seed: 1, taken: new Date(2023, 6, 4, 9, 41) });
    const phone = new Phone();
    phone.root = photos.root;
    const file = path.join(photos.root, 'IMG_0001.JPG');
    phone.found = q => ({
      parts: [{ kind: 'inPhoto', text: 'dog' }, { kind: 'year', text: '2023' }], total: 1, videos: 0, proNeeded: q.includes('dog'),
      photos: [{ id: 'p1', fileName: 'IMG_0001.JPG', sourcePath: file, folder: photos.root, isVideo: false, fileSizeBytes: 1, width: 0, height: 0, set: 'Unknown', kept: false }],
    });
    await phone.attach(page);
    await page.goto('/');
    await phoneTab(page, 'Library');
    const library = page.getByRole('region', { name: 'Library' });
    await expect(library.getByRole('button', { name: 'IMG_0001.JPG' })).toBeVisible({ timeout: 60_000 });

    await library.getByRole('button', { name: 'Search photos' }).click();
    const search = page.getByRole('dialog', { name: 'Search' });
    await expect(search.getByRole('region', { name: 'Try' })).toContainText('dog at the beach');
    await search.getByRole('searchbox', { name: 'Search photos' }).fill('dog 2023');
    await search.getByRole('searchbox', { name: 'Search photos' }).press('Enter');
    await expect(search.getByRole('list', { name: 'Searched for' })).toHaveText('In photodogYear2023');
    await expect(search.getByRole('note')).toHaveText(/is part of Pro\. Showing photos that match 2023\./);
    await expect(search.getByRole('heading', { name: '1 photo' })).toBeVisible();
    await expect(search).toContainText('Your photos and searches are not uploaded.');
    expect(phone.asked).toContain('search dog 2023');
    await search.getByRole('list', { name: 'Matches' }).getByRole('button', { name: 'IMG_0001.JPG' }).click();

    const viewer = page.getByRole('dialog', { name: 'IMG_0001.JPG' });
    const tools = viewer.getByRole('toolbar', { name: 'Photo' });
    await tools.getByRole('button', { name: 'Info' }).click();
    await expect(viewer.getByRole('region', { name: 'Info' })).toContainText('CameraiPhone 15 Pro');
    await tools.getByRole('button', { name: 'Favorite' }).click();
    await expect(tools.getByRole('button', { name: 'Favorite' })).toHaveAttribute('aria-pressed', 'true');
    expect([...phone.favorites]).toEqual([file]);

    await tools.getByRole('button', { name: 'Share' }).click();
    const share = page.getByRole('dialog', { name: 'Share photo' });
    await expect(share).toContainText('Shares a copy without location');
    await share.getByText('Remove camera details').click();
    await share.getByRole('button', { name: 'Share', exact: true }).click();
    await expect(share).toBeHidden();
    expect(phone.sharedPhotos).toEqual([{ path: file, removeLocation: true, removeCamera: true }]);
    await viewer.getByRole('button', { name: 'Back' }).click();
    await expect(viewer).toBeHidden();
    // Recent keeps the search, on this phone.
    await search.getByRole('button', { name: 'Clear' }).click();
    await expect(search.getByRole('region', { name: 'Recent' })).toContainText('dog 2023');
  });

  test('Year in review shows in Library while it is due, goes card by card, and shares the card showing, drawn as a picture @phone', async ({ page }) => {
    const phone = new Phone();
    phone.year = {
      year: 2026, photos: 7512, videos: 316, trips: 3, topTrips: [{ name: 'Glacier National Park', month: 7, photos: 512 }], months: [1, 2, 3, 4, 5, 6, 1402, 8, 9, 10, 11, 12], busiestMonth: 7,
      busiestPlace: { town: 'West Glacier', photos: 512 }, freed: 41_600_000_000, removed: 9846, longestStreak: 38, sessions: 142,
    };
    await phone.attach(page);
    await page.goto('/');
    await phoneTab(page, 'Library');
    const card = page.getByRole('region', { name: 'Year in review' });
    await expect(card).toContainText('Your 2026 in photos');
    await expect(card).toContainText('7,512 photos, 3 trips, 41.6 GB freed');
    await card.getByRole('button', { name: 'Watch' }).click();

    const stories = page.getByRole('dialog', { name: 'Year in review' });
    await expect(stories.getByRole('heading', { level: 2 })).toHaveText('2026');
    await stories.getByRole('button', { name: 'Next card' }).click();
    await expect(stories.getByRole('heading', { level: 2 })).toHaveText('3 trips');
    await expect(stories).toContainText('Most photos: Glacier National Park, with 512.');
    await stories.getByRole('button', { name: 'Next card' }).click();
    await expect(stories.getByRole('heading', { level: 2 })).toHaveText('July');
    await expect(stories).toContainText('1,402 photos. 512 of them are from West Glacier.');
    await stories.getByRole('button', { name: 'Share' }).click();
    await expect.poll(() => phone.shared).toEqual([{ name: 'Trovolve 2026.png', png: true }]);
    await stories.getByRole('button', { name: 'Close' }).click();
    await expect(stories).toBeHidden();
  });

  test('Trips lists the trips away from home, shows them on the map, opens one by day, makes its folder on the computer, and Change asks for home @phone', async ({ isMobile, page, photos }) => {
    photos.picture('IMG_0001.JPG', { seed: 1, taken: new Date(2026, 6, 18, 9, 41) });
    photos.picture('IMG_0002.JPG', { seed: 2, taken: new Date(2026, 6, 19, 10, 5) });
    const phone = new Phone();
    phone.root = photos.root;
    const picture = (n: number) => ({
      id: `p${n}`, fileName: `IMG_000${n}.JPG`, sourcePath: path.join(photos.root, `IMG_000${n}.JPG`), folder: photos.root, isVideo: false, fileSizeBytes: 1, width: 0, height: 0, set: 'Unknown', kept: false,
    });
    const home = { town: 'Bozeman', region: 'Montana', country: 'US', latitude: 45.68, longitude: -111.04, days: 0 };
    const glacier = {
      id: '20260718094100', name: 'Glacier National Park', folder: '2026-07 Glacier National Park', start: '2026-07-18', end: '2026-07-19', photos: 12, videos: 0,
      latitude: 48.68, longitude: -113.8, cover: picture(1), folderMade: false, suggested: 'Glacier National Park',
    };
    const bigSky = { ...glacier, id: '20260301090000', name: 'Big Sky, Montana', folder: '2026-03 Big Sky', start: '2026-03-01', end: '2026-03-04', photos: 214, latitude: 45.28, longitude: -111.37 };
    phone.trips = { home, homeChosen: false, trips: [glacier, bigSky], computer: 'Studio iMac', tripsFolder: 'Pictures/Trovolve', folder: null };
    phone.tripDays[glacier.id] = { trip: glacier, days: [{ date: '2026-07-18', photos: [picture(1)] }, { date: '2026-07-19', photos: [picture(2)] }] };
    phone.towns = [{ ...home, days: 120 }, { ...home, town: 'Butte', days: 12 }];
    await phone.attach(page);
    // Trip folders are part of Pro: this phone has it.
    await page.route(url => url.pathname.endsWith('/api/plan'), route => route.fulfill({ json: { tier: 'pro', kind: 'lifetime', started: null, until: null, sharedBy: null, store: 'App Store', removalsPerDay: 50, removedToday: 0, leftToday: null, offers: [] } }));
    await page.goto('/');
    await phoneTab(page, 'Library');
    const library = page.getByRole('region', { name: 'Library' });
    await expect(library.getByRole('button', { name: 'IMG_0001.JPG' })).toBeVisible({ timeout: 60_000 });

    const trips = await phoneCollection(page, 'Trips', isMobile);
    await expect(trips).toContainText('Grouped by place and date from photos taken away from home in Bozeman, Montana.');
    await expect(trips.getByRole('list', { name: 'Trips' }).getByRole('button')).toHaveText([/^Glacier National ParkJul 18 – 19, 2026 · 12 photos/, /^Big Sky, MontanaMar 1 – 4, 2026 · 214 photos/]);
    // A phone switches to the map; a tablet shows it beside the list, the trip chosen there opened from its card.
    if (isMobile) await trips.getByRole('radio', { name: 'Map' }).click();
    const map = trips.getByRole('group', { name: 'Map' });
    await expect(map.getByRole('img', { name: 'Home, Bozeman, Montana' })).toBeVisible();
    await map.getByRole('button', { name: 'Glacier National Park, 12 photos' }).click();
    if (!isMobile) await trips.getByRole('region', { name: 'Glacier National Park' }).getByRole('button', { name: 'Open trip' }).click();

    const layer = (name: string) => (isMobile ? page.getByRole('dialog', { name }) : page.getByRole('region', { name, exact: true }));
    const trip = layer('Glacier National Park');
    await expect(trip.getByRole('region', { name: 'Day 2 · Jul 19' })).toBeVisible();
    // An iPad at a computer's width.
    await expect(trip).toContainText(/Puts this trip’s copies in Pictures\/Trovolve\/2026-07 Glacier National Park\. Nothing changes on this (iPhone|iPad)\./);
    await trip.getByRole('region', { name: 'Day 1 · Jul 18' }).getByRole('button', { name: 'IMG_0001.JPG' }).click();
    const viewer = page.getByRole('dialog', { name: 'IMG_0001.JPG' });
    await expect(viewer).toBeVisible();
    await viewer.getByRole('button', { name: 'Back' }).click();
    await expect(viewer).toBeHidden();

    await trip.getByRole('button', { name: 'Make a folder on Studio iMac' }).click();
    await expect(message(page, 'Created Pictures/Trovolve/2026-07 Glacier National Park on Studio iMac.')).toBeVisible();
    await expect(trip.getByRole('button', { name: 'Folder created' })).toBeDisabled();
    expect(phone.asked).toContain(`trips/folder ${glacier.id}`);

    // Renamed: the trip and its folder take the name.
    await trip.getByRole('button', { name: 'Rename' }).click();
    const naming = page.getByRole('dialog', { name: 'Rename trip' });
    await naming.getByRole('textbox', { name: 'Name' }).fill('Glacier with Sam');
    await naming.getByRole('button', { name: 'Save' }).click();
    await expect(naming).toBeHidden();
    expect(phone.asked).toContain(`trips/name ${glacier.id} Glacier with Sam`);
    await expect(layer('Glacier with Sam').getByRole('heading', { name: 'Glacier with Sam', level: 2 })).toBeVisible();

    await layer('Glacier with Sam').getByRole('button', { name: 'Back' }).click();
    await trips.getByRole('button', { name: 'Change' }).click();
    const sheet = page.getByRole('dialog', { name: 'Home' });
    await sheet.getByRole('button', { name: /^Butte, Montana/ }).click();
    await expect(sheet).toBeHidden();
    expect(phone.asked).toContain('trips/home Butte');
    if (isMobile) await trips.getByRole('button', { name: 'Back' }).click();
    else await phoneTab(page, 'Library');
    await expect(trips).toBeHidden();
  });

  test('Fix dates and places gives wrong dates the day chosen and places from the nearest photo, with Undo, and has the computer name the copies by date @phone', async ({ page, isMobile }) => {
    const phone = new Phone();
    phone.fixes = {
      total: 374, dates: noFix({ count: 32, date: '2000-01-01' }), places: noFix({ count: 342, place: 'Glacier National Park', placeCount: 214 }),
      names: { computer: 'Office PC', files: 6941, style: 'asOnPhone', now: 'IMG_4821.HEIC', after: '2026-03-02 14.31.07.HEIC', stage: null, problem: null, renamed: null },
    };
    await phone.attach(page);
    // Pro, as fixing is.
    await page.route(url => url.pathname.endsWith('/api/plan'), route => route.fulfill({
      json: { tier: 'pro', kind: 'lifetime', started: null, until: null, sharedBy: null, store: 'App Store', removalsPerDay: 50, removedToday: 0, leftToday: null, offers: [] },
    }));
    await page.goto('/');
    await phoneTab(page, 'Library');
    const fixes = await phoneCollection(page, 'Needs fixing', isMobile);
    const dates = fixes.getByRole('region', { name: 'Camera clock not set' });
    await expect(dates).toContainText('32 items dated Jan 1, 2000');
    await dates.getByLabel('The date they were taken').fill('2026-03-02');
    await dates.getByRole('button', { name: 'Fix 32 dates' }).click();
    await expect(message(page, '32 dates fixed.')).toBeVisible();
    expect(phone.asked).toContain('fixes/dates {"date":"2026-03-02"}');
    await expect(dates).toContainText('Every date looks right.32 dates fixed');
    await dates.getByRole('button', { name: 'Undo' }).click();
    await expect(message(page, 'Put back 32 dates.')).toBeVisible();
    await expect(dates).toContainText('32 items dated Jan 1, 2000');

    const places = fixes.getByRole('region', { name: 'Missing location' });
    await expect(places).toContainText('214 of them get Glacier National Park.');
    await places.getByRole('button', { name: 'Add 342 locations' }).click();
    await expect(message(page, '342 locations added.')).toBeVisible();

    const files = fixes.getByRole('region', { name: 'File names on Office PC' });
    await expect(files).toContainText('IMG_4821.HEIC→2026-03-02 14.31.07.HEIC');
    await files.getByRole('button', { name: 'Rename 6,941 files' }).click();
    await expect(files).toContainText('6,941 files renamed');
    expect(phone.asked).toContain('fixes/names {"kind":"date"}');
    if (isMobile) await fixes.getByRole('button', { name: 'Back' }).click();
    else await phoneTab(page, 'Library');
    await expect(fixes).toBeHidden();
  });

  test('The hidden vault turns on with a recovery key to write down, hides the photos chosen, and puts one back @phone', async ({ page, photos, isMobile }) => {
    photos.picture('IMG_0001.JPG', { seed: 1, taken: new Date(2023, 11, 27, 23, 34) });
    photos.picture('IMG_0002.JPG', { seed: 2, taken: new Date(2023, 10, 2, 8, 0) });
    const phone = new Phone();
    phone.root = photos.root;
    await phone.attach(page);
    // Pro, as turning the vault on is.
    await page.route(url => url.pathname.endsWith('/api/plan'), route => route.fulfill({
      json: { tier: 'pro', kind: 'lifetime', started: null, until: null, sharedBy: null, store: 'App Store', removalsPerDay: 50, removedToday: 0, leftToday: null, offers: [] },
    }));
    // Library's ids, which the stand-in vault names what it hides by.
    page.on('response', async answer => {
      if (!/\/api\/organize\/files(\?|$)/.test(answer.url())) return;
      const listed = await answer.json().catch(() => null) as { files?: { id: string; name: string }[] } | null;
      for (const f of listed?.files ?? []) phone.names[f.id] = f.name;
    });
    await page.goto('/');
    await phoneTab(page, 'Library');
    const library = page.getByRole('region', { name: 'Library' });
    await expect(library.getByRole('button', { name: 'IMG_0001.JPG' })).toBeVisible({ timeout: 60_000 });
    const hidden = await phoneCollection(page, 'Hidden', isMobile);
    await expect(hidden).toContainText('Keep private photos behind Face ID.');
    await hidden.getByRole('button', { name: 'Turn on Hidden' }).click();
    const key = page.getByRole('dialog', { name: 'Recovery key' });
    await expect(key.getByLabel('Your recovery key')).toHaveText(RECOVERY_KEY);
    await key.getByRole('button', { name: 'I wrote it down' }).click();
    await expect(hidden).toContainText('Nothing is hidden yet.');

    await hidden.getByRole('button', { name: 'Add photos' }).click();
    const picker = page.getByRole('dialog', { name: 'Choose photos to hide' });
    await expect(picker).toContainText(/Then your (iPhone|iPad) asks before they leave Photos/);
    await picker.getByRole('button', { name: 'IMG_0001.JPG' }).click();
    await picker.getByRole('button', { name: 'IMG_0002.JPG' }).click();
    await picker.getByRole('button', { name: 'Hide 2 items' }).click();
    await expect(message(page, '2 items hidden.')).toBeVisible();
    await expect(picker).toBeHidden();
    await expect(hidden.getByRole('note')).toContainText('2 items are not backed up yet.');

    await hidden.getByRole('button', { name: 'IMG_0001.JPG' }).click();
    const viewer = page.getByRole('dialog', { name: 'IMG_0001.JPG' });
    await viewer.getByRole('button', { name: 'Unhide' }).click();
    await expect(message(page, 'IMG_0001.JPG is back in your library.')).toBeVisible();
    await expect(hidden.getByRole('note')).toContainText('1 item is not backed up yet.');
    await hidden.getByRole('button', { name: 'Lock' }).click();
    await expect(hidden).toContainText('Hidden is locked1 item');
    expect(phone.asked.filter(a => a.startsWith('vault/')).map(a => a.split(' ')[0])).toEqual(['vault/on', 'vault/hide', 'vault/unhide', 'vault/lock']);
  });

  test('On a tablet, the sidebar lists the collections and Settings, each shown beside it, Settings with Subscription beside it, and Back puts them away @phone', async ({ page, isMobile }) => {
    test.skip(isMobile, 'A phone shows them over its tabs.');
    await new Phone().attach(page);
    await page.goto('/');
    await phoneTab(page, 'Library');
    const tabs = page.getByRole('navigation', { name: 'Tabs' });
    const library = page.getByRole('region', { name: 'Library' });
    await expect(library.getByRole('button', { name: 'Search photos' })).toBeVisible();
    await expect(library.getByRole('button', { name: /^Trips, / })).toBeVisible();
    await tabs.getByRole('button', { name: /^Needs fixing/ }).click();
    await expect(page.getByRole('region', { name: 'Fix dates and places' })).toBeVisible();
    await expect(tabs.getByRole('button', { name: /^Needs fixing/ })).toHaveAttribute('aria-current', 'page');
    await tabs.getByRole('button', { name: 'Settings' }).click();
    const settings = page.getByRole('region', { name: 'Settings', exact: true });
    await expect(settings.getByRole('region', { name: 'Subscription' })).toBeVisible();
    await expect(settings.getByRole('button', { name: /\. Subscription$/ })).toHaveAttribute('aria-current', 'true');
    await page.goBack();
    await expect(settings).toBeHidden();
    await expect(library).toBeVisible();
  });

  test('Settings turns on a cloud service by signing in, connects a server the person names and renames, and Backup backs up to them @phone', async ({ page, isMobile }) => {
    const phone = new Phone();
    await phone.attach(page);
    await page.goto('/');
    const settings = await phoneSettings(page);

    // A cloud service: its switch signs in, as the system's sheet would, and it is on.
    const drive = settings.getByRole('switch', { name: 'Google Drive' });
    await expect(drive).not.toBeChecked();
    await settings.locator('label').filter({ has: page.getByRole('switch', { name: 'Google Drive' }) }).click();
    await expect(message(page, 'Google Drive is on.')).toBeVisible();
    await expect(drive).toBeChecked();
    await expect(settings.getByRole('button', { name: /^Google Drive.*alex\.m@example\.com/ })).toBeVisible();

    // A server: named by the person, checked, and on.
    await settings.getByRole('button', { name: /^Connect to server/ }).click();
    const connect = page.getByRole('dialog', { name: 'Connect to server' });
    await connect.getByRole('radio', { name: 'WebDAV' }).click();
    await connect.getByRole('textbox', { name: 'Name', exact: true }).fill('Home NAS');
    await connect.getByRole('textbox', { name: 'Server URL' }).fill('https://nas.local/dav');
    await connect.getByRole('textbox', { name: 'User name' }).fill('alex');
    await connect.getByLabel('Password').fill('not-a-real-password');
    await connect.getByRole('button', { name: 'Connect', exact: true }).click();
    await expect(message(page, 'Home NAS is connected and on.')).toBeVisible();
    expect(phone.asked.find(a => a.startsWith('locations/connect'))).toContain('"kind":"webDav","name":"Home NAS"');
    await expect(settings.getByRole('switch', { name: 'Home NAS' })).toBeChecked();

    // Renamed whenever the person likes, from its own page.
    await settings.getByRole('button', { name: /^Home NAS/ }).click();
    await page.getByRole('dialog', { name: 'Home NAS' }).getByRole('button', { name: 'Rename' }).click();
    const naming = page.getByRole('dialog', { name: 'Rename location' });
    await naming.getByLabel('Name').fill('Attic NAS');
    await naming.getByRole('button', { name: 'Save' }).click();
    await expect(message(page, 'Renamed Attic NAS.')).toBeVisible();
    const attic = page.getByRole('dialog', { name: 'Attic NAS' });
    await expect(attic.getByText('Files at Attic NAS are not deleted.', { exact: false })).toBeVisible();
    await attic.getByRole('button', { name: 'Back' }).click();
    await expect(settings.getByRole('switch', { name: 'Attic NAS' })).toBeChecked();

    // Backup lists the two, and backs up to both, each copy checked.
    await leaveSettings(page, settings, isMobile, 'Backup');
    if (isMobile) await phoneTab(page, 'Backup');
    const card = page.getByRole('region', { name: 'Locations' });
    await expect(card).toContainText('Google Drive');
    await expect(card).toContainText('Attic NAS');
    if (isMobile) {
      await card.locator('xpath=..').getByRole('button', { name: 'Back up now' }).click();
    } else {
      // A tablet lists every location, the first on beside the list with its own Back up now (T6).
      await expect(card.getByRole('button', { name: /^Google Drive/ })).toHaveAttribute('aria-current', 'true');
      await page.getByRole('region', { name: 'Google Drive details' }).getByRole('button', { name: 'Back up now' }).click();
    }
    await expect(message(page, 'Backing up to 2 locations. Every copy is checked.')).toBeVisible();
    expect(phone.asked.some(a => a.startsWith('locations/backup'))).toBe(true);
    if (isMobile) await expect(card).toContainText('2 files · backed up today');
    else await expect(page.getByRole('region', { name: 'Google Drive details' })).toContainText('Checked copies 2 files');

    // On a tablet, Connect to server and the server's page show beside the list too (T7).
    if (!isMobile) {
      await card.getByRole('button', { name: /^Attic NAS/ }).click();
      await expect(page.getByRole('region', { name: 'Attic NAS details' })).toContainText('WebDAV');
      await card.getByRole('button', { name: /^Connect to server/ }).click();
      const beside = page.getByRole('region', { name: 'Connect to server' });
      await expect(beside.getByRole('radio', { name: 'SFTP' })).toBeVisible();
      await beside.getByRole('button', { name: 'Cancel' }).click();
      await expect(beside).toBeHidden();
    }
  });

  test('The app icon menu and the widgets open Search, today’s swipe set, or Backup with a backup started @phone', async ({ page }) => {
    const phone = new Phone();
    await phone.attach(page);
    await page.goto('/');
    await phoneTab(page, 'Library');
    const tapped = async (opens: string) => {
      phone.opens = opens;
      await page.evaluate(() => window.dispatchEvent(new Event('trovolve-notice')));
    };
    await tapped('search');
    const search = page.getByRole('dialog', { name: 'Search' });
    await expect(search).toBeVisible();
    await search.getByRole('button', { name: /^(Cancel|Back)$/ }).click();
    await expect(search).toBeHidden();
    await tapped('backup');
    await expect(page.getByRole('heading', { level: 1, name: 'Backup' })).toBeVisible();
    await tapped('swipe');
    await expect(page.getByRole('heading', { level: 1, name: 'Clean up' })).toBeVisible();
  });

  test('Swipe to clean shows today\'s set one at a time, marks by a drag or a button, takes back the last, and deletes what is marked in one confirmation @phone', async ({ page, photos, isMobile }) => {
    photos.picture('IMG_0001.JPG', { seed: 1 });
    photos.picture('IMG_0002.JPG', { seed: 2 });
    const phone = new Phone();
    phone.root = photos.root;
    await phone.attach(page);
    await page.goto('/');
    const panel = page.getByRole('region', { name: 'Swipe to clean' });
    await expect(panel).toContainText('Today\u2019s set of 2 photos is ready');
    await panel.getByRole('button', { name: 'Start' }).click();

    // Dragged far enough to the left, the photo is marked to delete; nothing is deleted yet.
    const swiping = page.getByRole('dialog', { name: 'Swipe to clean' });
    await expect(swiping).toContainText('1 of 2');
    // Under the buttons on a phone; on a tablet, the panel beside them says what is marked.
    await expect(swiping).toContainText(isMobile ? 'Swipe left to delete, right to keep' : '0 items so far · confirmed at the end');
    const card = swiping.getByRole('img', { name: /^IMG_000/ });
    // Hovered first, which waits for the card to have settled where it is drawn.
    await card.hover();
    const box = (await card.boundingBox())!;
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    await page.mouse.down();
    await page.mouse.move(box.x + box.width / 2 - 160, box.y + box.height / 2, { steps: 8 });
    await page.mouse.up();
    await expect(swiping).toContainText('2 of 2');
    await expect(swiping).toContainText(isMobile ? /to free from 1 item/ : '1 item so far · confirmed at the end');
    await swiping.getByRole('button', { name: 'Undo' }).click();
    await expect(swiping).toContainText('1 of 2');
    await swiping.getByRole('button', { name: 'Delete' }).click();
    await swiping.getByRole('button', { name: 'Keep' }).click();

    // The set through, the summary: one marked, deleted after one confirmation.
    const summary = page.getByRole('dialog', { name: 'Swipe summary' });
    await expect(summary).toContainText('Today\u2019s set is done');
    await expect(summary).toContainText('ready to free from 1 item');
    await expect(summary).toContainText('1-day streak');
    await expect(summary.getByRole('region', { name: 'Marked to delete' }).getByRole('button')).toHaveCount(1);
    await summary.getByRole('button', { name: 'Delete 1 item' }).click();
    await expect(summary).toContainText('Space freed');
    await expect(summary).toContainText('A new set is ready tomorrow.');
    expect(phone.asked.filter(a => a.startsWith('swipe/')).map(a => a.split(' ')[0])).toEqual(['swipe/decide', 'swipe/undo', 'swipe/decide', 'swipe/decide', 'swipe/delete']);
    await summary.getByRole('button', { name: 'Done' }).click();
    await expect(panel).toContainText('Done for today. A new set is ready tomorrow.');
  });

  test('Library shows every photo by month; chosen ones go into a new album, or are deleted after a question saying where they wait @phone', async ({ page, photos }) => {
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
    await expect(viewer).toContainText('Dec 27, 2023No location · 11:34 PM');
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
    await expect(asked).toContainText('IMG_0002.JPG goes to Recently Deleted for 30 days.');
    await asked.getByRole('button', { name: 'Delete 1 item' }).click();
    await expect(message(page, '1 item deleted. It waits in Recently Deleted for 30 days.')).toBeVisible();
    // The program standing in for the phone is a computer's, which holds it in _Trovolve_Removed.
    expect(photos.exists('IMG_0002.JPG')).toBe(false);
  });

  test('Help, from Settings, is the phone\'s own guide, in its words, and finds a feature by the words a person would use @phone', async ({ page }) => {
    await new Phone().attach(page);
    await page.goto('/');
    const settings = await phoneSettings(page);
    await settings.getByRole('button', { name: 'Help' }).click();
    const help = page.getByRole('dialog', { name: 'Help' });
    const topics = help.getByRole('navigation', { name: 'Topics' }).getByRole('button');
    await expect(topics).toHaveCount(10);
    await expect(help.getByRole('article')).toHaveAccessibleName('Getting started');
    await expect(help.getByRole('article')).toContainText('The first launch');
    // An iPhone's words: albums, Trovolve Removed and Recently Deleted; nothing of a computer's screens.
    await help.getByRole('navigation', { name: 'Topics' }).getByRole('button', { name: /^Deleted photos/ }).click();
    await expect(help.getByRole('article')).toContainText('the album Trovolve Removed');
    await expect(help.getByRole('article')).toContainText('Recently Deleted');
    await expect(help.getByRole('article')).not.toContainText('_Trovolve_Removed');
    await help.getByRole('textbox', { name: 'Search the guide' }).fill('cancel subscription');
    await expect(topics).toHaveCount(1);
    await expect(help.getByRole('article')).toHaveAccessibleName('The free plan and Pro');
    await expect(help.getByRole('article')).toContainText('Manage in App Store');
  });

  test('What waits in Trovolve Removed from before is offered once at launch, and Settings moves it to Recently Deleted @phone', async ({ page, photos }) => {
    photos.picture('IMG_0001.JPG', { seed: 1 });
    photos.picture('_Trovolve_Removed/IMG_0002.JPG', { seed: 2 });
    const phone = new Phone();
    phone.root = photos.root;
    await phone.attach(page);
    await page.goto('/');
    const offer = page.getByRole('dialog', { name: '1 item waits in Trovolve Removed' });
    await expect(offer).toContainText('Move it to Recently Deleted');
    await expect(offer).toContainText('Trovolve now deletes straight to Recently Deleted, so nothing new goes to Trovolve Removed.');
    await offer.getByRole('button', { name: 'Not now' }).click();
    await expect(offer).toBeHidden();
    expect(photos.exists('_Trovolve_Removed/IMG_0002.JPG')).toBe(true);

    // Offered once only: Settings offers it from then on.
    await page.reload();
    let settings = await phoneSettings(page);
    await expect(page.getByRole('dialog', { name: /waits? in Trovolve Removed$/ })).toHaveCount(0);
    await expect(settings.getByText('Trovolve Removed').locator('xpath=ancestor::li')).toContainText(/1 item · \d+ KB/);
    await expect(settings.getByRole('button', { name: /^Photo access\s*Full access$/ })).toBeVisible();
    await settings.getByRole('button', { name: 'Move to Recently Deleted…' }).click();
    await page.getByRole('dialog', { name: '1 item waits in Trovolve Removed' }).getByRole('button', { name: 'Move 1 item' }).click();
    await expect(message(page, '1 item deleted. They wait in Recently Deleted for 30 days.')).toBeVisible();
    expect(photos.exists('_Trovolve_Removed/IMG_0002.JPG')).toBe(false);
    expect(photos.exists('IMG_0001.JPG')).toBe(true);

    settings = await phoneSettings(page);
    await expect(settings.getByRole('button', { name: /^Recently moved/ })).toBeVisible();
    await expect(settings.getByText('Trovolve Removed')).toHaveCount(0);
  });

  test('About, from Settings, names the phone app, which has no menu @phone', async ({ page }) => {
    await new Phone().attach(page);
    await page.goto('/');
    const settings = await phoneSettings(page);
    await expect(page.getByRole('button', { name: 'Menu' })).toBeHidden();
    await settings.getByRole('button', { name: /^About Trovolve/ }).click();
    await expect(page.getByRole('dialog', { name: 'About Trovolve' }).getByRole('article')).toContainText(/Version [\d.]+ for iPhone and iPad/);
  });

  test('On the free plan, Organize shows its suggestions with their real counts, and Move opens the Pro sheet, moving nothing @phone', async ({ page, photos }) => {
    photos.picture('IMG_0001.JPG', { seed: 1, at: PLACES.butte, taken: new Date(2023, 6, 4, 10, 0) });
    photos.picture('IMG_0002.JPG', { seed: 2, at: PLACES.butte, taken: new Date(2022, 6, 4, 10, 0) });
    const phone = new Phone();
    phone.root = photos.root;
    await phone.attach(page);
    await page.goto('/');
    await phoneTab(page, 'Organize');
    const list = page.getByRole('list', { name: 'Suggestions' });
    await expect(list.getByRole('button', { name: /^Butte/ })).toContainText('2 files', { timeout: 60_000 });
    await list.getByRole('button', { name: /^Butte/ }).click();
    await page.getByRole('main').getByRole('button', { name: 'Move 2 files' }).click();
    await expect(page.getByRole('dialog', { name: 'Trovolve Pro' })).toContainText('Organizing by place and date is part of Pro. Choosing photos yourself in Library and moving them stays free.');
    expect(photos.exists('IMG_0001.JPG')).toBe(true);
    expect(photos.exists('Butte')).toBe(false);
  });

  test('Organize suggests an album for each place, moves what is in none yet, numbering a name already taken there, and Undo puts it back @phone', async ({ page, photos, isMobile }) => {
    photos.picture('IMG_0001.JPG', { seed: 1, at: PLACES.butte, taken: new Date(2023, 6, 4, 10, 0) });
    photos.picture('IMG_0002.JPG', { seed: 2, at: PLACES.butte, taken: new Date(2022, 6, 4, 10, 0) });
    // Already in an album: left out of the suggestions, and its name taken where the first is going.
    photos.picture('Butte/2023/IMG_0001.JPG', { seed: 3, at: PLACES.butte, taken: new Date(2023, 6, 5, 10, 0) });
    const phone = new Phone();
    phone.root = photos.root;
    await phone.attach(page);
    // Organizing by place and date is part of Pro: this phone has it. The program standing in for the phone is a
    // computer's, on the free plan, so the suggestion goes to it as a move of the person's own.
    await page.route(url => url.pathname.endsWith('/api/plan'), route => route.fulfill({ json: { tier: 'pro', kind: 'lifetime', started: null, until: null, sharedBy: null, store: 'App Store', removalsPerDay: 50, removedToday: 0, leftToday: null, offers: [] } }));
    await page.route(url => url.pathname.endsWith('/api/organize/apply'), route => route.continue({ postData: JSON.stringify({ ...route.request().postDataJSON(), suggested: false }) }));
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
    await leaveSettings(page, settings, isMobile, 'Organize');
    await expect(page.getByText(/^Within 5 km · /)).toBeVisible();
  });

  test('Backup says what waits on the computer, backs up, pauses and carries on, and Send now sends what is chosen in Library, or beside the list on a tablet @phone', async ({ page, photos, isMobile }) => {
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
    const card = backup.getByRole('region', { name: 'DESKTOP-1', exact: true });
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
    await expect(page.getByRole('dialog', { name: 'Trovolve Pro' })).toContainText('Automatic backup is part of Pro. Sending photos yourself stays free.');
    await page.keyboard.press('Escape');
    expect(phone.asked.filter(a => a.startsWith('settings'))).toHaveLength(0);
    await backup.getByText('Include videos').click();
    await expect.poll(() => phone.asked.filter(a => a.startsWith('settings'))).toEqual([expect.stringContaining('"includeVideos":false')]);

    phone.set({ stage: 'ready', waiting: 2 });
    await backup.getByRole('button', { name: 'Choose photos to send' }).click();
    if (isMobile) {
      const library = page.getByRole('region', { name: 'Library' });
      await expect(page.getByRole('toolbar', { name: 'What is selected' })).toBeVisible();
      await library.getByRole('button', { name: 'IMG_0001.JPG' }).click();
      await page.getByRole('toolbar', { name: 'What is selected' }).getByRole('button', { name: 'Send' }).click();
    } else {
      // A tablet chooses beside Backup's list (T8).
      const picker = page.getByRole('region', { name: 'Choose photos' });
      await picker.getByRole('button', { name: 'IMG_0001.JPG' }).click();
      await expect(picker).toContainText('1 selected');
      await picker.getByRole('button', { name: 'Send to…' }).click();
    }
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
    await expect(freed).toContainText(/2 items removed from this (iPhone|iPad)\. The copies are on DESKTOP-1 in Pictures\/Trovolve\/Jamie's iPhone\./);
    await expect(freed).toContainText('Before112 of 128 GB');
    await expect(freed).toContainText('Now106 of 128 GB');
    expect(phone.asked).toContain('remove');
    await freed.getByRole('button', { name: 'Done' }).click();
    await expect(freed).toBeHidden();
    await expect(page.getByRole('region', { name: 'Library' })).toBeVisible();
  });
});
