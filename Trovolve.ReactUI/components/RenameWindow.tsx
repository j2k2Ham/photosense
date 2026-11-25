import React, { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { renameCopies, useRenaming } from '../lib/apiClient';
import { NAMING } from '../lib/fixes';
import type { BackupPhoneDto, FileNaming } from '../types';
import { files } from '../lib/phoneOrganize';
import { Segmented } from './ModeSwitch';
import { Scrim } from './Scrim';
import type { ToastKind } from './Toaster';

interface Props {
  readonly phone: BackupPhoneDto;
  notify(message: string, kind: ToastKind): void;
  onClose(): void;
}

const said = (e: unknown) => (e instanceof Error ? e.message : String(e));

/**
 * Rename files by date (D3): how a phone's copies on this computer are named, by date and time or with the place too,
 * the first few as they are and as they would be, and the button that renames them, those backed up from then on too;
 * or, once named by date, back to the phone's names for what arrives. Only the copies here change.
 */
export function RenameWindow({ phone, notify, onClose }: Props) {
  const [style, setStyle] = useState<FileNaming>(phone.names === 'asOnPhone' ? 'date' : phone.names);
  const preview = useRenaming(style === 'asOnPhone' ? undefined : phone.id, style).data;
  const [busy, setBusy] = useState(false);
  const close = useRef(onClose);
  useLayoutEffect(() => { close.current = onClose; });
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') close.current(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  // Back to the phone's names is offered once they are named otherwise.
  const choices: FileNaming[] = phone.names === 'asOnPhone' ? ['date', 'datePlace'] : ['date', 'datePlace', 'asOnPhone'];
  const options = choices.map(value => ({ value, name: NAMING[value] }));
  const count = preview?.count ?? 0;
  const label = style === 'asOnPhone' ? 'Keep the phone’s names for new backups'
    : count > 0 ? `Rename ${count.toLocaleString()} ${count === 1 ? 'file' : 'files'}` : 'Name new backups this way';

  async function rename() {
    setBusy(true);
    try {
      const renamed = await renameCopies(phone.id, style);
      notify(style === 'asOnPhone' ? `New backups from ${phone.name} keep the phone’s names.`
        : renamed > 0 ? `Renamed ${renamed.toLocaleString()} ${renamed === 1 ? 'file' : 'files'} by date. Recently moved, in Organize, can undo it.`
          : `New backups from ${phone.name} are named by date.`, 'ok');
      close.current();
    } catch (e) {
      notify(said(e), 'error');
      setBusy(false);
    }
  }

  return (
    <Scrim className="fixed inset-0 z-[45] flex items-center justify-center bg-scrim p-3 sm:p-6" onDismiss={() => close.current()}>
      <div role="dialog" aria-modal="true" aria-label="Rename files by date" className="flex max-h-full w-[680px] max-w-full flex-col gap-4 overflow-y-auto rounded-[20px] border border-line bg-pop p-5 shadow-dialog sm:p-7">
        <div>
          <h2 className="text-[19px] font-semibold">Rename files by date</h2>
          <p className="text-[13.5px] text-t2">{files(phone.held)} from {phone.name} in {phone.folder}</p>
        </div>
        <div className="flex"><Segmented<FileNaming> label="Names" value={style} onChange={setStyle} options={options} size="medium" /></div>
        {style !== 'asOnPhone' && (
          <div className="overflow-hidden rounded-xl border border-line">
          <table className="w-full text-[13px]">
            <thead className="label-caps text-left text-t2"><tr><th className="px-4 py-2.5 font-semibold">Now</th><th className="px-4 py-2.5 font-semibold">After</th></tr></thead>
            <tbody>
              {!preview && <tr><td colSpan={2} className="border-t border-line px-4 py-3 text-t2">Reading the files…</td></tr>}
              {preview?.rows.length === 0 && <tr><td colSpan={2} className="border-t border-line px-4 py-3 text-t2">Every file is named this way already, or does not say when it was taken.</td></tr>}
              {preview?.rows.map(r => (
                <tr key={r.now} className="border-t border-line"><td className="px-4 py-2.5 font-mono text-t2">{r.now}</td><td className="px-4 py-2.5 font-mono font-semibold">{r.after}</td></tr>
              ))}
            </tbody>
          </table>
          </div>
        )}
        <p className="text-[13px] leading-[1.5] text-t2">
          {style === 'asOnPhone'
            ? 'Files backed up from now on keep the names they have on the phone. Those renamed already stay as they are.'
            : 'Only the copies on this computer are renamed, and those backed up from now on. Files on the phone keep their names. Files with the same date and time get -2, -3 at the end, and a file that does not say when it was taken keeps its name.'}
        </p>
        <div className="flex justify-end gap-2.5">
          <button type="button" onClick={() => close.current()} className="pill-outline h-10 px-5 text-[14px]">Cancel</button>
          <button type="button" disabled={busy || (style !== 'asOnPhone' && !preview)} onClick={() => void rename()} className="pill-brand h-10 px-5 text-[14px]">{label}</button>
        </div>
      </div>
    </Scrim>
  );
}
