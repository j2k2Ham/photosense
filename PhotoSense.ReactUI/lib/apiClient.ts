import useSWR, { mutate } from 'swr';
import type {
  AboutDocument, AboutDto, BackupPhoneDto, BackupSettingsDto, BackupStateDto, BulkRemovalResultDto, EraseRemovedDto, FolderListingDto, GroupMode, GroupsPageDto, OrganizeApplyDto, OrganizeApplyRequest, OrganizeBatchDto, OrganizeDestination, OrganizeFilesDto, OrganizePlanDto,
  OrganizePlanFile, OrganizeProgressDto, OrganizeUndoDto, PairingStateDto, PhotoAccessDto, PlanBuyResultDto, PlanDto, PlanKind, FindingsDto, RemovePhotoResultDto, RemovedFilesDto, ScanProgressSnapshotDto, ScanStatusDto, StartScanRequest, LibraryBackupDto, SafeRemovalDto, SafeReviewDto, SwipeFreedDto, SwipeStateDto, BurstDto, CategoryName, CategoryRemovalDto, CategoryReportDto, PhotoDto,
  CompressibleVideoDto, CompressionQuality, CompressionStateDto, NoticeOpenedDto, NoticeStateDto, YearReviewDto, PhotoDetailsDto, SearchResultDto,
  TownDto, TripDetailDto, TripsDto, FileNaming, FixesDto, FixResultDto, RenamingDto, VaultDto, LocationsDto, LocationDetailsDto, ServerConnectionDto,
} from '../types';

import { API_BASE } from './apiBase';

export { appIconUrl } from './apiBase';

const API_KEY = process.env.NEXT_PUBLIC_API_KEY; // provided via env when auth enforced
// The server acts on photos only for requests carrying this header, which other web sites cannot send.
const authHeaders: Record<string, string> = { 'x-photosense-client': 'web', ...(API_KEY ? { 'x-api-key': API_KEY } : {}) };

/** A refusal from the service, with its status: 402 when what was asked for is part of Pro. */
export type ApiError = Error & { readonly status: number };

// The server answers failures with either plain text or { error }.
async function failure(res: Response): Promise<ApiError> {
  const text = await res.text();
  let said: string;
  try { said = JSON.parse(text).error ?? text; } catch { said = text || `Request failed (${res.status})`; }
  return Object.assign(new Error(said), { status: res.status });
}

// Android's web view hands the app no request's body, so there the page hands each body over beside its request,
// and the request carries the number the app gave it.
const android = (globalThis as { PhotoSenseAndroid?: { keep(body: string): string } }).PhotoSenseAndroid;

async function json<T>(url: string, init?: RequestInit): Promise<T> {
  const carried: Record<string, string> = android && typeof init?.body === 'string' ? { 'x-photosense-body': android.keep(init.body) } : {};
  const res = await fetch(url, { ...init, headers: { 'Content-Type': 'application/json', ...authHeaders, ...carried, ...init?.headers } });
  if (!res.ok) throw await failure(res);
  return res.json();
}

async function send(url: string, method: 'POST' | 'DELETE'): Promise<Response> {
  const res = await fetch(url, { method, headers: authHeaders });
  if (!res.ok) throw await failure(res);
  return res;
}

const planUrl = `${API_BASE}/plan`;
// A removal counts toward the free plan's limit each day, so the plan is asked again after one, with the groups and
// Clean up's categories, which leave out what is gone or marked keep.
const refreshGroups = () => mutate((key: unknown) => typeof key === 'string' && (key.includes('/scan/groups') || key.includes('/scan/status') || key.includes('/cleanup/categories') || key === planUrl));

/** Asks again for everything on screen, for when the service has been out of reach. */
export const retryNow = () => mutate(() => true);

/** Who makes PhotoSense, how to reach them, and the program answering with its version. */
export function useAbout() {
  return useSWR<AboutDto>(`${API_BASE}/about`, json);
}

async function text(url: string): Promise<string> {
  const res = await fetch(url);
  if (!res.ok) throw await failure(res);
  return res.text();
}

/** One of the documents About shows: the privacy policy, terms or support page (Markdown), or the third-party notices. */
export function useAboutText(document: AboutDocument) {
  return useSWR<string>(`${API_BASE}/about/${document}`, text);
}

const accessUrl = `${API_BASE}/library/access`;

/** How much of a phone's photos PhotoSense may see; on a computer, always everything. */
export function usePhotoAccess() {
  return useSWR<PhotoAccessDto>(accessUrl, json);
}

/** Asks the phone's own question of what PhotoSense may see, and answers once it is answered. */
export async function askForPhotos(): Promise<PhotoAccessDto> {
  const access = await json<PhotoAccessDto>(`${API_BASE}/library/ask`, { method: 'POST' });
  await mutate(accessUrl, access, { revalidate: false });
  return access;
}

/** Shows the phone's own picker for choosing more photos, and answers once it is put away. */
export async function chooseMorePhotos(): Promise<PhotoAccessDto> {
  const access = await json<PhotoAccessDto>(`${API_BASE}/library/choose-more`, { method: 'POST' });
  await mutate(accessUrl, access, { revalidate: false });
  // What the library holds has changed, and with it what Organize lists.
  await mutate((key: unknown) => typeof key === 'string' && key.includes('/organize/files?'));
  return access;
}

/** Opens PhotoSense's own page of the phone's settings, where its access to the photos is changed. */
export async function openPhotoSettings() {
  await send(`${API_BASE}/library/open-settings`, 'POST');
}

