import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { OrganizeFileDto, SendStateDto } from '../types';
import { confirmSending, deleteSent, startSending, stopSending, useSendState } from './apiClient';
import { isUnsorted, type Library } from './library';
import type { Platform } from './phone';

/** What is sent: what is in no album or folder yet, what was chosen in Library, or everything. */
export type SendWhat = 'unsorted' | 'chosen' | 'everything';

/** Copy keeps the photos on the phone; Move deletes each from it once the computer has checked every file of it. */
export type SendMode = 'copy' | 'move';

/** Which of Send's screens shows: the choice of what to send, or where an offer made has got to. */
export type SendScreen = 'choose' | 'waiting' | 'comparing' | 'sending' | 'sent' | 'failed';

/** How many files a choice comes to, and how large they are, Live Photo videos with their pictures. */
export interface SendSize {
  readonly count: number;
  readonly bytes: number;
}

/** Send's state on a phone or tablet, kept while other tabs are shown, so that a move finishes wherever the person is. */
export interface PhoneSend {
  readonly what: SendWhat;
  readonly mode: SendMode;
  /** The pictures and videos chosen in Library, by the ids Organize gave them. */
  readonly chosen: readonly string[];
  /** What each choice comes to; for what was chosen, how many were chosen. */
  readonly sizes: Readonly<Record<SendWhat, SendSize>>;
  readonly screen: SendScreen;
  /** Where the offer has got to, once one is made. */
  readonly state?: SendStateDto;
  /** The person said the numbers were different. */
  readonly saidDifferent: boolean;
  /** Why the offer could not be made. */
  readonly refused?: string;
  /** An answer or a deletion is under way. */
  readonly busy: boolean;
  /** A move is deleting from the phone by itself, which the phone asks about first. */
  readonly moving: boolean;
  /** How many pictures and videos were deleted from the phone after sending. */
  readonly deleted?: number;
  setWhat(what: SendWhat): void;
  setMode(mode: SendMode): void;
  /** What was chosen in Library, to be sent. */
  choose(ids: readonly string[]): void;
  offer(): void;
  /** Offers again what was offered last, after it stopped. */
  again(): void;
  answer(same: boolean): void;
  stop(): void;
  /** Deletes from the phone what the computer has checked. */
  remove(): void;
  /** Back to the choice of what to send. */
  done(): void;
}

/** Said after something is deleted from the phone, or when it could not be. */
type Notify = (message: string, kind: 'ok' | 'info' | 'error') => void;

// While the phone waits for the computer, compares numbers with it or sends, the offer stands whatever is shown.
const LIVE = new Set<SendStateDto['stage']>(['waiting', 'comparing', 'sending']);
const VIDEO = /\.(mov|mp4|m4v)$/i;
const NO_IDS: readonly string[] = [];

const messageOf = (e: unknown) => (e instanceof TypeError ? 'Cannot reach the PhotoSense server.' : e instanceof Error ? e.message : String(e));
const stem = (f: OrganizeFileDto) => `${f.folder.toLowerCase()}|${f.name.replace(/\.[^.]*$/, '').toLowerCase()}`;

/** "2 photos and 1 video", by the names of what goes. */
export function photosAndVideos(names: readonly string[]): string {
  const videos = names.filter(n => VIDEO.test(n)).length, photos = names.length - videos;
  const parts = [photos > 0 && `${photos.toLocaleString()} ${photos === 1 ? 'photo' : 'photos'}`, videos > 0 && `${videos.toLocaleString()} ${videos === 1 ? 'video' : 'videos'}`];
  return parts.filter(Boolean).join(' and ');
}

/** Up to three names, then how many more: "IMG_0001.JPG, IMG_0002.JPG, IMG_0003.JPG and 4 more". */
export function someNames(names: readonly string[]): string {
  const shown = names.slice(0, 3), more = names.length - shown.length;
  if (more > 0) return `${shown.join(', ')} and ${more.toLocaleString()} more`;
  return shown.length > 1 ? `${shown.slice(0, -1).join(', ')} and ${shown[shown.length - 1]}` : shown.join('');
}

/** "1 file", "4,212 files". */
export const countFiles = (n: number) => `${n.toLocaleString()} ${n === 1 ? 'file' : 'files'}`;

/**
 * The files a choice comes to, with the Live Photo videos of its pictures, which go with them. `pick` says which of the
 * pictures and videos Library shows are in it.
 */
export function filesFor(all: readonly OrganizeFileDto[], pick: (f: OrganizeFileDto) => boolean): OrganizeFileDto[] {
  const picked = all.filter(f => !f.livePhoto && pick(f));
  const stems = new Set(picked.map(stem));
  return [...picked, ...all.filter(f => f.livePhoto && stems.has(stem(f)))];
}

