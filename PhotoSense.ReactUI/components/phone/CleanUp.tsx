import React, { useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { imageUrl, removeDuplicates, removePhoto, setKept, thumbnailUrl, undoChange } from '../../lib/apiClient';
import { useBackLayer } from '../../lib/backStack';
import {
  allGone, allQuestion, copyGone, copyQuestion, detailRows, duplicates, keepSaid, matchNote, scannedLine, whereOf, type CleanUp, type DetailRow, type Question,
} from '../../lib/cleanup';
import { formatBytes, groupSummary, matchLabel } from '../../lib/format';
import { usePhone } from '../../lib/phone';
import type { DuplicateGroupDto, GroupMemberDto, GroupMode, PhotoDto, ScanStatusDto } from '../../types';
import type { ToastAction, ToastKind } from '../Toaster';
import { Chip, PhoneButton, Segmented, StickyFooter } from './Controls';
import { ChevronBackIcon, ChevronIcon, CloseIcon } from './Icons';
import { PushedHeader, PushedScreen, RootHeader, Screen, SplitView } from './Screens';
import { ConfirmSheet } from './Sheet';

type Notify = (message: string, kind: ToastKind, action?: ToastAction) => void;

const said = (e: unknown) => (e instanceof Error ? e.message : String(e));
const KINDS: readonly { readonly id: GroupMode; readonly label: string }[] = [{ id: 'duplicates', label: 'Duplicates' }, { id: 'similar', label: 'Similar' }];

/** What is waiting for a yes or no: one copy, or every duplicate. */
type Asked = { readonly kind: 'copy'; readonly group: DuplicateGroupDto; readonly member: GroupMemberDto } | { readonly kind: 'all'; readonly count: number; readonly bytes: number };

interface Props {
  readonly cleanUp: CleanUp;
  /** The library's own folder, for saying where a file is; absent until the phone has said. */
  readonly root?: string;
  readonly status?: ScanStatusDto;
  notify: Notify;
  onScanAgain(): void;
}

/**
 * Clean up on a phone or tablet: what deleting the duplicates would free, the groups of duplicates or similar shots,
 * and a group's review, pushed over the list on a phone and beside it on a tablet, with the picture full screen to flip
 * between the original and its copy.
 */
export function CleanUpScreen({ cleanUp, root, status, notify, onScanAgain }: Props) {
  const { tablet, words } = usePhone();
  const { mode, data, items, group, copy } = cleanUp;
  const [asked, setAsked] = useState<Asked>();
  const [comparing, setComparing] = useState<'original' | 'copy'>();
  const [busy, setBusy] = useState(false);
  const where = (p: PhotoDto) => whereOf(p, root, words);
  const removable = data?.removableCount ?? 0;

  // More groups are read as the end of those listed comes into view.
  const end = useRef<HTMLDivElement>(null);
  const { more, showMore } = cleanUp;
  useEffect(() => {
    if (!more) return undefined;
    const watch = new IntersectionObserver(seen => { if (seen.some(e => e.isIntersecting)) showMore(); }, { rootMargin: '600px' });
    watch.observe(end.current!);
    return () => watch.disconnect();
  }, [more, showMore, items.length]);

  async function act(run: () => Promise<void>) {
    setBusy(true);
    try { await run(); }
    catch (e) { notify(said(e), 'error'); }
    finally { setBusy(false); }
  }

  const undo = (batchId: string, what: string, one: boolean) => act(async () => {
    const result = await undoChange(batchId);
    if (result.restored > 0) notify(`Put ${what} back where ${one ? 'it was' : 'they were'}.`, 'ok');
    if (result.skipped > 0) notify(`${result.skipped.toLocaleString()} could not be put back. ${result.problems[0] ?? ''}`.trim(), 'error');
  });
  const undoAction = (batchId: string | null | undefined, what: string, one: boolean): ToastAction | undefined =>
    batchId ? { label: 'Undo', run: () => void undo(batchId, what, one) } : undefined;

  const deleteCopy = (from: DuplicateGroupDto, member: GroupMemberDto) => act(async () => {
    const result = await removePhoto(member.photo.id);
    setAsked(undefined);
    setComparing(undefined);
    // The copy after the one deleted takes its place: going through a group's copies does not start again from the first.
    cleanUp.chooseCopy(Math.max(0, Math.min(from.members.indexOf(member), from.members.length - 2)));
    notify(copyGone(member.photo, from.keeper, result.companions, where), 'ok', undoAction(result.batchId, member.photo.fileName, true));
  });

  const deleteAll = () => act(async () => {
    const result = await removeDuplicates(undefined, 'duplicates');
    setAsked(undefined);
    cleanUp.review(undefined);
    if (result.removed > 0) notify(allGone(result.removed, result.companions), 'ok', undoAction(result.batchId, `the ${duplicates(result.removed)}`, false));
    if (result.skipped > 0) notify(`${result.skipped.toLocaleString()} left alone. ${result.problems[0] ?? ''}`.trim(), 'error');
  });

  const toggleKeep = (photo: PhotoDto) => act(async () => {
    await setKept(photo.id, !photo.kept);
    notify(keepSaid(photo, !photo.kept, mode), 'ok');
  });

  const subtitle = mode === 'duplicates' ? 'Copies of the same photo or video' : 'Shots that look alike';
  const list = (
    <>
      <RootHeader title="Clean up" subtitle={subtitle} noGear={tablet} />
      <div className="flex flex-col gap-4 px-4 pb-4">
        {mode === 'duplicates' && removable > 0 && (
          <section aria-label="Duplicates to delete" className="flex flex-col gap-1.5 rounded-cell bg-cell p-[18px]">
            <h2 className="text-[26px] font-bold tabular-nums">{duplicates(removable)}</h2>
            <p className="text-[15px] leading-[1.4] text-t2">{removable === 1 ? 'It takes' : 'They take'} {formatBytes(data!.reclaimableBytes)}. The best copy of each picture or video is kept.</p>
            <PhoneButton kind="danger" disabled={busy} className="mt-3" onClick={() => setAsked({ kind: 'all', count: removable, bytes: data!.reclaimableBytes })}>Delete all duplicates</PhoneButton>
          </section>
        )}
        <Segmented label="Show" options={KINDS} value={mode} onChange={cleanUp.turnTo} />
        {items.length > 0 && <GroupRows groups={items} mode={mode} selected={tablet ? group?.key : undefined} onOpen={cleanUp.review} />}
        {data && items.length === 0 && (
          <p className="px-1 py-6 text-[15px] leading-[1.45] text-t2">
            {mode === 'duplicates' ? `No duplicates left. What you deleted waits in ${words.removed} until you delete it permanently.` : 'No similar shots left to review.'}
          </p>
        )}
        <div ref={end} />
        {status?.completed && (
          <div className="flex flex-col items-start gap-1 px-1">
            <p className="text-[14px] text-t2">{scannedLine(status.totalPhotos, status.completed, new Date())}</p>
            <button type="button" onClick={onScanAgain} className="py-1 text-[15px] font-semibold text-acc">Scan again</button>
          </div>
        )}
      </div>
    </>
  );

  const member = group?.members[copy];
  const review = group && member && (
    <Review group={group} member={member} index={copy} mode={mode} root={root} busy={busy} onBack={tablet ? undefined : () => cleanUp.review(undefined)}
      onCopy={cleanUp.chooseCopy} onCompare={setComparing} onDelete={() => setAsked({ kind: 'copy', group, member })} onKeep={() => void toggleKeep(member.photo)} />
  );
  const question: Question | undefined = asked?.kind === 'copy' ? copyQuestion(asked.member.photo, asked.group.keeper, where, words)
    : asked && allQuestion(asked.count, asked.bytes, words);

  return (
    <>
      {tablet ? (
        <Screen label="Clean up" fixed>
          <SplitView list={list} detail={review ?? (
            <>
              <PushedHeader title="Clean up" gear />
              {data && <p className="px-6 py-16 text-center text-[15px] leading-[1.45] text-t2">{mode === 'duplicates' ? `No duplicates left. What you deleted waits in ${words.removed} until you delete it permanently.` : 'No similar shots left to review.'}</p>}
            </>
          )} />
        </Screen>
      ) : review ? <PushedScreen label="Review">{review}</PushedScreen> : <Screen label="Clean up">{list}</Screen>}
      {/* Drawn over the whole page, tabs and all: inside the tab they would be under the shell's bars. */}
      {createPortal(<>
        {comparing && group && member && (
          <Compare group={group} member={member} index={copy} mode={mode} side={comparing} root={root} busy={busy} onSide={setComparing} onClose={() => setComparing(undefined)}
            onStep={by => { cleanUp.chooseCopy((copy + by + group.members.length) % group.members.length); setComparing('copy'); }}
            onDelete={() => setAsked({ kind: 'copy', group, member })} />
        )}
        {question && (
          <ConfirmSheet title={question.title} lines={question.lines} confirmLabel={question.button} busy={busy} onCancel={() => setAsked(undefined)}
            onConfirm={() => void (asked!.kind === 'copy' ? deleteCopy(asked!.group, asked!.member) : deleteAll())} />
        )}
      </>, document.body)}
    </>
  );
}

/** The groups as rows: two of their pictures, one over the other, the name of the one that stays, and what there is to delete. */
function GroupRows({ groups, mode, selected, onOpen }: { readonly groups: readonly DuplicateGroupDto[]; readonly mode: GroupMode; readonly selected?: string; onOpen(key: string): void }) {
  const { ios, tablet } = usePhone();
  return (
    <ul aria-label="Groups" className={ios
      ? 'overflow-hidden rounded-cell bg-cell [&>li+li]:border-t-[0.5px] [&>li+li]:border-sep'
      : 'flex flex-col gap-[2px] overflow-hidden rounded-[20px] [&>li]:rounded-[4px] [&>li]:bg-cell'}>
      {groups.map(g => {
        const summary = groupSummary(g, mode);
        const on = g.key === selected;
        return (
          <li key={g.key} className={on ? '!bg-sel' : ''}>
            <button type="button" aria-current={on ? 'true' : undefined} onClick={() => onOpen(g.key)} className="flex w-full items-center gap-3.5 px-3 py-3 text-left">
              <Stacked front={g.keeper} back={g.members[0].photo} />
              <span className="min-w-0 flex-1">
                <span className="block truncate font-code text-[14px] font-medium">{g.keeper.fileName}</span>
                <span className={`block truncate text-[13.5px] ${summary.allKept ? 'text-keep' : 'text-t2'}`}>{summary.text}</span>
              </span>
              <span aria-hidden className="grid h-6 min-w-6 place-items-center rounded-full bg-fill px-2 text-[12.5px] font-semibold tabular-nums">+{g.members.length}</span>
              {!tablet && <span aria-hidden className="text-t3"><ChevronIcon /></span>}
            </button>
          </li>
        );
      })}
    </ul>
  );
}

/** A small preview, or a dark tile with a play mark for a video, which has none. */
function Small({ photo, className }: { readonly photo: PhotoDto; readonly className: string }) {
  if (photo.isVideo) return <span className={`grid place-items-center bg-[#1c2124] ${className}`}><Play size={7} /></span>;
  return <img src={thumbnailUrl(photo.id)} alt="" loading="lazy" className={`bg-fill object-cover ${className}`} />;
}

/** Two pictures of a group, the one that stays in front. */
function Stacked({ front, back }: { readonly front: PhotoDto; readonly back: PhotoDto }) {
  return (
    <span aria-hidden className="relative h-[56px] w-[56px] shrink-0">
      <Small photo={back} className="absolute right-0 top-0 h-[46px] w-[46px] rounded-[9px] opacity-60" />
      <Small photo={front} className="absolute bottom-0 left-0 h-[46px] w-[46px] rounded-[9px] shadow-[0_0_0_2px_var(--cell)]" />
    </span>
  );
}

/** A white play mark, its size by half its height. */
const Play = ({ size }: { readonly size: number }) => (
  <span aria-hidden className="ml-[2px] h-0 w-0 border-l-white border-y-transparent" style={{ borderTopWidth: size, borderBottomWidth: size, borderLeftWidth: size * 1.6 }} />
);

interface ReviewProps {
  readonly group: DuplicateGroupDto;
  readonly member: GroupMemberDto;
  readonly index: number;
  readonly mode: GroupMode;
  readonly root?: string;
  readonly busy: boolean;
  /** Goes back to the list, on a phone; a tablet shows both. */
  onBack?(): void;
  onCopy(index: number): void;
  onCompare(side: 'original' | 'copy'): void;
  onDelete(): void;
  onKeep(): void;
}

/** One group's review: the one that stays beside a copy, what differs and what is the same, and what can be done. */
function Review({ group, member, index, mode, root, busy, onBack, onCopy, onCompare, onDelete, onKeep }: ReviewProps) {
  const { ios, tablet, words } = usePhone();
  const keeper = group.keeper, copy = member.photo;
  const similar = mode === 'similar';
  const summary = groupSummary(group, mode);
  const rows = detailRows(keeper, copy, root, words);
  const differ = rows.filter(r => r.differs), same = rows.filter(r => !r.differs);
  const n = group.members.length;
  const keepLabel = copy.kept ? 'Marked keep. Tap to unmark' : similar ? 'Keep this shot too' : 'Keep this copy too';
  return (
    <>
      <PushedHeader title={keeper.fileName} subtitle={summary.allKept || similar ? summary.text : `${summary.text} to free`} onBack={onBack} gear={tablet} />
      <div className={`flex flex-col gap-4 ${tablet ? 'px-4 pr-5' : 'px-4'}`}>
        <div className="flex flex-wrap items-center gap-x-2.5 gap-y-1">
          <span className="flex h-6 items-center rounded-full bg-ident-bg px-2.5 text-[11.5px] font-bold uppercase tracking-[.05em] text-ident">{matchLabel[member.match]}</span>
          <span className="text-[14px] text-t2">{matchNote(group, member)}</span>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Picture photo={keeper} label={similar ? 'Best shot' : 'Original'} keeper onOpen={() => onCompare('original')}
            where={`Stays in ${whereOf(keeper, root, words)}`} />
          <Picture photo={copy} label={`${similar ? 'Shot' : 'Copy'} ${index + 1} of ${n}`} onOpen={() => onCompare('copy')} where={`In ${whereOf(copy, root, words)}`} />
        </div>
        {n > 1 && (
          <div role="group" aria-label={similar ? 'Shots' : 'Copies'} className="flex gap-2 overflow-x-auto">
            {group.members.map((m, i) => <Chip key={m.photo.id} label={`${similar ? 'Shot' : 'Copy'} ${i + 1}`} on={i === index} onClick={() => onCopy(i)} />)}
          </div>
        )}
        {differ.length > 0 && <Details title="What differs" rows={differ} columns={['Original', similar ? 'This shot' : 'This copy']} ios={ios} />}
        {same.length > 0 && <Details title="The same in both" rows={same} ios={ios} />}
        <p className="px-1 text-[13.5px] text-t2">Tap either picture to see it full screen and flip between them.</p>
      </div>
      <StickyFooter>
        <PhoneButton kind="danger" disabled={busy || copy.kept} onClick={onDelete}>Delete {copy.fileName}</PhoneButton>
        <PhoneButton kind="text" disabled={busy} className="!h-10" onClick={onKeep}>{keepLabel}</PhoneButton>
      </StickyFooter>
    </>
  );
}

interface PictureProps {
  readonly photo: PhotoDto;
  readonly label: string;
  readonly keeper?: boolean;
  /** Where it is: "Stays in Photo Library", "In Butte › 2022". */
  readonly where: string;
  onOpen(): void;
}

/** One of the two pictures side by side: which it is, its format and size, its name and where it is. */
function Picture({ photo, label, keeper, where, onOpen }: PictureProps) {
  return (
    <div className="flex min-w-0 flex-col gap-1">
      <button type="button" aria-label={`See ${photo.fileName} full screen`} onClick={onOpen}
        className={`relative w-full overflow-hidden rounded-2xl bg-fill ${photo.isVideo ? 'aspect-[16/11]' : 'aspect-[3/4]'}`}>
        <Full photo={photo} fit="cover" />
        {keeper && <span aria-hidden className="absolute inset-0 rounded-2xl shadow-[inset_0_0_0_2.5px_var(--keep)]" />}
        <Badge label={label} className={`left-2 top-2 ${keeper ? 'bg-keep text-on-keep' : 'bg-black/55 text-white'}`} />
        <Badge label={`${photo.format ?? '?'} · ${formatBytes(photo.fileSizeBytes)}`} className="bottom-2 right-2 bg-black/55 text-white" />
      </button>
      <p className="truncate pt-1 font-code text-[12.5px]" title={photo.fileName}>{photo.fileName}</p>
      <p className={`truncate text-[12.5px] ${keeper ? 'text-keep' : 'text-t2'}`}>{where}</p>
    </div>
  );
}

const Badge = ({ label, className }: { readonly label: string; readonly className: string }) => (
  <span aria-hidden className={`absolute flex h-6 max-w-[calc(100%-16px)] items-center truncate rounded-full px-2.5 text-[11.5px] font-semibold ${className}`}>{label}</span>
);

/** The whole picture over its small preview, which shows at once while the whole one arrives; a play mark for a video. */
function Full({ photo, fit }: { readonly photo: PhotoDto; readonly fit: 'cover' | 'contain' }) {
  if (photo.isVideo) return <span className="absolute inset-0 grid place-items-center bg-[#1c2124]"><span className="grid h-12 w-12 place-items-center rounded-full bg-black/40"><Play size={8} /></span></span>;
  const f = fit === 'cover' ? 'object-cover' : 'object-contain';
  return (
    <>
      <img src={thumbnailUrl(photo.id)} alt="" className={`absolute inset-0 h-full w-full ${f}`} />
      <img key={photo.id} src={imageUrl(photo.id)} alt="" className={`absolute inset-0 h-full w-full ${f}`} />
    </>
  );
}

interface DetailsProps {
  readonly title: string;
  readonly rows: readonly DetailRow[];
  /** The two files' column headings, for what differs; what is the same has one value. */
  readonly columns?: readonly [string, string];
  readonly ios: boolean;
}

/** The details of the two files as a table: what differs, the copy's value marked, or what is the same in both. */
function Details({ title, rows, columns, ios }: DetailsProps) {
  const grid = columns ? 'grid-cols-[78px_minmax(0,1fr)_minmax(0,1fr)]' : 'grid-cols-[78px_minmax(0,1fr)]';
  const line = `grid ${grid} items-center gap-x-3 px-3.5 py-3 min-h-[44px]`;
  const value = (r: DetailRow, text: string, extra = '') => <span role="cell" className={`[overflow-wrap:anywhere] ${r.code ? 'font-code text-[12.5px]' : 'text-[14px]'} ${extra}`}>{text}</span>;
  return (
    <section aria-label={title} className="flex flex-col gap-2">
      <h3 className={ios ? 'px-1 text-[13px] font-semibold uppercase tracking-[.02em] text-t2' : 'px-1 text-[14px] font-medium text-acc'}>{title}</h3>
      <div role="table" aria-label={title} className={ios
        ? 'overflow-hidden rounded-cell bg-cell [&>*+*]:border-t-[0.5px] [&>*+*]:border-sep'
        : 'flex flex-col gap-[2px] overflow-hidden rounded-[20px] [&>*]:rounded-[4px] [&>*]:bg-cell'}>
        {columns && (
          <div role="row" className={`${line} !min-h-0 !py-2 text-[12.5px] font-semibold text-t2`}>
            <span role="columnheader" />
            <span role="columnheader">{columns[0]}</span>
            <span role="columnheader">{columns[1]}</span>
          </div>
        )}
        {rows.map(r => (
          <div role="row" key={r.label} className={line}>
            <span role="rowheader" className="flex items-center gap-2 text-[13.5px] text-t2">{columns && <span aria-hidden className="h-1.5 w-1.5 shrink-0 rounded-full bg-amber" />}{r.label}</span>
            {value(r, r.original)}
            {columns && value(r, r.copy, 'text-amber')}
          </div>
        ))}
      </div>
    </section>
  );
}

interface CompareProps {
  readonly group: DuplicateGroupDto;
  readonly member: GroupMemberDto;
  readonly index: number;
  readonly mode: GroupMode;
  readonly side: 'original' | 'copy';
  readonly root?: string;
  readonly busy: boolean;
  onSide(side: 'original' | 'copy'): void;
  /** Shows the copy before or after this one. */
  onStep(by: number): void;
  onDelete(): void;
  onClose(): void;
}

/** The original or a copy full screen, on black: tapping the picture flips between the two, in the same spot. */
function Compare({ group, member, index, mode, side, root, busy, onSide, onStep, onDelete, onClose }: CompareProps) {
  const { words } = usePhone();
  useBackLayer(true, onClose);
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);
  const similar = mode === 'similar';
  const original = side === 'original';
  const shown = original ? group.keeper : member.photo;
  const n = group.members.length;
  const label = original ? (similar ? 'Best shot' : 'Original') : `${similar ? 'Shot' : 'Copy'} ${index + 1} of ${n}`;
  const flip = () => onSide(original ? 'copy' : 'original');
  const step = (by: number, name: string, Icon: typeof ChevronIcon, at: string) => (
    <button type="button" aria-label={name} onClick={() => onStep(by)} className={`absolute top-1/2 z-10 grid h-11 w-11 -translate-y-1/2 place-items-center rounded-full bg-black/45 ${at}`}><Icon /></button>
  );
  return (
    <div role="dialog" aria-modal aria-label={`${shown.fileName}, full screen`} className="fixed inset-0 z-40 flex animate-fade-in flex-col bg-stage font-ui text-white">
      <div className="flex items-center gap-3 px-3 pb-2 pt-[calc(var(--safe-t)+8px)]">
        <button type="button" aria-label="Close" onClick={onClose} className="grid h-11 w-11 shrink-0 place-items-center rounded-full bg-white/[.14]"><CloseIcon size={20} /></button>
        <div className="min-w-0 flex-1 text-center">
          <p className="truncate font-code text-[14px] font-medium">{shown.fileName}</p>
          <p className="truncate text-[12.5px] text-white/[.72]">{original ? 'Stays in' : 'In'} {whereOf(shown, root, words)}</p>
        </div>
        <span className="w-11 shrink-0" />
      </div>
      <div className="relative mx-auto flex min-h-0 w-full max-w-[680px] flex-1 px-4 py-4">
        <button type="button" aria-label={original ? 'Show the copy' : 'Show the original'} onClick={flip} className="relative h-full w-full overflow-hidden rounded-[14px] bg-[#111]">
          <Full photo={shown} fit="contain" />
          <Badge label={label} className={`left-2.5 top-2.5 ${original ? 'bg-keep text-on-keep' : 'bg-white/[.22] text-white'}`} />
          <Badge label={`${shown.format ?? '?'} · ${formatBytes(shown.fileSizeBytes)}`} className="bottom-2.5 right-2.5 bg-black/55 text-white" />
        </button>
        {n > 1 && step(-1, `Previous ${similar ? 'shot' : 'copy'}`, ChevronBackIcon, 'left-6')}
        {n > 1 && step(1, `Next ${similar ? 'shot' : 'copy'}`, ChevronIcon, 'right-6')}
      </div>
      <div className="mx-auto flex w-full max-w-[680px] flex-col gap-3 px-4 pb-[calc(var(--safe-b)+12px)]">
        <div role="radiogroup" aria-label="Show" className="flex h-[38px] rounded-full bg-white/[.12] p-[2px]">
          {(['original', 'copy'] as const).map(s => (
            <button key={s} type="button" role="radio" aria-checked={side === s} onClick={() => onSide(s)}
              className={`flex-1 rounded-full text-[14px] font-semibold ${side === s ? 'bg-white/[.28]' : ''}`}>
              {s === 'original' ? (similar ? 'Best shot' : 'Original') : similar ? 'This shot' : 'This copy'}
            </button>
          ))}
        </div>
        <p className="text-center text-[13px] text-white/[.72]">Tap the picture to flip between them.</p>
        <PhoneButton kind="danger" disabled={busy || member.photo.kept} onClick={onDelete}>Delete {member.photo.fileName}</PhoneButton>
      </div>
    </div>
  );
}
