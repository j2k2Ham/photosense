import React, { useEffect, useMemo, useRef, useState } from 'react';
import { planOrganize } from '../../lib/apiClient';
import { formatBytes, leaf } from '../../lib/format';
import {
  PREVIEW_PAGE, checkFolderName, clashSummary, dateRange, joinPath, monthOf, relativeFolder, resolveNames, separatorOf, totalBytes, type Clash, type ClashChoice, type FileRef,
} from '../../lib/organize';
import type { OrganizeApplyRequest, OrganizeFileDto, OrganizePlanDto } from '../../types';
import { FolderPicker } from '../FolderPicker';
import { Segmented } from '../ModeSwitch';
import { WindowShell } from '../WindowShell';
import { NameClashWindow } from './NameClashWindow';
import { Check, Pager, Thumb, countFiles } from './shared';

interface Props {
  /** Whose folder this is: one that was suggested, or one of the person's own. */
  readonly kind: 'Suggested folder' | 'Your folder';
  readonly root: string;
  /** Every file set to go to the folder. */
  readonly files: readonly OrganizeFileDto[];
  readonly name: string;
  /** What the folder is split by inside, when it is: a subfolder for each year, month or place of its files. */
  readonly split?: 'year' | 'month' | 'place';
  /** The subfolder a file goes to in a folder that is split; none for a file with nothing to split it by. */
  subfolderOf?(file: OrganizeFileDto): string | undefined;
  /** "Includes Apgar (2 mi away)", for a folder that takes in nearby places. */
  readonly merge: string;
  readonly mode: 'move' | 'copy';
  /** Bring Live Photo videos and edit files along with their photos. */
  readonly companions: boolean;
  readonly busy: boolean;
  /** A question is up over this window, such as whether to delete a file: the keys are the question's. */
  readonly asking: boolean;
  placeOf(fileId: string): string | undefined;
  onName(name: string): void;
  onMode(mode: 'move' | 'copy'): void;
  onCompanions(bring: boolean): void;
  /** Carry it out: the request, and the files that go. */
  onMove(request: OrganizeApplyRequest, files: OrganizeFileDto[]): void;
  /** Asks to delete one of two files that share a name, saying which stays; nothing goes until that is confirmed. */
  onDelete(file: FileRef, stays: FileRef): void;
  onClose(): void;
}

const messageOf = (e: unknown) => (e instanceof TypeError ? 'Cannot reach the PhotoSense server.' : e instanceof Error ? e.message : String(e));

/** At most this many of a folder's subfolders are named in the preview. */
const NAMED_SUBFOLDERS = 6;

