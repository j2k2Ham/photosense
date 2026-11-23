import React, { useState } from 'react';
import {
  backUpToLocations, checkLocations, connectServer, organizeThumbnailUrl, deleteSent, disconnectLocation, locationInstead, pauseLocations, removeLocation, renameLocation, setCountAt,
  turnOffLocation, turnOnLocation, useLocationDetails, useLocations, type ApiError,
} from '../../lib/apiClient';
import { checkedAt } from '../../lib/backup';
import { formatBytes } from '../../lib/format';
import { items } from '../../lib/library';
import {
  CLOUD_FOLDERS, CLOUD_TILES, detailRows, heldLine, isCloud, isServer, KIND_NAMES, progressLine, SERVER_KINDS, startingSettings, statusLine, storageAmount, type ServerField,
} from '../../lib/locations';
import { limitLine } from '../../lib/plan';
import { deviceWord, usePhone } from '../../lib/phone';
import type { LocationDto, LocationsDto, OrganizeFileDto, SendResultDto } from '../../types';
import type { ToastKind } from '../Toaster';
import { HeaderTextButton, ListGroup, ListRow, PhoneButton, Radio, Segmented, StickyFooter } from './Controls';
import { LaptopIcon } from './Icons';
import { usePlanGate } from './ProSheet';
import { PhotoGrid } from './PhotoGrid';
import { LayerScreen, PlaceContext, PushedHeader, useInPlace } from './Screens';
import { ConfirmSheet, Sheet } from './Sheet';

type Notify = (message: string, kind: ToastKind) => void;

const said = (e: unknown) => (e instanceof Error ? e.message : String(e));
const status = (e: unknown) => (e as Partial<ApiError>).status;

/** A location's tile: the computer's or a server's glyph, or a cloud service's letter on its colour. */
export function LocationTile({ location }: { readonly location: Pick<LocationDto, 'kind'> }) {
  const tile = CLOUD_TILES[location.kind];
  if (tile) {
    return <span aria-hidden className="grid h-9 w-9 shrink-0 place-items-center rounded-[10px] text-[17px] font-bold text-white" style={{ background: `oklch(0.6 0.13 ${tile.hue})` }}>{tile.letter}</span>;
  }
  return (
    <span aria-hidden className="grid h-9 w-9 shrink-0 place-items-center rounded-[10px] bg-acc-soft text-on-acc-soft">
      {location.kind === 'computer' ? <LaptopIcon /> : <ServerGlyph />}
    </span>
  );
}

const ServerGlyph = () => (
  <svg aria-hidden width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round">
    <rect x="4" y="4" width="16" height="7" rx="2" /><rect x="4" y="13" width="16" height="7" rx="2" /><path d="M8 7.5h.01M8 16.5h.01" />
  </svg>
);

/**
 * Settings' Locations (53): the computer, Google Drive, OneDrive and Dropbox, and every location Connect to server
 * added, each with a switch; turning a cloud service on signs in to it, and the computer not paired yet pairs it. Connect to server adds one; the rule
 * for Safe to delete says on how many a copy must be checked. A row opens its details.
 */
