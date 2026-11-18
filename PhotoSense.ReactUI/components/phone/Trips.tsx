import React, { useEffect, useRef, useState } from 'react';
import { chooseHome, makeTripFolder, renameTrip, thumbnailUrl, useTowns, useTrip, useTrips } from '../../lib/apiClient';
import { useBackLayer } from '../../lib/backStack';
import { formatDuration } from '../../lib/format';
import { deviceWord, usePhone } from '../../lib/phone';
import { fileOf } from '../../lib/search';
import { dayTitle, folderNote, homeNote, NO_TRIPS, townName, tripCount, tripDates, tripLine, tripMap } from '../../lib/trips';
import type { OrganizeFileDto, PhotoDto, TownDto, TripDto, TripsDto } from '../../types';
import type { ToastKind } from '../Toaster';
import { ListGroup, ListRow, PhoneButton, Radio, Segmented } from './Controls';
import { HomeIcon, VideoIcon } from './Icons';
import { PhotoGrid } from './PhotoGrid';
import { LayerScreen, PushedHeader } from './Screens';
import { Sheet } from './Sheet';

type Notify = (message: string, kind: ToastKind) => void;

const said = (e: unknown) => (e instanceof Error ? e.message : String(e));

/** The picture shown for a trip, or a video's mark, as it has none. */
function Cover({ photo }: { readonly photo: PhotoDto }) {
  return photo.isVideo
    ? <span className="absolute inset-0 grid place-items-center bg-fill text-t2"><VideoIcon /></span>
    : <img src={thumbnailUrl(photo.id)} alt="" className="absolute inset-0 h-full w-full object-cover" />;
}

/** 26: Library's Trips tile, with how many there are, opening Trips. */
export function TripsTile({ trips, onOpen }: { readonly trips: TripsDto | undefined; onOpen(): void }) {
  const latest = trips?.trips[0];
  const count = trips === undefined ? 'Finding trips…' : trips.trips.length === 0 ? 'No trips yet' : `${trips.trips.length.toLocaleString()} trip${trips.trips.length === 1 ? '' : 's'}`;
  return (
    <div className="px-4 pb-3">
      <button type="button" onClick={onOpen} aria-label={`Trips, ${count}`} className="relative block h-[96px] w-full overflow-hidden rounded-[16px] bg-fill text-left">
        {latest && <Cover photo={latest.cover} />}
        <span className="absolute inset-x-0 bottom-0 flex flex-col gap-0.5 bg-[linear-gradient(rgba(0,0,0,0),rgba(0,0,0,.72))] px-3.5 pb-2.5 pt-8 text-white">
          <span className="text-[16px] font-semibold">Trips</span>
          <span className="text-[13px]">{count}</span>
        </span>
      </button>
    </div>
  );
}

interface Props {
  /** The library's files, which a photo of a trip opens in the viewer. */
  readonly files: readonly OrganizeFileDto[];
  /** The viewer is open over it, and puts itself away on Escape. */
  readonly frozen?: boolean;
  onOpen(file: OrganizeFileDto): void;
  notify: Notify;
  onClose(): void;
}

/**
 * 29–31: Trips, over every tab: the trips as a list or on a plain map, home with Change, and a trip with its days and
 * Make a folder on the computer. Built on the phone; nothing about where photos were taken leaves it.
 */
