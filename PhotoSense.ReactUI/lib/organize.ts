import type { OrganizeExistingDto, OrganizeFileDto, OrganizeFilesDto, OrganizePlaceDto, OrganizePlanDto } from '../types';
import { sameFolder } from './format';

/** Files on one page of the Organize gallery, and of the preview. */
export const GALLERY_PAGE = 60;
export const PREVIEW_PAGE = 100;
/** The distances, in miles, within which nearby places can share a folder. */
export const RADII = [1, 5, 15, 25, 50] as const;
/** Distances are worked out in kilometres and said in miles. */
export const KM_PER_MILE = 1.609344;

export type NameFormat = 'area' | 'city' | 'nest';

export interface OrganizeSettings {
  /** What the folders are made by first. */
  group: 'place' | 'date';
  near: 'combine' | 'separate';
  radiusMiles: number;
  nameFormat: NameFormat;
  /** What a folder for a place is split by inside: Place \ Year, Place \ Month, or not at all. */
  placeThen: 'year' | 'month' | 'none';
  dateGrain: 'year' | 'month';
  /** What a folder for a year or month is split by inside: Year \ Place, or not at all. */
  dateThen: 'place' | 'none';
}

export const defaultSettings: OrganizeSettings = { group: 'place', near: 'combine', radiusMiles: 5, nameFormat: 'area', placeThen: 'year', dateGrain: 'year', dateThen: 'none' };

/** Where a file went in this sitting. */
export interface OrganizedMark { label: string; copied: boolean; }

/** What the person has arranged so far, none of it carried out yet. */
export interface OrganizeChoices {
  /** Files put in one of their own folders: file id to folder id. */
  assigned: Readonly<Record<string, string>>;
  /** Files dropped onto a suggested folder: file id to that suggestion's key. */
  added: Readonly<Record<string, string>>;
  /** Suggested folders given another name: suggestion key to name. */
  renames: Readonly<Record<string, string>>;
  /** Files moved or copied in this sitting, by the path they are at now (for a copy, the original's path). */
  organized: Readonly<Record<string, OrganizedMark>>;
}

export const noChoices: OrganizeChoices = { assigned: {}, added: {}, renames: {}, organized: {} };

interface Point { latitude: number; longitude: number; }

