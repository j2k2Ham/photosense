import { useCallback, useMemo, useState } from 'react';
import type { OrganizeBatchDto, OrganizeFileDto, OrganizeFilesDto } from '../types';
import { leaf, sameFolder } from './format';
import { shortDistance, type DistanceUnit } from './distance';
import { isUnsorted, whereIs } from './library';
import { dateRange, defaultSettings, joinPath, MONTHS, relativeFolder, totalBytes, type OrganizeSettings, type Suggestion, suggest, noChoices } from './organize';
import type { Platform, Words } from './phone';

/** Which files Organize sorts on a phone: pictures and videos, pictures only, or videos only. */
export type FileKinds = 'both' | 'photos' | 'videos';

/** A suggestion moved in this sitting: what the row and the done state say of it, and how it is taken back. */
export interface Moved {
  readonly name: string;
  readonly count: number;
  readonly bytes: number;
  readonly range: string;
  /** The album or folder the files went to. */
  readonly path: string;
  /** What undoes it; absent when the move left nothing to undo. */
  readonly batchId?: string;
}

/** One row of Organize's list: a suggestion still to move, or one moved in this sitting. */
export interface OrganizeRow {
  readonly key: string;
  readonly suggestion?: Suggestion;
  readonly moved?: Moved;
}

/** An album made on the phone with no files yet: it is made on the phone when files are first put in it, from Library. */
export interface MadeAlbum { readonly name: string; readonly path: string }

/** Organize on a phone or tablet: how it sorts, what it suggests, and which suggestion is being looked at. */
export interface PhoneOrganize {
  readonly settings: OrganizeSettings;
  readonly kinds: FileKinds;
  readonly view: 'list' | 'map';
  /** The suggestions, then those moved in this sitting, in the list's order. */
  readonly rows: readonly OrganizeRow[];
  /** Files left where they are because nothing says where they were taken. */
  readonly unplaced: number;
  /** How many files the suggestions would move. */
  readonly toMove: number;
  /** The row being looked at: on a phone the one pushed over the list, if any; on a tablet the one beside it. */
  readonly open?: OrganizeRow;
  change(change: Partial<OrganizeSettings>): void;
  setKinds(kinds: FileKinds): void;
  setView(view: 'list' | 'map'): void;
  /** Looks at a row, or with none goes back to the list. */
  look(key: string | undefined): void;
  rename(key: string, name: string): void;
  /** Records a suggestion as moved, and looks at what was done (a tablet) or goes back to the list (a phone). */
  moved(key: string, done: Moved): void;
  /** Forgets a move that was undone. */
  undone(batchId: string): void;
}

/**
 * Organize's state on a phone or tablet. Only what is in no album yet is sorted (`isUnsorted`), so what was moved is
 * left out by itself, and only the kind of file shown. A Live Photo's video goes with its picture, never on its own.
 */
