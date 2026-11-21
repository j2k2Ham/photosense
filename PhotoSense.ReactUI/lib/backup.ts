import type { BackupStateDto, SafeReviewDto } from '../types';
import type { Platform } from './phone';
import { formatBytes } from './format';
import { items } from './library';

/** "Today, 9:41", "Yesterday, 18:02" or "Nov 10, 9:41": when something happened, as the backup screens say it. */
export function whenSaid(iso: string, now: Date): string {
  const at = new Date(iso);
  const time = at.toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' });
  const day = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
  const days = Math.round((day(now) - day(at)) / 86_400_000);
  if (days === 0) return `Today, ${time}`;
  if (days === 1) return `Yesterday, ${time}`;
  return `${at.toLocaleDateString('en-US', { month: 'short', day: 'numeric' })}, ${time}`;
}

/** "today at 9:41 AM", "yesterday at 6:02 PM" or "on Nov 10 at 9:41 AM": when, inside a sentence. */
export function checkedAt(iso: string, now: Date): string {
  const [day, time] = whenSaid(iso, now).split(', ');
  return `${day === 'Today' || day === 'Yesterday' ? day.toLowerCase() : `on ${day}`} at ${time}`;
}

/** The backup card's heading: what waits, how far a backup has got, or that everything is there. */
export function backupTitle(s: BackupStateDto): string {
  switch (s.stage) {
    case 'running': return `Backing up ${s.done.toLocaleString()} of ${s.total.toLocaleString()}`;
    case 'paused': return `Paused at ${s.done.toLocaleString()} of ${s.total.toLocaleString()}`;
    case 'failed': return 'The backup stopped';
    default: return s.waiting > 0 ? `${s.waiting.toLocaleString()} new ${s.waiting === 1 ? 'item' : 'items'} waiting` : 'Up to date';
  }
}

/**
 * The line under the heading: how much is left, how much waits and when it goes, or what is on the computer. Android
 * backs up by itself with the app closed too; an iPhone, for now, while PhotoSense is open.
 */
export function backupLine(s: BackupStateDto, now: Date, platform: Platform): string {
  if (s.stage === 'running' || s.stage === 'paused') return `${formatBytes(Math.max(0, s.totalBytes - s.bytes))} left · each file is checked after it arrives`;
  if (s.stage === 'failed') return s.problem ?? '';
  if (s.waiting === 0) return `${items(s.onComputer)} on ${s.computer}${s.lastBackup ? ` · backed up ${checkedAt(s.lastBackup, now)}` : ''}`;
  const next = !s.settings.automatic ? 'automatic backup is off'
    : s.settings.onlyWhileCharging ? `next backup while charging${platform === 'android' ? ' on Wi-Fi' : ''}`
      : platform === 'android' ? 'next backup on Wi-Fi' : 'next backup while PhotoSense is open';
  return `${formatBytes(s.waitingBytes)} · ${next}`;
}

/** What the backup card's button does now: back up, pause, carry on, or try again; none when everything is there. */
export function backupButton(s: BackupStateDto): 'Back up now' | 'Pause' | 'Resume' | 'Try again' | undefined {
  if (s.stage === 'running') return 'Pause';
  if (s.stage === 'paused') return 'Resume';
  if (s.stage === 'failed') return 'Try again';
  return s.waiting > 0 ? 'Back up now' : undefined;
}

/**
 * What Automatic backup's note says of when backups run by themselves: Android's WorkManager wakes the app on Wi-Fi
 * about once an hour; iOS wakes an iPhone or iPad when it chooses (a background processing task), and either backs up
 * whenever PhotoSense is open.
 */
export function automaticNote(platform: Platform, computer: string | null, onlyWhileCharging: boolean, device: string): string {
  if (platform === 'android') return `Backups run on Wi-Fi about once an hour${onlyWhileCharging ? ' while this phone is charging' : ''}, with PhotoSense closed too, and whenever it is open.`;
  const open = computer ? `whenever PhotoSense is open and ${computer} is on the same Wi-Fi` : 'whenever PhotoSense is open and on Wi-Fi';
  return `iOS decides when background backups run. They usually happen while this ${device} is charging on Wi-Fi. A backup also runs ${open}.`;
}

/** How far a backup has got, out of a hundred. */
export const backupPercent = (s: BackupStateDto) => (s.totalBytes > 0 ? Math.min(100, s.bytes / s.totalBytes * 100) : 0);

/** The share of the phone's storage Safe to delete would free, everything else taken, and free now, each out of a hundred. */
export function storageBar(safe: Pick<SafeReviewDto, 'storageTotal' | 'storageFree' | 'removable'>): { remove: number; other: number; free: number } {
  const total = Math.max(1, safe.storageTotal);
  const remove = Math.min(safe.removable.bytes, total - safe.storageFree) / total * 100;
  const free = safe.storageFree / total * 100;
  return { remove, other: Math.max(0, 100 - remove - free), free };
}

/** "28.1 GB", as the large figures of Safe to delete and Space freed write an amount: one decimal, at least a tenth of a gigabyte. */
export function gigabytes(bytes: number): string {
  return `${Math.max(bytes / 1e9, bytes > 0 ? 0.1 : 0).toFixed(1)} GB`;
}

/** "112 of 128 GB", "83.9 of 128 GB": how much of the phone's storage is taken, to a tenth of a gigabyte. */
export const ofStorage = (used: number, total: number) => `${Number((used / 1e9).toFixed(1))} of ${Math.round(total / 1e9)} GB`;