/** How the person's choice of what to send stands, and the offer made of it. */
export function useSend(library: Library, platform: Platform, watching: boolean, notify: Notify, bin: string): PhoneSend {
  const [what, setWhat] = useState<SendWhat>('unsorted');
  const [mode, setMode] = useState<SendMode>('copy');
  const [chosen, setChosen] = useState<readonly string[]>(NO_IDS);
  // An offer made since Send last showed its choice: what came of it stays shown until Done.
  const [offered, setOffered] = useState<readonly string[]>();
  const [saidDifferent, setSaidDifferent] = useState(false);
  const [refused, setRefused] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [deleted, setDeleted] = useState<number>();
  // A move deletes once, by itself; should the person say no to the phone's question, Delete is offered instead.
  const moveTried = useRef(false);
  const [moving, setMoving] = useState(false);
  const { data } = useSendState(watching || offered !== undefined);

  const all = library.listing?.files;
  const sets = useMemo(() => {
    const files = all ?? [], root = library.root ?? '', picked = new Set(chosen);
    return {
      unsorted: filesFor(files, f => isUnsorted(f, root, platform)),
      chosen: filesFor(files, f => picked.has(f.id)),
      everything: filesFor(files, () => true),
    };
  }, [all, library.root, platform, chosen]);
  const sizes = useMemo(() => {
    const size = (files: readonly OrganizeFileDto[]) => ({ count: files.length, bytes: files.reduce((n, f) => n + f.sizeBytes, 0) });
    return { unsorted: size(sets.unsorted), chosen: { count: chosen.length, bytes: 0 }, everything: size(sets.everything) };
  }, [sets, chosen.length]);

  const start = useCallback((ids: readonly string[]) => {
    setRefused(undefined);
    setSaidDifferent(false);
    setDeleted(undefined);
    moveTried.current = false;
    startSending([...ids]).then(() => setOffered(ids), e => setRefused(messageOf(e)));
  }, []);

  const live = data !== undefined && LIVE.has(data.stage);
  const ended = offered !== undefined && (data?.stage === 'sent' || data?.stage === 'failed');
  const screen: SendScreen = live || ended ? data!.stage as SendScreen : 'choose';

  const said = useCallback((count: number, names: readonly string[], computer: string, moved: boolean) => {
    const they = names.length === 1 ? 'It is' : 'They are';
    setDeleted(count);
    if (count === 0) notify('There was nothing left to delete.', 'info');
    else if (moved) notify(`Moved ${photosAndVideos(names)} to ${computer}. ${they} in ${bin} on this phone for 30 days.`, 'ok');
    else notify(`Deleted ${photosAndVideos(names)} from this phone. ${they} in ${bin} for 30 days, and the copies on ${computer} stay.`, 'ok');
  }, [notify, bin]);

  // With Move, what the computer has checked is deleted as soon as sending is through; the phone asks first.
  const movable = mode === 'move' && ended && data!.stage === 'sent' && data!.deletable.length > 0;
  useEffect(() => {
    if (!movable || moveTried.current) return;
    moveTried.current = true;
    const names = data!.deletable, computer = data!.computer!;
    setMoving(true);
    deleteSent().then(result => said(result.deleted, names, computer, true), e => notify(messageOf(e), 'error')).finally(() => setMoving(false));
  }, [movable, data, said, notify]);

  return {
    what, mode, chosen, sizes, screen, state: screen === 'choose' ? undefined : data, saidDifferent, refused, busy, moving, deleted,
    setWhat, setMode,
    choose(ids) {
      setChosen(ids);
      setWhat('chosen');
    },
    offer() {
      start(what === 'chosen' ? chosen : sets[what].filter(f => !f.livePhoto).map(f => f.id));
    },
    again() {
      start(offered!);
    },
    answer(same) {
      setBusy(true);
      if (!same) setSaidDifferent(true);
      confirmSending(same).catch(e => notify(messageOf(e), 'error')).finally(() => setBusy(false));
    },
    stop() {
      stopSending().catch(e => notify(messageOf(e), 'error'));
      setOffered(undefined);
    },
    remove() {
      const names = data!.deletable, computer = data!.computer!;
      setBusy(true);
      deleteSent().then(result => said(result.deleted, names, computer, false), e => notify(messageOf(e), 'error')).finally(() => setBusy(false));
    },
    done() {
      setOffered(undefined);
      setDeleted(undefined);
      setSaidDifferent(false);
    },
  };
}