/** The address of a scanned photo's preview. */
export const thumbnailUrl = (id: string) => `${API_BASE}/photos/${id}/thumbnail`;
/** The address of a scanned photo, at a size a browser can show. */
export const imageUrl = (id: string) => `${API_BASE}/photos/${id}/image`;
/** The address of a scanned video, which a browser plays a part at a time. */
export const videoUrl = (id: string) => `${API_BASE}/photos/${id}/video`;

/** Opens the file in the default viewer or player of the machine the server runs on. */
export async function openInViewer(id: string){
  await send(`${API_BASE}/photos/${id}/open`, 'POST');
}

/** Groups on one page of the gallery. */
export const PAGE_SIZE = 50;

const groupsUrl = (mode: GroupMode, filter: string, page: number, hideKept: boolean) =>
  `${API_BASE}/scan/groups?mode=${mode}&page=${page}&pageSize=${PAGE_SIZE}&hideKept=${hideKept}&q=${encodeURIComponent(filter)}`;

/** A page of the groups a scan found, asked again every five seconds, keeping the last page while the next comes. */
export function useGroups(mode: GroupMode, filter: string, page: number, hideKept: boolean) {
  return useSWR<GroupsPageDto>(groupsUrl(mode, filter, page, hideKept), json, { refreshInterval: 5000, keepPreviousData: true });
}

/** Groups to a page on a phone, the most the service gives at once. */
export const PHONE_PAGE = 200;

/**
 * The groups of one kind from the first page through the one given, as one list, asked again every five seconds: a
 * phone's list grows as it is scrolled. A group that moved from one page to the next as they were read is listed once.
 */
export function useGroupsThrough(mode: GroupMode, pages: number) {
  const page = (n: number) => `${API_BASE}/scan/groups?mode=${mode}&page=${n}&pageSize=${PHONE_PAGE}&hideKept=false&q=`;
  return useSWR<GroupsPageDto>(`${API_BASE}/scan/groups?mode=${mode}&pageSize=${PHONE_PAGE}&through=${pages}`, async () => {
    const read = await Promise.all(Array.from({ length: pages }, (_, i) => json<GroupsPageDto>(page(i + 1))));
    const seen = new Set<string>();
    return { ...read[0], items: read.flatMap(p => p.items).filter(g => !seen.has(g.key) && !!seen.add(g.key)) };
  }, { refreshInterval: 5000, keepPreviousData: true });
}

/**
 * Fetches a page of groups before it is asked for, with the pictures its tiles show, so that turning to
 * it shows it at once. The page's own request finds this waiting, shows it, and still asks for itself.
 */
export async function prefetchGroups(mode: GroupMode, filter: string, page: number, hideKept: boolean): Promise<void> {
  const url = groupsUrl(mode, filter, page, hideKept);
  try {
    const ahead = json<GroupsPageDto>(url);
    await mutate(url, ahead, { revalidate: false });
    for (const group of (await ahead).items) if (!group.keeper.isVideo) new Image().src = thumbnailUrl(group.keeper.id);
  } catch {
    // Only a head start: the page asks for itself when it is turned to, and reports what goes wrong then.
  }
}

/** Follows the scan's log, asking every second and a half for the lines logged since it last asked. */
export function connectLogStream(onLine: (l: string)=>void) {
  let disposed = false;
  let pollTimer: ReturnType<typeof setTimeout> | undefined;
  const poll = async () => {
    try {
      const res = await fetch(`${API_BASE}/scan/logs?limit=200`, { headers: authHeaders });
      if (res.ok) {
        const json = await res.json();
        for (const i of json.items ?? []) onLine(`${i.timestamp} ${i.level} ${i.message}`);
      }
    } catch {/* asked again in a moment */}
    // Dismissed while this poll was in flight: it was the last.
    if (!disposed) pollTimer = setTimeout(poll, 1500);
  };
  void poll();
  return () => {
    disposed = true;
    clearTimeout(pollTimer);
  };
}

/** Marks a photo to keep (bulk removal skips it), or clears the mark. */
export async function setKept(id: string, kept: boolean){
  await send(`${API_BASE}/photos/${id}/keep?kept=${kept}`, 'POST');
  await refreshGroups();
}

/** Moves one file, and the sidecars and Live Photo video that belong to it alone, to the holding folder. */
export async function removePhoto(id: string): Promise<RemovePhotoResultDto> {
  const res = await send(`${API_BASE}/photos/${id}?physical=true`, 'DELETE');
  const result = await res.json();
  await refreshGroups();
  return result;
}

/**
 * Moves the duplicates of one group, or of every group, to the holding folder. Asked for a group's similar
 * shots instead, it moves those: they go a group at a time, never all at once.
 */
export async function removeDuplicates(groupKey?: string, mode: GroupMode = 'duplicates'): Promise<BulkRemovalResultDto> {
  // The kind is always said: asked for similar shots with no group named, the service refuses, where a
  // request that left the kind out would be taken as "every duplicate".
  const query = [groupKey && `group=${encodeURIComponent(groupKey)}`, mode === 'similar' && 'mode=similar'].filter(Boolean).join('&');
  const res = await send(`${API_BASE}/photos/bulk/remove-duplicates${query && `?${query}`}`, 'POST');
  const result = await res.json();
  await refreshGroups();
  return result;
}

/**
 * Moves the duplicates of each of the groups named to the holding folder, or their similar shots when
 * asked for those, as one change: should one file not move, none does. The best of each group stays.
 */
