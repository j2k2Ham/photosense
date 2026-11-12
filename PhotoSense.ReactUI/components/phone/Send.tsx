import React, { useState } from 'react';
import { createPortal } from 'react-dom';
import { formatBytes } from '../../lib/format';
import { deviceName, usePhone } from '../../lib/phone';
import { countFiles, photosAndVideos, someNames, type PhoneSend, type SendMode, type SendWhat } from '../../lib/send';
import type { SendStateDto } from '../../types';
import { ListGroup, PhoneButton, Radio, Segmented } from './Controls';
import { CloseIcon, DoneIcon, LaptopIcon, WifiIcon } from './Icons';
import { RootHeader, Screen } from './Screens';
import { ConfirmSheet } from './Sheet';

interface Props {
  readonly send: PhoneSend;
  /** The library is still being read, so what is in it is not all counted yet. */
  readonly reading: boolean;
  /** Opens Library choosing, for "What you chose in Library" with nothing chosen yet. */
  onChoose(): void;
}

const MODES = [{ id: 'copy', label: 'Copy' }, { id: 'move', label: 'Move' }] as const;

/** A card of a few lines on a soft colour: what to know, or what was done. */
const Card = ({ tone = 'cell', children }: { readonly tone?: 'cell' | 'acc' | 'keep'; readonly children: React.ReactNode }) => (
  <div className={`flex flex-col gap-1 rounded-cell px-4 py-3.5 text-[15px] leading-[1.4] ${{ cell: 'bg-cell text-t2', acc: 'bg-acc-soft text-on-acc-soft', keep: 'bg-keep-soft text-t1' }[tone]}`}>{children}</div>
);

/** A disc with a sign in it, over a screen's heading: done, stopped, or the computer waited for. */
const Disc = ({ tone, children }: { readonly tone: 'keep' | 'danger'; readonly children: React.ReactNode }) => (
  <span aria-hidden className={`grid h-[68px] w-[68px] place-items-center rounded-full ${tone === 'keep' ? 'bg-keep-soft text-keep' : 'bg-danger-soft text-danger-t'}`}>{children}</span>
);

const Heading = ({ children }: { readonly children: React.ReactNode }) => <h2 className="text-balance text-center text-[24px] font-bold leading-[1.2] [overflow-wrap:anywhere]">{children}</h2>;
const Note = ({ children }: { readonly children: React.ReactNode }) => <p className="text-balance text-center text-[15px] leading-[1.45] text-t2 [overflow-wrap:anywhere]">{children}</p>;

/**
 * Send to computer: what to send and whether to copy or move it, then the phone offering it and waiting for a computer,
 * the number both show, the sending, and what was sent, with deleting it from the phone afterwards.
 */
