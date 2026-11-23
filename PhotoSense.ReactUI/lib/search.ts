import { useCallback, useMemo } from 'react';
import type { OrganizeFileDto, PhotoDto, SearchPartDto } from '../types';
import { sameFolder } from './format';
import { joinPath } from './organize';
import { useStored } from './stored';

/** Searches offered before anything is typed, each the kind of words search knows: things in photos, places and dates. */
export const SEARCH_TRY: readonly string[] = ['dog at the beach', 'sunset over mountains', 'birthday cake'];

/** How many past searches Recent lists. */
export const RECENT_SEARCHES = 5;

const KEY = 'photosense-searches';

/** What each kind of part is called on its chip. */
export const PART_LABELS: Readonly<Record<SearchPartDto['kind'], string>> = { inPhoto: 'In photo', place: 'Place', month: 'Month', year: 'Year' };

// The searches kept, latest first; none when nothing readable was.
function searchesIn(kept: string | null | undefined): string[] {
  try {
    const read: unknown = JSON.parse(kept ?? '[]');
    return Array.isArray(read) ? read.filter((q): q is string => typeof q === 'string') : [];
  } catch {
    return [];
  }
}

/**
 * The searches made on this phone, latest first, and a way to add one. They are kept in the page's own storage on the
 * phone, and never sent anywhere.
 */
export function useRecentSearches(): [readonly string[], (query: string) => void] {
  const [kept, keep] = useStored(KEY);
  const recent = useMemo(() => searchesIn(kept), [kept]);
  const add = useCallback((query: string) => keep(before => {
    const others = searchesIn(before).filter(q => q.toLowerCase() !== query.toLowerCase());
    return JSON.stringify([query, ...others].slice(0, RECENT_SEARCHES));
  }), [keep]);
  return [recent, add];
}

/**
 * The card on the free plan when a search names things in photos: "Searching for what is in photos, like “dog” and
 * “beach”, is part of Pro. Showing photos that match 2023."
 */
export function proLine(parts: readonly SearchPartDto[]): string {
  const things = parts.filter(p => p.kind === 'inPhoto').slice(0, 2).map(p => `“${p.text}”`).join(' and ');
  const rest = parts.filter(p => p.kind !== 'inPhoto').map(p => p.text);
  return `Searching for what is in photos, like ${things}, is part of Pro.${rest.length > 0 ? ` Showing photos that match ${rest.join(', ')}.` : ''}`;
}

/** What matches, by kind: "360 photos", "3 videos", "360 photos and 3 videos", or "No matches". */
export function foundLine(total: number, videos: number): string {
  const said = (n: number, one: string) => `${n.toLocaleString()} ${one}${n === 1 ? '' : 's'}`;
  const parts = [total - videos > 0 && said(total - videos, 'photo'), videos > 0 && said(videos, 'video')].filter(Boolean);
  return parts.length === 0 ? 'No matches' : parts.join(' and ');
}

/** The library's file for a search's photo, by its path; undefined while the library has not read it. */
export function fileOf(photo: PhotoDto, files: readonly OrganizeFileDto[]): OrganizeFileDto | undefined {
  return files.find(f => sameFolder(joinPath(f.folder, f.name), photo.sourcePath));
}

/** What the Share photo sheet says it shares: "Shares a copy without location or camera details", or the original. */
export function shareLine(removeLocation: boolean, removeCamera: boolean): string {
  const without = [removeLocation && 'location', removeCamera && 'camera details'].filter(Boolean);
  return without.length > 0 ? `Shares a copy without ${without.join(' or ')}` : 'Shares the original file';
}

/** What Remove location says of the place: left out, kept, or that the photo says none. */
export function placeLine(place: string | undefined, removeLocation: boolean): string {
  if (place === undefined) return 'This photo does not say where it was taken.';
  return removeLocation ? `${place} is left out of the copy.` : `The copy includes ${place}.`;
}