/** Everything a folder would take, where it would go and under what names, before anything moves. */
export function MovePreview({ kind, root, files, name, split, subfolderOf, merge, mode, companions, busy, asking, placeOf, onName, onMode, onCompanions, onMove, onDelete, onClose }: Props) {
  const [excluded, setExcluded] = useState<Record<string, boolean>>({});
  const [choices, setChoices] = useState<Record<string, ClashChoice>>({});
  // A folder chosen for the files in place of the root, and whether they go straight into it.
  const [base, setBase] = useState<string>();
  const [direct, setDirect] = useState(false);
  const [page, setPage] = useState(1);
  const [choosing, setChoosing] = useState(false);
  const [reviewing, setReviewing] = useState<number>();
  const [plan, setPlan] = useState<OrganizePlanDto>();
  const [problem, setProblem] = useState<string>();

  const named = direct ? { name: '' } : checkFolderName(name);
  const folderName = named.error === undefined ? named.name : undefined;
  const basePath = base ?? root, copy = mode === 'copy';
  const asked = useMemo(() => files.map(f => ({ id: f.id, subfolder: subfolderOf?.(f) })), [files, subfolderOf]);

  // Asked afresh whenever where the files go changes; an answer to an earlier question is dropped.
  useEffect(() => {
    // What was worked out for the folder as it was says nothing about the folder as it is now.
    setPlan(undefined);
    setProblem(undefined);
    if (folderName === undefined) return;
    let latest = true;
    planOrganize({ basePath, folderName, direct }, asked).then(
      answer => { if (latest) setPlan(answer); },
      e => { if (latest) setProblem(messageOf(e)); });
    return () => { latest = false; };
  }, [basePath, folderName, direct, asked]);

  // A file already in the folder it would go to has nowhere to be moved, and is not part of the plan.
  const planned = useMemo(() => (plan ? new Set(plan.items.map(i => i.id)) : undefined), [plan]);
  const movable = files.filter(f => planned?.has(f.id) ?? true);
  const going = movable.filter(f => !excluded[f.id]);
  const resolved = useMemo(() => (plan ? resolveNames(files, plan, choices, excluded) : { clashes: [] as Clash[], names: {} }), [files, plan, choices, excluded]);
  const clashes = resolved.clashes, clashing = new Set(clashes.map(c => c.file.id));
  // What is already there is being asked about: at first, and again whenever the files or where they go change.
  const waiting = folderName !== undefined && !plan && !problem;
  // While it is asked about again, as after one of two files with a name was deleted, the names being gone through stay as they were.
  const settled = useRef<Clash[]>([]);
  if (!waiting) settled.current = clashes;
  const reviewed = waiting ? settled.current : clashes;
  const all = going.length === movable.length, verb = copy ? 'be copied' : 'move';
  const pages = Math.max(1, Math.ceil(files.length / PREVIEW_PAGE)), from = (Math.min(page, pages) - 1) * PREVIEW_PAGE;

  const sources = new Map<string, number>();
  for (const f of going) sources.set(relativeFolder(root, f.folder), (sources.get(relativeFolder(root, f.folder)) ?? 0) + 1);
  const from4 = [...sources.entries()].sort((a, b) => b[1] - a[1]);
  const inside = [...new Set(going.map(f => subfolderOf?.(f)).filter((n): n is string => n !== undefined))].sort();
  // In a folder split by place, a file with no location has no place to go to but the folder itself.
  const loose = inside.length > 0 ? going.filter(f => subfolderOf!(f) === undefined).length : 0;
  const more = inside.length - NAMED_SUBFOLDERS;
  const destination = plan?.destination ?? joinPath(basePath, direct ? undefined : folderName);
  const label = direct ? leaf(basePath) : folderName ?? '';
  const ready = going.length > 0 && plan !== undefined && !busy;

  useEffect(() => {
    // The windows opened from this one take Esc while they are open.
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape' && !busy && !asking && !choosing && reviewing === undefined) onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  });
  // With no name left to go through, the going through is over: it does not start again by itself should one turn up later.
  const none = !waiting && clashes.length === 0;
  useEffect(() => { if (none) setReviewing(undefined); }, [none]);

  const toggle = (id: string) => setExcluded(x => ({ ...x, [id]: !x[id] }));
  const resolve = (clash: Clash, how: 'auto' | 'rename' | 'skip') => {
    const id = clash.file.id;
    setExcluded(x => ({ ...x, [id]: how === 'skip' }));
    // A name already typed for it is kept; otherwise one is offered that says when the file is from.
    if (how !== 'skip') setChoices(c => ({ ...c, [id]: how === 'auto' ? { mode: 'auto' } : c[id]?.mode === 'rename' ? c[id] : { mode: 'rename', name: `${clash.base} ${monthOf(clash.file)}` } }));
  };
  const numberTheRest = () => {
    const rest = clashes.slice(reviewing! + 1).map(c => c.file.id);
    setExcluded(x => Object.fromEntries(Object.entries(x).filter(([id]) => !rest.includes(id))));
    setChoices(c => Object.fromEntries(Object.entries(c).filter(([id]) => !rest.includes(id))));
    setReviewing(undefined);
  };
  const move = () => onMove(
    { basePath, folderName: folderName!, direct, mode, companions, label, files: going.map(f => ({ id: f.id, name: resolved.names[f.id], subfolder: subfolderOf?.(f) })) }, going);

  return (
    <>
      <WindowShell label="Preview" columns="grid-cols-[minmax(0,1fr)_400px]" onClose={() => { if (!busy) onClose(); }} header={(
        <>
          <span className="whitespace-nowrap text-[18px] font-semibold">Preview</span>
          <span className="whitespace-nowrap rounded-md bg-s2 px-[9px] py-[3px] text-[12px] font-semibold text-t2">{kind}</span>
          <span className="whitespace-nowrap text-[14px] text-t3">Nothing has moved yet</span>
          <span className="flex-1" />
        </>
      )}>
        <div className="flex min-h-0 min-w-0 flex-col bg-bg">
          <div className="flex shrink-0 flex-wrap items-center gap-3 px-6 pb-3.5 pt-4">
            <p className="text-[15px]">
              <span className="font-semibold">{going.length.toLocaleString()}</span> {all ? `${going.length === 1 ? 'file' : 'files'} will ${verb}` : `of ${movable.length.toLocaleString()} files will ${verb}`}
            </p>
            <span className="text-[13px] text-t3">Click a file to leave it where it is</span>
            <button type="button" onClick={() => setExcluded(all ? Object.fromEntries(movable.map(f => [f.id, true])) : {})} className="text-[13px] text-t2 underline underline-offset-[3px]">{all ? 'Leave all out' : 'Include all'}</button>
            <span className="flex-1" />
            <Pager page={Math.min(page, pages)} pages={pages} onPage={setPage} />
          </div>
          <ul aria-label="Files in this folder" className="grid min-h-0 flex-1 grid-cols-[repeat(auto-fill,minmax(112px,1fr))] content-start gap-x-2.5 gap-y-4 overflow-y-auto px-6 pb-6">
            {files.slice(from, from + PREVIEW_PAGE).map(f => {
              const settled = planned !== undefined && !planned.has(f.id), off = !!excluded[f.id];
              return (
                <li key={f.id}>
                  <button type="button" aria-pressed={!off && !settled} disabled={settled} onClick={() => toggle(f.id)} title={f.name}
                    className={`flex w-full min-w-0 select-none flex-col gap-[5px] text-left ${off || settled ? 'opacity-35' : ''}`}>
                    <span className="relative block aspect-square overflow-hidden rounded-[9px]">
                      <Thumb file={f} className="h-full w-full" play={12} />
                      {!settled && <Check small on={!off} />}
                      {clashing.has(f.id) && <span className="absolute bottom-1.5 left-1.5 rounded-[5px] bg-[#f3c96a] px-1.5 py-0.5 text-[10px] font-bold tracking-[0.05em] text-[#3a2a08]">NAME TAKEN</span>}
                      {settled && <span className="absolute bottom-1.5 left-1.5 rounded-[5px] bg-black/60 px-1.5 py-0.5 text-[10px] font-bold tracking-[0.05em] text-white">ALREADY THERE</span>}
                    </span>
                    <span className="flex min-w-0 flex-col">
                      <span className="truncate text-[12.5px] font-medium">{f.name}</span>
                      <span className="truncate text-[11.5px] text-t3">{relativeFolder(root, f.folder)}</span>
                    </span>
                  </button>
                </li>
              );
            })}
          </ul>
        </div>

        <aside className="flex min-h-0 flex-col gap-5 overflow-y-auto border-l border-line p-6">
          {direct ? (
            <div className="flex flex-col gap-1.5 rounded-xl bg-s2 px-3.5 py-3 text-[13.5px]">
              <p>The files go straight into {leaf(basePath)}, without a new folder.</p>
              <button type="button" onClick={() => setDirect(false)} className="self-start text-[13px] text-t2 underline underline-offset-[3px]">Make a new folder there instead</button>
            </div>
          ) : (
            <label className="flex flex-col gap-[7px]">
              <span className="text-[13px] text-t2">Folder name</span>
              <input value={name} onChange={e => onName(e.target.value)} aria-invalid={named.error !== undefined}
                className={`h-[46px] rounded-xl border bg-bg px-3.5 text-[15px] font-medium outline-none ${named.error === undefined ? 'border-line' : 'border-rose'}`} />
              {named.error === undefined
                ? <span className="text-[12.5px] text-t3">Rename it here. Use \ to make subfolders.</span>
                : <span role="alert" className="text-[12.5px] text-rose-t">{named.error}</span>}
            </label>
          )}

          <div className="flex flex-col gap-2">
            <Segmented label="Move or copy" size="wide" value={mode} onChange={onMode} options={[{ value: 'move', name: 'Move' }, { value: 'copy', name: 'Copy' }]} />
            <p className="text-[12.5px] text-t3">{copy ? `The originals stay where they are. The copies use ${formatBytes(totalBytes(going))} more space.` : 'The files leave their current folders. Nothing is copied.'}</p>
          </div>

          <div className="flex flex-col gap-1.5">
            <div className="flex items-center gap-2.5">
              <span className="label-caps flex-1 tracking-[0.08em]">Goes to</span>
              {base !== undefined && <button type="button" onClick={() => { setBase(undefined); setDirect(false); }} className="text-[12.5px] text-t2 underline underline-offset-[3px]">Use the default</button>}
              <button type="button" onClick={() => setChoosing(true)} className="pill-quiet h-[30px] border border-line px-3.5 text-[12.5px]">Change</button>
            </div>
            <p aria-label="Destination" className="font-mono text-[13px] leading-normal [overflow-wrap:anywhere]">{destination}{separatorOf(root)}</p>
            {/* For thousands of files the service takes a moment to say what is already there. */}
            {waiting && (
              <p role="status" className="flex items-center gap-2 text-[12.5px] text-t3">
                <span aria-hidden className="h-3.5 w-3.5 animate-spin rounded-full border-2 border-s3 border-t-brand" />Checking what is already there…
              </p>
            )}
            {problem
              ? <p role="alert" className="text-[12.5px] text-rose-t">{problem}</p>
              : <p className="text-[12.5px] text-t3">
                {inside.length > 0 && `Split into a subfolder for each ${split}: ${inside.slice(0, NAMED_SUBFOLDERS).join(', ')}${more > 0 ? ` and ${more.toLocaleString()} more` : ''}. `}
                {loose > 0 && `${countFiles(loose)} with no location ${loose === 1 ? 'goes' : 'go'} into the folder itself. `}
                {plan && (plan.exists ? 'This folder already exists, so the files are added to it.' : 'A new folder is made.')}
              </p>}
          </div>

          <dl className="grid grid-cols-[72px_minmax(0,1fr)] gap-x-3.5 gap-y-[9px] text-[14px]">
            <dt className="text-t3">Files</dt><dd>{countFiles(going.length)}</dd>
            <dt className="text-t3">Size</dt><dd>{formatBytes(totalBytes(going))}</dd>
            <dt className="text-t3">Taken</dt><dd>{dateRange(going)}</dd>
            <dt className="text-t3">From</dt>
            <dd className="flex flex-col gap-[3px]">
              {from4.slice(0, 4).map(([folder, n]) => (
                <span key={folder} className="flex gap-2.5"><span className="min-w-0 flex-1 font-mono text-[13px] [overflow-wrap:anywhere]">{folder}</span><span className="font-mono text-[12px] font-medium text-t3">{n.toLocaleString()}</span></span>
              ))}
              {from4.length > 4 && <span className="text-[12.5px] text-t3">and {from4.length - 4} more {from4.length - 4 === 1 ? 'folder' : 'folders'}</span>}
            </dd>
          </dl>
          {merge && <p className="text-[13px] text-ident">{merge}</p>}

          {clashes.length > 0 && (
            <div className="flex flex-col gap-[7px] rounded-xl bg-amber-bg px-4 py-3.5 text-[13.5px]">
              <div className="label-caps text-amber">Name already taken</div>
              <p>{countFiles(clashes.length)} {clashes.length === 1 ? 'has' : 'have'} the same name as a file already there. Nothing is replaced.</p>
              <p className="text-[13px] text-t2">{clashSummary(clashes)}</p>
              {clashes.slice(0, 3).map(c => <p key={c.file.id} className="font-mono text-[12.5px] text-t2">{c.file.name} → {c.final ?? 'stays where it is'}</p>)}
              {clashes.length > 3 && <p className="text-[12.5px] text-t3">and {(clashes.length - 3).toLocaleString()} more</p>}
              <button type="button" onClick={() => setReviewing(0)} className="pill mt-1 h-[34px] self-start border border-line bg-pop px-4 text-[13px] hover:bg-s2">Review each name</button>
            </div>
          )}

          {(plan?.companions ?? 0) > 0 && (
            <label className="flex cursor-pointer select-none items-start gap-2.5 text-[13.5px]">
              <input type="checkbox" checked={companions} onChange={e => onCompanions(e.target.checked)} className="mt-0.5 h-[18px] w-[18px] shrink-0 accent-[var(--brand)]" />
              <span>Bring Live Photo videos and edit files along with their photos <span className="text-t3">· {plan!.companions.toLocaleString()} found</span></span>
            </label>
          )}

          <div className="mt-auto flex flex-col gap-2.5">
            <button type="button" disabled={!ready} onClick={move} className="pill-brand h-12 text-[15px] disabled:opacity-45">{busy ? (copy ? 'Copying…' : 'Moving…') : `${copy ? 'Copy' : 'Move'} ${countFiles(going.length)}`}</button>
            <button type="button" disabled={busy} onClick={onClose} className="pill-outline h-11 text-[14px] font-medium">Cancel</button>
            <p className="text-center text-[12.5px] text-t3">You can undo this afterwards from Recently moved.</p>
          </div>
        </aside>
      </WindowShell>

      {reviewing !== undefined && reviewed.length > 0 && (
        <NameClashWindow clashes={reviewed} index={Math.min(reviewing, reviewed.length - 1)} root={root} frozen={busy || asking || waiting} placeOf={placeOf} onIndex={setReviewing} onResolve={resolve}
          onWanted={(clash, wanted) => setChoices(c => ({ ...c, [clash.file.id]: { mode: 'rename', name: wanted } }))} onNumberTheRest={numberTheRest} onDelete={onDelete} onClose={() => setReviewing(undefined)} />
      )}
      {choosing && (
        <FolderPicker title="Choose where the files go" startAt={basePath} pickLabel="Make the new folder here" onCancel={() => setChoosing(false)}
          onPick={path => { setBase(path); setDirect(false); setChoosing(false); }}
          other={{ label: 'Put the files here', onPick: path => { setBase(path); setDirect(true); setChoosing(false); } }} />
      )}
    </>
  );
}