export function useOrganize(listing: OrganizeFilesDto | undefined, platform: Platform, tablet: boolean, unit: DistanceUnit): PhoneOrganize {
  const [chosen, setSettings] = useState<OrganizeSettings>(defaultSettings);
  const settings = useMemo(() => ({ ...chosen, unit }), [chosen, unit]);
  const [kinds, setKinds] = useState<FileKinds>('both');
  const [view, setView] = useState<'list' | 'map'>('list');
  const [renames, setRenames] = useState<Readonly<Record<string, string>>>({});
  const [done, setDone] = useState<Readonly<Record<string, Moved>>>({});
  const [looking, setLooking] = useState<string>();

  const sorting = useMemo(() => {
    if (!listing) return undefined;
    const goes = (f: OrganizeFileDto) => kinds === 'both' || (kinds === 'photos' ? !f.isVideo : f.isVideo && !f.livePhoto);
    return { ...listing, files: listing.files.filter(f => isUnsorted(f, listing.root, platform) && goes(f)) };
  }, [listing, platform, kinds]);
  const suggestions = useMemo(() => (sorting ? suggest(sorting, settings, { ...noChoices, renames }) : []), [sorting, settings, renames]);
  const rows = useMemo(() => {
    const live: OrganizeRow[] = suggestions.map(s => ({ key: s.key, suggestion: s }));
    const keys = new Set(suggestions.map(s => s.key));
    const gone: OrganizeRow[] = Object.entries(done).filter(([key]) => !keys.has(key)).map(([key, moved]) => ({ key, moved }));
    const count = (r: OrganizeRow) => r.suggestion?.files.length ?? r.moved!.count;
    // By place the largest come first; by date the latest.
    return [...live, ...gone].sort(settings.group === 'place' ? (a, b) => count(b) - count(a) : (a, b) => b.key.localeCompare(a.key));
  }, [suggestions, done, settings.group]);
  const unplaced = sorting && settings.group === 'place' ? sorting.files.filter(f => f.place == null).length : 0;
  const toMove = suggestions.reduce((n, s) => n + s.files.length, 0);
  const open = rows.find(r => r.key === looking) ?? (tablet ? rows[0] : undefined);

  const undone = useCallback((batchId: string) => setDone(d => Object.fromEntries(Object.entries(d).filter(([, m]) => m.batchId !== batchId))), []);
  return {
    settings, kinds, view, rows, unplaced, toMove, open, setKinds, setView, undone,
    change(change) {
      // Grouping by date sorts each year by place inside, and by place each place by year, as the design starts them.
      const group = change.group && change.group !== settings.group ? (change.group === 'date' ? { dateThen: 'place' as const } : { placeThen: 'year' as const }) : {};
      setSettings(s => ({ ...s, ...group, ...change }));
      if (change.nameFormat || change.group) setRenames({});
      if (change.group) setLooking(undefined);
    },
    look: setLooking,
    rename(key, name) { setRenames(r => ({ ...r, [key]: name })); },
    moved(key, moved) {
      setDone(d => ({ ...d, [key]: moved }));
      setLooking(tablet ? key : undefined);
    },
  };
}

/** What the options come to, in a line: "Within 5 mi · Landmark or area · Place › Year". */
export function optionsLine(s: OrganizeSettings, kinds: FileKinds, words: Words): string {
  const sep = words.separator;
  const parts = s.group === 'place'
    ? [s.near === 'combine' ? `Within ${shortDistance(s.radius, s.unit)}` : 'Keep separate', s.nameFormat === 'town' ? 'Town' : 'Landmark or area', insideLine(s, words)]
    : ['By year', s.dateThen === 'place' ? `Year${sep}Place` : 'Year'];
  if (kinds !== 'both') parts.push(kinds === 'photos' ? 'Photos only' : 'Videos only');
  return parts.join(' · ');
}

/** What an album holds inside, as the preview says it: "Place › Year", or "Nothing". */
export function insideLine(s: OrganizeSettings, words: Words): string {
  if (s.group === 'date') return s.dateThen === 'place' ? `Year${words.separator}Place` : 'Nothing';
  return s.placeThen === 'none' ? 'Place' : `Place${words.separator}${s.placeThen === 'year' ? 'Year' : 'Month'}`;
}

/** The line under a suggestion's name and size: why it is named as it is, what it takes in, or the places inside a year. */
export function rowNote(s: Suggestion): string | undefined {
  if (s.split === 'place') {
    const places = new Set(s.files.map(s.subfolderOf).filter(Boolean)).size;
    if (s.files.filter(f => f.place == null).length * 2 > s.files.length) return 'No location on most';
    return `${places} ${places === 1 ? 'place' : 'places'} inside`;
  }
  if (s.fallback) return 'No landmark known here, so it is named after the town.';
  return s.merge ? `${s.merge}.` : undefined;
}

/** "724 files", "1 file". */
export const files = (n: number) => `${n.toLocaleString()} ${n === 1 ? 'file' : 'files'}`;

/** What a suggestion's row says of its size and when its files are from. */
export function rowFacts(row: OrganizeRow): { count: number; bytes: number; range: string } {
  if (row.moved) return row.moved;
  const f = row.suggestion!.files;
  return { count: f.length, bytes: totalBytes(f), range: dateRange(f) };
}

/** A place an album or folder can be made in, as the person chooses it. */
export interface Place { readonly path: string; readonly title: string; readonly detail: string }

/**
 * Where a new album or folder can go. On an iPhone, the top of the library, or a folder of albums already made (one that
 * holds albums, as a folder in Photos does); on Android, Pictures, beside the camera in DCIM, or a folder already made
 * there, Android letting apps make folders only inside DCIM, Pictures or Movies.
 */
