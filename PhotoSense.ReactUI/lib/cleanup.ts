import { useCallback, useState } from 'react';
import type { DuplicateGroupDto, GroupMemberDto, GroupMode, GroupsPageDto, PhotoDto } from '../types';
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
  /** The group being reviewed: on a phone the one pushed over the list, if any; on a tablet the one beside it. */
  readonly group?: DuplicateGroupDto;
  /** Which of its copies is shown beside the one that stays. */
  readonly copy: number;
  /** Lists duplicates or similar shots, from the first. */
  turnTo(mode: GroupMode): void;
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
  const group = found ?? (tablet && !chosen ? items[0] : undefined);
  // The same each time, as the list watches for its end by it.
  const showMore = useCallback(() => setPages(p => p + 1), []);
  return {
    mode, data, items, group,
    more: !!data && pages < data.totalPages,
    copy: group ? Math.min(copy, group.members.length - 1) : 0,
    turnTo(next) { setMode(next); setPages(1); setChosen(undefined); setCopy(0); },
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

/** The question before one copy is deleted: the file that stays, then the file that goes, each with where it is. */
export function copyQuestion(copy: PhotoDto, keeper: PhotoDto, where: (p: PhotoDto) => string, words: Words): Question {
  return {
    title: `Delete ${copy.fileName}?`,
    lines: [`${keeper.fileName} stays, in ${where(keeper)}.`, `${copy.fileName}, in ${where(copy)}, goes to ${words.removed}, where Undo can still bring it back.`],
    button: `Delete ${copy.fileName}`,
  };
}

/** The question before every duplicate is deleted. */
export function allQuestion(count: number, bytes: number, words: Words): Question {
  return {
    title: 'Delete all duplicates?',
    lines: [
      `${count.toLocaleString()} ${count === 1 ? 'copy goes' : 'copies go'} to ${words.removed} (${formatBytes(bytes)}).`,
      'The best copy of each picture or video stays. Copies marked keep are skipped.',
      'Similar shots are not included.',
    ],
    button: `Delete ${duplicates(count)}`,
  };
}

/**
 * What was said once a copy has gone: the file that went and the file that stays. Two of one name are told apart by
 * where each is, as they could be taken for one file.
 */
export function copyGone(copy: PhotoDto, keeper: PhotoDto, companions: number, where: (p: PhotoDto) => string): string {
  if (copy.fileName.toLowerCase() === keeper.fileName.toLowerCase())
    return `${copy.fileName}${linkedFiles(companions)}, in ${where(copy)}, deleted. The one in ${where(keeper)} stays.`;
  return `${copy.fileName}${linkedFiles(companions)} deleted. ${keeper.fileName} stays.`;
}

/** What was said once every duplicate has gone. */
export const allGone = (removed: number, companions: number) => `${duplicates(removed)}${linkedFiles(companions)} deleted. The best copy of each stays.`;

/** What was said once a copy is marked keep, or no longer is. */
export function keepSaid(photo: PhotoDto, kept: boolean, mode: GroupMode): string {
  if (!kept) return `${photo.fileName} is no longer marked keep.`;
  return mode === 'duplicates' ? `${photo.fileName} is marked keep. Delete all duplicates skips it.` : `${photo.fileName} is marked keep.`;
}
