import useSWR, { mutate } from 'swr';
import * as signalR from '@microsoft/signalr';
import type {
  BulkRemovalResultDto, EraseRemovedDto, FolderListingDto, GroupMode, GroupsPageDto, OrganizeApplyDto, OrganizeApplyRequest, OrganizeBatchDto, OrganizeDestination, OrganizeFilesDto, OrganizePlanDto,
  OrganizePlanFile, OrganizeProgressDto, OrganizeUndoDto, RemovedFilesDto, ScanProgressSnapshotDto, ScanStatusDto, StartScanRequest,
} from '../types';

// Base URL can point at Blazor server (proxy) or Functions API.
const API_BASE = process.env.NEXT_PUBLIC_API_BASE ?? 'http://localhost:7071/api';

const API_KEY = process.env.NEXT_PUBLIC_API_KEY; // provided via env when auth enforced
// The server acts on photos only for requests carrying this header, which other web sites cannot send.
const authHeaders: Record<string, string> = { 'x-photosense-client': 'web', ...(API_KEY ? { 'x-api-key': API_KEY } : {}) };

// The server answers failures with either plain text or { error }.
async function failure(res: Response): Promise<Error> {
  const text = await res.text();
  try { return new Error(JSON.parse(text).error ?? text); } catch { return new Error(text || `Request failed (${res.status})`); }
}

async function json<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, { ...init, headers: { 'Content-Type': 'application/json', ...authHeaders, ...init?.headers } });
  if (!res.ok) throw await failure(res);
  return res.json();
}

async function send(url: string, method: 'POST' | 'DELETE'): Promise<Response> {
  const res = await fetch(url, { method, headers: authHeaders });
  if (!res.ok) throw await failure(res);
  return res;
}

const refreshGroups = () => mutate((key: unknown) => typeof key === 'string' && (key.includes('/scan/groups') || key.includes('/scan/status')));

/** Asks again for everything on screen, for when the service has been out of reach. */
export const retryNow = () => mutate(() => true);

export const thumbnailUrl = (id: string) => `${API_BASE}/photos/${id}/thumbnail`;
export const imageUrl = (id: string) => `${API_BASE}/photos/${id}/image`;
export const videoUrl = (id: string) => `${API_BASE}/photos/${id}/video`;

/** Opens the file in the default viewer or player of the machine the server runs on. */
export async function openInViewer(id: string){
  await send(`${API_BASE}/photos/${id}/open`, 'POST');
}

/** Groups on one page of the gallery. */
export const PAGE_SIZE = 50;

const groupsUrl = (mode: GroupMode, filter: string, page: number, hideKept: boolean) =>
  `${API_BASE}/scan/groups?mode=${mode}&page=${page}&pageSize=${PAGE_SIZE}&hideKept=${hideKept}&q=${encodeURIComponent(filter)}`;