export function LocationsGroup({ notify, onPair, chosen, onChoose }: {
  notify: Notify;
  onPair(): void;
  /** On a tablet's Backup, the location whose page shows beside the list, lit there. */
  readonly chosen?: string;
  /** On a tablet's Backup, shows a location's page, or Connect to server (`connect`), beside the list instead of over it. */
  onChoose?(what: string): void;
}) {
  const data = useLocations(true).data;
  const gate = usePlanGate();
  const [signingIn, setSigningIn] = useState<string>();
  const [open, setOpen] = useState<string>();
  const [connecting, setConnecting] = useState<LocationDto | true>();
  if (!data) return <p role="status" className="px-1 text-[14px] text-t2">Looking at where backups go…</p>;

  async function act(run: () => Promise<void>, done?: string) {
    try {
      await run();
      if (done) notify(done, 'ok');
    } catch (e) {
      notify(said(e), 'error');
    }
  }

  async function turn(location: LocationDto, on: boolean) {
    if (!on && location.id === data!.held) return notify(heldLine(location.name), 'info');
    if (!on) return act(() => turnOffLocation(location.id), `${location.name} is off. Nothing there was deleted.`);
    if (location.kind === 'computer' && !location.connected) return onPair();
    setSigningIn(location.id);
    try {
      await turnOnLocation(location.id);
      notify(`${location.name} is on.`, 'ok');
    } catch (e) {
      if (status(e) === 402) {
        // The free plan always backs up to the computer; the one it counts is the other that is on.
        const on = data!.locations.find(l => l.on && l.id !== location.id && l.kind !== 'computer')!;
        const current = on.name;
        // Once backups have gone to the one that is on, the free plan stays with it: only Pro adds another.
        gate.offer('location', () => void act(() => turnOnLocation(location.id), `${location.name} is on.`),
          { current, wanted: location.name, run: on.id === data!.held ? undefined : () => void act(() => locationInstead(location.id), `${location.name} is on in place of ${current}.`) });
      } else {
        notify(said(e), status(e) === 409 ? 'info' : 'error');
      }
    } finally {
      setSigningIn(undefined);
    }
  }

  const startConnecting = () => (onChoose ? onChoose('connect') : setConnecting(true));
  const connect = () => (data.pro ? startConnecting() : gate.offer('server', startConnecting));
  const opened = data.locations.find(l => l.id === open);
  return (
    <>
      <div className="flex flex-col gap-2">
        <ListGroup header="Locations">
          {data.locations.map(l => (
            <li key={l.id} className={`flex min-h-[60px] items-center gap-3 px-4 py-2 ${chosen === l.id ? 'bg-sel' : ''}`}>
              <button type="button" aria-current={chosen === l.id ? 'true' : undefined} className="flex min-w-0 flex-1 items-center gap-3 text-left"
                onClick={() => (l.kind === 'computer' && !l.connected ? onPair() : onChoose ? onChoose(l.id) : setOpen(l.id))}>
                <LocationTile location={l} />
                <span className="flex min-w-0 flex-col">
                  <span className="truncate text-[16px]">{l.name}</span>
                  <span className={`truncate text-[13px] ${l.problem ? 'text-danger-t' : 'text-t2'}`}>{signingIn === l.id ? 'Signing in…' : statusLine(l)}</span>
                </span>
              </button>
              {/* The label makes the drawn switch the one tapped. */}
              <label className="flex shrink-0 cursor-pointer items-center">
                <input type="checkbox" role="switch" aria-label={l.name} checked={l.on} disabled={signingIn !== undefined || (isCloud(l.kind) && !l.available)}
                  onChange={e => void turn(l, e.target.checked)} className="peer sr-only" />
                <SwitchLook on={l.on} />
              </label>
            </li>
          ))}
          <ListRow chevron onClick={connect}>
            <span className="flex items-center gap-2 text-acc">Connect to server…{!data.pro && <span className="rounded-full bg-acc-soft px-[7px] text-[11px] font-bold text-on-acc-soft">PRO</span>}</span>
          </ListRow>
        </ListGroup>
        <p className="px-1 text-[13px] leading-[1.45] text-t2">
          {data.pro
            ? 'Turn on the places that should keep a copy of your photos and videos. Trovolve backs up to every one that is on.'
            : 'Turn on the places that should keep a copy of your photos and videos. The free plan backs up to your computer and one other location. Pro adds as many as you like.'}
        </p>
      </div>
      <div className="flex flex-col gap-2">
        <ListGroup header="Safe to delete">
          <ListRow detail={<Segmented<'1' | '2'> label="Count a photo as backed up when its copy is checked on" options={[{ id: '1', label: 'Any location' }, { id: '2', label: '2 locations' }]}
            value={data.countAt === 2 ? '2' : '1'} onChange={v => void act(() => setCountAt(Number(v)))} className="w-[230px]" />}>Checked on</ListRow>
        </ListGroup>
        <p className="px-1 text-[13px] leading-[1.45] text-t2">
          {data.countAt === 2
            ? 'A photo counts as backed up once its copy is checked on 2 locations that are on.'
            : 'A photo counts as backed up once its copy is checked on any location that is on.'}
        </p>
      </div>
      {/* Over everything, Settings on a tablet included. */}
      <PlaceContext.Provider value={false}>
        {opened && <LocationScreen location={opened} held={opened.id === data.held} notify={notify} onEdit={() => { setOpen(undefined); setConnecting(opened); }} onClose={() => setOpen(undefined)} />}
        {connecting && <ConnectServerScreen editing={connecting === true ? undefined : connecting} notify={notify} onClose={() => setConnecting(undefined)} />}
      </PlaceContext.Provider>
    </>
  );
}

