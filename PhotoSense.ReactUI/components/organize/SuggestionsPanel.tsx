import React, { useState } from 'react';
import { RADII, type MapLayout, type NameFormat, type OrganizeSettings, type Suggestion } from '../../lib/organize';
import { useDismiss } from '../../lib/useDismiss';
import type { OrganizeBatchDto } from '../../types';
import { Segmented } from '../ModeSwitch';
import { CustomFolderCard, type OwnFolder } from './CustomFolderCard';
import { FolderMap } from './FolderMap';
import { RecentMoves } from './RecentMoves';
import { SuggestionCard } from './SuggestionCard';
import { countFiles, useDrop } from './shared';

export type { OwnFolder } from './CustomFolderCard';

/** Which folder's files alone the gallery is showing. */
export type Focus = { kind: 'suggested' | 'own'; key: string } | { kind: 'unplaced' };

interface Props {
  readonly settings: OrganizeSettings;
  readonly showMap: boolean;
  readonly suggestions: readonly Suggestion[];
  readonly map?: MapLayout;
  readonly focus?: Focus;
  /** Files with no place that nothing has been arranged for. */
  readonly unplaced: number;
  readonly folders: readonly OwnFolder[];
  /** How many files are selected in the gallery. */
  readonly picked: number;
  /** How many files are being dragged; none when nothing is. */
  readonly dragging: number;
  /** The name of a new folder is being typed in the panel. */
  readonly naming: boolean;
  readonly name: string;
  readonly batches: readonly OrganizeBatchDto[];
  readonly busy: boolean;
  /** The listing is still being read, so there is nothing yet to say there is none of. */
  readonly reading: boolean;
  readonly rootName: string;
  onSettings(change: Partial<OrganizeSettings>): void;
  onShowMap(show: boolean): void;
  onFocus(focus?: Focus): void;
  onPreview(target: { kind: 'suggested' | 'own'; key: string }): void;
  onDrop(target: { kind: 'suggested' | 'own'; key: string } | 'new'): void;
  onGroupByDate(): void;
  onName(name: string): void;
  onStartNaming(): void;
  onCancelNaming(): void;
  onCreate(): void;
  onAddPicked(folderId: string): void;
  onRemoveFolder(folderId: string): void;
  onUndo(batch: OrganizeBatchDto): void;
}

const FORMATS: { value: NameFormat; name: string; example: string }[] = [
  { value: 'area', name: 'Landmark or area', example: 'Old Faithful, Yellowstone' },
  { value: 'city', name: 'Town, state', example: 'Butte, Montana' },
  { value: 'nest', name: 'State \\ landmark or area', example: 'Montana\\Uptown Butte' },
];

const Row = ({ label, children }: { readonly label: string; readonly children: React.ReactNode }) => (
  <div className="flex items-center gap-3"><span className="w-[104px] shrink-0 text-t2">{label}</span>{children}</div>
);

