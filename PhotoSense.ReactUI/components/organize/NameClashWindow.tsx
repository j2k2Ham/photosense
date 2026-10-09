import React, { useEffect, useState } from 'react';
import { organizeImageUrl, organizeThumbnailUrl } from '../../lib/apiClient';
import { formatBytes } from '../../lib/format';
import { relativeFolder, shortDate, type Clash, type FileRef } from '../../lib/organize';
import { Segmented } from '../ModeSwitch';
import { Key, WindowShell } from '../WindowShell';
import { BinIcon } from './shared';

type View = 'side' | 'moving' | 'there';

interface Props {
  readonly clashes: readonly Clash[];
  /** Which of them is showing. */
  readonly index: number;
  readonly root: string;
  /** Nothing is to be done for the moment: something is being carried out, or a question is up over the window. */
  readonly frozen: boolean;
  /** Where a file of the folder was taken, in a few words; undefined for a file that is not among the folder's own. */
  placeOf(fileId: string): string | undefined;
  onIndex(index: number): void;
  /** What should happen to the file whose name is taken. */
  onResolve(clash: Clash, how: 'auto' | 'rename' | 'skip'): void;
  /** The name typed for it, without the extension. */
  onWanted(clash: Clash, name: string): void;
  /** Every name after this one gets a number, and the window closes. */
  onNumberTheRest(): void;
  /** Asks to delete one of the files shown, saying which of them stays; nothing goes until that is confirmed. */
  onDelete(file: FileRef, stays: FileRef): void;
  onClose(): void;
}

/** One of the files side by side: as it is listed, and under ownName what it is called where it now is. */
interface Shown extends FileRef { ownName: string; date: string; width: number; height: number; isVideo: boolean; badge: string; moving: boolean; }

const pixels = (f: { width: number; height: number }) => (f.width > 0 ? `${f.width} × ${f.height}` : 'Not known');

/** One of the files at full size, letterboxed, its preview underneath while it loads; a still tile for a video. */
function Picture({ file }: { readonly file: Shown }) {
  if (file.isVideo)
    return (
      <span className="flex h-full w-full items-center justify-center bg-[#1c2124]">
        <span aria-hidden className="flex h-14 w-14 items-center justify-center rounded-full bg-white/20"><span className="ml-1 h-0 w-0 border-y-[10px] border-l-[16px] border-y-transparent border-l-white" /></span>
      </span>
    );
  return (
    <span className="block h-full w-full bg-contain bg-center bg-no-repeat" style={{ backgroundImage: `url("${organizeThumbnailUrl(file.id)}")` }}>
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img key={file.id} src={organizeImageUrl(file.id)} alt={file.name} className="h-full w-full object-contain" />
    </span>
  );
}

/** The small bin under a file: for when the two are the same file, or one of them is not wanted. */
function Bin({ file, disabled, onDelete }: { readonly file: Shown; readonly disabled: boolean; onDelete(): void }) {
  return (
    <button type="button" disabled={disabled} aria-label={`Delete ${file.ownName}, ${file.badge.toLowerCase()}`} title="Delete this file" onClick={onDelete}
      className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full border border-rose-line text-rose-t hover:bg-rose-bg disabled:cursor-not-allowed disabled:opacity-40">
      <BinIcon className="h-4 w-4" />
    </button>
  );
}

function Option({ on, title, onPick, children }: { readonly on: boolean; readonly title: string; onPick(): void; readonly children?: React.ReactNode }) {
  return (
    <div className={`flex items-start gap-3 rounded-xl border px-3.5 py-3 ${on ? 'border-brand bg-sel' : 'border-line bg-bg'}`}>
      <button type="button" role="radio" aria-checked={on} aria-label={title} onClick={onPick} className={`mt-px flex h-[18px] w-[18px] shrink-0 items-center justify-center rounded-full border-[1.5px] ${on ? 'border-brand' : 'border-t3'}`}>
        <span aria-hidden className={`h-2 w-2 rounded-full ${on ? 'bg-brand' : ''}`} />
      </button>
      {/* The whole card picks the option, as well as the round mark that says which is on. */}
      {/* eslint-disable-next-line jsx-a11y/click-events-have-key-events, jsx-a11y/no-static-element-interactions */}
      <div className="flex min-w-0 flex-1 cursor-pointer flex-col gap-0.5" onClick={onPick}>
        <span className="text-[14px] font-semibold">{title}</span>
        {children}
      </div>
    </div>
  );
}

/**
 * A file is going where its name is already taken: the file beside whatever has the name, what differs
 * between them, and the choice of what to do. Nothing is ever replaced.
 */