/** The switch's look beside the input it draws. */
const SwitchLook = ({ on }: { readonly on: boolean }) => (
  <span aria-hidden className={`relative h-[31px] w-[51px] shrink-0 rounded-full transition after:absolute after:left-[2px] after:top-[2px] after:h-[27px] after:w-[27px] after:rounded-full after:bg-white after:shadow after:transition peer-focus-visible:ring-2 peer-focus-visible:ring-acc peer-disabled:opacity-40 ${on ? 'bg-sw-on after:translate-x-5' : 'bg-sw-off'}`} />
);

interface ScreenProps {
  readonly location: LocationDto;
  /** Backups have gone here on the free plan, which is held to it: it is not turned off, signed out of or removed. */
  readonly held?: boolean;
  /** On a tablet's Backup, how backing up to the locations stands, and its buttons. */
  readonly actions?: React.ReactNode;
  notify: Notify;
  /** Opens Connect to server with this location's settings, to change it. */
  onEdit(): void;
  onClose(): void;
}

/**
 * A location in full (61–62): how it stands, how full it is where it says, its folder and the copies it holds checked,
 * how a location added is reached (its keys masked), Rename and Edit for one added, and Turn off, Disconnect or Remove
 * location, none of which deletes anything there.
 */
export function LocationScreen({ location, held, actions, notify, onEdit, onClose }: ScreenProps) {
  const inPlace = useInPlace();
  const { platform, tablet, ios } = usePhone();
  const details = useLocationDetails(location.id).data;
  const [asking, setAsking] = useState<'remove' | 'disconnect'>();
  const [naming, setNaming] = useState(false);
  const now = new Date();
  const device = deviceWord(platform, tablet);
  const where = details?.where ?? CLOUD_FOLDERS[location.kind]?.folder;

  async function act(run: () => Promise<void>, done: string, close: boolean) {
    try {
      await run();
      notify(done, 'ok');
      if (close) onClose();
    } catch (e) {
      notify(said(e), 'error');
    }
  }

  return (
    // Beside a tablet's list, where the computer's card already bears its name.
    <LayerScreen label={inPlace ? `${location.name} details` : location.name} onBack={onClose} frozen={asking !== undefined || naming}>
      <PushedHeader title={location.name} onBack={inPlace ? undefined : onClose} />
      <div className="mx-auto flex w-full max-w-[560px] flex-col gap-5 px-4 pb-6 pt-2">
        <section aria-label="How it stands" className="flex items-center gap-3 rounded-cell bg-cell p-4">
          <LocationTile location={location} />
          <span className="flex min-w-0 flex-col">
            <span className="text-[16px] font-semibold">{location.on ? 'On' : 'Off'}</span>
            <span className={`text-[13.5px] ${location.problem ? 'text-danger-t' : 'text-t2'}`}>{statusLine(location)}</span>
          </span>
        </section>
        {actions}
        {details?.space && (
          <section aria-label="Storage" className="flex flex-col gap-1.5">
            <div role="progressbar" aria-label={`${location.name} storage`} aria-valuemin={0} aria-valuemax={details.space.total} aria-valuenow={details.space.used} className="h-2 overflow-hidden rounded-full bg-fill2">
              <div className="h-full rounded-full bg-acc" style={{ width: `${Math.min(100, (details.space.used / details.space.total) * 100)}%` }} />
            </div>
            <p className="text-[13px] text-t2">{storageAmount(details.space.used)} of {storageAmount(details.space.total)} used</p>
          </section>
        )}
        {details?.problem && <p role="alert" className="text-[14px] text-danger-t">{details.problem}</p>}
        <ListGroup header="Backups">
          {where && <ListRow detail={where}>Folder</ListRow>}
          <ListRow detail={items(location.copies).replace('item', 'file')}>Checked copies</ListRow>
          <ListRow detail={location.lastBackup ? checkedAt(location.lastBackup, now) : 'Not yet'}>Last backup</ListRow>
          {location.kind !== 'computer' && <ListRow detail={location.lastChecked ? checkedAt(location.lastChecked, now) : 'Not yet'}>Last checked</ListRow>}
        </ListGroup>
        <p className="-mt-3 px-1 text-[13px] leading-[1.45] text-t2">Trovolve compares each file&apos;s checksum with the copy in {location.name} before it counts as backed up.</p>
        {isServer(location.kind) && (
          <>
            <ListGroup header="Connection">
              <ListRow detail={KIND_NAMES[location.kind]}>Type</ListRow>
              {detailRows(location).map(([label, value]) => <ListRow key={label} detail={value}>{label}</ListRow>)}
              <ListRow detail={ios ? `In the Keychain on this ${device}` : `In the Android Keystore on this ${device}`}>Keys and passwords</ListRow>
            </ListGroup>
            <div className="flex gap-2">
              <PhoneButton kind="secondary" onClick={() => setNaming(true)}>Rename</PhoneButton>
              <PhoneButton kind="secondary" onClick={onEdit}>Edit</PhoneButton>
            </div>
          </>
        )}
        <div className="flex flex-col gap-2">
          {location.on && <PhoneButton kind="secondary" onClick={() => (held ? notify(heldLine(location.name), 'info') : void act(() => turnOffLocation(location.id), `${location.name} is off. Nothing there was deleted.`, false))}>Turn off</PhoneButton>}
          {isCloud(location.kind) && location.connected && <PhoneButton kind="danger" onClick={() => (held ? notify(heldLine(location.name), 'info') : setAsking('disconnect'))}>Disconnect</PhoneButton>}
          {isServer(location.kind) && <PhoneButton kind="danger" onClick={() => (held ? notify(heldLine(location.name), 'info') : setAsking('remove'))}>Remove location</PhoneButton>}
          <p className="px-1 text-center text-[13px] leading-[1.45] text-t2">Files at {location.name} are not deleted. Photos backed up only here stop counting as safe to delete.</p>
        </div>
      </div>
      {asking && (
        <ConfirmSheet title={asking === 'remove' ? `Remove ${location.name}?` : `Disconnect ${location.name}?`} confirmLabel={asking === 'remove' ? 'Remove location' : 'Disconnect'}
          lines={[`Files at ${location.name} are not deleted.`, 'Photos backed up only here stop counting as safe to delete.']}
          onCancel={() => setAsking(undefined)}
          onConfirm={() => { setAsking(undefined); void act(() => (asking === 'remove' ? removeLocation(location.id) : disconnectLocation(location.id)),
            asking === 'remove' ? `${location.name} was removed. Nothing there was deleted.` : `${location.name} is disconnected. Nothing there was deleted.`, true); }} />
      )}
      {naming && <NameSheet name={location.name} onClose={() => setNaming(false)} onSave={name => { setNaming(false); void act(() => renameLocation(location.id, name), `Renamed ${name}.`, false); }} />}
    </LayerScreen>
  );
}

