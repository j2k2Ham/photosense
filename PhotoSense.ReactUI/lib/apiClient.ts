import useSWR from 'swr';
import type { DuplicateGroupDto, ScanProgressSnapshotDto, StartScanRequest } from '../types';

// Base URL can point at Blazor server (proxy) or Functions API.
const API_BASE = process.env.NEXT_PUBLIC_API_BASE ?? 'http://localhost:7071/api';

async function json<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, { ...init, headers: { 'Content-Type': 'application/json', ...(init?.headers||{}) } });
  if (!res.ok) throw new Error(await res.text());
  return res.json();
}

export function useDuplicateGroups(filter: string, near: boolean) {
  const key = `${API_BASE}/scan/groups?near=${near}&q=${encodeURIComponent(filter||'')}`;
  return useSWR<DuplicateGroupDto[]>(key, json, { refreshInterval: 4000 });
}

export function useScanProgress(instanceId?: string) {
  const key = instanceId ? `${API_BASE}/scan/progress/${instanceId}` : null;
  return useSWR<ScanProgressSnapshotDto>(key, json, { refreshInterval: 1500 });
}

export async function startScan(req: StartScanRequest) {
  return json<{ instanceId: string }>(`${API_BASE}/scan/start`, { method: 'POST', body: JSON.stringify(req) });
}