const EARTH_KM = 6371;
const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
/** Characters a folder or file name cannot hold on Windows, the strictest of the systems this runs on. */
const UNUSABLE = /[<>:"|?*/]/;

/** The distance between two positions over the surface of the earth. */
export function haversineKm(a: Point, b: Point): number {
  const rad = Math.PI / 180, dLat = (b.latitude - a.latitude) * rad, dLon = (b.longitude - a.longitude) * rad;
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(a.latitude * rad) * Math.cos(b.latitude * rad) * Math.sin(dLon / 2) ** 2;
  return 2 * EARTH_KM * Math.asin(Math.sqrt(h));
}

/**
 * Gathers places into groups by single linkage: two places share a group when they are within the
 * distance of each other, or are joined by a chain of places that each are. Returns the groups as lists
 * of positions in <paramref name="places"/>, each in ascending order.
 */
export function clusterPlaces(places: readonly Point[], radiusKm: number): number[][] {
  const parent = places.map((_, i) => i);
  const find = (i: number): number => (parent[i] === i ? i : (parent[i] = find(parent[i])));
  for (let i = 0; i < places.length; i++)
    for (let j = i + 1; j < places.length; j++)
      if (haversineKm(places[i], places[j]) <= radiusKm) parent[find(i)] = find(j);
  const groups = new Map<number, number[]>();
  places.forEach((_, i) => groups.set(find(i), [...(groups.get(find(i)) ?? []), i]));
  return [...groups.values()];
}

/** A place's name with whatever a folder name cannot hold taken out. */
const usable = (name: string) => name.replace(/[<>:"|?*/\\]+/g, ' ').replace(/\s+/g, ' ').trim().replace(/\.+$/, '');

/** What a place is called for short: its landmark or area when one is known, otherwise its town. */
export const placeLabel = (p: OrganizePlaceDto) => p.area || p.town;

/** The folder name a place gives, in the chosen style. "\" in it makes a subfolder. */
export function placeName(p: OrganizePlaceDto, format: NameFormat): string {
  if (format === 'city') return `${usable(p.town)}, ${usable(p.state)}`;
  return format === 'nest' ? `${usable(p.state)}\\${usable(placeLabel(p))}` : usable(placeLabel(p));
}

/** What tells one place from another across listings. */
export const placeKey = (p: OrganizePlaceDto) => [p.country, p.state, p.town, p.area ?? ''].join('|');

/** The separator the service's paths use, going by the folder it gave. */
export const separatorOf = (root: string) => (root.includes('\\') || !root.includes('/') ? '\\' : '/');

/** A folder inside another; either slash in a part makes a subfolder. */
export function joinPath(root: string, ...parts: (string | undefined)[]): string {
  const sep = separatorOf(root);
  const names = parts.flatMap(p => (p ?? '').split(/[\\/]+/)).map(n => n.trim()).filter(Boolean);
  return [root.replace(/[\\/]+$/, ''), ...names].join(sep);
}

/** Where a file is. Worked out for every file of a folder at a time, so by the shortest way there is. */
export function pathOf(f: OrganizeFileDto): string {
  const sep = separatorOf(f.folder);
  return f.folder.endsWith(sep) ? f.folder + f.name : f.folder + sep + f.name;
}
export const yearOf = (f: OrganizeFileDto) => f.date.slice(0, 4);
export const monthOf = (f: OrganizeFileDto) => f.date.slice(0, 7);

/** "Dec 31, 2023": the day a file is from. */
export function shortDate(date: string): string {
  return `${MONTHS[Number(date.slice(5, 7)) - 1].slice(0, 3)} ${Number(date.slice(8, 10))}, ${date.slice(0, 4)}`;
}

/** "Jan 2021 – Dec 2023": the months a set of files spans. */
export function dateRange(files: readonly OrganizeFileDto[]): string {
  if (files.length === 0) return '–';
  const months = files.map(monthOf).sort();
  const say = (month: string) => `${MONTHS[Number(month.slice(5, 7)) - 1].slice(0, 3)} ${month.slice(0, 4)}`;
  const first = say(months[0]), last = say(months[months.length - 1]);
  return first === last ? first : `${first} – ${last}`;
}

export const totalBytes = (files: readonly { sizeBytes: number }[]) => files.reduce((sum, f) => sum + f.sizeBytes, 0);

/** A file as far as asking about it and deleting it go: which it is, what it is called, how large, and the folder it is in. */
export type FileRef = Pick<OrganizeFileDto, 'id' | 'name' | 'sizeBytes' | 'folder'>;

/** A folder as it is called inside the root: "Top level", the path below the root, or the whole path when it is elsewhere. */
export function relativeFolder(root: string, folder: string): string {
  const base = root.replace(/[\\/]+$/, '');
  if (sameFolder(base, folder)) return 'Top level';
  const next = folder[base.length];
  return sameFolder(folder.slice(0, base.length), base) && (next === '\\' || next === '/') ? folder.slice(base.length + 1) : folder;
}

/** Everything about a file that the search box looks through. */
export function searchText(f: OrganizeFileDto, places: readonly OrganizePlaceDto[]): string {
  const p = f.place == null ? undefined : places[f.place];
  const where = p ? [p.town, p.area, p.state].filter(Boolean).join(' ') : 'no location';
  return [f.name, where, shortDate(f.date), MONTHS[Number(f.date.slice(5, 7)) - 1]].join(' ').toLowerCase();
}

/** Where a file was taken, in a word or two. */
export function placeOf(f: OrganizeFileDto, places: readonly OrganizePlaceDto[]): string {
  return f.place == null ? 'No location' : places[f.place].town;
}

/** A folder suggested for files that belong together. */
export interface Suggestion {
  key: string;
  name: string;
  files: OrganizeFileDto[];
  /** The places gathered into it, as positions in the listing's places; none for a folder by date. */
  members: number[];
  /** The place with the most files, which the folder is named after. */
  dominant?: number;
  /** What it is split by inside: a subfolder for each year, month or place of its files. Absent when it is not split. */
  split?: 'year' | 'month' | 'place';
  /** "Includes Apgar (2 mi away)", when nearby places were combined. */
  merge: string;
  /** No landmark is known for the place, so its town's name was used. */
  fallback: boolean;
  /** The subfolder a file of it goes to; none for a folder that is not split, or a file with nothing to split it by. */
  subfolderOf(f: OrganizeFileDto): string | undefined;
  /** The folder a file of it goes to. */
  destinationOf(f: OrganizeFileDto): string;
}

/** At most this many of the places combined into a folder are named on its card. */
const NAMED_PLACES = 3;

/**
 * The folders to suggest. Files in the person's own folders, files already organized, and files that
 * already sit in the folder they would go to are left out; a suggestion with nothing left is not made.
 */
export function suggest(listing: OrganizeFilesDto, settings: OrganizeSettings, choices: OrganizeChoices): Suggestion[] {
  const { root, files, places } = listing;
  const free = (f: OrganizeFileDto) => !choices.organized[pathOf(f)] && !choices.assigned[f.id];
  const named = (key: string, name: string) => (key in choices.renames ? choices.renames[key] : name);
  const out: Suggestion[] = [];

  // Places are gathered whether they come first or second; left alone when the folders are by date only.
  const byPlace = settings.group === 'place', placed = byPlace || settings.dateThen === 'place';
  const groups = !placed ? [] : settings.near === 'combine' ? clusterPlaces(places, settings.radiusMiles * KM_PER_MILE) : places.map((_, i) => [i]);
  const count = places.map(() => 0);
  for (const f of files) if (f.place != null) count[f.place]++;
  const groupOfPlace = new Map(groups.flatMap((members, g) => members.map(i => [i, g] as const)));
  // The place with the most files, which a group is named after.
  const dominants = groups.map(members => members.reduce((a, b) => (count[b] > count[a] ? b : a)));

  if (byPlace) {
    const keyOf = (members: number[]) => `p:${members.map(i => placeKey(places[i])).join('+')}`;
    const keys = groups.map(keyOf), groupOfKey = new Map(keys.map((key, g) => [key, g]));
    const split = settings.placeThen === 'none' ? undefined : settings.placeThen;
    const subfolderOf = (f: OrganizeFileDto) => (split === 'year' ? yearOf(f) : split === 'month' ? monthOf(f) : undefined);
    // Each file is put with its folder in one pass over the files: a pass for every folder is far too slow for
    // a folder of tens of thousands. A file dropped onto a suggested folder stays with that folder for as long
    // as it is still suggested; any other goes with the folder of its place.
    const taken: OrganizeFileDto[][] = groups.map(() => []);
    for (const f of files) {
      if (!free(f)) continue;
      const g = groupOfKey.get(choices.added[f.id]) ?? (f.place == null ? undefined : groupOfPlace.get(f.place));
      if (g !== undefined) taken[g].push(f);
    }
    for (const [g, members] of groups.entries()) {
      const key = keys[g], dominant = dominants[g], at = places[dominant];
      const name = named(key, placeName(at, settings.nameFormat));
      const destinationOf = (f: OrganizeFileDto) => joinPath(root, name, subfolderOf(f));
      const others = members.filter(i => i !== dominant)
        .map(i => `${placeLabel(places[i])} (${Math.max(1, Math.round(haversineKm(at, places[i]) / KM_PER_MILE))} mi away)`);
      const more = others.length - NAMED_PLACES;
      out.push({
        key, name, members, dominant, subfolderOf, destinationOf, split,
        files: taken[g].filter(f => !sameFolder(f.folder, destinationOf(f))),
        merge: others.length === 0 ? '' : `Includes ${others.slice(0, NAMED_PLACES).join(', ')}${more > 0 ? ` and ${more} more` : ''}`,
        fallback: !at.area && settings.nameFormat !== 'city' && !(key in choices.renames),
      });
    }
    return out.filter(s => s.files.length > 0).sort((a, b) => b.files.length - a.files.length);
  }

  const natural = (f: OrganizeFileDto) => (settings.dateGrain === 'year' ? `y:${yearOf(f)}` : `m:${monthOf(f)}`);
  const unarranged = files.filter(free), keys = new Set(unarranged.map(natural));
  const by = new Map<string, OrganizeFileDto[]>();
  for (const f of unarranged) {
    const key = keys.has(choices.added[f.id]) ? choices.added[f.id] : natural(f);
    const held = by.get(key);
    if (held) held.push(f); else by.set(key, [f]);
  }
  // Inside a year or month, a file goes with the folder its place makes; one with no place stays in the year or month itself.
  const split = placed ? 'place' as const : undefined;
  const placeNames = dominants.map(i => placeName(places[i], settings.nameFormat));
  const subfolderOf = (f: OrganizeFileDto) => (f.place == null ? undefined : placeNames[groupOfPlace.get(f.place)!]);
  // The latest first.
  for (const key of [...by.keys()].sort().reverse()) {
    const name = named(key, key.slice(2));
    const destinationOf = (f: OrganizeFileDto) => joinPath(root, name, subfolderOf(f));
    out.push({
      key, name, members: [], split, merge: '', fallback: false, subfolderOf, destinationOf,
      files: by.get(key)!.filter(f => !sameFolder(f.folder, destinationOf(f))),
    });
  }
  return out.filter(s => s.files.length > 0);
}

/** Files with no place that nothing has been arranged for: what the "No location" card counts. */
export function unplaced(listing: OrganizeFilesDto, choices: OrganizeChoices, suggestions: readonly Suggestion[]): OrganizeFileDto[] {
  const keys = new Set(suggestions.map(s => s.key));
  return listing.files.filter(f => f.place == null && !choices.organized[pathOf(f)] && !choices.assigned[f.id] && !keys.has(choices.added[f.id]));
}

/** A folder name as it will be used, or what is wrong with it. */
export function checkFolderName(typed: string, existing: readonly string[] = []): { name: string; error?: undefined } | { error: string } {
  const name = typed.trim().replace(/^[\\/]+|[\\/]+$/g, '');
  if (!name) return { error: 'Give the new folder a name.' };
  if (UNUSABLE.test(name)) return { error: 'Folder names cannot contain < > : " / | ? or *' };
  if (existing.some(e => e.toLowerCase() === name.toLowerCase())) return { error: `You already have a folder called ${name}.` };
  return { name };
}

/** The tag over a file's picture: the folder it is set to go to, or the one it went to. */
export function fileTag(f: OrganizeFileDto, choices: OrganizeChoices, folderNames: Readonly<Record<string, string>>, suggested: ReadonlyMap<string, Suggestion>): { text: string; done: boolean } | undefined {
  const went = choices.organized[pathOf(f)];
  const going = folderNames[choices.assigned[f.id]] ?? (went ? undefined : suggested.get(choices.added[f.id])?.name);
  if (going) return { text: `For ${going}`, done: false };
  return went ? { text: `${went.copied ? 'Copied to' : 'In'} ${went.label}`, done: true } : undefined;
}

// ---- names already taken

/** A file name apart from its extension: "IMG_1.JPG" is "IMG_1" and ".JPG". */
export function splitName(name: string): { base: string; ext: string } {
  const dot = name.lastIndexOf('.');
  return dot > 0 ? { base: name.slice(0, dot), ext: name.slice(dot) } : { base: name, ext: '' };
}

/** The first of "name (1).ext", "name (2).ext" and so on that nothing has yet. Names are compared without regard to case. */
export function nextFreeName(base: string, ext: string, taken: ReadonlySet<string>): string {
  for (let n = 1; ; n++) {
    const name = `${base} (${n})${ext}`;
    if (!taken.has(name.toLowerCase())) return name;
  }
}

/** What to do about a file whose name is taken where it is going, when it is not simply given a number. */
export type ClashChoice = { mode: 'auto' } | { mode: 'rename'; name: string };

/** A file that has, or will have, the name another is arriving under. */
export interface TakenBy extends Omit<OrganizeExistingDto, 'id'> {
  id: string;
  /** The folder it is in now: the one the other is going to, or for another file of the move, the one it is still in. */
  folder: string;
  /** What it is called now. Another file of the move is listed under the name it will arrive by, which may not be this. */
  ownName: string;
  /** Not there yet: it is another file of this same move. */
  incoming: boolean;
}

export interface Clash {
  file: OrganizeFileDto;
  /** The folder it is going to. */
  folder: string;
  base: string;
  ext: string;
  mode: 'auto' | 'rename' | 'skip';
  /** The name typed for it, without the extension. */
  wanted: string;
  /** The name typed cannot be used, so the file gets a number instead. */
  bad: boolean;
  /** The name a number gives it. */
  auto: string;
  /** The name it arrives under; absent when it stays where it is. */
  final?: string;
  takenBy: TakenBy[];
}

export interface NamePlan {
  /** Files whose own name is taken where they are going, in the order of the files. */
  clashes: Clash[];
  /** The name every file that goes arrives under, by file id. */
  names: Record<string, string>;
}

/**
 * Settles the name each file arrives under so that nothing is replaced: not a file already in the
 * folder, and not another file of the same move. Files are taken in order; one whose name is taken gets
 * the first free number unless a usable name was typed for it. Files left out take no name.
 */
export function resolveNames(files: readonly OrganizeFileDto[], plan: OrganizePlanDto, choices: Readonly<Record<string, ClashChoice>>, excluded: Readonly<Record<string, boolean>>): NamePlan {
  const folderOf = new Map(plan.items.map(i => [i.id, i.folder]));
  const onDisk = new Map(plan.clashes.map(c => [c.id, c.existing]));
  const taken = new Map(Object.entries(plan.taken).map(([folder, names]) => [folder, new Set(names.map(n => n.toLowerCase()))]));
  const arrived = new Map<string, { file: OrganizeFileDto; name: string }[]>();
  const result: NamePlan = { clashes: [], names: {} };

  for (const file of files) {
    const folder = folderOf.get(file.id);
    if (folder === undefined) continue;
    const names = taken.get(folder) ?? new Set<string>(), arrivals = arrived.get(folder) ?? [];
    taken.set(folder, names);
    arrived.set(folder, arrivals);
    const off = !!excluded[file.id];
    const arrive = (name: string) => {
      names.add(name.toLowerCase());
      arrivals.push({ file, name });
      result.names[file.id] = name;
    };
    if (!names.has(file.name.toLowerCase())) {
      if (!off) arrive(file.name);
      continue;
    }

    const { base, ext } = splitName(file.name), choice = choices[file.id] ?? { mode: 'auto' };
    const wanted = choice.mode === 'rename' ? choice.name : base;
    const mode = off ? 'skip' : choice.mode;
    const bad = mode === 'rename' && (!wanted.trim() || /[<>:"|?*/\\]/.test(wanted) || names.has((wanted.trim() + ext).toLowerCase()));
    const auto = nextFreeName(base, ext, names);
    const final = off ? undefined : mode === 'rename' && !bad ? wanted.trim() + ext : auto;
    // The same name with a number is the same family: "(1)" being taken as well is why the next is "(2)".
    const family = new RegExp(`^${escape(base)}( \\(\\d+\\))?${escape(ext)}$`, 'i');
    const coming = arrivals.filter(a => family.test(a.name)).map(a => ({
      id: a.file.id, name: a.name, sizeBytes: a.file.sizeBytes, date: a.file.date, width: a.file.width, height: a.file.height,
      isVideo: a.file.isVideo, placeName: null, identical: false, incoming: true, folder: a.file.folder, ownName: a.file.name,
    }));
    result.clashes.push({
      file, folder, base, ext, mode, wanted, bad, auto, final,
      takenBy: [...(onDisk.get(file.id) ?? []).map(e => ({ ...e, incoming: false, folder, ownName: e.name })), ...coming],
    });
    if (final !== undefined) arrive(final);
  }
  return result;
}

const escape = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

/** "18 get a number · 2 renamed · 1 left where it is": what will happen to the files whose names are taken. */
export function clashSummary(clashes: readonly Clash[]): string {
  const skipped = clashes.filter(c => c.mode === 'skip').length, renamed = clashes.filter(c => c.mode === 'rename' && !c.bad).length;
  const numbered = clashes.length - skipped - renamed;
  return [
    numbered > 0 && `${numbered.toLocaleString()} ${numbered === 1 ? 'gets' : 'get'} a number`,
    renamed > 0 && `${renamed.toLocaleString()} renamed`,
    skipped > 0 && `${skipped.toLocaleString()} left where ${skipped === 1 ? 'it is' : 'they are'}`,
  ].filter(Boolean).join(' · ');
}

// ---- the map

export interface MapDot {
  key: string; x: number; y: number; size: number; label: string; labelled: boolean; labelSide: 'left' | 'right'; tip: string;
  /** Where the dot is on the earth, for a map that is drawn from that. */
  latitude: number; longitude: number;
}
export interface MapLayout {
  dots: MapDot[];
  /** The reach within which places were combined, around each place: centre and diameter, in percent of the width. */
  rings: { x: number; y: number; diameter: number }[];
  /** The places gathered into a dot that stands for several. */
  merged: { x: number; y: number }[];
  /** Height over width that keeps distances true in both directions. */
  aspect: number;
  /** A round distance and how much of the width it covers, for the scale. */
  scale: { miles: number; percent: number };
}

/** The largest folders are labelled on the map; the rest say what they are on hover. */
const LABELLED = 6;
const SCALES = [1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000];

/**
 * Lays the suggested folders out as dots on a plain map: east to the right, north up, distances true to
 * the middle latitude, with a margin so that no dot sits on the edge. Positions are in percent.
 */
export function mapLayout(suggestions: readonly Suggestion[], places: readonly OrganizePlaceDto[], settings: OrganizeSettings): MapLayout | undefined {
  const shown = suggestions.filter(s => s.members.length > 0);
  if (shown.length === 0) return undefined;
  const points = shown.flatMap(s => s.members.map(i => places[i]));
  const lats = points.map(p => p.latitude), lons = points.map(p => p.longitude);
  const midLat = (Math.min(...lats) + Math.max(...lats)) / 2, shrink = Math.max(0.05, Math.cos(midLat * Math.PI / 180));
  const kmPerDegree = 2 * Math.PI * EARTH_KM / 360;
  const east = (lon: number) => lon * kmPerDegree * shrink, north = (lat: number) => lat * kmPerDegree;
  // At least 20 km (some 12 miles) across, so that a single town is not blown up to fill the map.
  const spanX = Math.max(20, east(Math.max(...lons)) - east(Math.min(...lons))), spanY = Math.max(20, north(Math.max(...lats)) - north(Math.min(...lats)));
  const radiusKm = settings.radiusMiles * KM_PER_MILE;
  const margin = 0.14 * Math.max(spanX, spanY) + (settings.near === 'combine' ? radiusKm / 2 : 0);
  const width = spanX + 2 * margin, height = spanY + 2 * margin;
  const left = (east(Math.min(...lons)) + east(Math.max(...lons))) / 2 - width / 2, top = (north(Math.min(...lats)) + north(Math.max(...lats))) / 2 + height / 2;
  const x = (p: Point) => (east(p.longitude) - left) / width * 100, y = (p: Point) => (top - north(p.latitude)) / height * 100;

  const layout: MapLayout = { dots: [], rings: [], merged: [], aspect: height / width, scale: { miles: 0, percent: 0 } };
  // The map is never less than some 16 miles across, so there is always a scale that fits a quarter of it.
  const miles = SCALES.filter(s => s <= width / KM_PER_MILE / 4).pop()!;
  layout.scale = { miles, percent: miles * KM_PER_MILE / width * 100 };
  shown.forEach((s, rank) => {
    // The dot sits at the middle of the suggestion's files, not of its places.
    const counts = new Map<number, number>();
    for (const f of s.files) if (f.place != null && s.members.includes(f.place)) counts.set(f.place, (counts.get(f.place) ?? 0) + 1);
    const weigh = (i: number) => counts.get(i) ?? 0, total = s.members.reduce((n, i) => n + weigh(i), 0);
    const centre = total === 0 ? places[s.dominant!] : {
      latitude: s.members.reduce((sum, i) => sum + places[i].latitude * weigh(i), 0) / total,
      longitude: s.members.reduce((sum, i) => sum + places[i].longitude * weigh(i), 0) / total,
    };
    const cx = x(centre);
    layout.dots.push({
      key: s.key, x: cx, y: y(centre), latitude: centre.latitude, longitude: centre.longitude, size: Math.round(Math.min(40, 12 + Math.sqrt(s.files.length) * 0.8)),
      label: places[s.dominant!].town, labelled: rank < LABELLED, labelSide: cx > 68 ? 'left' : 'right',
      tip: `${s.name} · ${s.files.length.toLocaleString()} ${s.files.length === 1 ? 'file' : 'files'}`,
    });
    if (s.members.length > 1) for (const i of s.members) layout.merged.push({ x: x(places[i]), y: y(places[i]) });
    if (settings.near === 'combine') for (const i of s.members) layout.rings.push({ x: x(places[i]), y: y(places[i]), diameter: radiusKm / width * 100 });
  });
  return layout;
}
