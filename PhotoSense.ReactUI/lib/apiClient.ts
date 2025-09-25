import useSWR, { mutate } from 'swr';
import * as signalR from '@microsoft/signalr';
import type { DuplicateGroupDto, ScanProgressSnapshotDto, StartScanRequest } from '../types';

interface GroupPage {
  mode: 'exact' | 'near';
  page: number; pageSize: number; total: number; totalPages: number; threshold?: number;
  items: DuplicateGroupDto[];
}

// Base URL can point at Blazor server (proxy) or Functions API.
const API_BASE = process.env.NEXT_PUBLIC_API_BASE ?? 'http://localhost:7071/api';

async function json<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, { ...init, headers: { 'Content-Type': 'application/json', ...(init?.headers||{}) } });
  if (!res.ok) throw new Error(await res.text());
  return res.json();
}

export function useDuplicateGroups(filter: string, near: boolean, threshold: number, page: number, hideKept: boolean) {
  const key = `${API_BASE}/scan/groups?near=${near}&threshold=${threshold}&page=${page}&hideKept=${hideKept}&q=${encodeURIComponent(filter||'')}`;
  return useSWR<GroupPage>(key, json, { refreshInterval: 5000 });
}

export function connectLogStream(onLine: (l: string)=>void) {
  // Try SignalR first
  const baseRoot = API_BASE.replace(/\/api$/,'');
  let disposed = false;
  (async () => {
    try {
      const r = await fetch(`${baseRoot}/api/negotiate`, { method: 'POST' });
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
      const es = new EventSource(`${baseRoot}/api/scan/logs/stream?follow=true`);
      es.onmessage = e => { if (e.data) onLine(e.data); };
      es.onerror = () => { es.close(); };
      if (disposed) es.close();
    }
  })();
  return () => { disposed = true; };
}

export async function keepPhoto(id: string){
  await fetch(`${API_BASE}/photos/${id}/keep`, { method: 'POST' });
  mutate((key:string)=> key?.includes('/scan/groups'));
}
export async function movePhoto(id: string, target: string){
  await fetch(`${API_BASE}/photos/${id}/move?target=${encodeURIComponent(target)}`, { method: 'POST' });
  mutate((key:string)=> key?.includes('/scan/groups'));
}
export async function deletePhoto(id: string){
  await fetch(`${API_BASE}/photos/${id}`, { method: 'DELETE' });
  mutate((key:string)=> key?.includes('/scan/groups'));
}

export function useScanProgress(instanceId?: string) {
  const key = instanceId ? `${API_BASE}/scan/progress/${instanceId}` : null;
  return useSWR<ScanProgressSnapshotDto>(key, json, { refreshInterval: 1500 });
}

export async function startScan(req: StartScanRequest) {
  return json<{ instanceId: string }>(`${API_BASE}/scan/start`, { method: 'POST', body: JSON.stringify(req) });
}