export function TripsScreen({ files, frozen = false, onOpen, notify, onClose }: Props) {
  const { platform, tablet } = usePhone();
  const trips = useTrips(true);
  const data = trips.data;
  const [view, setView] = useState<'list' | 'map'>('list');
  const [open, setOpen] = useState<string>();
  const [choosing, setChoosing] = useState(false);
  const [naming, setNaming] = useState(false);
  const closeTrip = () => setOpen(undefined);
  useBackLayer(open !== undefined, closeTrip);
  useFolderEnded(data, notify);
  const trip = data?.trips.find(t => t.id === open);

  function openPhoto(photo: PhotoDto) {
    const file = fileOf(photo, files);
    if (file) onOpen(file);
    else notify(`PhotoSense is still reading ${photo.fileName}. Try again in a moment.`, 'info');
  }

  return (
    <LayerScreen label={trip ? trip.name : 'Trips'} onBack={trip ? closeTrip : onClose} frozen={frozen || choosing || naming}
      footer={trip && data ? <FolderFooter trip={trip} trips={data} device={deviceWord(platform, tablet)} notify={notify} /> : undefined}>
      {trip
        ? <TripView trip={trip} onBack={closeTrip} onPhoto={openPhoto} onRename={() => setNaming(true)} />
        : <>
          <PushedHeader title="Trips" onBack={onClose} />
          <div className="mx-auto flex w-full max-w-[720px] flex-col gap-3 px-4 pb-6">
            <Segmented<'list' | 'map'> label="Show" options={[{ id: 'list', label: 'List' }, { id: 'map', label: 'Map' }]} value={view} onChange={setView} />
            {trips.error !== undefined && <p role="alert" className="py-10 text-center text-[15px] text-t2">Trips could not be found: {said(trips.error)}</p>}
            {trips.error === undefined && !data && <p className="py-10 text-center text-[15px] text-t2">Finding trips…</p>}
            {data && !data.home && <p className="py-10 text-center text-[15px] leading-[1.4] text-t2">None of your photos say where they were taken, so there are no trips yet.</p>}
            {data?.home && <>
              <div className="flex items-start gap-3">
                <p className="min-w-0 flex-1 text-[14px] leading-[1.4] text-t2">{homeNote(data.home)}</p>
                <button type="button" onClick={() => setChoosing(true)} className="shrink-0 text-[14.5px] font-semibold text-acc">Change</button>
              </div>
              {data.trips.length === 0 && <p className="py-8 text-center text-[15px] leading-[1.4] text-t2">{NO_TRIPS}</p>}
              {data.trips.length > 0 && view === 'list' && (
                <ul aria-label="Trips" className="flex flex-col gap-3">
                  {data.trips.map(t => (
                    <li key={t.id}>
                      <button type="button" onClick={() => setOpen(t.id)} aria-label={`${t.name}, ${tripLine(t)}`} className="relative block aspect-video w-full overflow-hidden rounded-[16px] text-left">
                        <Cover photo={t.cover} />
                        <span className="absolute inset-x-0 bottom-0 flex flex-col gap-0.5 bg-[linear-gradient(rgba(0,0,0,0),rgba(0,0,0,.72))] px-3.5 pb-3 pt-10 text-white">
                          <span className="text-[17px] font-semibold">{t.name}</span>
                          <span className="text-[13.5px]">{tripLine(t)}</span>
                        </span>
                      </button>
                    </li>
                  ))}
                </ul>
              )}
              {data.trips.length > 0 && view === 'map' && <TravelMap trips={data.trips} home={data.home} onOpen={setOpen} />}
            </>}
          </div>
        </>}
      {choosing && data?.home && <HomeSheet home={data.home} chosen={data.homeChosen} notify={notify} onClose={() => setChoosing(false)} />}
      {naming && trip && <NameSheet trip={trip} notify={notify} onClose={() => setNaming(false)} />}
    </LayerScreen>
  );
}

/** Says when a trip's folder has been made, or why it was not, once the phone has finished asking. */
function useFolderEnded(trips: TripsDto | undefined, notify: Notify) {
  const was = useRef<string | undefined>(undefined);
  const folder = trips?.folder;
  useEffect(() => {
    if (was.current === `${folder?.id}:running` && folder && folder.stage !== 'running') {
      if (folder.stage === 'ready') notify(`Created ${folder.made} on ${trips!.computer}.`, 'ok');
      else notify(folder.problem!, 'error');
    }
    was.current = folder ? `${folder.id}:${folder.stage}` : undefined;
  }, [folder, trips, notify]);
}

