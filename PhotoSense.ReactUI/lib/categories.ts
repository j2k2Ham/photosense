import type { CategoryName, CategoryReportDto, PhotoDto } from '../types';
import { formatBytes } from './format';

/** A row of Find: a category of Clean up, or the similar shots, which Clean up lists by group. */
export type FindId = CategoryName | 'similar';

/** What each of Find's rows, and the screen it opens, is called. */
export const FIND_LABELS: Record<FindId, string> = {
  largeVideos: 'Large videos', similar: 'Similar shots', messaging: 'Messaging apps', duplicates: 'Duplicates', bursts: 'Bursts', screenshots: 'Screenshots', blurry: 'Blurry',
};

/** The hue of each row's tile on an iPhone, as the design gives it. */
export const FIND_HUES: Record<FindId, number> = { largeVideos: 28, similar: 262, messaging: 152, duplicates: 200, bursts: 330, screenshots: 232, blurry: 60 };

/** A row of Find: what it is, how many there are to delete, and what they take. */
export interface FindRow {
  readonly id: FindId;
  readonly count: number;
  readonly bytes: number;
}

/**
 * Find's rows, largest first: Clean up's categories with the similar shots among them. Messaging apps shows only where
 * there is something from one, or on Android, whose folders are where it is found.
 */
export function findRows(report: CategoryReportDto | undefined, similar: { count: number; bytes: number } | undefined, android: boolean): FindRow[] {
  const rows: FindRow[] = (report?.categories ?? []).map(c => ({ id: c.category, count: c.count, bytes: c.bytes }));
  if (similar) rows.push({ id: 'similar', count: similar.count, bytes: similar.bytes });
  return rows.filter(r => r.id !== 'messaging' || android || r.count > 0).sort((a, b) => b.bytes - a.bytes);
}

/** "1 item", "284 items". */
const items = (n: number) => `${n.toLocaleString()} ${n === 1 ? 'item' : 'items'}`;

/** A row's line under its name: what there is, or why there is nothing. */
export function rowLine(row: FindRow, unmeasured: number): string {
  if (row.count === 0) return row.id === 'blurry' && unmeasured > 0 ? 'Scan again to find blurry shots' : 'Nothing left to review';
  const what = row.id === 'bursts' ? `${row.count.toLocaleString()} extra ${row.count === 1 ? 'shot' : 'shots'}` : items(row.count);
  return `${row.id === 'messaging' ? 'WhatsApp and Telegram · ' : ''}${what} · ${formatBytes(row.bytes)}`;
}

/** What Clean up has found to free: each category's own, the similar shots aside, which are for the person to choose among. */
export const freeable = (report: CategoryReportDto | undefined) => (report?.categories ?? []).reduce((n, c) => n + c.bytes, 0);

/** How full a phone's storage is, in whole percent. */
export const percentFull = (total: number, free: number) => (total > 0 ? Math.round(((total - free) / total) * 100) : 0);

/** A video's frame by its shorter side: "4K", "1080p", "720p"; nothing for smaller, or a size not known. */
export function resolution(width: number, height: number): string | undefined {
  const short = Math.min(width, height);
  if (short >= 2160) return '4K';
  if (short >= 1080) return '1080p';
  return short >= 720 ? '720p' : undefined;
}

/** What a grid's photos are called, counted: "1,284 screenshots", "318 blurry shots", "40 photos and videos". */
export function counted(category: 'screenshots' | 'blurry' | 'messaging', n: number): string {
  const nouns = { screenshots: ['screenshot', 'screenshots'], blurry: ['blurry shot', 'blurry shots'], messaging: ['photo or video', 'photos and videos'] }[category];
  return `${n.toLocaleString()} ${nouns[n === 1 ? 0 : 1]}`;
}

/** The ids of the largest of the photos given, no more than `allowed`: what goes when the free plan has fewer left today. */
export function largestIds(photos: readonly PhotoDto[], allowed: number): string[] {
  return [...photos].sort((a, b) => b.fileSizeBytes - a.fileSizeBytes).slice(0, allowed).map(p => p.id);
}

/** What the photos given take. */
export const bytesOf = (photos: readonly PhotoDto[]) => photos.reduce((n, p) => n + p.fileSizeBytes, 0);

/** An amount of bytes found or chosen, said: "0 KB" for none, where a file's own size is never said as nothing. */
export const amountOf = (bytes: number) => (bytes > 0 ? formatBytes(bytes) : '0 KB');

/** What the photos given take, said: "0 KB" when there are none. */
export const sizeOf = (photos: readonly PhotoDto[]) => amountOf(bytesOf(photos));

/** "3 items deleted. They wait in Recently Deleted for 30 days.": what a phone says once its own bin has them. */
export const deletedSaid = (n: number, bin: string) => `${items(n)} deleted. ${n === 1 ? 'It waits' : 'They wait'} in ${bin} for 30 days.`;

/** Up to three of the files given by name, then how many more: "IMG_1.PNG, IMG_2.PNG and 10 more". */
export function namesOf(photos: readonly PhotoDto[]): string {
  const named = photos.slice(0, photos.length > 3 ? 2 : 3).map(p => p.fileName);
  const rest = photos.length - named.length;
  if (rest > 0) return `${named.join(', ')} and ${rest.toLocaleString()} more`;
  return named.length === 1 ? named[0] : `${named.slice(0, -1).join(', ')} and ${named.at(-1)}`;
}
