import React, { useState } from 'react';
import { refreshSearch, thumbnailUrl, useSearch } from '../../lib/apiClient';
import { useBackLayer } from '../../lib/backStack';
import { deviceWord, usePhone } from '../../lib/phone';
import { fileOf, foundLine, PART_LABELS, proLine, SEARCH_TRY, useRecentSearches } from '../../lib/search';
import type { OrganizeFileDto } from '../../types';
import type { ToastKind } from '../Toaster';
import { ListGroup, ListRow } from './Controls';
import { ArrowBackIcon, ClockIcon, CloseIcon, SearchIcon, VideoIcon } from './Icons';
import { usePlanGate } from './ProSheet';

/** Library's search field: it opens Search. */
export function SearchField({ onOpen, side }: { onOpen(): void; /** Beside a tablet's title, 340 pixels wide, rather than across a phone. */ readonly side?: boolean }) {
  return (
    <div className={side ? 'w-[340px] max-w-full' : 'px-4 pb-3'}>
      <button type="button" onClick={onOpen}
        className="flex h-[var(--srch-h)] w-full items-center gap-2.5 rounded-full bg-srch-bg px-3.5 text-left text-[length:var(--fs-body)] text-t2">
        <SearchIcon />Search photos
      </button>
    </div>
  );
}

interface Props {
  /** The library's files, which a match opens in the viewer. */
  readonly files: readonly OrganizeFileDto[];
  onOpen(file: OrganizeFileDto): void;
  notify(message: string, kind: ToastKind): void;
  onClose(): void;
}

/**
 * 27: Search, over Library. Before a search, the searches made before and some to try; then the search in its parts,
 * the free plan's card when it names things in photos, and what matches in a grid. It runs on the phone, which keeps
 * the searches made.
 */
export function SearchScreen({ files, onOpen, notify, onClose }: Props) {
  const { platform, ios, tablet } = usePhone();
  const gate = usePlanGate();
  const [typed, setTyped] = useState('');
  const [asked, setAsked] = useState('');
  const [recent, addRecent] = useRecentSearches();
  const found = useSearch(asked);
  useBackLayer(true, onClose);

  function run(query: string) {
    const q = query.trim();
    setTyped(q);
    setAsked(q);
    if (q) addRecent(q);
  }

  function open(photo: Parameters<typeof fileOf>[0]) {
    const file = fileOf(photo, files);
    if (file) onOpen(file);
    else notify(`PhotoSense is still reading ${photo.fileName}. Try again in a moment.`, 'info');
  }

  const note = <p className="px-8 text-center text-[13.5px] leading-[1.4] text-t2">Search runs on this {deviceWord(platform, tablet)}. Your photos and searches are not uploaded.</p>;
  const list = (header: string, queries: readonly string[], icon: React.ReactNode) => queries.length > 0 && (
    <ListGroup header={header}>
      {queries.map(q => <ListRow key={q} onClick={() => run(q)}><span className="flex items-center gap-3"><span className="text-t2">{icon}</span>{q}</span></ListRow>)}
    </ListGroup>
  );

  return (
    <div role="dialog" aria-modal aria-label="Search" className="fixed inset-0 z-30 flex animate-fade-in flex-col bg-bg font-ui text-t1">
      <form role="search" onSubmit={e => { e.preventDefault(); run(typed); }} className="flex shrink-0 items-center gap-2.5 px-4 pb-2.5 pt-[calc(var(--safe-t)+6px)]">
        {!ios && <button type="button" aria-label="Back" onClick={onClose} className="-ml-1.5 grid h-10 w-10 shrink-0 place-items-center rounded-full"><ArrowBackIcon /></button>}
        <label className="flex h-[var(--srch-h)] min-w-0 flex-1 items-center gap-2.5 rounded-full bg-srch-bg pl-3.5 pr-1.5 text-t2">
          <SearchIcon />
          {/* The field the person came for, so it takes the keyboard at once. */}
          {/* eslint-disable-next-line jsx-a11y/no-autofocus */}
          <input type="search" autoFocus value={typed} aria-label="Search photos" placeholder="Search photos" enterKeyHint="search"
            onChange={e => { setTyped(e.target.value); if (!e.target.value) setAsked(''); }}
            className="h-full min-w-0 flex-1 bg-transparent text-[length:var(--fs-body)] text-t1 outline-none placeholder:text-t2 [&::-webkit-search-cancel-button]:hidden" />
          {typed && <button type="button" aria-label="Clear" onClick={() => { setTyped(''); setAsked(''); }} className="grid h-[30px] w-[30px] shrink-0 place-items-center rounded-full"><CloseIcon size={15} /></button>}
        </label>
        {ios && <button type="button" onClick={onClose} className="shrink-0 text-[17px] text-acc">Cancel</button>}
      </form>
      <div className="flex min-h-0 flex-1 flex-col gap-5 overflow-y-auto pb-6 pt-1.5 [&>section]:px-4">
        {!asked && <>
          {list('Recent', recent, <ClockIcon />)}
          {list('Try', SEARCH_TRY, <SearchIcon size={18} />)}
          {note}
        </>}
        {asked && found.error !== undefined && (
          <p role="alert" className="px-6 text-center text-[15px] text-t2">The search could not be made: {found.error instanceof Error ? found.error.message : String(found.error)}</p>
        )}
        {asked && found.error === undefined && !found.data && <p className="px-6 text-center text-[15px] text-t2">Searching…</p>}
        {asked && found.data && <>
          <ul aria-label="Searched for" className="flex flex-wrap gap-1.5 px-4">
            {found.data.parts.map(p => (
              <li key={`${p.kind}-${p.text}`} className="flex h-8 items-center gap-1.5 rounded-full bg-fill px-3 text-[14.5px]">
                <span className="text-[12.5px] text-t2">{PART_LABELS[p.kind]}</span>{p.text}
              </li>
            ))}
          </ul>
          {found.data.proNeeded && (
            <div role="note" className="mx-4 flex flex-col gap-3 rounded-cell bg-acc-soft px-4 py-3.5 text-on-acc-soft">
              <p className="text-[14.5px] leading-[1.4]">{proLine(found.data.parts)}</p>
              <button type="button" onClick={() => gate.offer('search', () => void refreshSearch())} className="h-9 self-start rounded-full bg-acc px-4 text-[14px] font-semibold text-on-acc">Get Pro</button>
            </div>
          )}
          <h2 className="px-4 text-[17px] font-semibold">{foundLine(found.data.total, found.data.videos)}</h2>
          {found.data.photos.length > 0 && (
            <ul aria-label="Matches" className="grid grid-cols-4 gap-[2px]">
              {found.data.photos.map(photo => (
                <li key={photo.id}>
                  <button type="button" aria-label={photo.fileName} onClick={() => open(photo)} className="relative block aspect-square w-full overflow-hidden bg-fill">
                    {photo.isVideo
                      ? <span className="absolute inset-0 grid place-items-center text-t2"><VideoIcon /></span>
                      : <img src={thumbnailUrl(photo.id)} alt="" loading="lazy" className="h-full w-full object-cover" />}
                  </button>
                </li>
              ))}
            </ul>
          )}
          {note}
        </>}
      </div>
    </div>
  );
}
