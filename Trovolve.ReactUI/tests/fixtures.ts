import type { BackupPhoneDto, BackupSettingsDto, BackupStateDto, SafeReviewDto, DuplicateGroupDto, GroupMemberDto, GroupMode, GroupsPageDto, MatchKind, OrganizeFileDto, OrganizePlaceDto, PhotoDto, PlanDto, PlanOfferDto, SwipeCardDto, SwipeStateDto, LocationDto, LocationsDto } from '../types';

/** A picture as the server describes it; tests override only what they are about. */
export function photo(overrides: Partial<PhotoDto> = {}): PhotoDto {
  return {
    id: 'p1',
    fileName: 'IMG_4198.JPG',
    sourcePath: 'C:\\photos\\2024\\IMG_4198.JPG',
    folder: 'C:\\photos\\2024',
    fileSizeBytes: 6_093_000,
    width: 4032,
    height: 3024,
    format: 'JPEG',
    isVideo: false,
    set: 'Primary',
    kept: false,
    ...overrides,
  };
}

export function video(overrides: Partial<PhotoDto> = {}): PhotoDto {
  return photo({ id: 'v1', fileName: 'IMG_0042.MOV', format: 'MOV', isVideo: true, width: 1920, height: 1080, durationSeconds: 84, ...overrides });
}

export function member(photoOverrides: Partial<PhotoDto> = {}, match: MatchKind = 'identical', keeperReason = 'Same quality; kept the one with the plainer name'): GroupMemberDto {
  return { photo: photo({ id: 'm1', fileName: 'IMG_4198 (1).JPG', ...photoOverrides }), match, keeperReason };
}

export function group(overrides: Partial<DuplicateGroupDto> = {}): DuplicateGroupDto {
  return { key: 'g1', keeper: photo(), members: [member()], reclaimableBytes: 6_093_000, ...overrides };
}

export function groupsPage(items: DuplicateGroupDto[], overrides: Partial<GroupsPageDto> = {}, mode: GroupMode = 'duplicates'): GroupsPageDto {
  return {
    mode,
    page: 1,
    pageSize: 50,
    total: items.length,
    totalPages: 1,
    removableCount: items.reduce((n, g) => n + g.members.filter(m => !m.photo.kept).length, 0),
    reclaimableBytes: items.reduce((n, g) => n + g.reclaimableBytes, 0),
    items,
    ...overrides,
  };
}

/** A response as fetch resolves it, for standing in for the server. */
export function reply(body?: unknown, status = 200): Response {
  if (body === undefined) return new Response(null, { status });
  return new Response(typeof body === 'string' ? body : JSON.stringify(body), { status });
}

/** A file as the Organize listing describes it. */
export function organizeFile(overrides: Partial<OrganizeFileDto> = {}): OrganizeFileDto {
  return { id: 'o1', name: 'IMG_4198.JPG', folder: 'C:\photos', sizeBytes: 6_093_000, isVideo: false, date: '2023-06-09T14:03:22', dateFromFile: false, width: 4032, height: 3024, ...overrides };
}

export function organizePlace(overrides: Partial<OrganizePlaceDto> = {}): OrganizePlaceDto {
  return { town: 'Anaconda', state: 'Montana', country: 'US', latitude: 46.1283, longitude: -112.9423, ...overrides };
}

/** What the store sells, at the prices the design shows. */
export const OFFERS: PlanOfferDto[] = [
  { kind: 'yearly', price: '$19.99', amount: 19.99, currency: 'USD' },
  { kind: 'monthly', price: '$2.99', amount: 2.99, currency: 'USD' },
  { kind: 'lifetime', price: '$39.00', amount: 39, currency: 'USD' },
  { kind: 'family', price: '$29.99', amount: 29.99, currency: 'USD' },
];

/** A plan as the service says it: the free plan with 38 of 50 left today, on an iPhone, unless the test says otherwise. */
export function plan(overrides: Partial<PlanDto> = {}): PlanDto {
  return { tier: 'free', kind: null, started: null, until: null, sharedBy: null, store: 'App Store', removalsPerDay: 50, removedToday: 12, leftToday: 38, offers: OFFERS, testPlan: null, ...overrides };
}

