import { useMemo } from 'react';
import useSWR from 'swr';
import type { OrganizeFileDto, OrganizeFilesDto } from '../types';
import { libraryRoot, useOrganizeFiles } from './apiClient';
import { sameFolder } from './format';
import { joinPath, MONTHS, relativeFolder, shortDate } from './organize';
import type { Platform, Words } from './phone';
import { useOrganizeReading } from './useOrganizeReading';

/** What Library shows: everything, what has not been put in an album or folder, what has no place, or the videos. */
export type LibraryFilter = 'all' | 'unsorted' | 'unplaced' | 'videos';

/** The filters, in their order, with their names. */
export const LIBRARY_FILTERS: readonly { readonly id: LibraryFilter; readonly label: string }[] = [
  { id: 'all', label: 'All' }, { id: 'unsorted', label: 'Not organized' }, { id: 'unplaced', label: 'No location' }, { id: 'videos', label: 'Videos' },
];

/** The phone's library as Library shows it. */
export interface Library {
  /** The one folder the phone's photos are shown as, once the phone has said. */
  readonly root?: string;
  /** Its pictures and videos, latest first. A Live Photo's video goes with its picture, and is not shown on its own. */
  readonly files: readonly OrganizeFileDto[];
  /** Everything known of them, places included, once anything has been read. */
  readonly listing?: OrganizeFilesDto;
  /** While the library is still being read: how many files of how many have been. */
  readonly reading?: { readonly done: number; readonly total: number };
  /** What went wrong reading it, if anything did. */
  readonly error?: unknown;
}

const NONE: readonly OrganizeFileDto[] = [];

/** The phone's whole library, read as Organize reads a folder, with what has been read shown as it comes. Nothing is asked while `on` is false. */
export function useLibrary(on: boolean): Library {
  const root = useSWR<string>(on ? 'photosense-library-root' : null, libraryRoot, { revalidateOnFocus: false }).data;
  const whole = useOrganizeFiles(root);
  const reading = root !== undefined && !whole.data && !whole.error;
  const soFar = useOrganizeReading(root, reading);
  const listing = whole.data ?? soFar?.listing;
  const files = useMemo(() => latestFirst(listing?.files ?? NONE), [listing]);
  return { root, files, listing, reading: reading ? { done: soFar?.done ?? 0, total: soFar?.total ?? 0 } : undefined, error: whole.error };
}

/** Pictures and videos latest first, without the videos of Live Photos, which go wherever their pictures go. */
export function latestFirst(files: readonly OrganizeFileDto[]): OrganizeFileDto[] {
  return files.filter(f => !f.livePhoto).sort((a, b) => b.date.localeCompare(a.date));
}

/**
 * Not yet in an album or folder: at the top of the library on an iPhone; on Android also still where the camera put it,
 * in DCIM/Camera.
 */
export function isUnsorted(f: OrganizeFileDto, root: string, platform: Platform): boolean {
  return sameFolder(f.folder, root) || (platform === 'android' && sameFolder(f.folder, joinPath(root, 'DCIM', 'Camera')));
}

/** Whether a file is among those a filter shows. */
export function shows(filter: LibraryFilter, f: OrganizeFileDto, root: string, platform: Platform): boolean {
  if (filter === 'unsorted') return isUnsorted(f, root, platform);
  if (filter === 'unplaced') return f.place == null;
  return filter === 'all' || f.isVideo;
}

/** How many files each filter shows. */
export function filterCounts(files: readonly OrganizeFileDto[], root: string, platform: Platform): Record<LibraryFilter, number> {
  const counts: Record<LibraryFilter, number> = { all: 0, unsorted: 0, unplaced: 0, videos: 0 };
  for (const f of files) for (const { id } of LIBRARY_FILTERS) if (shows(id, f, root, platform)) counts[id]++;
  return counts;
}

/** Files that are in order already, split by the month they are from: "2023-12" and its files. */
export function byMonth(files: readonly OrganizeFileDto[]): { month: string; files: OrganizeFileDto[] }[] {
  const months: { month: string; files: OrganizeFileDto[] }[] = [];
  for (const f of files) {
    const month = f.date.slice(0, 7);
    if (months.at(-1)?.month !== month) months.push({ month, files: [] });
    months.at(-1)!.files.push(f);
  }
  return months;
}

/** "December 2023", for "2023-12". */
export const monthName = (month: string) => `${MONTHS[Number(month.slice(5, 7)) - 1]} ${month.slice(0, 4)}`;

/** "1 item", "15 items". */
export const items = (n: number) => `${n.toLocaleString()} ${n === 1 ? 'item' : 'items'}`;

/** "Dec 27, 2023, 11:34 PM": when a file was taken, as the camera's clock had it. */
export function takenAt(date: string): string {
  const hour = Number(date.slice(11, 13));
  return `${shortDate(date)}, ${hour % 12 || 12}:${date.slice(14, 16)} ${hour < 12 ? 'AM' : 'PM'}`;
}

/** Where a file is, in the platform's words: "Photo Library", "Photo Library › Trips › Butte", "Phone/DCIM/Camera". */
export function whereIs(folder: string, root: string, words: Words): string {
  const inside = relativeFolder(root, folder);
  return inside === 'Top level' ? words.library : [words.library, ...inside.split(/[\\/]/)].join(words.separator);
}

/** An album or folder files can be put in. */
export interface Album {
  readonly path: string;
  /** Its name below the library: "Anaconda", "Trips › Butte", "DCIM/Camera". */
  readonly label: string;
}

/** The albums or folders the library's files are in, by name, apart from the top of the library itself. */
export function albumsOf(files: readonly OrganizeFileDto[], root: string, words: Words): Album[] {
  const paths = [...new Set(files.map(f => f.folder))].filter(p => !sameFolder(p, root));
  return paths.map(path => ({ path, label: relativeFolder(root, path).split(/[\\/]/).join(words.separator) })).sort((a, b) => a.label.localeCompare(b.label));
}

/** Where a new album or folder is made: at the top of an iPhone's library; in Pictures on Android, which keeps photos only in DCIM, Pictures or Movies. */
export const newAlbumBase = (root: string, platform: Platform) => (platform === 'ios' ? root : joinPath(root, 'Pictures'));

/** The question before files are deleted, naming them: its title, what happens, and its button. */
export function deleteQuestion(names: readonly string[], words: Words): { title: string; lines: string[]; button: string } {
  const n = names.length, one = n === 1;
  const named = names.slice(0, 3).join(', ') + (n > 3 ? ` and ${(n - 3).toLocaleString()} more` : '');
  return {
    title: `Delete ${items(n)}?`,
    lines: [
      `${named} ${one ? 'goes' : 'go'} to ${words.removed}.`,
      `Undo can bring ${one ? 'it' : 'them'} back. Delete permanently, in Settings, sends ${one ? 'it' : 'them'} to ${words.bin}.`,
    ],
    button: `Delete ${items(n)}`,
  };
}