export function NameClashWindow({ clashes, index, root, frozen, placeOf, onIndex, onResolve, onWanted, onNumberTheRest, onDelete, onClose }: Props) {
  const [view, setView] = useState<View>('side');
  const clash = clashes[index], file = clash.file, last = index === clashes.length - 1;
  const first = clash.takenBy[0];
  const shown: Shown[] = [
    { ...file, ownName: file.name, badge: 'Moving', moving: true },
    ...clash.takenBy.map(t => ({ ...t, badge: t.incoming ? 'Also moving' : 'Already there', moving: false })),
  ];
  const one = view === 'moving' ? shown[0] : shown[1];
  // Whichever goes, the one beside it stays: the file that is moving, or for that one, the file that has its name.
  const known = (s: Shown): FileRef => ({ id: s.id, name: s.ownName, sizeBytes: s.sizeBytes, folder: s.folder });
  const bin = (s: Shown) => <Bin file={s} disabled={frozen} onDelete={() => onDelete(known(s), known(s.moving ? shown[1] : shown[0]))} />;

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      // While a name is being typed, the keys belong to the name; while a question is up, to the question.
      if (frozen || (e.target instanceof Element && e.target.closest('input'))) return;
      if (e.key === 'Escape') onClose();
      if (e.key === 'ArrowLeft' && index > 0) onIndex(index - 1);
      if (e.key === 'ArrowRight' && !last) onIndex(index + 1);
      if (e.key === ' ' && !(e.target instanceof Element && e.target.closest('button'))) {
        e.preventDefault();
        setView(v => (v === 'there' ? 'moving' : 'there'));
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  });

  const goingTo = relativeFolder(root, clash.folder);
  const rows = [
    ['Taken', shortDate(file.date), shortDate(first.date)],
    ['Size', formatBytes(file.sizeBytes), formatBytes(first.sizeBytes)],
    [file.isVideo ? 'Video' : 'Pixels', pixels(file), pixels(first)],
    ['Place', placeOf(file.id) ?? 'No location', placeOf(first.id) ?? first.placeName ?? 'No location'],
    // The one arriving is still in its old folder; the other is, or will be, where it is going.
    ['Folder', relativeFolder(root, file.folder), goingTo],
  ];

  return (
    <WindowShell raised label={`Name already taken: ${file.name}`} columns="grid-cols-[minmax(0,1fr)_420px]" onClose={onClose} header={(
      <>
        <div className="flex min-w-0 flex-1 items-center gap-3">
          <span className="truncate text-[18px] font-semibold">Name already taken</span>
          <span className="truncate rounded-md bg-amber-bg px-2 py-[3px] font-mono text-[12.5px] font-medium text-amber">{file.name}</span>
        </div>
        <Segmented<View> label="View" size="medium" value={view} onChange={setView} options={[{ value: 'side', name: 'Side by side' }, { value: 'moving', name: 'Moving file' }, { value: 'there', name: 'Already there' }]} />
        <div className="flex flex-1 items-center justify-end gap-2.5">
          <button type="button" disabled={index === 0} onClick={() => onIndex(index - 1)} className="pill-outline h-9 px-4 text-[14px] font-medium">Previous</button>
          <span className="whitespace-nowrap font-mono text-[13px] font-medium text-t2">{index + 1} of {clashes.length}</span>
          <button type="button" disabled={last} onClick={() => onIndex(index + 1)} className="pill-outline h-9 px-4 text-[14px] font-medium">Next</button>
        </div>
      </>
    )}>
      <div className="flex min-h-0 min-w-0 flex-col gap-3 bg-stage p-6">
        {view === 'side' ? (
          <div className="grid min-h-0 flex-1 gap-5" style={{ gridTemplateColumns: `repeat(${shown.length}, minmax(0, 1fr))` }}>
            {shown.map(s => (
              <figure key={s.id} className="flex min-h-0 flex-col items-center gap-2">
                <div className={`min-h-0 w-full flex-1 overflow-hidden rounded-[10px] ${s.moving ? 'p-[3px] shadow-[inset_0_0_0_3px_var(--brand)]' : 'p-px shadow-[inset_0_0_0_1px_var(--line)]'}`}><Picture file={s} /></div>
                <figcaption className="flex min-w-0 max-w-full flex-col items-center gap-1.5 text-center">
                  <span className={`badge ${s.moving ? 'bg-ident-bg text-ident' : 'bg-same-bg text-same'}`}>{s.badge}</span>
                  <span className="text-[14px] [overflow-wrap:anywhere]">{s.name}</span>
                  <span className="text-[13px] text-t2">{formatBytes(s.sizeBytes)} · {shortDate(s.date)}</span>
                  {bin(s)}
                </figcaption>
              </figure>
            ))}
          </div>
        ) : (
          <figure className="flex min-h-0 flex-1 flex-col gap-2">
            <div className={`relative min-h-0 flex-1 overflow-hidden rounded-[10px] ${one.moving ? 'p-[3px] shadow-[inset_0_0_0_3px_var(--brand)]' : 'p-px shadow-[inset_0_0_0_1px_var(--line)]'}`}>
              <Picture file={one} />
              <span className={`badge absolute left-4 top-4 ${one.moving ? 'bg-brand text-on-brand' : 'bg-black/60 text-white'}`}>{one.badge}</span>
            </div>
            <figcaption className="flex items-center justify-center gap-3 text-[14px] text-t2"><span className="min-w-0 [overflow-wrap:anywhere]">{one.name} · {formatBytes(one.sizeBytes)}</span>{bin(one)}</figcaption>
          </figure>
        )}
        <p className="text-center text-[13px] text-t3">Press <Key>Space</Key> to flip between the two files in the same spot · <Key>←</Key> <Key>→</Key> for the previous or next name</p>
      </div>

      <aside className="flex min-h-0 flex-col gap-[18px] overflow-y-auto border-l border-line p-6">
        <p className="text-[14.5px] text-t2">
          {first.incoming
            ? `${file.name} is going to ${goingTo}, and so is another file of this move with the same name.`
            : `${file.name} is going to ${goingTo}, which already has a file with this name.`}
        </p>
        <table className="w-full border-separate border-spacing-y-0.5 text-[13.5px]">
          <thead>
            <tr className="text-left text-[11.5px] font-semibold uppercase tracking-[0.07em] text-t3">
              <th className="w-16 px-2.5 py-1.5 font-semibold"><span className="sr-only">What</span></th>
              <th className="py-1.5 font-semibold text-ident">Moving</th>
              <th className="py-1.5 font-semibold">{first.incoming ? 'Also moving' : 'Already there'}</th>
            </tr>
          </thead>
          <tbody>
            {rows.map(([label, a, b]) => (
              <tr key={label} className={a === b ? '' : 'bg-diff'}>
                <th scope="row" className="rounded-l-lg px-2.5 py-1.5 text-left font-normal text-t3">{label}</th>
                <td className="max-w-0 truncate py-1.5 pr-2.5">{a}</td>
                <td className={`max-w-0 truncate rounded-r-lg py-1.5 pr-2.5 ${a === b ? 'text-t3' : ''}`}>{a === b ? 'Same' : b}</td>
              </tr>
            ))}
          </tbody>
        </table>
        {first.identical && <p className="rounded-xl bg-ident-bg px-3.5 py-3 text-[13.5px]">These two files are byte-for-byte the same. Leaving this one where it is avoids keeping a duplicate.</p>}
        {clash.takenBy.length > 1 && <p className="text-[13px] text-t2">{clash.takenBy[1].name} is also there, so the next free number is {clash.auto.slice(clash.base.length + 1, clash.auto.length - clash.ext.length)}.</p>}

        <div className="label-caps tracking-[0.08em]">What should happen</div>
        <div role="radiogroup" aria-label="What should happen" className="flex flex-col gap-2">
          <Option on={clash.mode === 'auto'} title="Add a number" onPick={() => onResolve(clash, 'auto')}>
            <span className="font-mono text-[12.5px] text-t2 [overflow-wrap:anywhere]">{clash.auto}</span>
          </Option>
          <Option on={clash.mode === 'rename'} title="Rename it" onPick={() => { if (clash.mode !== 'rename') onResolve(clash, 'rename'); }}>
            {clash.mode === 'rename' && (
              <>
                {/* eslint-disable-next-line jsx-a11y/no-static-element-interactions, jsx-a11y/click-events-have-key-events */}
                <span onClick={e => e.stopPropagation()} className={`mt-1.5 flex h-10 items-center gap-2 rounded-[10px] border bg-s1 pl-3 pr-1.5 ${clash.bad ? 'border-rose' : 'border-line'}`}>
                  {/* eslint-disable-next-line jsx-a11y/no-autofocus */}
                  <input autoFocus value={clash.wanted} onChange={e => onWanted(clash, e.target.value)} aria-label="New name" aria-invalid={clash.bad} className="min-w-0 flex-1 bg-transparent font-mono text-[13.5px] outline-none" />
                  <span className="rounded-md bg-s2 px-[7px] py-[3px] font-mono text-[12.5px] font-medium text-t3">{clash.ext}</span>
                </span>
                {clash.bad && <span role="alert" className="mt-1.5 text-[12.5px] text-rose-t">That name is taken or has characters file names cannot use. It will get a number instead.</span>}
              </>
            )}
          </Option>
          <Option on={clash.mode === 'skip'} title="Leave it where it is" onPick={() => onResolve(clash, 'skip')}>
            <span className="text-[12.5px] text-t2">It stays in {relativeFolder(root, file.folder)} and is not moved.</span>
          </Option>
        </div>

        <div className="mt-auto flex flex-col gap-2.5">
          <button type="button" onClick={() => (last ? onClose() : onIndex(index + 1))} className="pill-brand h-[46px] text-[15px]">{last ? 'Done' : `Next name · ${index + 2} of ${clashes.length}`}</button>
          <button type="button" onClick={onClose} className="pill-outline h-11 text-[14px] font-medium">Back to preview</button>
          {!last && <button type="button" onClick={onNumberTheRest} className="text-center text-[13px] text-t3 underline underline-offset-[3px]">Add a number to all the remaining names</button>}
        </div>
      </aside>
    </WindowShell>
  );
}