export async function removeGroups(groupKeys: string[], mode: GroupMode): Promise<BulkRemovalResultDto> {
  // With no group named the service would take every duplicate.
  if (groupKeys.length === 0) return { removed: 0, bytes: 0, skipped: 0, companions: 0, problems: [] };
  // Named in the body, as more of them than an address holds can be ticked.
  const result = await json<BulkRemovalResultDto>(`${API_BASE}/photos/bulk/remove-duplicates${mode === 'similar' ? '?mode=similar' : ''}`,
    { method: 'POST', body: JSON.stringify({ groups: groupKeys }) });
  await refreshGroups();
  return result;
}

/** Forgets everything earlier scans recorded. The photos themselves are not touched. */
export async function clearResults(): Promise<{ forgotten: number }> {
  const res = await send(`${API_BASE}/scan/reset`, 'POST');
  const result = await res.json();
  await refreshGroups();
  return result;
}

/** How many groups of each kind there are now, and how many files are copies, asked afresh: for saying what a scan found. */
export async function fetchGroupTotals(): Promise<{ duplicates: number; similar: number; copies: number }> {
  const first = (mode: GroupMode) => json<GroupsPageDto>(groupsUrl(mode, '', 1, false));
  const [duplicates, similar] = await Promise.all([first('duplicates'), first('similar')]);
  return { duplicates: duplicates.total, similar: similar.total, copies: duplicates.removableCount };
}

/** How many files are on record, and whether the last scan has finished. */
export function useScanStatus() {
  return useSWR<ScanStatusDto>(`${API_BASE}/scan/status`, json, { refreshInterval: 5000 });
}

/** How far a scan has got, asked every second and a half; nothing is asked while no scan is named. */
export function useScanProgress(instanceId?: string) {
  const key = instanceId ? `${API_BASE}/scan/progress/${instanceId}` : null;
  return useSWR<ScanProgressSnapshotDto>(key, json, { refreshInterval: 1500 });
}

/** Lists the folders inside a folder of the computer the service runs on; with no path, the places to start from. */
export function browseFolders(path?: string) {
  return json<FolderListingDto>(`${API_BASE}/folders${path ? `?path=${encodeURIComponent(path)}` : ''}`);
}

/** Starts a scan of the folders asked for, and gives the id it runs under. */
export async function startScan(req: StartScanRequest) {
  return json<{ instanceId: string }>(`${API_BASE}/scan/start`, { method: 'POST', body: JSON.stringify(req) });
}

/** Stops the scan named; what it has read so far is kept, and the next scan carries on from there. */
export async function stopScan(instanceId: string) {
  await send(`${API_BASE}/scan/stop/${encodeURIComponent(instanceId)}`, 'POST');
}

/** On a phone, the one folder its photos are shown as: the Photo Library on an iPhone, the Phone on Android. */
export async function libraryRoot(): Promise<string> {
  const top = (await browseFolders()).folders[0];
  if (!top) throw new Error('The phone did not say where its photos are.');
  return top.path;
}

// ---- Organize

/** The address of the preview of a file in the folder being organized. */
export const organizeThumbnailUrl = (id: string) => `${API_BASE}/organize/files/${id}/thumbnail`;
/** The address of a file in the folder being organized, at a size a browser can show. */
export const organizeImageUrl = (id: string) => `${API_BASE}/organize/files/${id}/image`;

// Everything Organize shows changes when files are moved, and so does what Clean up lists of the same files.
const refreshOrganize = () => Promise.all([mutate((key: unknown) => typeof key === 'string' && (key.includes('/organize/files?') || key.includes('/organize/batches'))), refreshGroups()]);

/** The pictures and videos of a folder, with when and where each was taken. Nothing is asked until a folder is chosen. */
export function useOrganizeFiles(root?: string) {
  const key = root ? `${API_BASE}/organize/files?root=${encodeURIComponent(root)}` : null;
  // Listing a large folder takes a moment: it is asked for again when files move, not each time the window comes forward.
  return useSWR<OrganizeFilesDto>(key, json, { revalidateOnFocus: false, shouldRetryOnError: false });
}

/** How far the reading of a folder has got, with the files read beyond the ones the page says it already has. */
export function fetchOrganizeProgress(root: string, from: number, places: number) {
  return json<OrganizeProgressDto>(`${API_BASE}/organize/progress?root=${encodeURIComponent(root)}&from=${from}&places=${places}`);
}

/** The latest moves and copies that can still be undone. */
export function useOrganizeBatches() {
  return useSWR<OrganizeBatchDto[]>(`${API_BASE}/organize/batches`, json, { revalidateOnFocus: false });
}

/** Where each file would go, and which names are already taken there. Nothing is moved. */
export function planOrganize(destination: OrganizeDestination, files: OrganizePlanFile[]) {
  return json<OrganizePlanDto>(`${API_BASE}/organize/plan`, { method: 'POST', body: JSON.stringify({ ...destination, files }) });
}

/** Moves or copies the files. */
export async function applyOrganize(request: OrganizeApplyRequest) {
  const result = await json<OrganizeApplyDto>(`${API_BASE}/organize/apply`, { method: 'POST', body: JSON.stringify(request) });
  await refreshOrganize();
  return result;
}

/** Takes files out of the folder being organized: they go to the folder inside it that holds removed files. */
export async function removeOrganize(root: string, fileIds: string[]) {
  const result = await json<OrganizeApplyDto>(`${API_BASE}/organize/remove`, { method: 'POST', body: JSON.stringify({ root, files: fileIds.map(id => ({ id })) }) });
  await refreshOrganize();
  return result;
}

