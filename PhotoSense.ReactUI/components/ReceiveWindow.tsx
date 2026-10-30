import React, { useCallback, useId, useState } from 'react';
import { confirmReceiving, startReceiving, stopReceiving, usePhones, useReceiveState } from '../lib/apiClient';
import { leaf } from '../lib/format';
import { FolderPicker } from './FolderPicker';
import type { ToastKind } from './Toaster';
import { Buttons, Compare, messageOf, Named, Progress, TransferWindow, Where } from './TransferParts';

interface Props {
  /** Where the photos go unless another folder is chosen. */
  readonly folder?: string;
  notify(message: string, kind: ToastKind): void;
  onClose(): void;
}

// While the computer connects, compares numbers with the phone or receives, closing the window stops it.
const LIVE = new Set(['idle', 'connecting', 'comparing', 'receiving']);

const files = (n: number) => `${n.toLocaleString()} ${n === 1 ? 'file' : 'files'}`;

/** A computer receiving, into a folder of its own, the photos a phone with PhotoSense offers. */
export function ReceiveWindow({ folder: first, notify, onClose }: Props) {
  const id = useId();
  const [folder, setFolder] = useState(first || undefined);
  const [picking, setPicking] = useState(false);
  const [chosen, setChosen] = useState<string>();
  const [receiving, setReceiving] = useState(false);
  const [refused, setRefused] = useState<string>();
  const [busy, setBusy] = useState(false);
  const phones = usePhones(!receiving);
  const { data: state } = useReceiveState(receiving);
  const found = phones.data?.phones ?? [];
  // One phone found is the one chosen.
  const phone = found.find(p => p.key === chosen) ?? (found.length === 1 ? found[0] : undefined);

  const close = useCallback(() => {
    if (receiving && state && LIVE.has(state.stage)) stopReceiving().catch(() => undefined);
    onClose();
  }, [receiving, state, onClose]);

  async function start() {
    setBusy(true);
    setRefused(undefined);
    try {
      await startReceiving(phone!.key, folder!);
      setReceiving(true);
    } catch (e) {
      setRefused(messageOf(e));
    } finally {
      setBusy(false);
    }
  }

  async function answer(same: boolean) {
    setBusy(true);
    try { await confirmReceiving(same); }
    catch (e) { notify(messageOf(e), 'error'); }
    finally { setBusy(false); }
  }

  function choosing() {
    return (
      <>
        <fieldset className="flex flex-col gap-2">
          <legend className="label-caps pb-2">Phone</legend>
          {phones.data === undefined && <p className="text-[14px] text-t2">Looking for phones…</p>}
          {phones.data !== undefined && found.length === 0 && (
            <div className="flex flex-col gap-1.5 text-[14px] text-t2">
              <p className="font-medium text-t1">No phone is offering photos yet.</p>
              <p>On the phone, open PhotoSense, select photos in Organize and choose <Named>Send to computer</Named>. The phone and this computer need to be on the same Wi-Fi, or the phone plugged into this computer with a cable; on Windows, a cable needs Apple Devices or iTunes.</p>
            </div>
          )}
          {found.map(p => (
            <label key={p.key} className="flex cursor-pointer items-center gap-3 rounded-xl border border-line px-3.5 py-2.5 has-[:checked]:border-brand">
              <input type="radio" name={`${id}-phone`} checked={phone?.key === p.key} onChange={() => setChosen(p.key)} className="accent-[var(--brand)]" />
              <span className="min-w-0 flex-1 truncate text-[15px]">{p.name}</span>
              <span className="text-[12.5px] text-t3">{p.via === 'usb' ? 'Cable' : 'Wi-Fi'}</span>
            </label>
          ))}
        </fieldset>
        <div className="flex flex-col gap-2">
          <span className="label-caps">Into</span>
          {folder ? (
            <div className="flex items-center gap-2.5">
              <span className="min-w-0 flex-1 truncate font-mono text-[13px]" title={folder}>{folder}</span>
              <button type="button" onClick={() => setPicking(true)} className="pill-quiet h-[34px] shrink-0 px-3.5 text-[13px] font-medium">Change</button>
            </div>
          ) : (
            <button type="button" onClick={() => setPicking(true)} className="pill-outline h-10 self-start px-4 text-[14px]">Choose a folder</button>
          )}
          <p className="text-[13px] text-t2">Nothing there is replaced: a file whose name is taken arrives under a new one, and one that is there already is not copied again.</p>
        </div>
        {refused && <p role="alert" className="text-[14px] text-rose-t">{refused}</p>}
        <Buttons>
          <button type="button" onClick={close} className="pill-outline h-11 px-5 text-[15px]">Cancel</button>
          <button type="button" disabled={busy || !phone || !folder} onClick={() => void start()} className="pill-brand h-11 px-5 text-[15px]">
            {folder ? `Receive into ${leaf(folder)}` : 'Receive photos'}
          </button>
        </Buttons>
      </>
    );
  }

  function following() {
    const from = state?.phone ?? phone?.name ?? 'the phone';
    const back = (
      <button type="button" onClick={() => setReceiving(false)} className="pill-outline h-11 px-5 text-[15px]">{state?.stage === 'done' ? 'Receive more' : 'Try again'}</button>
    );
    switch (state?.stage) {
      case 'comparing':
        return <Compare code={state.code!} other={from} busy={busy} onAnswer={same => void answer(same)} />;
      case 'receiving':
        return (
          <>
            <p className="text-[15px] text-t2">Receiving from <Named>{from}</Named> into <Where>{state.folder!}</Where>.</p>
            <Progress label={`Receiving from ${from}`} done={state.done} total={state.total} bytes={state.bytes} totalBytes={state.totalBytes} />
            <Buttons><button type="button" onClick={close} className="pill-outline h-11 px-5 text-[15px]">Stop</button></Buttons>
          </>
        );
      case 'done': {
        const arrived = state.done - state.alreadyThere;
        return (
          <>
            <p className="text-[15px] text-t2">
              {arrived > 0 ? <>{files(arrived)} arrived in <Where>{state.folder!}</Where>.</> : <>Nothing new arrived in <Where>{state.folder!}</Where>.</>}
              {state.alreadyThere > 0 && ` ${files(state.alreadyThere)} ${state.alreadyThere === 1 ? 'was' : 'were'} there already, so ${state.alreadyThere === 1 ? 'it was' : 'they were'} not copied again.`}
            </p>
            <p className="text-[13.5px] text-t2">If Move was chosen on the phone, it now deletes them from itself; otherwise it offers to.</p>
            <Buttons>{back}<button type="button" onClick={close} className="pill-brand h-11 px-5 text-[15px]">Close</button></Buttons>
          </>
        );
      }
      case 'failed':
        return (
          <>
            <p role="alert" className="text-[15px] text-rose-t">{state.problem}</p>
            <Buttons><button type="button" onClick={close} className="pill-outline h-11 px-5 text-[15px]">Close</button>{back}</Buttons>
          </>
        );
      default:
        return (
          <>
            <p className="text-[15px] text-t2">Connecting to <Named>{from}</Named>…</p>
            <Buttons><button type="button" onClick={close} className="pill-outline h-11 px-5 text-[15px]">Stop</button></Buttons>
          </>
        );
    }
  }

  return (
    <>
      <TransferWindow title="Receive from phone" frozen={picking} onClose={close}>{receiving ? following() : choosing()}</TransferWindow>
      {picking && (
        <FolderPicker title="Choose where the photos go" startAt={folder} pickLabel="Put them here"
          onPick={path => { setFolder(path); setPicking(false); }} onCancel={() => setPicking(false)} />
      )}
    </>
  );
}
