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

/**
 * Provides unified log stream consumption. Attempts SignalR hub first; falls back to REST polling.
 * Backend endpoints:
 *  POST /scan/logs/negotiate (SignalR) -> connection info (url, accessToken)
 *  GET  /scan/logs?limit=... -> { items, count }
 */
export function useScanLogs(options: UseScanLogsOptions = {}): UseScanLogsState {
  const { maxItems = 500, pollIntervalMs = 1500, restLimit = 200, disableSignalR, startPaused } = options;
  const [logs, setLogs] = useState<ScanLogItem[]>([]);
  const [status, setStatus] = useState<UseScanLogsState['status']>(startPaused ? 'paused' : 'idle');
  const statusRef = useRef(status);
  useEffect(()=>{ statusRef.current = status; }, [status]);
  const [error, setError] = useState<string>();
  const disposedRef = useRef(false);
  const pausedRef = useRef(!!startPaused);
  const connRef = useRef<signalR.HubConnection | null>(null);
  const pollTimerRef = useRef<number | null>(null);
  const baseRoot = API_BASE.replace(/\/api$/, '');

  const pushItems = useCallback((items: ScanLogItem[]) => {
    if (!items.length) return;
    setLogs(prev => {
      const merged = [...prev, ...items];
      if (merged.length <= maxItems) return merged;
      return merged.slice(merged.length - maxItems);
    });
  }, [maxItems]);

  const clear = useCallback(() => setLogs([]), []);

  const stopAll = useCallback(async () => {
    if (pollTimerRef.current) {
      clearTimeout(pollTimerRef.current);
      pollTimerRef.current = null;
    }
    const conn = connRef.current;
    if (conn) {
      try { await conn.stop(); } catch { /* ignore */ }
      connRef.current = null;
    }
  }, []);

  const pause = useCallback(() => {
    pausedRef.current = true;
    setStatus('paused');
    stopAll();
  }, [stopAll]);

  const resume = useCallback(() => {
    if (!pausedRef.current) return;
    pausedRef.current = false;
    setStatus('idle');
  }, []);

  // Attempt SignalR then fallback to REST when status transitions from idle
  useEffect(() => {
    if (status !== 'idle' || pausedRef.current) return;
    let cancelled = false;

    const attemptSignalR = async () => {
      if (disableSignalR) throw new Error('SignalR disabled');
      setStatus('connecting');
      const res = await fetch(`${baseRoot}/api/scan/logs/negotiate`, { method: 'POST' });
      if (!res.ok) throw new Error(await res.text() || 'negotiate failed');
      const info = await res.json();
      if (cancelled || disposedRef.current) return;
      const conn = new signalR.HubConnectionBuilder()
        .withUrl(info.url, { accessTokenFactory: () => info.accessToken })
        .withAutomaticReconnect()
        .build();
      connRef.current = conn;
      conn.on('log', (instanceId: string, ts: string, level: string, message: string) => {
        pushItems([{ instanceId, timestamp: ts, level, message }]);
      });
      conn.onclose(() => {
        if (disposedRef.current || pausedRef.current) return;
        // Fallback to polling if closed after some attempts
        if (statusRef.current !== 'polling') startPolling();
      });
      await conn.start();
      if (disposedRef.current || cancelled || pausedRef.current) {
        await conn.stop();
        return;
      }
      setStatus('streaming');
    };

    const runPollOnce = async () => {
      if (disposedRef.current || pausedRef.current || statusRef.current === 'streaming') return;
      try {
        const r = await fetch(`${baseRoot}/api/scan/logs?limit=${restLimit}`);
        if (r.ok) {
          const json = await r.json();
          const raw: any[] = json.items || [];
          if (raw.length) {
            pushItems(raw.map(i => ({
              instanceId: i.instanceId ?? 'n/a',
              timestamp: i.timestamp,
              level: i.level,
              message: i.message
            })) as ScanLogItem[]);
          }
        } else if (r.status === 429) {
          pollTimerRef.current = window.setTimeout(runPollOnce, pollIntervalMs * 2) as any;
          return;
        }
      } catch (e:any) {
        setError(e.message || String(e));
      }
      pollTimerRef.current = window.setTimeout(runPollOnce, pollIntervalMs) as any;
    };

    const startPolling = () => {
      if (disposedRef.current || pausedRef.current) return;
      setStatus('polling');
      runPollOnce();
    };

    (async () => {
      try {
        await attemptSignalR();
      } catch (err:any) {
        setError(err.message || String(err));
        startPolling();
      }
    })();

    return () => { cancelled = true; };
  }, [status, baseRoot, disableSignalR, pollIntervalMs, pushItems, restLimit]);

  // Cleanup on unmount
  useEffect(() => {
    return () => {
      disposedRef.current = true;
      stopAll();
    };
  }, [stopAll]);

  return { logs, status, error, pause, resume, clear };
}