export function SendScreen({ send, reading, onChoose }: Props) {
  const { platform, tablet, words } = usePhone();
  const [asking, setAsking] = useState(false);
  const state = send.state;
  const computer = state?.computer ?? 'the computer';

  function choose() {
    const size = send.sizes[send.what];
    // What is in the library is counted once it has all been read; what was chosen, at once.
    const counting = reading && send.what !== 'chosen';
    const rows: { id: SendWhat; title: string; detail: string }[] = [
      { id: 'unsorted', title: `Everything not in ${platform === 'ios' ? 'an album' : 'a folder'} yet`, detail: reading ? 'Counting…' : `${countFiles(send.sizes.unsorted.count)} · ${formatBytes(send.sizes.unsorted.bytes)}` },
      { id: 'chosen', title: 'What you chose in Library', detail: send.chosen.length === 0 ? 'Nothing chosen yet. Tap to choose.' : `${send.chosen.length.toLocaleString()} ${send.chosen.length === 1 ? 'item' : 'items'}` },
      { id: 'everything', title: 'Everything', detail: reading ? 'Counting…' : `${countFiles(send.sizes.everything.count)} · ${formatBytes(send.sizes.everything.bytes)}` },
    ];
    const pick = (id: SendWhat) => (id === 'chosen' && send.chosen.length === 0 ? onChoose() : send.setWhat(id));
    const via = platform === 'ios' ? `The computer finds this ${tablet ? 'iPad' : 'iPhone'} on the same Wi-Fi, or over a USB cable.` : `The computer finds this ${deviceName(platform, tablet)} on the same Wi-Fi.`;
    const noun = send.what === 'chosen' ? (size.count === 1 ? 'item' : 'items') : (size.count === 1 ? 'file' : 'files');
    return (
      <>
        <div role="radiogroup" aria-label="What to send">
          <ListGroup header="What to send">
            {rows.map(r => (
              <li key={r.id}>
                <button type="button" role="radio" aria-checked={r.id === send.what} onClick={() => pick(r.id)} className="flex min-h-[60px] w-full items-center gap-3.5 px-4 py-2.5 text-left">
                  <Radio on={r.id === send.what} />
                  <span className="flex min-w-0 flex-col"><span className="text-[16px] font-semibold">{r.title}</span><span className="text-[13.5px] text-t2">{r.detail}</span></span>
                </button>
              </li>
            ))}
          </ListGroup>
        </div>
        <section aria-label="Copy or move" className="flex flex-col gap-2">
          <h3 className={platform === 'ios' ? 'px-1 text-[13px] font-semibold uppercase tracking-[.02em] text-t2' : 'px-1 text-[14px] font-medium text-acc'}>Copy or move</h3>
          <Segmented<SendMode> label="Copy or move" options={MODES} value={send.mode} onChange={send.setMode} />
          <p className="px-1 text-[14px] leading-[1.45] text-t2">
            {send.mode === 'copy'
              ? 'The photos stay on this phone. Once the computer has them all, you can delete them here.'
              : `Each photo is deleted from this phone once the computer has checked every file of it. It goes to ${words.bin}.`}
          </p>
        </section>
        <Card tone="acc"><span className="flex items-start gap-3"><span className="shrink-0 pt-px"><WifiIcon /></span>{via}</span></Card>
        {send.refused && <p role="alert" className="px-1 text-[15px] text-danger-t">{send.refused}</p>}
        <PhoneButton disabled={size.count === 0 || counting} onClick={send.offer}>
          {size.count === 0 || counting ? 'Choose what to send' : `Offer ${size.count.toLocaleString()} ${noun} to a computer`}
        </PhoneButton>
      </>
    );
  }

  function waiting(s: SendStateDto) {
    return (
      <>
        <div aria-hidden className="relative mx-auto my-2 grid h-[150px] w-[150px] place-items-center">
          <span className="send-pulse absolute inset-0 rounded-full border-2 border-acc" />
          <span className="send-pulse-late absolute inset-0 rounded-full border-2 border-acc" />
          <span className="grid h-[84px] w-[84px] place-items-center rounded-full bg-acc text-on-acc"><LaptopIcon /></span>
        </div>
        <Heading>Waiting for a computer</Heading>
        <Note>{s.phone} is offering {countFiles(s.total)}. Keep PhotoSense open on this screen.</Note>
        <ol aria-label="On the computer" className="flex flex-col overflow-hidden rounded-cell bg-cell [&>li+li]:border-t-[0.5px] [&>li+li]:border-sep">
          {['Open PhotoSense on the computer.', <>Choose <b className="font-semibold">Receive from phone</b> in its menu.</>, `Pick ${s.phone}.`].map((step, i) => (
            <li key={i} className="flex min-h-[52px] items-center gap-3.5 px-4 py-2.5 text-[16px]">
              <span aria-hidden className="grid h-6 w-6 shrink-0 place-items-center rounded-full bg-fill text-[13px] font-semibold text-t2">{i + 1}</span>
              <span className="min-w-0">{step}</span>
            </li>
          ))}
        </ol>
        <PhoneButton kind="secondary" onClick={send.stop}>Stop offering</PhoneButton>
      </>
    );
  }

  function comparing(s: SendStateDto) {
    return (
      <>
        <Heading>Does {computer} show this number?</Heading>
        <p className="mx-auto rounded-[20px] bg-cell px-7 py-4 font-code text-[44px] font-medium tabular-nums tracking-[.06em]" aria-label={`The number: ${s.code}`}>{s.code}</p>
        <Note>You compare it once for each computer. After that, everything sent between them is encrypted.</Note>
        <div className="flex flex-col gap-2.5">
          <PhoneButton disabled={send.busy} onClick={() => send.answer(true)}>It&apos;s the same number</PhoneButton>
          <PhoneButton kind="secondary" disabled={send.busy} onClick={() => send.answer(false)}>It&apos;s different</PhoneButton>
        </div>
      </>
    );
  }

  function sending(s: SendStateDto) {
    const percent = s.totalBytes > 0 ? Math.floor(s.bytes / s.totalBytes * 100) : 0;
    return (
      <>
        <div className="flex flex-col gap-3">
          <p className="text-[56px] font-bold leading-none tabular-nums">{percent}%</p>
          <div role="progressbar" aria-label={`Sending to ${computer}`} aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent} className="h-2 overflow-hidden rounded-full bg-fill">
            <div className="h-full rounded-full bg-acc transition-[width]" style={{ width: `${percent}%` }} />
          </div>
          <div className="flex flex-col gap-0.5">
            <p className="text-[15px] font-semibold tabular-nums">{s.done.toLocaleString()} of {countFiles(s.total)} · {formatBytes(s.bytes)} of {formatBytes(s.totalBytes)}</p>
            <p className="text-[14px] text-t2 [overflow-wrap:anywhere]">To {computer}{s.where ? `, in ${s.where}` : ''}</p>
            {s.current && <p className="truncate font-code text-[13px] text-t2">{s.current}</p>}
          </div>
        </div>
        <Card>Keep PhotoSense open and the screen on until it finishes. Nothing is deleted from this phone while sending.</Card>
        <PhoneButton kind="secondary" onClick={send.stop}>Stop sending</PhoneButton>
      </>
    );
  }

  function sent(s: SendStateDto) {
    const all = s.done === s.total;
    const free = s.deletable.length > 0 && send.deleted === undefined && !send.moving;
    return (
      <>
        <div className="flex flex-col items-center gap-3">
          <Disc tone="keep"><DoneIcon /></Disc>
          <Heading>{all ? `${countFiles(s.done)} ${s.done === 1 ? 'is' : 'are'} on ${computer}` : `${s.done.toLocaleString()} of ${countFiles(s.total)} are on ${computer}`}</Heading>
          <Note>{s.where ? `In ${s.where}. ` : ''}Each file was checked against the one on this phone.</Note>
        </div>
        {send.moving && <Card>Deleting them from this phone. The phone asks you first.</Card>}
        {free && (
          <Card>
            <span className="text-[16px] font-semibold text-t1">Free up space on this phone</span>
            <span>The copies on {computer} stay. Deleted photos wait in {words.bin} for 30 days.</span>
            <PhoneButton kind="danger" className="mt-2" onClick={() => setAsking(true)}>Delete {s.deletable.length.toLocaleString()} from this phone</PhoneButton>
          </Card>
        )}
        {send.deleted !== undefined && send.deleted > 0 && <Card tone="keep">Deleted from this phone. They wait in {words.bin} for 30 days.</Card>}
        <PhoneButton kind="secondary" onClick={send.done}>Done</PhoneButton>
      </>
    );
  }

  function failed(s: SendStateDto) {
    return (
      <>
        <div className="flex flex-col items-center gap-3">
          <Disc tone="danger"><CloseIcon size={28} /></Disc>
          <Heading>{s.done === 0 ? 'Stopped. Nothing was sent.' : `Stopped. ${s.done.toLocaleString()} of ${countFiles(s.total)} are on ${computer}.`}</Heading>
          <p role="alert" className="text-balance text-center text-[15px] leading-[1.45] text-t2 [overflow-wrap:anywhere]">
            {send.saidDifferent ? 'The numbers did not match. Check that you picked this phone on the computer, then try again.' : s.problem}
          </p>
        </div>
        <PhoneButton onClick={send.again}>Try again</PhoneButton>
        <PhoneButton kind="secondary" onClick={send.done}>Done</PhoneButton>
      </>
    );
  }

  const body = !state ? choose() : { waiting, comparing, sending, sent, failed }[send.screen as Exclude<typeof send.screen, 'choose'>](state);
  return (
    <Screen label="Send to computer">
      <RootHeader title="Send to computer" subtitle="Copy or move to PhotoSense on a Windows PC or Mac" />
      <div className="mx-auto flex w-full max-w-[560px] flex-col gap-5 px-4 pb-4 pt-2">{body}</div>
      {asking && state && createPortal(
        <ConfirmSheet title={state.deletable.length === 1 ? `Delete ${state.deletable[0]} from this phone?` : `Delete ${photosAndVideos(state.deletable)} from this phone?`}
          lines={[
            `${someNames(state.deletable)} ${state.deletable.length === 1 ? 'goes' : 'go'} to ${words.bin} on this phone, with any Live Photo videos and edits.`,
            `The copies on ${computer} stay${state.where ? `, in ${state.where}` : ''}.`,
            `Only files the computer has checked are deleted. They wait in ${words.bin} for 30 days.`,
          ]}
          confirmLabel={`Delete ${photosAndVideos(state.deletable)}`} busy={send.busy}
          onConfirm={() => { send.remove(); setAsking(false); }} onCancel={() => setAsking(false)} />,
        document.body,
      )}
    </Screen>
  );
}
