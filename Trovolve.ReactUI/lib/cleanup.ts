import { useCallback, useState } from 'react';
import type { CategoryName, DuplicateGroupDto, GroupMemberDto, GroupMode, GroupsPageDto, PhotoDto } from '../types';
import { useGroupsThrough } from './apiClient';
import { formatBytes, formatDimensions, formatDuration, leaf, linkedFiles, matchMeaning } from './format';
import { takenAt, whereIs } from './library';
import { MONTHS } from './organize';
import type { Words } from './phone';

/** A phone's Clean up: which groups are listed, how many of them, and the one being reviewed. */
export interface CleanUp {
  readonly mode: GroupMode;
  /** What the service said of the groups listed, once it has. */
  readonly data?: GroupsPageDto;
  /** The groups read so far. */
  readonly items: readonly DuplicateGroupDto[];
  /** There are more groups than have been read. */
  readonly more: boolean;
  /** A list of groups is open, from Find; otherwise Clean up's first screen shows. */
  readonly listing: boolean;
  /** A category's screen opened from Find, or Free up space from the storage bar; none while the first screen or a list shows. */
  readonly opened?: Exclude<CategoryName, 'duplicates'> | 'free';
  /** The group being reviewed: on a phone the one pushed over the list, if any; on a tablet the one beside it. */
  readonly group?: DuplicateGroupDto;
  /** Which of its copies is shown beside the one that stays. */
  readonly copy: number;
  /** Opens the list of duplicates or of similar shots, from Find. */
  openList(mode: GroupMode): void;
  /** Goes back from the list to Clean up's first screen. */
  closeList(): void;
  /** Opens a category's screen, or Free up space. */
  open(what: Exclude<CategoryName, 'duplicates'> | 'free'): void;
  /** Goes back from a category's screen to Clean up's first screen. */
  close(): void;
  /** Reads the next groups, as the end of the list comes into view. */
  showMore(): void;
  /** Reviews a group, or, with none, goes back to the list. */
  review(key: string | undefined): void;
  chooseCopy(index: number): void;
}

const NONE: readonly DuplicateGroupDto[] = [];

/**
 * Clean up's state on a phone or tablet. A group reviewed that goes, as it does once its last copy is deleted, gives
 * way on a tablet to the group that took its place in the list; a phone goes back to the list.
 */
export function useCleanUp(tablet: boolean): CleanUp {
  const [mode, setMode] = useState<GroupMode>('duplicates');
  const [pages, setPages] = useState(1);
  const [chosen, setChosen] = useState<{ key: string; index: number }>();
  const [copy, setCopy] = useState(0);
  const [listing, setListing] = useState(false);
  const [opened, setOpened] = useState<Exclude<CategoryName, 'duplicates'> | 'free'>();
  const answer = useGroupsThrough(mode, pages).data;
  // While the other kind is still being read, the groups of the kind before are not shown in its place.
  const data = answer?.mode === mode ? answer : undefined;
  const items = data?.items ?? NONE;
  const found = chosen && items.find(g => g.key === chosen.key);
  if (chosen && data && !found) {
    const next = tablet ? items[Math.min(chosen.index, items.length - 1)] : undefined;
    setChosen(next && { key: next.key, index: chosen.index });
    setCopy(0);
  }
  const group = found ?? (tablet && listing && !chosen ? items[0] : undefined);
  // The same each time, as the list watches for its end by it.
  const showMore = useCallback(() => setPages(p => p + 1), []);
  return {
    mode, data, items, group, listing, opened,
    more: !!data && pages < data.totalPages,
    copy: group ? Math.min(copy, group.members.length - 1) : 0,
    openList(next) { setMode(next); setPages(1); setChosen(undefined); setCopy(0); setListing(true); },
    closeList() { setListing(false); setChosen(undefined); setCopy(0); },
    open: setOpened,
    close() { setOpened(undefined); },
    showMore,
    review(key) { setChosen(key === undefined ? undefined : { key, index: items.findIndex(g => g.key === key) }); setCopy(0); },
    chooseCopy: setCopy,
  };
}

/** "1 duplicate", "264 duplicates". */
export const duplicates = (n: number) => `${n.toLocaleString()} ${n === 1 ? 'duplicate' : 'duplicates'}`;