/** The menu of ways to name a place's folder, each with an example. */
function FormatMenu({ value, onChange }: { readonly value: NameFormat; onChange(value: NameFormat): void }) {
  const [open, setOpen] = useState(false);
  const ref = useDismiss<HTMLDivElement>(() => setOpen(false));
  return (
    <div ref={ref} className="relative shrink-0">
      <button type="button" aria-haspopup="listbox" aria-expanded={open} aria-label="Folder names" onClick={() => setOpen(o => !o)}
        className="pill h-[34px] gap-2.5 border border-line bg-bg px-3.5 text-[13px] font-medium hover:bg-s2">
        {FORMATS.find(f => f.value === value)!.name}<span aria-hidden className="text-[9px] text-t3">▼</span>
      </button>
      {open && (
        <div role="listbox" aria-label="Folder names" className="absolute left-0 top-10 z-10 flex w-[280px] flex-col gap-0.5 rounded-[14px] border border-line bg-pop p-1.5 shadow-pop">
          {FORMATS.map(f => (
            <button key={f.value} type="button" role="option" aria-selected={f.value === value} onClick={() => { onChange(f.value); setOpen(false); }}
              className={`flex flex-col gap-0.5 rounded-[10px] px-3 py-[9px] text-left hover:bg-s2 ${f.value === value ? 'bg-sel' : ''}`}>
              <span className="text-[13.5px] font-semibold">{f.name}</span>
              <span className="font-mono text-[12px] text-t3">{f.example}</span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

/** A place to drop dragged files, shown while something is being dragged. */
function DropPill({ name, onDrop }: { readonly name: string; onDrop(): void }) {
  const drop = useDrop(onDrop);
  return (
    <span {...drop.handlers} data-drop={name} className={`flex h-10 items-center gap-[9px] rounded-full border-[1.5px] border-dashed px-4 text-[13.5px] font-semibold ${drop.hot ? 'border-brand bg-brand text-on-brand' : 'border-t3 bg-bg text-t1'}`}>
      <span aria-hidden className="pointer-events-none h-3 w-4 rounded-[3px] bg-current opacity-60" /><span className="pointer-events-none">{name}</span>
    </span>
  );
}

/** The panel on the right: how to group, the folders that grouping suggests, the person's own folders, and what was moved. */
export function SuggestionsPanel(p: Props) {
  const { settings: s, suggestions } = p, byPlace = s.group === 'place';
  // Places are part of it when the folders are by place, or by date and then by place.
  const placed = byPlace || s.dateThen === 'place';
  const suggestedFiles = suggestions.reduce((n, x) => n + x.files.length, 0);
  const focusOf = (kind: 'suggested' | 'own', key: string) => p.focus?.kind === kind && p.focus.key === key;
  return (
    <aside aria-label="Suggested folders" className="flex min-h-0 flex-col border-l border-line bg-s1">
      <div className="flex shrink-0 flex-col gap-3.5 border-b border-line px-5 pb-4 pt-[18px]">
        <div className="flex items-center gap-3">
          <div className="flex min-w-0 flex-1 items-baseline gap-2.5">
            <h2 className="whitespace-nowrap text-[17px] font-semibold">Suggested folders</h2>
            <span className="truncate text-[13px] text-t3">{suggestions.length > 0 && `${suggestions.length} · ${countFiles(suggestedFiles)}`}</span>
          </div>
          {byPlace && (
            <label className="flex cursor-pointer select-none items-center gap-[9px] whitespace-nowrap text-[13px] text-t2">
              <input type="checkbox" role="switch" checked={p.showMap} onChange={e => p.onShowMap(e.target.checked)} className="peer sr-only" />
              <span aria-hidden className="relative h-[18px] w-8 rounded-full bg-s3 transition after:absolute after:left-0.5 after:top-0.5 after:h-3.5 after:w-3.5 after:rounded-full after:bg-t1 after:transition peer-checked:bg-brand peer-checked:after:translate-x-3.5 peer-checked:after:bg-on-brand" />
              Map
            </label>
          )}
        </div>
        <div className="flex flex-col gap-2.5 text-[13px]">
          <Row label="Group by">
            <Segmented label="Group by" value={s.group} onChange={group => p.onSettings({ group })} options={[{ value: 'place', name: 'Place' }, { value: 'date', name: 'Date' }]} />
          </Row>
          {!byPlace && (
            <Row label="One folder per">
              <Segmented label="One folder per" value={s.dateGrain} onChange={dateGrain => p.onSettings({ dateGrain })} options={[{ value: 'year', name: 'Year' }, { value: 'month', name: 'Month' }]} />
            </Row>
          )}
          <Row label="Then by">
            {byPlace
              ? <Segmented label="Then by" value={s.placeThen} onChange={placeThen => p.onSettings({ placeThen })} options={[{ value: 'year', name: 'Year' }, { value: 'month', name: 'Month' }, { value: 'none', name: 'Nothing' }]} />
              : <Segmented label="Then by" value={s.dateThen} onChange={dateThen => p.onSettings({ dateThen })} options={[{ value: 'place', name: 'Place' }, { value: 'none', name: 'Nothing' }]} />}
          </Row>
          {placed && (
            <>
              <Row label="Nearby places">
                <div className="flex min-w-0 flex-1 flex-wrap items-center gap-2">
                  <Segmented label="Nearby places" value={s.near} onChange={near => p.onSettings({ near })} options={[{ value: 'combine', name: 'Combine' }, { value: 'separate', name: 'Keep separate' }]} />
                  {s.near === 'combine' && (
                    <span className="flex shrink-0 items-center gap-2">
                      <span className="text-t3">within</span>
                      <Segmented label="Combine places within" value={s.radiusMiles} onChange={radiusMiles => p.onSettings({ radiusMiles })} options={RADII.map(r => ({ value: r, name: r === 50 ? '50 mi' : String(r) }))} />
                    </span>
                  )}
                </div>
              </Row>
              <Row label="Folder names"><FormatMenu value={s.nameFormat} onChange={nameFormat => p.onSettings({ nameFormat })} /></Row>
            </>
          )}
        </div>
      </div>

      {p.dragging > 0 && (
        <div role="group" aria-label="Drop the files on a folder" className="flex shrink-0 flex-col gap-2.5 border-b border-brand bg-sel px-5 py-3.5">
          <p className="text-[13.5px] font-semibold">Drop {countFiles(p.dragging)} on one of your folders, or on any suggested folder below</p>
          <div className="flex flex-wrap gap-2">
            {p.folders.map(f => <DropPill key={f.id} name={f.name} onDrop={() => p.onDrop({ kind: 'own', key: f.id })} />)}
            <DropPill name="New folder" onDrop={() => p.onDrop('new')} />
          </div>
        </div>
      )}

      <div className="flex min-h-0 flex-1 flex-col gap-2.5 overflow-y-auto px-5 pb-7 pt-4">
        {byPlace && p.showMap && p.map && (
          <FolderMap map={p.map} focus={p.focus?.kind === 'suggested' ? p.focus.key : undefined} onFocus={key => p.onFocus(key ? { kind: 'suggested', key } : undefined)}
            note={s.near === 'combine' ? `Places within ${s.radiusMiles === 1 ? '1 mile' : `${s.radiusMiles} miles`} of each other share a folder.` : 'Every place gets its own folder.'} />
        )}
        {suggestions.map(x => (
          <SuggestionCard key={x.key} suggestion={x} on={focusOf('suggested', x.key)} onFocus={() => p.onFocus(focusOf('suggested', x.key) ? undefined : { kind: 'suggested', key: x.key })}
            onPreview={() => p.onPreview({ kind: 'suggested', key: x.key })} onDrop={() => p.onDrop({ kind: 'suggested', key: x.key })} />
        ))}
        {suggestions.length === 0 && !p.reading && (
          <p className="px-2 py-6 text-center text-[14px] text-t3">{byPlace ? 'Every file with a location is organized or in one of your folders.' : 'Every file is organized or in one of your folders.'}</p>
        )}
        {byPlace && p.unplaced > 0 && !p.reading && (
          <div className="flex items-center gap-3.5 rounded-[14px] border border-dashed border-line p-3.5">
            <div className="flex min-w-0 flex-1 flex-col gap-0.5">
              <p className="text-[14.5px] font-semibold">No location · {countFiles(p.unplaced)}</p>
              <p className="text-[13px] text-t2">These have no place in their details, so they are left out here. Group them by date instead.</p>
            </div>
            <div className="flex shrink-0 flex-col gap-1.5">
              <button type="button" aria-label="Show the files with no location" onClick={() => p.onFocus({ kind: 'unplaced' })} className="pill-outline h-8 px-3.5 text-[13px]">Show</button>
              <button type="button" onClick={p.onGroupByDate} className="pill-quiet h-8 border border-line px-3.5 text-[13px]">Group by date</button>
            </div>
          </div>
        )}

        <div className="mt-2 flex items-center gap-2.5 border-t border-line pt-[18px]">
          <h3 className="flex-1 text-[17px] font-semibold">Your folders</h3>
          <button type="button" onClick={p.onStartNaming} className="pill-outline h-[34px] px-4 text-[13.5px]">New folder</button>
        </div>
        {p.naming && (
          <div className="flex flex-col gap-2 rounded-[14px] border border-brand bg-bg p-3">
            {/* eslint-disable-next-line jsx-a11y/no-autofocus */}
            <input autoFocus value={p.name} onChange={e => p.onName(e.target.value)} onKeyDown={e => { if (e.key === 'Enter') p.onCreate(); }}
              aria-label="Folder name" placeholder="Folder name, for example Trips\Glacier 2022" className="h-10 rounded-[10px] border border-line bg-s1 px-3 text-[14px] outline-none placeholder:text-t3" />
            <div className="flex items-center gap-2">
              <p className="flex-1 text-[12.5px] text-t3">
                {p.picked > 0 ? `The ${p.picked.toLocaleString()} selected ${p.picked === 1 ? 'file goes' : 'files go'} in it. Use \\ for subfolders.` : 'Use \\ for subfolders. Add files to it afterwards.'}
              </p>
              <button type="button" onClick={p.onCancelNaming} className="pill-outline h-[34px] px-3.5 text-[13px] font-medium">Cancel</button>
              <button type="button" onClick={p.onCreate} className="pill-brand h-[34px] px-4 text-[13px]">Create</button>
            </div>
          </div>
        )}
        {p.folders.map(f => (
          <CustomFolderCard key={f.id} folder={f} on={focusOf('own', f.id)} picked={p.picked} onFocus={() => p.onFocus(focusOf('own', f.id) ? undefined : { kind: 'own', key: f.id })}
            onAdd={() => p.onAddPicked(f.id)} onPreview={() => p.onPreview({ kind: 'own', key: f.id })} onRemove={() => p.onRemoveFolder(f.id)} onDrop={() => p.onDrop({ kind: 'own', key: f.id })} />
        ))}
        {p.folders.length === 0 && !p.naming && <p className="px-0.5 pt-0.5 text-[13px] text-t3">Make a folder of your own, then select or drag files onto it. Files you add are left out of the suggestions.</p>}

        {p.batches.length > 0 && <RecentMoves batches={p.batches} busy={p.busy} onUndo={p.onUndo} />}
        <p className="pt-3.5 text-[12.5px] text-t3">
          Folders are made inside <span className="font-mono text-t2">{p.rootName}</span>. Files already in the right folder are left alone, and nothing is ever replaced: if a name is taken, (1) is added.
        </p>
      </div>
    </aside>
  );
}