/** Rename: the name the person gives a location they added. */
function NameSheet({ name, onSave, onClose }: { readonly name: string; onSave(name: string): void; onClose(): void }) {
  const [typed, setTyped] = useState(name);
  return (
    <Sheet title="Rename location" onClose={onClose}>
      <form className="flex flex-col gap-3 px-4 pb-3 pt-2" onSubmit={e => { e.preventDefault(); onSave(typed); }}>
        <label className="flex flex-col gap-1.5 text-[14px] text-t2">
          Name
          <input value={typed} onChange={e => setTyped(e.target.value)} maxLength={60} className="h-12 rounded-cell bg-cell px-4 text-[length:var(--fs-body)] text-t1 outline-none" />
        </label>
        <PhoneButton disabled={!typed.trim()} onClick={() => onSave(typed)}>Save</PhoneButton>
      </form>
    </Sheet>
  );
}

/**
 * Connect to server (58–60): the kind, a name the person chooses, and the fields each kind needs; Connect lists the
 * folder, writes a test file, reads it back and deletes it, and only then is the location added. A failure leaves the
 * page filled in, saying what to change. Changing one added starts from its settings; its keys are typed again.
 */
export function ConnectServerScreen({ editing, notify, onClose }: { readonly editing?: LocationDto; notify: Notify; onClose(): void }) {
  const inPlace = useInPlace();
  const { ios, platform, tablet } = usePhone();
  const [kind, setKind] = useState(SERVER_KINDS.find(k => k.kind === editing?.kind) ?? SERVER_KINDS[0]);
  const [name, setName] = useState(editing?.name ?? '');
  const [settings, setSettings] = useState<Record<string, string>>({ ...startingSettings(kind), ...editing?.settings });
  const [secrets, setSecrets] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<string>();
  const device = deviceWord(platform, tablet);

  function choose(next: typeof kind) {
    setKind(next);
    setSettings(startingSettings(next));
    setSecrets({});
    setProblem(undefined);
  }

  async function connect() {
    setBusy(true);
    setProblem(undefined);
    try {
      await connectServer({ id: editing?.id ?? null, kind: kind.kind, name, settings, secrets });
      notify(`${name.trim() || KIND_NAMES[kind.kind]} is connected and on.`, 'ok');
      onClose();
    } catch (e) {
      setProblem(said(e));
    } finally {
      setBusy(false);
    }
  }

  const label = (f: ServerField) => (f.type === 'secret' && settings.auth === 'SSH key' ? 'Private key' : f.label);
  const field = (f: ServerField) => {
    if (f.type === 'segments') return <ListRow key={f.key} detail={<Segmented label={f.label} options={f.options!.map(o => ({ id: o, label: o }))} value={settings[f.key]} onChange={v => setSettings({ ...settings, [f.key]: v })} className="w-[200px]" />}>{f.label}</ListRow>;
    const value = f.type === 'secret' ? secrets[f.key] ?? '' : settings[f.key] ?? '';
    const change = (v: string) => (f.type === 'secret' ? setSecrets({ ...secrets, [f.key]: v }) : setSettings({ ...settings, [f.key]: v }));
    return (
      <li key={f.key}>
        <label className="flex min-h-[52px] items-center gap-3 px-4 py-2 text-[16px]">
          <span className="w-[130px] shrink-0">{label(f)}{f.required && <span aria-hidden className="text-t3"> *</span>}</span>
          {f.type === 'select'
            ? <select aria-label={f.label} value={value} onChange={e => change(e.target.value)} className="min-w-0 flex-1 bg-transparent text-right text-t2 outline-none">{f.options!.map(o => <option key={o}>{o}</option>)}</select>
            : <input aria-label={label(f)} type={f.type === 'secret' ? 'password' : 'text'} value={value} placeholder={f.placeholder} autoCapitalize="off" autoCorrect="off" spellCheck={false}
              onChange={e => change(e.target.value)} className="min-w-0 flex-1 bg-transparent text-right text-t1 outline-none placeholder:text-t3" />}
        </label>
      </li>
    );
  };

  return (
    <LayerScreen label="Connect to server" onBack={onClose}
      footer={<PhoneButton disabled={busy} onClick={() => void connect()}>{busy ? 'Checking…' : editing ? 'Save' : 'Connect'}</PhoneButton>}>
      <PushedHeader title={editing ? `Edit ${editing.name}` : 'Connect to server'} onBack={inPlace ? undefined : onClose}
        trailing={inPlace ? <HeaderTextButton onClick={onClose}>Cancel</HeaderTextButton> : undefined} />
      <div className="mx-auto flex w-full max-w-[560px] flex-col gap-5 px-4 pb-6 pt-2">
        {!editing && (
          <div role="radiogroup" aria-label="Kind" className="flex flex-wrap gap-2">
            {SERVER_KINDS.map(k => (
              <button key={k.kind} type="button" role="radio" aria-checked={k === kind} onClick={() => choose(k)}
                className={`h-[34px] rounded-chip border px-[14px] text-[14px] font-medium ${k === kind ? 'border-transparent bg-chip-on-bg text-chip-on-fg' : 'border-chip-bd bg-chip-bg'}`}>{k.label}</button>
            ))}
          </div>
        )}
        <p className="px-1 text-[14px] leading-[1.45] text-t2">{kind.help}</p>
        {problem && (
          <section role="alert" aria-label="Could not connect" className="rounded-cell bg-danger-soft p-4">
            <p className="text-[15px] font-semibold text-danger-t">Could not connect</p>
            <p className="text-[14px] leading-[1.45] text-t1">{problem}</p>
          </section>
        )}
        <ListGroup header="Location">
          <li>
            <label className="flex min-h-[52px] items-center gap-3 px-4 py-2 text-[16px]">
              <span className="w-[130px] shrink-0">Name</span>
              <input aria-label="Name" value={name} placeholder={`My ${kind.label}`} onChange={e => setName(e.target.value)} className="min-w-0 flex-1 bg-transparent text-right outline-none placeholder:text-t3" />
            </label>
          </li>
        </ListGroup>
        {kind.groups.map(([group, fields]) => <ListGroup key={group} header={group}>{fields.map(field)}</ListGroup>)}
        <p className="px-1 text-[13px] leading-[1.45] text-t2">
          Keys and passwords are saved {ios ? `in the Keychain on this ${device}` : `in the Android Keystore on this ${device}`} and are only sent to this location.
        </p>
      </div>
    </LayerScreen>
  );
}