export function placesFor(root: string, folders: readonly string[], platform: Platform): Place[] {
  const inside = (folder: string) => relativeFolder(root, folder);
  // Every folder that holds another: those a file is in are albums, those that hold albums are folders of them.
  const holders = new Set<string>();
  for (const folder of folders) {
    const below = inside(folder);
    // The top of the library holds nothing above it; a folder outside the library is no place to make one.
    if (below === 'Top level' || below === folder) continue;
    const parts = below.split(/[\\/]/);
    for (let n = 1; n < parts.length; n++) holders.add(joinPath(root, ...parts.slice(0, n)));
  }
  if (platform === 'ios') {
    return [{ path: root, title: 'Photo Library', detail: 'At the top level of your albums' },
      ...[...holders].sort().map(path => ({ path, title: leaf(path), detail: 'A folder of albums made earlier' }))];
  }
  const pictures = joinPath(root, 'Pictures'), dcim = joinPath(root, 'DCIM');
  const allowed = (path: string) => ['Pictures', 'DCIM', 'Movies'].includes(inside(path).split(/[\\/]/)[0]);
  const others = [...holders].filter(p => allowed(p) && !sameFolder(p, pictures) && !sameFolder(p, dcim) && !sameFolder(p, joinPath(root, 'Movies'))).sort();
  return [{ path: pictures, title: 'Pictures', detail: 'Pictures, on this phone' }, { path: dcim, title: 'DCIM', detail: 'Beside the camera\'s own folder' },
    ...others.map(path => ({ path, title: leaf(path), detail: `${inside(path).split(/[\\/]/).join('/')}, made earlier` }))];
}

/** The preview's files in sections, by the subfolder each goes to: years, months or places, in order; or one section, "All files". */
export function sections(going: readonly OrganizeFileDto[], subfolderOf: (f: OrganizeFileDto) => string | undefined, split?: 'year' | 'month' | 'place'): { title: string; files: OrganizeFileDto[] }[] {
  if (!split) return [{ title: 'All files', files: [...going] }];
  const by = new Map<string, OrganizeFileDto[]>();
  for (const f of going) {
    const at = subfolderOf(f) ?? '';
    by.set(at, [...(by.get(at) ?? []), f]);
  }
  const title = (at: string) => (at === '' ? (split === 'place' ? 'No location' : 'Other') : split === 'month' ? `${MONTHS[Number(at.slice(5, 7)) - 1]} ${at.slice(0, 4)}` : at);
  return [...by.keys()].sort((a, b) => (a === '' ? 1 : b === '' ? -1 : a.localeCompare(b))).map(at => ({ title: title(at), files: by.get(at)! }));
}

/** "Just now", "2 min ago", "3 h ago", "Yesterday, 6:12 PM", "Oct 28, 6:12 PM": when something was done. */
export function ago(utc: string, now: Date): string {
  const at = new Date(utc), minutes = Math.floor((now.getTime() - at.getTime()) / 60_000);
  if (minutes < 1) return 'Just now';
  if (minutes < 60) return `${minutes} min ago`;
  const yesterday = new Date(now);
  yesterday.setDate(now.getDate() - 1);
  const hour = at.getHours(), time = `${hour % 12 || 12}:${String(at.getMinutes()).padStart(2, '0')} ${hour < 12 ? 'AM' : 'PM'}`;
  if (at.toDateString() === now.toDateString()) return `${Math.floor(minutes / 60)} h ago`;
  if (at.toDateString() === yesterday.toDateString()) return `Yesterday, ${time}`;
  return `${MONTHS[at.getMonth()].slice(0, 3)} ${at.getDate()}, ${time}`;
}

/** What a change in Recently moved did, in a line: "Moved 724 files to Anaconda", "Deleted 3 files from Photo Library". */
export function batchLine(b: OrganizeBatchDto, root: string | undefined, words: Words): string {
  if (b.mode === 'remove') return `Deleted ${files(b.count)} from ${root ? whereIs(b.label, root, words) : leaf(b.label)}`;
  return `${b.mode === 'copy' ? 'Copied' : 'Moved'} ${files(b.count)} to ${b.label}`;
}

/**
 * Where an album or folder goes, as the design names a destination: "Photo Library › Anaconda" on an iPhone; on Android
 * "Pictures/Anaconda", the phone's storage going unsaid where things are put.
 */
export function goesTo(path: string, root: string, words: Words, platform: Platform): string {
  if (platform === 'ios') return whereIs(path, root, words);
  const inside = relativeFolder(root, path);
  return inside === 'Top level' ? words.library : inside.split(/[\/]/).join('/');
}