/** Where a file is, in the platform's words, once the library's own folder is known; until then, its folder's name. */
export function whereOf(photo: PhotoDto, root: string | undefined, words: Words): string {
  return root ? whereIs(photo.folder, root, words) : leaf(photo.folder);
}

const TIMES: Record<number, string> = { 2: 'twice', 3: 'three times', 4: 'four times' };

/** Why a copy is in the group, beside its kind of match: "Same bytes, saved twice.", "Converted and resized." */
export function matchNote(group: DuplicateGroupDto, member: GroupMemberDto): string {
  if (member.match === 'identical') {
    const n = group.members.length + 1;
    return `Same bytes, saved ${TIMES[n] ?? `${n.toLocaleString()} times`}.`;
  }
  if (member.match === 'similar') return matchMeaning.similar.split('. ')[0] + '.';
  const keeper = group.keeper, copy = member.photo;
  const converted = (copy.format ?? '?') !== (keeper.format ?? '?');
  const resized = copy.width !== keeper.width || copy.height !== keeper.height;
  if (converted && resized) return 'Converted and resized.';
  if (converted) return `The same picture, as ${keeper.format ?? '?'} and as ${copy.format ?? '?'}.`;
  return resized ? 'The same picture, resized.' : 'The same picture, saved again.';
}

/** One line of the review's comparison: what is compared, and each file's value. */
export interface DetailRow {
  readonly label: string;
  readonly original: string;
  readonly copy: string;
  readonly differs: boolean;
  /** A name or a place in the library, in the code face. */
  readonly code: boolean;
}

/** The original and a copy, detail by detail, in the design's order. */
export function detailRows(original: PhotoDto, copy: PhotoDto, root: string | undefined, words: Words): DetailRow[] {
  const size = (p: PhotoDto) => (p.durationSeconds == null ? formatDimensions(p) : `${formatDimensions(p)} · ${formatDuration(p.durationSeconds)}`);
  const rows: [string, (p: PhotoDto) => string, boolean?][] = [
    ['Name', p => p.fileName, true],
    ['Format', p => p.format ?? '?'],
    ['File size', p => formatBytes(p.fileSizeBytes)],
    [words.Album, p => whereOf(p, root, words), true],
    ['Taken', p => (p.takenOn ? takenAt(p.takenOn) : 'No date')],
    ['Place', p => p.placeName ?? 'No location'],
    [original.isVideo ? 'Video' : 'Pixel size', size],
  ];
  return rows.map(([label, of, code = false]) => ({ label, original: of(original), copy: of(copy), differs: of(original) !== of(copy), code }));
}

/** "Scanned 6,941 photos and videos today at 4:25 PM.": how much the last scan took in, and when it finished. */
export function scannedLine(total: number, completed: string, now: Date): string {
  const at = new Date(completed);
  const day = (d: Date) => d.toDateString();
  const yesterday = new Date(now);
  yesterday.setDate(now.getDate() - 1);
  const date = `${MONTHS[at.getMonth()].slice(0, 3)} ${at.getDate()}${at.getFullYear() === now.getFullYear() ? '' : `, ${at.getFullYear()}`}`;
  const when = day(at) === day(now) ? 'today' : day(at) === day(yesterday) ? 'yesterday' : `on ${date}`;
  const hour = at.getHours();
  const time = `${hour % 12 || 12}:${String(at.getMinutes()).padStart(2, '0')} ${hour < 12 ? 'AM' : 'PM'}`;
  return `Scanned ${total.toLocaleString()} ${total === 1 ? 'photo or video' : 'photos and videos'} ${when} at ${time}.`;
}

/** A question in a sheet: its title, what will happen, and its button. */
export interface Question { readonly title: string; readonly lines: readonly string[]; readonly button: string }

/**
 * The question before one copy is deleted: the file that stays, then the file that goes, each with where it is. It goes
 * to the phone's Recently Deleted or bin (Jamie, 2026-11-17), which the phone asks about too.
 */
export function copyQuestion(copy: PhotoDto, keeper: PhotoDto, where: (p: PhotoDto) => string, words: Words): Question {
  return {
    title: `Delete ${copy.fileName}?`,
    lines: [`${keeper.fileName} stays, in ${where(keeper)}.`, `${copy.fileName}, in ${where(copy)}, goes to ${words.bin} for 30 days.`],
    button: `Delete ${copy.fileName}`,
  };
}