/** Send to (64): every location that is on, the one chosen last first; Send sends what was chosen there. */
export function SendToSheet({ locations, count, chosen: first, onSend, onClose }: {
  readonly locations: readonly LocationDto[];
  /** How many pictures and videos were chosen. */
  readonly count: number;
  /** The location chosen last time. */
  readonly chosen?: string;
  onSend(location: LocationDto): void;
  onClose(): void;
}) {
  const [chosen, setChosen] = useState(locations.find(l => l.id === first) ?? locations[0]);
  return (
    <Sheet title="Send to" onClose={onClose}>
      <div className="flex flex-col gap-3 px-4 pb-3 pt-2">
        <div role="radiogroup" aria-label="Locations" className="flex flex-col gap-2">
          {locations.map(l => (
            <button key={l.id} type="button" role="radio" aria-checked={l === chosen} onClick={() => setChosen(l)} className="flex min-h-[56px] items-center gap-3 rounded-[16px] bg-sheet-cell px-3.5 py-2 text-left">
              <Radio on={l === chosen} />
              <LocationTile location={l} />
              <span className="min-w-0 flex-1 truncate text-[16px]">{l.name}</span>
            </button>
          ))}
        </div>
        <PhoneButton onClick={() => onSend(chosen)}>Send {items(count)} to {chosen.name}</PhoneButton>
      </div>
    </Sheet>
  );
}

