import React, { useEffect, useId, useState } from 'react';
import { browseFolders } from '../../lib/apiClient';
import { leaf, sameFolder } from '../../lib/format';
import { checkFolderName, joinPath, relativeFolder } from '../../lib/organize';
import type { FolderListingDto } from '../../types';
import { FolderPicker } from '../FolderPicker';
import { Segmented } from '../ModeSwitch';
import { Scrim } from '../Scrim';
import { countFiles } from './shared';

/** Where a folder of the person's own is: a new one made in `base`, or `base` itself, already there, which the files go straight into. */
export interface FolderPlace { name: string; base: string; direct: boolean; }

interface Props {
  /** How many selected files go in the folder; none when it is made empty, to be filled afterwards. */
  readonly count: number;
  /** The folder being organized: a new folder is made in it unless another place is chosen. */
  readonly root: string;
  /** Where the person's folders are already: none is set up twice. */
  readonly taken: readonly string[];
  onDone(folder: FolderPlace): void;
  onCancel(): void;
}

const messageOf = (e: unknown) => (e instanceof TypeError ? 'Cannot reach the PhotoSense server.' : e instanceof Error ? e.message : String(e));
/** A path, cut short at its start when it is too long for its box: the folder's own name is at its end. */
function Path({ path }: { readonly path: string }) {
  return (
    <span dir="rtl" title={path} className="block h-10 min-w-0 flex-1 truncate rounded-xl border border-line bg-bg px-3.5 text-left font-mono text-[13px] leading-[38px]">
      <bdi dir="ltr">{path}</bdi>
    </span>
  );
}