/** 30: the trips as pins on a plain map, each with its count and name, and home. */
function TravelMap({ trips, home, onOpen }: { readonly trips: readonly TripDto[]; readonly home: TownDto; onOpen(id: string): void }) {
  const { platform, tablet } = usePhone();
  const map = tripMap(trips, home);
  // A label goes under its pin, from the pin's left near the map's left edge, to its right near the right edge.
  const under = { start: 'translate(-18px, 22px)', middle: 'translate(-50%, 22px)', end: 'translate(calc(-100% + 18px), 22px)' };
  return (
    <>
      <div role="group" aria-label="Map" className="relative h-[420px] overflow-hidden rounded-[20px] bg-map [background-image:linear-gradient(var(--map-line)_1px,transparent_1px),linear-gradient(90deg,var(--map-line)_1px,transparent_1px)] [background-size:40px_40px]">
        <span aria-label={`Home, ${townName(home)}`} role="img" className="absolute flex -translate-x-[13px] -translate-y-1/2 items-center gap-1.5" style={{ left: `${map.home.x}%`, top: `${map.home.y}%` }}>
          <span className="grid h-[26px] w-[26px] place-items-center rounded-full bg-white text-black shadow-[0_0_0_2px_var(--bg)]"><HomeIcon /></span>
          <span className="rounded-md bg-bg px-1.5 py-0.5 text-[12.5px] font-semibold">Home</span>
        </span>
        {/* Every label under every pin, so that one trip's name never hides another's count. */}
        {map.pins.map(p => (
          <span key={p.id} aria-hidden className="pointer-events-none absolute whitespace-nowrap rounded-md bg-bg px-1.5 py-0.5 text-[12.5px] font-semibold"
            style={{ left: `${p.x}%`, top: `${p.y}%`, transform: under[p.label] }}>{p.name}</span>
        ))}
        {map.pins.map(p => (
          <button key={p.id} type="button" aria-label={`${p.name}, ${tripCount(p.photos, trips.find(t => t.id === p.id)!.videos)}`} onClick={() => onOpen(p.id)}
            className="absolute grid h-9 min-w-9 -translate-x-1/2 -translate-y-1/2 place-items-center rounded-full border-2 border-bg bg-acc px-2 text-[13px] font-bold tabular-nums text-on-acc shadow-[0_2px_8px_rgba(0,0,0,.3)]"
            style={{ left: `${p.x}%`, top: `${p.y}%` }}>
            {p.photos.toLocaleString()}
          </button>
        ))}
      </div>
      <p className="text-[13.5px] text-t2">Each pin is a trip, with its photo count. The map is drawn on this {deviceWord(platform, tablet)}; where photos were taken is never sent anywhere.</p>
    </>
  );
}

/** 31: a trip: its cover, name, dates and count, and a grid of what was taken each day. */
function TripView({ trip, onBack, onPhoto, onRename }: { readonly trip: TripDto; onBack(): void; onPhoto(photo: PhotoDto): void; onRename(): void }) {
  const detail = useTrip(trip.id);
  return (
    <>
      <PushedHeader title={trip.name} onBack={onBack} />
      {/* A phone's column on a tablet too, until its own layout (T4). */}
      <div className="mx-auto w-full max-w-[720px]">
      <div className="px-4">
        <div className="relative aspect-[16/10] w-full overflow-hidden rounded-[20px]"><Cover photo={trip.cover} /></div>
        <h2 className="mt-4 font-title text-[28px] font-bold leading-tight">{trip.name}</h2>
        <p className="mt-1 text-[14.5px] text-t2">{tripDates(trip.start, trip.end)} · {tripCount(trip.photos, trip.videos)}</p>
        <button type="button" onClick={onRename} className="mt-2 text-[15px] font-semibold text-acc">Rename</button>
      </div>
      {detail.error !== undefined && <p role="alert" className="px-4 py-8 text-center text-[15px] text-t2">The trip could not be read: {said(detail.error)}</p>}
      {detail.data?.days.map((day, i) => (
        <section key={day.date} aria-label={dayTitle(i, day.date)} className="mt-5">
          <h3 className="px-4 pb-2 text-[15px] font-semibold">{dayTitle(i, day.date)}</h3>
          <PhotoGrid onTap={id => onPhoto(day.photos.find(p => p.id === id)!)}
            items={day.photos.map(p => ({ id: p.id, name: p.fileName, thumbnail: p.isVideo ? undefined : thumbnailUrl(p.id), duration: p.durationSeconds == null ? undefined : formatDuration(p.durationSeconds) }))} />
        </section>
      ))}
      </div>
    </>
  );
}

interface FooterProps {
  readonly trip: TripDto;
  readonly trips: TripsDto;
  /** What the phone is called in sentences: "iPhone", "phone", "iPad". */
  readonly device: string;
  notify: Notify;
}