/** The question before every duplicate is deleted, or, with `some`, as many as the free plan has left today. */
export function allQuestion(count: number, bytes: number, words: Words, some = false): Question {
  return {
    title: some ? `Delete ${duplicates(count)}?` : 'Delete all duplicates?',
    lines: [
      `${count.toLocaleString()} ${count === 1 ? 'copy goes' : 'copies go'} to ${words.bin} (${formatBytes(bytes)}) for 30 days.`,
      some ? 'The largest go first. The best copy of each picture or video stays.' : 'The best copy of each picture or video stays. Copies marked keep are skipped.',
      'Similar shots are not included.',
    ],
    button: `Delete ${duplicates(count)}`,
  };
}

/**
 * The groups whose copies, together, come to no more than `count`, the groups that free the most first: what "Delete L
 * now" takes when the free plan has fewer removals left than there are duplicates. Copies marked keep are not counted,
 * as they stay.
 */
export function someGroups(groups: readonly DuplicateGroupDto[], count: number): { keys: string[]; count: number; bytes: number } {
  const taken = { keys: [] as string[], count: 0, bytes: 0 };
  for (const g of [...groups].sort((a, b) => b.reclaimableBytes - a.reclaimableBytes)) {
    const copies = g.members.filter(m => !m.photo.kept).length;
    if (copies === 0 || taken.count + copies > count) continue;
    taken.keys.push(g.key);
    taken.count += copies;
    taken.bytes += g.reclaimableBytes;
  }
  return taken;
}

/**
 * What was said once a copy has gone: the file that went and the file that stays. Two of one name are told apart by
 * where each is, as they could be taken for one file.
 */
export function copyGone(copy: PhotoDto, keeper: PhotoDto, companions: number, where: (p: PhotoDto) => string, bin: string): string {
  const waits = `The copy waits in ${bin} for 30 days.`;
  if (copy.fileName.toLowerCase() === keeper.fileName.toLowerCase())
    return `${copy.fileName}${linkedFiles(companions)}, in ${where(copy)}, deleted. The one in ${where(keeper)} stays. ${waits}`;
  return `${copy.fileName}${linkedFiles(companions)} deleted. ${keeper.fileName} stays. ${waits}`;
}

/** The folder a file is in, by its path: what is before its name. */
const folderOf = (path: string) => path.slice(0, Math.max(path.lastIndexOf('/'), path.lastIndexOf('\\')));

/**
 * "The originals stay, such as IMG_4821.HEIC in Photo Library.": an original kept in place of the copies that went, and
 * where it is, so that the copies are not taken for the photos themselves (Jamie, 2026-11-20); empty when none is known.
 * @param stays The originals kept, by path, as the service said.
 * @param where Names a folder as the person knows it.
 */
export function originalsStay(stays: readonly string[], where: (folder: string) => string): string {
  const first = stays[0];
  if (first === undefined) return '';
  return `${stays.length === 1 ? 'The original stays:' : 'The originals stay, such as'} ${leaf(first)} in ${where(folderOf(first))}.`;
}

/**
 * "Deleted 12 copies. The originals stay, such as IMG_4821.HEIC in Photo Library.": what a phone says once the
 * duplicates have gone, naming an original that stays and where it is, then where the copies wait.
 * @param stays The originals kept, by path, as the service said.
 * @param where Names a folder as the person knows it.
 * @param waiting Where the copies wait: "The copies wait in Recently Deleted for 30 days."
 */
export function allGone(removed: number, companions: number, stays: readonly string[], where: (folder: string) => string, waiting: string): string {
  const deleted = `Deleted ${removed.toLocaleString()} ${removed === 1 ? 'copy' : 'copies'}${linkedFiles(companions)}.`;
  return `${deleted} ${originalsStay(stays, where) || 'The best copy of each stays.'} ${waiting}`;
}

/** What was said once a copy is marked keep, or no longer is. */
export function keepSaid(photo: PhotoDto, kept: boolean, mode: GroupMode): string {
  if (!kept) return `${photo.fileName} is no longer marked keep.`;
  return mode === 'duplicates' ? `${photo.fileName} is marked keep. Delete all duplicates skips it.` : `${photo.fileName} is marked keep.`;
}