export function useGroups(mode: GroupMode, filter: string, page: number, hideKept: boolean) {
  return useSWR<GroupsPageDto>(groupsUrl(mode, filter, page, hideKept), json, { refreshInterval: 5000, keepPreviousData: true });
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

/** Follows the scan's log: over SignalR where the service offers it, otherwise by asking again every few seconds. */
export function connectLogStream(onLine: (l: string)=>void) {
  const baseRoot = API_BASE.replace(/\/api$/,'');
  let disposed = false;
  let conn: signalR.HubConnection | undefined;
  let pollTimer: ReturnType<typeof setTimeout> | undefined;
  (async () => {
    try {
      // Updated negotiate route path to match backend (scan/logs/negotiate)
      const r = await fetch(`${baseRoot}/api/scan/logs/negotiate`, { method: 'POST' });
      if (!r.ok) throw new Error('negotiate failed');
      const info = await r.json();
      if (disposed) return;
      conn = new signalR.HubConnectionBuilder()
        .withUrl(info.url, { accessTokenFactory: () => info.accessToken })
        .withAutomaticReconnect()
        .build();
      conn.on('log', (_instanceId: string, ts: string, level: string, msg: string) => {
        onLine(`${ts} ${level} ${msg}`);
      });
      await conn.start();
    } catch {
      if (disposed) return;
      // Fallback to simple polling REST endpoint as SSE stream not implemented.
      const poll = async () => {
        try {
          const res = await fetch(`${baseRoot}/api/scan/logs?limit=200`);
          if (res.ok) {
            const json = await res.json();
            for (const i of json.items ?? []) {
              onLine(`${i.timestamp} ${i.level} ${i.message}`);
            }
          }
        } catch {/* ignore */}
        // Dismissed while this poll was in flight: it was the last.
        if (!disposed) pollTimer = setTimeout(poll, 1500);
      };
      poll();
    }
  })();
  return () => {
    disposed = true;
    clearTimeout(pollTimer);
    // A connection that never opened, or has already closed, has nothing left to stop.
    conn?.stop().catch(() => undefined);
  };
}

/** Marks a photo to keep (bulk removal skips it), or clears the mark. */
export async function setKept(id: string, kept: boolean){
  await send(`${API_BASE}/photos/${id}/keep?kept=${kept}`, 'POST');
  await refreshGroups();
}

/** Moves one file, and the sidecars and Live Photo video that belong to it alone, to the holding folder. */
export async function removePhoto(id: string): Promise<{ companions: number }> {
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

/** Forgets everything earlier scans recorded. The photos themselves are not touched. */
export async function clearResults(): Promise<{ forgotten: number }> {
  const res = await send(`${API_BASE}/scan/reset`, 'POST');
  const result = await res.json();
  await refreshGroups();
  return result;
}

/** How many groups of each kind there are now, asked afresh: for saying what a scan found. */
export async function fetchGroupTotals(): Promise<{ duplicates: number; similar: number }> {
  const total = async (mode: GroupMode) => (await json<GroupsPageDto>(groupsUrl(mode, '', 1, false))).total;
  const [duplicates, similar] = await Promise.all([total('duplicates'), total('similar')]);
  return { duplicates, similar };
}

/** How many files are on record, and whether the last scan has finished. */
export function useScanStatus() {
  return useSWR<ScanStatusDto>(`${API_BASE}/scan/status`, json, { refreshInterval: 5000 });
}

export function useScanProgress(instanceId?: string) {
  const key = instanceId ? `${API_BASE}/scan/progress/${instanceId}` : null;
  return useSWR<ScanProgressSnapshotDto>(key, json, { refreshInterval: 1500 });
}

/** Lists the folders inside a folder of the computer the service runs on; with no path, the places to start from. */
export function browseFolders(path?: string) {
  return json<FolderListingDto>(`${API_BASE}/folders${path ? `?path=${encodeURIComponent(path)}` : ''}`);
}

export async function startScan(req: StartScanRequest) {
  return json<{ instanceId: string }>(`${API_BASE}/scan/start`, { method: 'POST', body: JSON.stringify(req) });
}

// ---- Organize

export const organizeThumbnailUrl = (id: string) => `${API_BASE}/organize/files/${id}/thumbnail`;
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

/** Takes one move, copy or removal back. */
export async function undoOrganize(batchId: string): Promise<OrganizeUndoDto> {
  const res = await send(`${API_BASE}/organize/undo/${encodeURIComponent(batchId)}`, 'POST');
  const result = await res.json();
  await refreshOrganize();
  return result;
}

/** Opens a file in the default viewer or player of the machine the server runs on. */
export async function openOrganizeFile(id: string) {
  await send(`${API_BASE}/organize/files/${id}/open`, 'POST');
}

// ---- Removed files

/** How much is waiting in the folders that hold removed files: inside the folders given, and wherever else the service knows of. */
export function fetchRemoved(roots: string[]) {
  return json<RemovedFilesDto>(`${API_BASE}/removed?${roots.map(r => `root=${encodeURIComponent(r)}`).join('&')}`);
}

/** Erases everything in those folders. This cannot be taken back. */
export async function eraseRemoved(folders: string[]) {
  const result = await json<EraseRemovedDto>(`${API_BASE}/removed/erase`, { method: 'POST', body: JSON.stringify({ folders }) });
  // A deletion whose files are erased can no longer be undone, so it leaves the list of what can be.
  await mutate((key: unknown) => typeof key === 'string' && key.includes('/organize/batches'));
  return result;
}
