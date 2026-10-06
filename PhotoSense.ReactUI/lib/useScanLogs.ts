import { useCallback, useEffect, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';

const API_BASE = process.env.NEXT_PUBLIC_API_BASE ?? 'http://localhost:7071/api';

export interface ScanLogItem {
  instanceId: string;
  timestamp: string;
  level: string;
  message: string;
}

export interface UseScanLogsOptions {
  /** Max number of items to keep in memory (ring buffer behavior). Default 500. */
  maxItems?: number;
  /** REST polling interval when SignalR unavailable (ms). Default 1500 */
  pollIntervalMs?: number;
  /** Max items requested per REST poll. Default 200 */
  restLimit?: number;
  /** Force disable SignalR attempt (testing / debug) */
  disableSignalR?: boolean;
  /** Start paused; call resume() to begin. */
  startPaused?: boolean;
}

export interface UseScanLogsState {
  logs: ScanLogItem[];
  status: 'idle' | 'connecting' | 'streaming' | 'polling' | 'error' | 'paused';
  error?: string;
  pause: () => void;
  resume: () => void;
  clear: () => void;
}

/** A line as the polling endpoint returns it. */
interface PolledLine { instanceId?: string; timestamp: string; level: string; message: string; }

const messageOf = (e: unknown) => (e instanceof Error ? e.message : String(e));

/**
 * Provides unified log stream consumption. Attempts SignalR hub first; falls back to REST polling.
 * Backend endpoints:
 *  POST /scan/logs/negotiate (SignalR) -> connection info (url, accessToken)
 *  GET  /scan/logs?limit=... -> { items, count }
 */
export function useScanLogs(options: UseScanLogsOptions = {}): UseScanLogsState {
  const { maxItems = 500, pollIntervalMs = 1500, restLimit = 200, disableSignalR = false, startPaused = false } = options;
  const [logs, setLogs] = useState<ScanLogItem[]>([]);
  const [status, setStatus] = useState<UseScanLogsState['status']>(startPaused ? 'paused' : 'idle');
  const [error, setError] = useState<string>();
  // Every start (on mount, on resume) takes the next number. Pausing or unmounting moves the number on,
  // which retires whatever an earlier start still has in flight.
  const runRef = useRef(0);
  const connRef = useRef<signalR.HubConnection | null>(null);
  const pollTimerRef = useRef<ReturnType<typeof setTimeout>>();
  const baseRoot = API_BASE.replace(/\/api$/, '');

  const pushItems = useCallback((items: ScanLogItem[]) => {
    if (!items.length) return;
    setLogs(prev => {
      const merged = [...prev, ...items];
      return merged.length <= maxItems ? merged : merged.slice(merged.length - maxItems);
    });
  }, [maxItems]);

  const clear = useCallback(() => setLogs([]), []);

  const stopAll = useCallback(() => {
    runRef.current++;
    clearTimeout(pollTimerRef.current);
    const conn = connRef.current;
    connRef.current = null;
    // A connection that has already closed has nothing left to stop.
    conn?.stop().catch(() => undefined);
  }, []);

  const pause = useCallback(() => {
    stopAll();
    setStatus('paused');
  }, [stopAll]);

  const resume = useCallback(() => setStatus(s => (s === 'paused' ? 'idle' : s)), []);

  // Attempt SignalR then fallback to REST when status transitions to idle
  useEffect(() => {
    if (status !== 'idle') return;
    const run = ++runRef.current;
    const live = () => run === runRef.current;

    const poll = async () => {
      let wait = pollIntervalMs;
      try {
        const res = await fetch(`${baseRoot}/api/scan/logs?limit=${restLimit}`);
        if (res.ok) {
          const body: { items?: PolledLine[] } = await res.json();
          if (live()) pushItems((body.items ?? []).map(i => ({ instanceId: i.instanceId ?? 'n/a', timestamp: i.timestamp, level: i.level, message: i.message })));
        } else if (res.status === 429) {
          wait = pollIntervalMs * 2; // asked to slow down
        }
      } catch (e) {
        if (live()) setError(messageOf(e));
      }
      if (live()) pollTimerRef.current = setTimeout(poll, wait);
    };

    const startPolling = () => {
      setStatus('polling');
      void poll();
    };

    const stream = async () => {
      if (disableSignalR) throw new Error('SignalR disabled');
      setStatus('connecting');
      const res = await fetch(`${baseRoot}/api/scan/logs/negotiate`, { method: 'POST' });
      if (!res.ok) throw new Error(await res.text() || 'negotiate failed');
      const info = await res.json();
      if (!live()) return;
      const conn = new signalR.HubConnectionBuilder()
        .withUrl(info.url, { accessTokenFactory: () => info.accessToken })
        .withAutomaticReconnect()
        .build();
      conn.on('log', (instanceId: string, ts: string, level: string, message: string) => {
        if (live()) pushItems([{ instanceId, timestamp: ts, level, message }]);
      });
      // Closed for good, reconnecting having been given up: carry on by polling.
      conn.onclose(() => { if (live()) startPolling(); });
      await conn.start();
      if (!live()) {
        await conn.stop();
        return;
      }
      connRef.current = conn;
      setStatus('streaming');
    };

    stream().catch(e => {
      if (!live()) return;
      setError(messageOf(e));
      startPolling();
    });
  }, [status, baseRoot, disableSignalR, pollIntervalMs, pushItems, restLimit]);

  // Cleanup on unmount
  useEffect(() => stopAll, [stopAll]);

  return { logs, status, error, pause, resume, clear };
}