/**
 * After sending (65): how many are there, every copy checked, and the offer to delete the originals from this device,
 * to its Recently Deleted or bin after its own question, within the free removals; on an iPhone the iCloud warning.
 */
export function SentSheet({ sent, location, cloudPhotos, notify, onDone }: {
  readonly sent: SendResultDto;
  readonly location: string;
  /** The iPhone is signed in to iCloud, which may keep its photos too. */
  readonly cloudPhotos: boolean;
  notify: Notify;
  onDone(): void;
}) {
  const { platform, tablet, ios, words } = usePhone();
  const inPlace = useInPlace();
  const gate = usePlanGate();
  const [busy, setBusy] = useState(false);
  const device = deviceWord(platform, tablet);

  const remove = () => gate.remove(sent.matched, 'items', allowed => void (async () => {
    setBusy(true);
    try {
      const done = await deleteSent(allowed);
      notify(`${items(done.removed)} deleted from this ${device}. They wait in ${words.bin} for 30 days. The copies in ${location} stay.`, 'ok');
      onDone();
    } catch (e) {
      notify(said(e), 'error');
    } finally {
      setBusy(false);
    }
  })(), true);

  const body = (
    <>
      <div className="flex flex-col gap-2 px-5 pb-2 pt-4">
        <h2 className="font-title text-[21px] font-bold">{items(sent.matched)} sent to {location}</h2>
        <p className="text-[15px] leading-[1.45] text-t2">
          {sent.matched === sent.items ? `Each one matches the copy in ${location}.` : `${items(sent.items - sent.matched)} did not match the copy in ${location}, and stay${sent.items - sent.matched === 1 ? 's' : ''} on this ${device}. Send again to try once more.`}
          {sent.matched > 0 && ` What takes ${formatBytes(sent.bytes)} here can be deleted from this ${device}.`}
        </p>
        {ios && cloudPhotos && sent.matched > 0 && (
          <p className="rounded-[16px] bg-amber-soft px-4 py-3 text-[14px] leading-[1.4]">If iCloud Photos is on, deleting here also deletes them from iCloud and your other Apple devices. The copies in {location} are not affected.</p>
        )}
      </div>
      <div className="flex flex-col gap-2 px-4 pt-3">
        {sent.matched > 0 && <PhoneButton kind="danger" disabled={busy} onClick={remove}>Delete {items(sent.matched)} from this {device}</PhoneButton>}
        {sent.matched > 0 && limitLine(gate.plan) && <p className="text-center text-[13px] text-t2">{limitLine(gate.plan)}</p>}
        <PhoneButton kind="secondary" onClick={onDone}>Done</PhoneButton>
      </div>
    </>
  );
  // On a tablet's Backup it fills the pane beside the list (T9).
  if (inPlace) return <section aria-label="Sent" className="mx-auto flex w-full max-w-[560px] flex-col gap-2 pb-6 pt-[calc(var(--safe-t)+24px)]">{body}</section>;
  return <Sheet title="Sent" bare onClose={onDone}>{body}</Sheet>;
}