/** Make a folder on the computer, or why it cannot yet, with where the folder goes and that the phone keeps its photos. */
function FolderFooter({ trip, trips, device, notify }: FooterProps) {
  const [asking, setAsking] = useState(false);
  if (!trips.computer) return <p className="text-center text-[13px] leading-[1.4] text-t2">Pair with a computer to make this trip a folder on it: in Settings, choose Computer.</p>;
  const running = trips.folder?.id === trip.id && trips.folder.stage === 'running';

  async function make() {
    setAsking(true);
    try {
      await makeTripFolder(trip.id);
    } catch (e) {
      notify(said(e), 'error');
    } finally {
      setAsking(false);
    }
  }

  const label = trip.folderMade ? 'Folder created' : running || asking ? 'Making the folder…' : `Make a folder on ${trips.computer}`;
  return <div className="mx-auto flex w-full max-w-[540px] flex-col gap-2">
    <PhoneButton kind={trip.folderMade ? 'secondary' : 'primary'} disabled={trip.folderMade || running || asking} onClick={() => void make()}>{label}</PhoneButton>
    <p className="text-center text-[13px] leading-[1.4] text-t2">{folderNote(trip, trips.computer, trips.tripsFolder, device)}</p>
  </div>;
}

/** Change: home among the towns photos were taken in, or PhotoSense's own choice. */
function HomeSheet({ home, chosen, notify, onClose }: { readonly home: TownDto; readonly chosen: boolean; notify: Notify; onClose(): void }) {
  const towns = useTowns(true).data;
  const [busy, setBusy] = useState(false);
  const isHome = (t: TownDto) => chosen && t.town === home.town && t.region === home.region && t.country === home.country;

  async function pick(town: TownDto | null) {
    setBusy(true);
    try {
      await chooseHome(town);
      onClose();
    } catch (e) {
      notify(said(e), 'error');
      setBusy(false);
    }
  }

  return (
    <Sheet title="Home" onClose={onClose}>
      <div className="flex flex-col gap-3 px-4 pb-3 pt-2">
        <p className="text-[14px] leading-[1.4] text-t2">Photos taken more than 30 miles from home make trips.</p>
        <ListGroup inSheet>
          <ListRow disabled={busy} onClick={() => void pick(null)} detail={chosen ? undefined : townName(home)}>
            <span className="flex items-center gap-3"><Radio on={!chosen} />Where most photos are taken</span>
          </ListRow>
          {towns?.map(t => (
            <ListRow key={`${t.town}|${t.region}|${t.country}`} disabled={busy} onClick={() => void pick(t)} detail={`${t.days.toLocaleString()} day${t.days === 1 ? '' : 's'}`}>
              <span className="flex items-center gap-3"><Radio on={isHome(t)} />{townName(t)}</span>
            </ListRow>
          ))}
        </ListGroup>
      </div>
    </Sheet>
  );
}

/** Rename: the trip's name as the person wants it, which names its folder on the computer too, or PhotoSense's own again. */
function NameSheet({ trip, notify, onClose }: { readonly trip: TripDto; notify: Notify; onClose(): void }) {
  const [busy, setBusy] = useState(false);
  const field = useRef<HTMLInputElement>(null);

  async function save(name: string | null) {
    setBusy(true);
    try {
      await renameTrip(trip.id, name);
      onClose();
    } catch (e) {
      notify(said(e), 'error');
      setBusy(false);
    }
  }

  return (
    <Sheet title="Rename trip" onClose={onClose}>
      <form className="flex flex-col gap-3 px-4 pb-3 pt-2" onSubmit={e => { e.preventDefault(); void save(field.current!.value); }}>
        <label className="flex flex-col gap-1.5 text-[14px] text-t2">
          Name
          {/* The field the person came for, so it takes the keyboard at once. */}
          {/* eslint-disable-next-line jsx-a11y/no-autofocus */}
          <input ref={field} autoFocus defaultValue={trip.name} maxLength={60} enterKeyHint="done"
            className="h-12 rounded-cell bg-cell px-4 text-[length:var(--fs-body)] text-t1 outline-none" />
        </label>
        <p className="text-[13px] leading-[1.4] text-t2">Its folder on the computer takes the name too, with the month before it.</p>
        <PhoneButton disabled={busy} onClick={() => void save(field.current!.value)}>Save</PhoneButton>
        {trip.name !== trip.suggested && (
          <PhoneButton kind="text" disabled={busy} onClick={() => void save(null)}>Use PhotoSense’s name: {trip.suggested}</PhoneButton>
        )}
      </form>
    </Sheet>
  );
}