/** How a phone backs up as it starts: by hand, with videos, keeping favorites and the last 30 days. */
export const BACKUP_SETTINGS: BackupSettingsDto = { automatic: false, onlyWhileCharging: true, includeVideos: true, keepFavorites: true, keepRecent: true };

/** A phone paired with Studio iMac, with 380 items waiting; tests override only what they are about. */
export function backupState(overrides: Partial<BackupStateDto> = {}): BackupStateDto {
  return {
    stage: 'ready', computer: 'Studio iMac', folder: "Pictures/Trovolve/Alex's iPhone", waiting: 380, waitingBytes: 2.1e9, onComputer: 6561, done: 0, total: 0, bytes: 0, totalBytes: 0,
    lastChecked: null, lastBackup: null, strip: [], problem: null, settings: BACKUP_SETTINGS, ...overrides,
  };
}

/** What Safe to delete could free on a 128 GB phone with 16 GB free; tests override only what they are about. */
export function safeReview(overrides: Partial<SafeReviewDto> = {}): SafeReviewDto {
  return {
    computer: 'Studio iMac', folder: "Pictures/Trovolve/Alex's iPhone", lastChecked: null, checked: { count: 6561, bytes: 31.1e9 }, favorites: { count: 214, bytes: 1.1e9 },
    recent: { count: 349, bytes: 1.9e9 }, removable: { count: 5998, bytes: 28.1e9 }, storageTotal: 128e9, storageFree: 16e9, cloudPhotos: false, settings: BACKUP_SETTINGS, ...overrides,
  };
}

/** A phone backing up to this computer, as Phones lists it; tests override only what they are about. */
export function backupPhone(overrides: Partial<BackupPhoneDto> = {}): BackupPhoneDto {
  return {
    id: 'phone-1', name: "Alex's iPhone", kind: 'iPhone', folder: "Pictures/Trovolve/Alex's iPhone", path: "C:\\Users\\alex\\Pictures\\Trovolve\\Alex's iPhone", paused: false,
    paired: '2026-11-10T09:00:00Z', lastBackup: null, lastChecked: null, held: 6747, heldBytes: 32.1e9, safe: 5998, safeBytes: 28.1e9, tripFolders: 0, tripsFolder: 'Pictures/Trovolve', names: 'asOnPhone', tripsPath: 'C:\\Users\\alex\\Pictures\\Trovolve', arriving: null, events: [], ...overrides,
  };
}


/** A photo of Swipe to clean, not swiped yet, of a megabyte; tests override only what they are about. */
export function swipeCard(id: string, overrides: Partial<SwipeCardDto> = {}): SwipeCardDto {
  return { id, bytes: 1024 ** 2, onComputer: false, delete: null, spared: false, ...overrides };
}

/** Swipe to clean on Tuesday 17 November 2026, with the set given, nothing swiped this week and nothing freed. */
export function swipeState(overrides: Partial<SwipeStateDto> = {}): SwipeStateDto {
  const week = ['2026-11-11', '2026-11-12', '2026-11-13', '2026-11-14', '2026-11-15', '2026-11-16', '2026-11-17'].map(date => ({ date, swiped: false }));
  return { date: '2026-11-17', set: [], earlier: [], week, streak: 0, freed: null, unswiped: 0, ...overrides };
}

/** A place a phone backs up to, as the service lists it: Google Drive, not signed in to, unless the test says otherwise. */
export const location = (more: Partial<LocationDto> = {}): LocationDto => ({
  id: 'google-drive', kind: 'googleDrive', name: 'Google Drive', on: false, connected: false, available: true, account: null, settings: {}, copies: 0,
  lastBackup: null, lastChecked: null, problem: null, ...more,
});

/** The locations as the service lists them: the computer paired and on, and the three cloud services, unless the test says otherwise. */
export const locations = (more: Partial<LocationsDto> = {}): LocationsDto => ({
  locations: [
    location({ id: 'computer', kind: 'computer', name: 'Studio iMac', on: true, connected: true, settings: { folder: 'Pictures/Trovolve' }, copies: 6941, lastBackup: '2026-11-19T08:00:00Z' }),
    location(), location({ id: 'onedrive', kind: 'oneDrive', name: 'OneDrive' }), location({ id: 'dropbox', kind: 'dropbox', name: 'Dropbox' }),
  ],
  countAt: 1, pro: false, activity: null, held: null, ...more,
});