/** Takes one move, copy or removal back, from Organize or Clean up alike. */
export async function undoChange(batchId: string): Promise<OrganizeUndoDto> {
  const res = await send(`${API_BASE}/organize/undo/${encodeURIComponent(batchId)}`, 'POST');
  const result = await res.json();
  await refreshOrganize();
  return result;
}

/** Opens a file in the default viewer or player of the machine the server runs on. */
export async function openOrganizeFile(id: string) {
  await send(`${API_BASE}/organize/files/${id}/open`, 'POST');
}

// ---- The plan

/** The plan, how many removals are left today, and the plans for sale with the store's prices. */
export function usePlan() {
  return useSWR<PlanDto>(planUrl, json);
}

/** Puts the store's own sheet for buying a plan in front of the person, and answers how it ended, with the plan then. */
export async function buyPlan(kind: PlanKind): Promise<PlanBuyResultDto> {
  const result = await json<PlanBuyResultDto>(`${planUrl}/buy`, { method: 'POST', body: JSON.stringify({ kind }) });
  await mutate(planUrl, result.plan, { revalidate: false });
  // The trial's terms promise a reminder two days before it ends: starting it turns the reminder on, which asks the phone first.
  if (result.outcome === 'bought' && kind === 'yearly') await changeNotices({ trialReminder: true });
  return result;
}

/** Asks the store again for what was bought with the same account, and answers with the plan then. */
export async function restorePurchases(): Promise<PlanDto> {
  const plan = await json<PlanDto>(`${planUrl}/restore`, { method: 'POST' });
  await mutate(planUrl, plan, { revalidate: false });
  return plan;
}

/** Opens the store's own page for changing or cancelling the subscription. */
export async function manageSubscription() {
  await send(`${planUrl}/manage`, 'POST');
}

// ---- Pairing a phone with a computer

const pairingUrl = `${API_BASE}/pairing/state`;
// Pairing is waited on while it is shown, or while it is under way.
const PAIRING_LIVE: ReadonlySet<PairingStateDto['stage']> = new Set(['waiting', 'scanning', 'finding', 'connecting', 'comparing']);

/** Where pairing has got to, on whichever this program is: asked every second while it is shown, or under way; nothing is asked otherwise. */
export function usePairing(watching: boolean) {
  return useSWR<PairingStateDto>(watching ? pairingUrl : null, json, { refreshInterval: latest => (latest !== undefined && PAIRING_LIVE.has(latest.stage) ? 1000 : 0) });
}