/**
 * Send now on a tablet (T8): Library's pictures and videos to choose from, filling the pane beside Backup's list, and
 * Send to… with how many are chosen.
 */
export function SendPicker({ files, onSend, onClose }: { readonly files: readonly OrganizeFileDto[]; onSend(ids: string[]): void; onClose(): void }) {
  const [picked, setPicked] = useState<ReadonlySet<string>>(new Set());
  const tap = (id: string) => setPicked(before => { const next = new Set(before); if (!next.delete(id)) next.add(id); return next; });
  const bytes = files.filter(f => picked.has(f.id)).reduce((n, f) => n + f.sizeBytes, 0);
  const all = picked.size === files.length && files.length > 0;
  return (
    <LayerScreen label="Choose photos" onBack={onClose} footer={(
      <StickyFooter>
        <div className="flex items-center gap-3">
          <p className="flex min-w-0 flex-1 flex-col text-[14px]"><span className="font-semibold">{picked.size.toLocaleString()} selected</span>{picked.size > 0 && <span className="text-t2">{formatBytes(bytes)}</span>}</p>
          <span className="w-[160px]"><PhoneButton disabled={picked.size === 0} onClick={() => onSend([...picked])}>Send to…</PhoneButton></span>
        </div>
      </StickyFooter>
    )}>
      <PushedHeader title="Choose photos" trailing={(
        <span className="flex gap-1">
          <HeaderTextButton onClick={() => setPicked(all ? new Set() : new Set(files.map(f => f.id)))}>{all ? 'Deselect all' : 'Select all'}</HeaderTextButton>
          <HeaderTextButton onClick={onClose}>Cancel</HeaderTextButton>
        </span>
      )} />
      <PhotoGrid selecting selected={picked} onTap={tap}
        items={files.map(f => ({ id: f.id, name: f.name, thumbnail: f.isVideo ? undefined : organizeThumbnailUrl(f.id) }))} />
    </LayerScreen>
  );
}

