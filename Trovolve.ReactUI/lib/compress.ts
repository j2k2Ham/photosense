import { useCallback, useEffect, useRef } from 'react';
import type { CompressibleVideoDto, CompressionQuality, CompressionStateDto, NoticeStateDto } from '../types';
import { changeNotices, openPhotoSettings, refreshCompressed, useCompression, useNotices } from './apiClient';
import { formatBytes } from './format';
import type { ToastAction, ToastKind } from '../components/Toaster';

/** The qualities a video can be compressed at, in their order, with what each makes of it. */
export const QUALITIES: readonly { readonly id: CompressionQuality; readonly label: string; readonly note: string }[] = [
  { id: 'high', label: 'High', note: 'Original resolution, HEVC' },
  { id: 'balanced', label: 'Balanced', note: '1080p, HEVC' },
  { id: 'small', label: 'Small', note: '720p, HEVC' },
];

/** What compressing these videos at `quality` would save, all told, and how many it can compress: those with an estimate. */
export function savings(videos: readonly CompressibleVideoDto[], quality: CompressionQuality): { readonly count: number; readonly bytes: number; readonly ids: string[] } {
  const worth = videos.filter(v => v.estimates[quality] != null);
  return { count: worth.length, bytes: worth.reduce((n, v) => n + v.video.fileSizeBytes - v.estimates[quality]!, 0), ids: worth.map(v => v.video.id) };
}

/** What a compression that has ended says: what it saved and where the originals wait, or why it stopped. */
export function endedSaid(state: CompressionStateDto, bin: string): { readonly message: string; readonly kind: ToastKind } {
  const n = state.results.length;
  const originals = n === 1 ? 'The original is' : 'The originals are';
  const kept = state.originalsDeleted === false
    ? `${n === 1 ? 'The original and the smaller copy are' : 'The originals and the smaller copies are'} both kept.`
    : n > 0 ? `${originals} in ${bin} for 30 days.` : '';
  if (state.stage === 'failed') return { message: [state.problem ?? 'The video could not be compressed.', n > 0 ? `${n.toLocaleString()} compressed first. ${kept}` : ''].filter(Boolean).join(' '), kind: 'error' };
  if (state.stage === 'stopped') return { message: `Stopped. ${n === 0 ? 'Nothing was compressed.' : `${n.toLocaleString()} compressed. ${kept}`}`, kind: 'info' };
  return { message: state.originalsDeleted === false ? kept : `Saved ${formatBytes(state.saved)}. ${kept}`, kind: 'ok' };
}

/**
 * Says when a compression ends, wherever the person is, and reads again what it changed. Watches how compressing
 * stands while `on`, which is asked every second while it runs.
 */
export function useCompressionEnded(on: boolean, notify: (message: string, kind: ToastKind, action?: ToastAction) => void, bin: string,
  onShare?: (saved: number, videos: number) => void) {
  const state = useCompression(on).data;
  const was = useRef<CompressionStateDto['stage'] | undefined>(undefined);
  useEffect(() => {
    if (was.current === 'running' && state && state.stage !== 'running') {
      const said = endedSaid(state, bin);
      // What was saved can be shared, once the originals went.
      const share = onShare && state.originalsDeleted && state.saved > 0 ? { label: 'Share', run: () => onShare(state.saved, state.results.length) } : undefined;
      notify(said.message, said.kind, share);
      void refreshCompressed();
    }
    was.current = state?.stage;
  }, [state, notify, bin, onShare]);
}

/** What is said when the phone will not let Trovolve send notifications, with the way to its settings. */
export const NOTICES_OFF = 'Notifications are off for Trovolve. Turn them on in Settings.';

/**
 * The notifications' switches: which are on, and turning one on or off, which asks the phone first and, refused, says
 * where to turn them on.
 */
export function useNoticeSwitches(on: boolean, notify: (message: string, kind: ToastKind, action?: ToastAction) => void) {
  const state = useNotices(on).data;
  const change = useCallback(async (asked: Partial<Pick<NoticeStateDto, 'lowStorage' | 'trialReminder'>>) => {
    try {
      const now = await changeNotices(asked);
      if (!now.allowed && (asked.lowStorage || asked.trialReminder)) notify(NOTICES_OFF, 'error', { label: 'Settings', run: () => void openPhotoSettings() });
    } catch (e) {
      notify(e instanceof Error ? e.message : String(e), 'error');
    }
  }, [notify]);
  return { state, change };
}