/** Sets up a folder of the person's own for the selected files: a new one, wherever it is to be made, or one already there. */
export function FolderDialog({ count, root, taken, onDone, onCancel }: Props) {
  const [kind, setKind] = useState<'new' | 'existing'>('new');
  const [name, setName] = useState('');
  const [inside, setInside] = useState(root);
  // A name is found wanting only once it has been tried, not while it is being typed.
  const [tried, setTried] = useState(false);
  const [choosing, setChoosing] = useState(false);
  // The folders looked through for one already there, and the one picked among them.
  const [listing, setListing] = useState<FolderListingDto>();
  const [picked, setPicked] = useState<string>();
  const [looking, setLooking] = useState(false);
  const [problem, setProblem] = useState<string>();
  const titleId = useId(), nameId = useId(), hintId = useId();

  useEffect(() => {
    // The folder picker over this takes Esc while it is open.
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape' && !choosing) onCancel(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [choosing, onCancel]);

  async function look(path?: string) {
    setLooking(true);
    setProblem(undefined);
    setPicked(undefined);
    try { setListing(await browseFolders(path)); } catch (e) { setProblem(messageOf(e)); } finally { setLooking(false); }
  }
  function turnTo(next: 'new' | 'existing') {
    setKind(next);
    // The folders are looked through only once they are asked for, starting where the files are.
    if (next === 'existing' && !listing && !looking) void look(root);
  }

  // Typed as the check's answer, so that every kind of answer is known to say whether there is an error.
  const checked: ReturnType<typeof checkFolderName> = checkFolderName(name);
  const typed = checked.error === undefined ? checked.name : undefined;
  const nameProblem = checked.error ?? (taken.some(t => sameFolder(t, joinPath(inside, typed))) ? `You already have a folder called ${typed} there.` : undefined);
  const here = listing?.path ?? undefined;
  // What the files would go into: the folder picked in the list, or one opened to list what is inside it. The list starts
  // in the folder being organized, which is not taken for a choice until it is picked from the folder above it.
  const target = picked ?? (here !== undefined && !sameFolder(here, root) ? here : undefined);
  const twice = target !== undefined && taken.some(t => sameFolder(t, target));

  function done() {
    // For a folder already there, the button that gets here is offered only once one is picked that is not set up already.
    if (kind === 'existing') return onDone({ name: sameFolder(target!, root) ? leaf(root) : relativeFolder(root, target!), base: target!, direct: true });
    if (nameProblem !== undefined) return setTried(true);
    onDone({ name: typed!, base: inside, direct: false });
  }

  return (
    <Scrim className="fixed inset-0 z-[45] flex items-center justify-center bg-scrim p-3 sm:p-6" onDismiss={onCancel}>
      <div role="dialog" aria-modal="true" aria-labelledby={titleId} className="flex max-h-full w-[560px] max-w-full flex-col gap-5 overflow-y-auto rounded-[20px] border border-line bg-pop p-5 shadow-dialog sm:p-7">
        <div className="flex flex-col gap-1.5">
          <h2 id={titleId} className="text-[21px] font-semibold">{count > 0 ? `Put ${countFiles(count)} in a folder` : 'New folder'}</h2>
          <p className="text-[14px] text-t2">A new folder, or one that is already there. Nothing moves until you preview it and confirm.</p>
        </div>
        <Segmented<'new' | 'existing'> label="Which folder" size="wide" value={kind} onChange={turnTo} options={[{ value: 'new', name: 'New folder' }, { value: 'existing', name: 'Folder already there' }]} />

        {kind === 'new' ? (
          <div className="flex flex-col gap-4">
            <div className="flex flex-col gap-1.5">
              <label htmlFor={nameId} className="text-[13px] text-t2">Name</label>
              {/* eslint-disable-next-line jsx-a11y/no-autofocus -- the window is opened to name a folder */}
              <input id={nameId} autoFocus value={name} onChange={e => setName(e.target.value)} onKeyDown={e => { if (e.key === 'Enter') done(); }}
                aria-invalid={tried && nameProblem !== undefined} aria-describedby={hintId} placeholder="For example Trips\Glacier 2022"
                className={`h-11 rounded-xl border bg-bg px-3.5 text-[15px] placeholder:text-t3 ${tried && nameProblem ? 'border-rose' : 'border-line'}`} />
              {tried && nameProblem
                ? <span id={hintId} role="alert" className="text-[12.5px] text-rose-t">{nameProblem}</span>
                : <span id={hintId} className="text-[12.5px] text-t3">Use \ to make a folder inside a folder.</span>}
            </div>
            <div className="flex flex-col gap-1.5">
              <span className="text-[13px] text-t2">Made inside</span>
              <div className="flex items-center gap-2">
                <Path path={inside} />
                <button type="button" onClick={() => setChoosing(true)} className="pill-quiet h-10 shrink-0 border border-line px-4 text-[13.5px]">Change</button>
              </div>
              {!sameFolder(inside, root) && (
                <button type="button" onClick={() => setInside(root)} className="self-start text-[12.5px] text-t2 underline underline-offset-[3px]">Make it in {leaf(root)} instead</button>
              )}
            </div>
          </div>
        ) : (
          <div className="flex flex-col gap-2">
            <div className="flex items-center gap-2">
              {/* From the top of a disk, Up goes to the list of disks and places to start from. */}
              <button type="button" disabled={looking || here === undefined} onClick={() => void look(listing?.parent ?? undefined)} className="pill-quiet h-10 shrink-0 px-4 text-[13.5px]">Up</button>
              <Path path={here ?? 'This computer'} />
            </div>
            <ul aria-label="Folders" aria-busy={looking} className="flex h-[232px] flex-col gap-0.5 overflow-y-auto rounded-xl border border-line bg-bg p-1.5">
              {problem && <li role="alert" className="px-3 py-2 text-[13.5px] text-rose-t [overflow-wrap:anywhere]">{problem}</li>}
              {!listing && looking && <li className="px-3 py-2 text-[13.5px] text-t3">Loading…</li>}
              {listing?.folders.length === 0 && <li className="px-3 py-2 text-[13.5px] text-t3">No folders inside this one.</li>}
              {listing?.folders.map(f => (
                <li key={f.path} className="flex items-center gap-1">
                  <button type="button" aria-pressed={picked === f.path} disabled={looking} title={f.path} onClick={() => setPicked(p => (p === f.path ? undefined : f.path))} onDoubleClick={() => void look(f.path)}
                    className={`flex min-w-0 flex-1 items-center gap-3 rounded-[10px] px-3 py-2 text-left text-[14px] ${picked === f.path ? 'bg-sel font-semibold' : 'hover:bg-s2'}`}>
                    <span aria-hidden className="h-3.5 w-[18px] shrink-0 rounded-[3px] bg-brand" />
                    <span className="truncate">{f.name}</span>
                  </button>
                  <button type="button" aria-label={`Open ${f.name}`} title="See the folders inside it" disabled={looking} onClick={() => void look(f.path)}
                    className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full text-[18px] text-t2 hover:bg-s2">›</button>
                </li>
              ))}
            </ul>
            {twice
              ? <p role="alert" className="text-[12.5px] text-rose-t">{leaf(target!)} is already one of your folders. Use Add here on its card.</p>
              : <p className="text-[12.5px] text-t3">{target === undefined ? 'Pick a folder, or open one to see the folders inside it.' : 'Click a folder to pick it; › shows the folders inside it.'} Nothing in it is replaced: a name that is taken gets a number.</p>}
          </div>
        )}

        <div className="flex flex-wrap justify-end gap-2.5 pt-1">
          <button type="button" onClick={onCancel} className="pill-outline h-11 px-5 text-[15px]">Cancel</button>
          {kind === 'new'
            ? <button type="button" onClick={done} className="pill-brand h-11 px-5 text-[15px]">Create folder</button>
            : <button type="button" disabled={target === undefined || twice || looking} onClick={done} className="pill-brand h-11 max-w-full truncate px-5 text-[15px]">{target === undefined ? 'Use this folder' : `Use ${leaf(target)}`}</button>}
        </div>
      </div>
      {choosing && (
        <FolderPicker title="Choose where the new folder is made" startAt={inside} pickLabel="Make it here" onCancel={() => setChoosing(false)}
          onPick={path => { setInside(path); setChoosing(false); }} />
      )}
    </Scrim>
  );
}