async function pairing(what: string, body?: unknown): Promise<PairingStateDto> {
  const state = await json<PairingStateDto>(`${API_BASE}/pairing/${what}`, { method: 'POST', ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
  await mutate(pairingUrl, state, { revalidate: false });
  return state;
}

/** On a computer: shows a new QR code and code, and waits for a phone. */
export const openPairing = () => pairing('open');
/** On a phone: opens the camera, and pairs with the computer whose QR code it sees. */
export const scanPairing = () => pairing('scan');
/** On a phone: finds the computer showing the code typed. */
export const enterPairingCode = (code: string) => pairing('code', { code });
/** The person's answer to whether the numbers on the two match. */
export const confirmPairing = (same: boolean) => pairing('confirm', { same });
/** On a computer: names it as phones show it. */
export const nameComputer = (name: string) => pairing('name', { name });

/** On a computer: stops waiting for a phone. */
export async function closePairing() {
  await send(`${API_BASE}/pairing/close`, 'POST');
  await mutate(pairingUrl);
}

/** On a phone: stops pairing. */
export async function stopPairing() {
  await send(`${API_BASE}/pairing/stop`, 'POST');
  await mutate(pairingUrl);
}

/** What the scans so far have found to free, by kind: asked every two seconds while a scan runs. */
export function useFound(watching: boolean, scanning: boolean) {
  return useSWR<FindingsDto>(watching ? `${API_BASE}/scan/found` : null, json, { refreshInterval: scanning ? 2000 : 0 });
}

// ---- Removed files

const removedUrl = (roots: readonly string[]) => `${API_BASE}/removed?${roots.map(r => `root=${encodeURIComponent(r)}`).join('&')}`;

/** How much is waiting in the folders that hold removed files: inside the folders given, and wherever else the service knows of. */
export function fetchRemoved(roots: string[]) {
  return json<RemovedFilesDto>(removedUrl(roots));
}

/** How much is waiting to be erased, asked afresh each time it is shown; nothing is asked while `roots` is null. */
export function useRemoved(roots: readonly string[] | null) {
  return useSWR<RemovedFilesDto>(roots ? removedUrl(roots) : null, json, { revalidateOnFocus: false });
}

/** Erases everything in those folders. This cannot be taken back. */
export async function eraseRemoved(folders: string[]) {
  const result = await json<EraseRemovedDto>(`${API_BASE}/removed/erase`, { method: 'POST', body: JSON.stringify({ folders }) });
  // A deletion whose files are erased can no longer be undone, so it leaves the list of what can be; and nothing waits now.
  await mutate((key: unknown) => typeof key === 'string' && (key.includes('/organize/batches') || key.includes('/removed?')));
  return result;
}

// ---- Backing up from a phone to a computer

const backupUrl = `${API_BASE}/backup`;
const backupLibraryUrl = `${API_BASE}/backup/library`;
const safeUrl = `${API_BASE}/backup/safe`;
const phonesUrl = `${API_BASE}/backup/phones`;
// What a backup or Safe to delete changes: how backing up stands, what is on the computer, what is safe to delete.
const refreshBackup = () => mutate((key: unknown) => typeof key === 'string' && (key === backupUrl || key === backupLibraryUrl || key === safeUrl));

/** On a phone: how backing up stands, asked every second while a backup runs and every half minute otherwise; nothing is asked while `watching` is off. */
export function useBackup(watching: boolean) {
  return useSWR<BackupStateDto>(watching ? backupUrl : null, json, { refreshInterval: latest => (latest?.stage === 'running' ? 1000 : 30_000) });
}

/** On a phone: which of the library's pictures and videos are on the computer, and which wait. */
export function useLibraryBackup(on: boolean) {
  return useSWR<LibraryBackupDto>(on ? backupLibraryUrl : null, json);
}

/** On a phone: what Safe to delete could delete, and what the keep rules keep. */
export function useSafeReview(on: boolean) {
  return useSWR<SafeReviewDto>(on ? safeUrl : null, json);
}

async function backingUp(what: string, body?: unknown): Promise<BackupStateDto> {
  const state = await json<BackupStateDto>(`${backupUrl}/${what}`, { method: 'POST', ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
  await mutate(backupUrl, state, { revalidate: false });
  await refreshBackup();
  return state;
}

/** On a phone: backs up what waits, or carries on a backup paused; with `files`, sends only those (Send now). */
export const startBackup = (files?: readonly string[]) => backingUp('start', files ? { files } : undefined);
/** On a phone: pauses the backup going on. */
export const pauseBackup = () => backingUp('pause');
/** On a phone: backs up by itself if a backup is due, as the app opens. */
export const backUpIfDue = () => backingUp('auto');
/** On a phone: how it backs up by itself, and what Safe to delete keeps. */
export const saveBackupSettings = (settings: BackupSettingsDto) => backingUp('settings', settings);

/** On a phone: Safe to delete, the largest first and no more than `atMost`; the phone asks first. Each counts toward the day's free removals. */
export async function removeSafe(atMost?: number): Promise<SafeRemovalDto> {
  const removed = await json<SafeRemovalDto>(`${backupUrl}/remove`, { method: 'POST', ...(atMost === undefined ? {} : { body: JSON.stringify({ atMost }) }) });
  await refreshBackup();
  await mutate(planUrl);
  return removed;
}

/** On a computer: every phone that backs up to it, asked every two seconds while one is arriving and every ten otherwise. */
export function useBackupPhones(on: boolean) {
  return useSWR<BackupPhoneDto[]>(on ? phonesUrl : null, json, { refreshInterval: latest => (latest?.some(p => p.arriving) ? 2000 : 10_000) });
}

async function phoneChange(id: string, what: 'folder' | 'pause' | 'trips', body: unknown) {
  const phone = await json<BackupPhoneDto>(`${phonesUrl}/${encodeURIComponent(id)}/${what}`, { method: 'POST', body: JSON.stringify(body) });
  await mutate(phonesUrl);
  return phone;
}

/** On a computer: sends a phone's backups to another folder from now on. */
export const moveBackups = (id: string, folder: string) => phoneChange(id, 'folder', { folder });
/** On a computer: makes a phone's trip folders in another folder from now on; those made already stay. */
export const tripsGoTo = (id: string, folder: string) => phoneChange(id, 'trips', { folder });
/** On a computer: pauses a phone's backups, or lets them run again. */
export const pausePhone = (id: string, paused: boolean) => phoneChange(id, 'pause', { paused });


const swipeUrl = `${API_BASE}/swipe`;

/** On a phone: how Swipe to clean stands today; nothing is asked while `on` is false. */
export function useSwipe(on: boolean) {
  return useSWR<SwipeStateDto>(on ? swipeUrl : null, json, { revalidateOnFocus: false });
}

async function swiping(what: string, body?: unknown): Promise<SwipeStateDto> {
  const state = await json<SwipeStateDto>(`${swipeUrl}/${what}`, { method: 'POST', ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
  await mutate(swipeUrl, state, { revalidate: false });
  return state;
}

/** Keeps a photo of today's set, or marks it to delete; nothing is deleted yet. */
export const decideSwipe = (id: string, del: boolean) => swiping('decide', { id, delete: del });

/** Takes back the last decision of today. */
export const undoSwipe = () => swiping('undo');

/** On the summary, keeps a photo marked to delete after all, or marks it again. */
export const spareSwipe = (id: string, spared: boolean) => swiping('spare', { id, spared });

/**
 * Deletes what is marked, the largest first and no more than `atMost`, to the phone's Recently Deleted or bin after its
 * own question; the rest stay marked for another day. Library, what is backed up and the plan's count are read again.
 */
export async function deleteSwiped(atMost?: number): Promise<SwipeFreedDto> {
  const freed = await json<SwipeFreedDto>(`${swipeUrl}/delete`, { method: 'POST', ...(atMost === undefined ? {} : { body: JSON.stringify({ atMost }) }) });
  await Promise.all([mutate(swipeUrl), refreshOrganize(), refreshBackup(), mutate(planUrl)]);
  return freed;
}

const categoriesUrl = `${API_BASE}/cleanup/categories`;

/** What Clean up's categories hold, and a phone's storage; nothing is asked while `on` is false. */
export function useCategories(on: boolean) {
  return useSWR<CategoryReportDto>(on ? categoriesUrl : null, json);
}

/** The pictures or videos of a category: duplicates' copies and large videos largest first, the rest latest first. */
export function useCategoryPhotos(category: Exclude<CategoryName, 'bursts'>) {
  return useSWR<PhotoDto[]>(`${categoriesUrl}/${category}`, json);
}

/** The bursts, latest first, each with the shot kept; nothing is asked while `on` is false. */
export function useBursts(on: boolean) {
  return useSWR<BurstDto[]>(on ? `${categoriesUrl}/bursts` : null, json);
}

/**
 * Deletes the pictures and videos chosen from Clean up's categories: on a phone to its own bin after its question, on a
 * computer into _PhotoSense_Removed with Undo. The categories, the groups, Library and the plan's count are read again.
 */
export async function deleteChosen(ids: readonly string[]): Promise<CategoryRemovalDto> {
  const done = await json<CategoryRemovalDto>(`${categoriesUrl}/delete`, { method: 'POST', body: JSON.stringify({ ids }) });
  await refreshOrganize();
  return done;
}

const compressUrl = `${API_BASE}/compress`;

/** On a phone: the large videos, largest first, each with about what its copy takes at each quality; nothing is asked while `on` is false. */
export function useCompressVideos(on: boolean) {
  return useSWR<CompressibleVideoDto[]>(on ? `${compressUrl}/videos` : null, json);
}

/** On a phone: how compressing stands, asked every second while it runs; nothing is asked while `on` is false. */
export function useCompression(on: boolean) {
  return useSWR<CompressionStateDto>(on ? compressUrl : null, json, { refreshInterval: latest => (latest?.stage === 'running' ? 1000 : 0) });
}

/** Starts compressing the videos given at the quality given, in the background, and shows how it stands at once. */
export async function startCompression(ids: readonly string[], quality: CompressionQuality): Promise<CompressionStateDto> {
  const state = await json<CompressionStateDto>(`${compressUrl}/start`, { method: 'POST', body: JSON.stringify({ ids, quality }) });
  await mutate(compressUrl, state, { revalidate: false });
  return state;
}

/** Stops compressing after the video it is on; what was compressed is kept. */
export async function cancelCompression(): Promise<CompressionStateDto> {
  const state = await json<CompressionStateDto>(`${compressUrl}/cancel`, { method: 'POST' });
  await mutate(compressUrl, state, { revalidate: false });
  return state;
}

/**
 * Once compressing has ended: the large videos, the categories, the groups, Library and the plan are read again, as the
 * originals may have gone and their smaller copies come.
 */
export const refreshCompressed = () => Promise.all([mutate(`${compressUrl}/videos`), refreshOrganize()]);

const noticesUrl = `${API_BASE}/notices`;

/** On a phone: which notifications are on, and whether the phone lets PhotoSense send any; nothing is asked while `on` is false. */
export function useNotices(on: boolean) {
  return useSWR<NoticeStateDto>(on ? noticesUrl : null, json);
}

/** Turns low-storage alerts or the trial's reminder on or off; turning one on may ask the phone's own question first. */
export async function changeNotices(change: Partial<Pick<NoticeStateDto, 'lowStorage' | 'trialReminder'>>): Promise<NoticeStateDto> {
  const state = await json<NoticeStateDto>(noticesUrl, { method: 'POST', body: JSON.stringify(change) });
  await mutate(noticesUrl, state, { revalidate: false });
  return state;
}

/** What the last notification tapped asked to open, once. */
export const openedNotice = () => json<NoticeOpenedDto>(`${noticesUrl}/opened`);

/** On a phone: Year in review, the year due now (null the rest of the year) and its figures; nothing is asked while `on` is false. */
export function useYear(on: boolean) {
  return useSWR<YearReviewDto>(on ? `${API_BASE}/year` : null, json);
}

// A request that answers with nothing, its body carried as json's are.
async function post(url: string, body: string): Promise<void> {
  const carried: Record<string, string> = android ? { 'x-photosense-body': android.keep(body) } : {};
  const res = await fetch(url, { method: 'POST', body, headers: { 'Content-Type': 'application/json', ...authHeaders, ...carried } });
  if (!res.ok) throw await failure(res);
}

/** Hands a card, a PNG in base 64, to the phone's share sheet, named `name`. */
export const shareImage = (image: string, name: string) => post(`${API_BASE}/share`, JSON.stringify({ image, name }));

const searchUrl = `${API_BASE}/search`;

/** On a phone: the photos that match a search, latest first; nothing is asked while `query` is empty. Searches stay on the phone. */
export function useSearch(query: string) {
  return useSWR<SearchResultDto>(query ? `${searchUrl}?q=${encodeURIComponent(query)}` : null, json);
}

/** Asks every search again, for when Pro has been bought and things in photos can be searched. */
export const refreshSearch = () => mutate((key: unknown) => typeof key === 'string' && key.startsWith(`${searchUrl}?`));

const detailsUrl = (path: string) => `${API_BASE}/viewer/details?path=${encodeURIComponent(path)}`;

/** On a phone: a picture or video's camera and whether it is a favorite; nothing is asked while `path` is undefined. */
export function usePhotoDetails(path: string | undefined) {
  return useSWR<PhotoDetailsDto>(path === undefined ? null : detailsUrl(path), json);
}

/** Marks a picture or video a favorite in the phone's own photos app, or takes the mark away. */
export async function setFavorite(path: string, favorite: boolean): Promise<PhotoDetailsDto> {
  const details = await json<PhotoDetailsDto>(`${API_BASE}/viewer/favorite`, { method: 'POST', body: JSON.stringify({ path, favorite }) });
  await mutate(detailsUrl(path), details, { revalidate: false });
  return details;
}

/** Hands a copy of a picture or video to the phone's share sheet, without where it was taken and the camera's details as asked. */
export const sharePhoto = (path: string, removeLocation: boolean, removeCamera: boolean) => post(`${API_BASE}/share/photo`, JSON.stringify({ path, removeLocation, removeCamera }));

const tripsUrl = `${API_BASE}/trips`;

/**
 * On a phone: home, the trips, the latest first, the computer, and the trip folder being made; asked every second while
 * one is being made. Nothing is asked while `on` is false.
 */
export function useTrips(on: boolean) {
  return useSWR<TripsDto>(on ? tripsUrl : null, json, { refreshInterval: latest => (latest?.folder?.stage === 'running' ? 1000 : 0) });
}

/** On a phone: a trip with its days; nothing is asked while `id` is undefined. */
export function useTrip(id: string | undefined) {
  return useSWR<TripDetailDto>(id === undefined ? null : `${tripsUrl}/${encodeURIComponent(id)}`, json);
}

/** On a phone: the towns photos were taken in, those on the most days first, to choose home from; nothing is asked while `on` is false. */
export function useTowns(on: boolean) {
  return useSWR<TownDto[]>(on ? `${tripsUrl}/towns` : null, json);
}

// Trips and every trip asked for, which a new home changes.
const refreshTrips = () => mutate((key: unknown) => typeof key === 'string' && key.startsWith(tripsUrl));

/** Makes `town` home for Trips, or with null lets PhotoSense work it out again. */
export async function chooseHome(town: TownDto | null): Promise<void> {
  await post(`${tripsUrl}/home`, JSON.stringify({ town }));
  await refreshTrips();
}

/** Names a trip as the person wants it, its folder too; with null, PhotoSense's own name again. */
export async function renameTrip(id: string, name: string | null): Promise<void> {
  await post(`${tripsUrl}/${encodeURIComponent(id)}/name`, JSON.stringify({ name }));
  await refreshTrips();
}

/** Makes a trip's folder on the computer, sending first what is not there yet; `useTrips` says how it stands. */
export async function makeTripFolder(id: string): Promise<void> {
  await post(`${tripsUrl}/${encodeURIComponent(id)}/folder`, '{}');
  await mutate(tripsUrl);
}

const fixesUrl = `${API_BASE}/fixes`;

/**
 * On a phone: what needs a date or a place, and how the computer names the phone's copies; asked every second while
 * the computer renames them. Nothing is asked while `on` is false.
 */
export function useFixes(on: boolean) {
  return useSWR<FixesDto>(on ? fixesUrl : null, json, { refreshInterval: latest => (latest?.names?.stage === 'running' ? 1000 : 0) });
}

// A fix, or its Undo, after which everything that shows a date or place is asked again.
async function fix(what: string, body?: unknown): Promise<number> {
  const result = await json<FixResultDto>(`${fixesUrl}/${what}`, { method: 'POST', ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
  await mutate(() => true);
  return result.changed;
}

/** Gives every photo with a wrong date the day chosen, "2026-03-02", each keeping its time of day; the phone asks first. */
export const fixDates = (date: string) => fix('dates', { date });
/** Gives every photo with no place that of the one taken nearest in time, within 30 minutes; the phone asks first. */
export const addPlaces = () => fix('places');
/** Puts back what the last fix of a kind changed; the phone asks first. */
export const undoFix = (kind: 'dates' | 'places') => fix('undo', { kind });

/** On a phone: has the computer name its copies so, now and as they arrive; `useFixes` says how it stands. */
export async function renameOnComputer(style: FileNaming): Promise<void> {
  await post(`${fixesUrl}/names`, JSON.stringify({ kind: style }));
  await mutate(fixesUrl);
}

const vaultUrl = `${API_BASE}/vault`;

/**
 * On a phone: how the hidden vault stands, and, once unlocked, what it holds; asked every second while its items are
 * brought back from the computer. Nothing is asked while `on` is false.
 */
export function useVault(on: boolean) {
  return useSWR<VaultDto>(on ? vaultUrl : null, json, { refreshInterval: latest => (latest?.restore?.stage === 'running' ? 1000 : 0) });
}

/** Turns the hidden vault on (Pro), unlocked; gives its recovery key, to write down. */
export async function turnOnVault(): Promise<string> {
  const { recoveryKey } = await json<{ recoveryKey: string }>(`${vaultUrl}/on`, { method: 'POST' });
  await mutate(vaultUrl);
  return recoveryKey;
}

/** Takes a vault's recovery key and brings its items back from the computer; `useVault` says how it stands. */
export async function recoverVault(recoveryKey: string): Promise<void> {
  await post(`${vaultUrl}/recover`, JSON.stringify({ recoveryKey }));
  await mutate(vaultUrl);
}

/** Asks the phone's own question; true when the person passed it and the vault is open. */
export async function unlockVault(): Promise<boolean> {
  const { unlocked } = await json<{ unlocked: boolean }>(`${vaultUrl}/unlock`, { method: 'POST' });
  await mutate(vaultUrl);
  return unlocked;
}

/** Locks the hidden vault. */
export async function lockVault(): Promise<void> {
  await send(`${vaultUrl}/lock`, 'POST');
  await mutate(vaultUrl);
}

/** The vault's recovery key, while it is unlocked, to write down again. */
export async function vaultRecoveryKey(): Promise<string> {
  return (await json<{ recoveryKey: string }>(`${vaultUrl}/key`)).recoveryKey;
}

// A change of what the vault holds, after which the library and the vault are asked again.
async function vaultChange(what: 'hide' | 'unhide' | 'delete', ids: readonly string[]): Promise<number> {
  const result = await json<FixResultDto>(`${vaultUrl}/${what}`, { method: 'POST', body: JSON.stringify({ ids }) });
  await mutate(() => true);
  return result.changed;
}

/** Seals Library's pictures and videos into the vault; the phone then asks before they leave the library. */
export const hideItems = (ids: readonly string[]) => vaultChange('hide', ids);
/** Puts items of the vault back in the library, each with its date and place. */
export const unhideItems = (ids: readonly string[]) => vaultChange('unhide', ids);
/** Puts items back and sends them to Recently Deleted or the bin, after the phone asks. */
export const deleteHidden = (ids: readonly string[]) => vaultChange('delete', ids);

/** An item's small preview, made when it was hidden; shown only while the vault is open. */
export function vaultThumbnailUrl(id: string): string {
  return `${vaultUrl}/${encodeURIComponent(id)}/thumbnail`;
}

/** The picture as the vault's viewer shows it. */
export function vaultImageUrl(id: string): string {
  return `${vaultUrl}/${encodeURIComponent(id)}/image`;
}

/** On a computer: what naming a phone's copies so would make of the first few, and how many; nothing is asked while `id` is undefined. */
export function useRenaming(id: string | undefined, style: FileNaming) {
  return useSWR<RenamingDto>(id === undefined ? null : `${phonesUrl}/${encodeURIComponent(id)}/names?style=${style}`, json);
}

/** On a computer: names a phone's copies so, now and as they arrive, or keeps the phone's names for those that arrive. */
export async function renameCopies(id: string, style: FileNaming): Promise<number> {
  const result = await json<FixResultDto>(`${phonesUrl}/${encodeURIComponent(id)}/names`, { method: 'POST', body: JSON.stringify({ kind: style }) });
  await mutate(phonesUrl);
  return result.changed;
}


const locationsUrl = `${API_BASE}/locations`;

/** On a phone: every place it backs up to, the Safe to delete rule, and what they are doing, asked every second while they are busy. */
export function useLocations(on: boolean) {
  return useSWR<LocationsDto>(on ? locationsUrl : null, json, { refreshInterval: latest => (latest?.activity?.stage === 'running' ? 1000 : 0) });
}

/** On a phone: one location in full, asked of it now. */
export function useLocationDetails(id: string | undefined) {
  return useSWR<LocationDetailsDto>(id ? `${locationsUrl}/${encodeURIComponent(id)}` : null, json);
}

// A change to the locations, after which they are asked again.
async function changeLocations(path: string, body: unknown = {}): Promise<void> {
  try { await post(`${locationsUrl}/${path}`, JSON.stringify(body)); } finally { await mutate(locationsUrl); }
}

/** Turns a location on, signing in to a cloud service first if need be; refused with 402 on the free plan with another on. */
export const turnOnLocation = (id: string) => changeLocations(`${encodeURIComponent(id)}/on`);

/** Turns a location off. Nothing there is deleted. */
export const turnOffLocation = (id: string) => changeLocations(`${encodeURIComponent(id)}/off`);

/** On the free plan, turns this location on in place of the one that is on. */
export const locationInstead = (id: string) => changeLocations(`${encodeURIComponent(id)}/instead`);

/** Signs out of a cloud service. Nothing there is deleted. */
export const disconnectLocation = (id: string) => changeLocations(`${encodeURIComponent(id)}/disconnect`);

/** Removes a location Connect to server added. Nothing there is deleted. */
export const removeLocation = (id: string) => changeLocations(`${encodeURIComponent(id)}/remove`);

/** Names a location Connect to server added. */
export const renameLocation = (id: string, name: string) => changeLocations(`${encodeURIComponent(id)}/name`, { name });

/** On how many locations a copy must be checked before a photo counts as backed up: 1 or 2. */
export const setCountAt = (countAt: number) => changeLocations('count', { countAt });

/** Backs up to every location that is on. */
export const backUpToLocations = () => changeLocations('backup');

/** Check all backups. */
export const checkLocations = () => changeLocations('check');

/** Pauses what the locations are doing. */
export const pauseLocations = () => changeLocations('pause');

/** Backs up to the locations other than the computer when one is due. */
export const locationsIfDue = () => changeLocations('auto');

/** Send to: sends Library's pictures and videos chosen to one location. */
export const sendToLocation = (id: string, ids: string[]) => changeLocations(`${encodeURIComponent(id)}/send`, { ids });

/** Connect to server (Pro): checks the location, then adds or changes it; its id. */
export async function connectServer(connection: ServerConnectionDto): Promise<string> {
  try {
    return (await json<{ id: string }>(`${locationsUrl}/connect`, { method: 'POST', body: JSON.stringify(connection) })).id;
  } finally {
    await mutate(locationsUrl);
  }
}

/** Deletes from the phone what was sent and matched, the largest first and no more than `atMost`, after the phone's own question. */
export async function deleteSent(atMost?: number): Promise<SafeRemovalDto> {
  try {
    return await json<SafeRemovalDto>(`${locationsUrl}/sent/delete`, { method: 'POST', body: JSON.stringify({ atMost }) });
  } finally {
    await Promise.all([mutate(locationsUrl), mutate(planUrl)]);
  }
}
