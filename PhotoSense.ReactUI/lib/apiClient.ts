import useSWR, { mutate } from 'swr';
import * as signalR from '@microsoft/signalr';
import type { BulkRemovalResultDto, GroupMode, GroupsPageDto, ScanProgressSnapshotDto, StartScanRequest } from '../types';

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
  const res = await fetch(url, { ...init, headers: { 'Content-Type': 'application/json', ...authHeaders, ...(init?.headers||{}) } });
  if (!res.ok) throw await failure(res);
  return res.json();
}

async function send(url: string, method: 'POST' | 'DELETE'): Promise<Response> {
  const res = await fetch(url, { method, headers: authHeaders });
  if (!res.ok) throw await failure(res);
  return res;
}

const refreshGroups = () => mutate((key: unknown) => typeof key === 'string' && key.includes('/scan/groups'));

export const thumbnailUrl = (id: string) => `${API_BASE}/photos/${id}/thumbnail`;
export const imageUrl = (id: string) => `${API_BASE}/photos/${id}/image`;

export function useGroups(mode: GroupMode, filter: string, page: number, hideKept: boolean) {
  const key = `${API_BASE}/scan/groups?mode=${mode}&page=${page}&hideKept=${hideKept}&q=${encodeURIComponent(filter||'')}`;
  return useSWR<GroupsPageDto>(key, json, { refreshInterval: 5000, keepPreviousData: true });
}

/**
 * Deprecated. Prefer useScanLogs hook in useScanLogs.ts which handles SignalR vs REST polling.
 * Kept for backward compatibility for any existing components.
 */
export function connectLogStream(onLine: (l: string)=>void) {
  const baseRoot = API_BASE.replace(/\/api$/,'');
  let disposed = false;
  (async () => {
    try {
      // Updated negotiate route path to match backend (scan/logs/negotiate)
      const r = await fetch(`${baseRoot}/api/scan/logs/negotiate`, { method: 'POST' });
      if (!r.ok) throw new Error('negotiate failed');
      const info = await r.json();
      const conn = new signalR.HubConnectionBuilder()
        .withUrl(info.url, { accessTokenFactory: () => info.accessToken })
        .withAutomaticReconnect()
        .build();
      conn.on('log', (_instanceId: string, ts: string, level: string, msg: string) => {
        onLine(`${ts} ${level} ${msg}`);
      });
      await conn.start();
      if (disposed) await conn.stop();
    } catch {
      if (disposed) return;
      // Fallback to simple polling REST endpoint as SSE stream not implemented.
      const poll = async () => {
        if (disposed) return;
        try {
          const res = await fetch(`${baseRoot}/api/scan/logs?limit=200`);
          if (res.ok) {
            const json = await res.json();
            for (const i of json.items ?? []) {
              onLine(`${i.timestamp} ${i.level} ${i.message}`);
            }
          }
        } catch {/* ignore */}
        if (!disposed) setTimeout(poll, 1500);
      };
      poll();
    }
  })();
  return () => { disposed = true; };
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

/** Moves the duplicates of one group, or of every group, to the holding folder. */
export async function removeDuplicates(groupKey?: string): Promise<BulkRemovalResultDto> {
  const res = await send(`${API_BASE}/photos/bulk/remove-duplicates${groupKey ? `?group=${encodeURIComponent(groupKey)}` : ''}`, 'POST');
  const result = await res.json();
  await refreshGroups();
  return result;
}

export function useScanProgress(instanceId?: string) {
  const key = instanceId ? `${API_BASE}/scan/progress/${instanceId}` : null;
  return useSWR<ScanProgressSnapshotDto>(key, json, { refreshInterval: 1500 });
}

export async function startScan(req: StartScanRequest) {
  return json<{ instanceId: string }>(`${API_BASE}/scan/start`, { method: 'POST', body: JSON.stringify(req) });
}