/**
 * The Backup tab's card for the locations other than the computer (51–52): each one that is on with how it stands,
 * or how far it has got while backing up; Back up now or Pause, and Check all backups.
 */
export function LocationsCard({ data, notify, onSettings }: { readonly data: LocationsDto; notify: Notify; onSettings(): void }) {
  const on = data.locations.filter(l => l.on && l.kind !== 'computer');
  const activity = data.activity;
  const running = activity?.stage === 'running';
  const now = new Date();
  return (
    <div className="flex flex-col gap-2">
      <ListGroup header="Locations" trailing={on.length > 0 ? `${on.length} on` : undefined}>
        {on.map(l => {
          const progress = running ? activity!.locations.find(p => p.id === l.id) : undefined;
          return (
            <li key={l.id} className="flex min-h-[60px] items-center gap-3 px-4 py-2">
              <LocationTile location={l} />
              <span className="flex min-w-0 flex-1 flex-col">
                <span className="truncate text-[16px]">{l.name}</span>
                <span className={`truncate text-[13px] ${(progress?.problem ?? l.problem) ? 'text-danger-t' : 'text-t2'}`}>
                  {progress ? progressLine(progress) : l.problem ?? (l.lastBackup ? `${items(l.copies).replace('item', 'file')} · backed up ${checkedAt(l.lastBackup, now)}` : 'Not backed up yet')}
                </span>
              </span>
            </li>
          );
        })}
        <ListRow chevron onClick={onSettings}>{on.length > 0 ? 'Choose locations' : 'Back up to cloud storage or your own server'}</ListRow>
      </ListGroup>
      {on.length > 0 && <LocationsButtons data={data} notify={notify} />}
    </div>
  );
}

/** Back up now (or Pause while backing up) to every location that is on but the computer, and Check all backups. */
export function LocationsButtons({ data, notify }: { readonly data: LocationsDto; notify: Notify }) {
  const on = data.locations.filter(l => l.on && l.kind !== 'computer');
  const running = data.activity?.stage === 'running';
  async function act(run: () => Promise<void>, done: string) {
    try {
      await run();
      notify(done, 'info');
    } catch (e) {
      notify(said(e), 'error');
    }
  }
  return (
    <div className="flex gap-2">
      {running
        ? <PhoneButton kind="secondary" onClick={() => void act(pauseLocations, 'Paused. What is there stays there.')}>Pause</PhoneButton>
        : <PhoneButton kind="secondary" onClick={() => void act(backUpToLocations, `Backing up to ${on.length === 1 ? on[0].name : `${on.length} locations`}. Every copy is checked.`)}>Back up now</PhoneButton>}
      <PhoneButton kind="secondary" disabled={running} onClick={() => void act(checkLocations, 'Checking every copy.')}>Check all backups</PhoneButton>
    </div>
  );
}
