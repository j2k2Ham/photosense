import React from 'react';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ReceiveWindow } from '../../components/ReceiveWindow';
import { photosAndVideos, SendWindow, someNames } from '../../components/SendWindow';
import { Progress, TransferWindow } from '../../components/TransferParts';
import type { FoundPhoneDto, ReceiveStateDto, SendStateDto } from '../../types';
import { deferred } from '../deferred';

const api = vi.hoisted(() => ({
  useSendState: vi.fn(), startSending: vi.fn(), confirmSending: vi.fn(), stopSending: vi.fn(), deleteSent: vi.fn(),
  usePhones: vi.fn(), useReceiveState: vi.fn(), startReceiving: vi.fn(), confirmReceiving: vi.fn(), stopReceiving: vi.fn(),
}));
vi.mock('../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../lib/apiClient')>()), ...api }));
vi.mock('../../components/FolderPicker', () => ({
  FolderPicker: ({ title, pickLabel, onPick, onCancel }: { title: string; pickLabel: string; onPick(path: string): void; onCancel(): void }) => (
    <div role="dialog" aria-label={title}>
      <button type="button" onClick={() => onPick('D:\\Pictures\\From the phone')}>{pickLabel}</button>
      <button type="button" onClick={onCancel}>Cancel picking</button>
    </div>
  ),
}));

const notify = vi.fn();
const onClose = vi.fn();
let sendState: SendStateDto | undefined;
let receiveState: ReceiveStateDto | undefined;
let phones: FoundPhoneDto[] | undefined;

const sending = (changes: Partial<SendStateDto> = {}): SendStateDto => ({
  stage: 'waiting', code: null, computer: null, done: 0, total: 2, bytes: 0, totalBytes: 2_000_000, deletable: [], where: null, phone: "Jamie's iPhone", problem: null, ...changes,
});
const receiving = (changes: Partial<ReceiveStateDto> = {}): ReceiveStateDto => ({
  stage: 'connecting', code: null, phone: "Jamie's iPhone", done: 0, total: 0, bytes: 0, totalBytes: 0, alreadyThere: 0, folder: 'C:\\Photos', problem: null, ...changes,
});
const IPHONE: FoundPhoneDto = { key: 'wifi:1', name: "Jamie's iPhone", via: 'wifi' };
const IPAD: FoundPhoneDto = { key: 'usb:3', name: 'iPhone or iPad on the cable', via: 'usb' };

beforeEach(() => {
  for (const mock of [...Object.values(api), notify, onClose]) mock.mockReset();
  sendState = sending();
  receiveState = undefined;
  phones = [IPHONE];
  api.useSendState.mockImplementation(() => ({ data: sendState }));
  api.startSending.mockImplementation(async () => sendState);
  api.confirmSending.mockImplementation(async () => sendState);
  api.stopSending.mockImplementation(async () => sendState);
  api.usePhones.mockImplementation((looking: boolean) => ({ data: looking && phones ? { phones } : undefined }));
  api.useReceiveState.mockImplementation((on: boolean) => ({ data: on ? receiveState : undefined }));
  api.startReceiving.mockImplementation(async () => receiveState);
  api.confirmReceiving.mockImplementation(async () => receiveState);
  api.stopReceiving.mockImplementation(async () => receiveState);
});

const dialog = (name: string) => screen.getByRole('dialog', { name });
const button = (name: string | RegExp) => screen.getByRole('button', { name });

describe('sending from the phone', () => {
  async function send(files: string[] = ['f1', 'f2']) {
    const view = render(<SendWindow files={files} notify={notify} onClose={onClose} />);
    await waitFor(() => expect(screen.queryByText('Getting ready…')).not.toBeInTheDocument());
    return { ...view, redraw: () => view.rerender(<SendWindow files={files} notify={notify} onClose={onClose} />) };
  }

  it('offers the chosen files at once, says how the computer finds the phone, and stops offering when closed', async () => {
    const started = deferred<SendStateDto>();
    api.startSending.mockReturnValueOnce(started.promise);
    render(<SendWindow files={['f1', 'f2']} notify={notify} onClose={onClose} />);
    expect(dialog('Send to computer')).toHaveTextContent('Getting ready…');
    expect(api.startSending).toHaveBeenCalledExactlyOnceWith(['f1', 'f2']);
    await act(async () => started.resolve(sendState!));
    expect(dialog('Send to computer')).toHaveTextContent("Waiting for your computer. On the computer, open PhotoSense and choose Receive from phone in the menu. This phone shows there as Jamie's iPhone.");
    expect(dialog('Send to computer')).toHaveTextContent('2 files are offered, with any Live Photo videos and edits.');
    await userEvent.click(button('Stop'));
    expect(api.stopSending).toHaveBeenCalledOnce();
    expect(onClose).toHaveBeenCalledOnce();
  });

  it('stops offering on Escape too, and says one file as one', async () => {
    sendState = sending({ total: 1 });
    api.stopSending.mockRejectedValue(new Error('gone'));
    await send(['f1']);
    expect(dialog('Send to computer')).toHaveTextContent('1 file is offered');
    await userEvent.keyboard('{Escape}');
    expect([api.stopSending.mock.calls.length, onClose.mock.calls.length]).toEqual([1, 1]);
  });

  it('asks whether the computer shows the same number, gives the answer, and says when it could not', async () => {
    sendState = sending({ stage: 'comparing', code: '482 917', computer: 'DESKTOP-1' });
    await send();
    expect(dialog('Send to computer')).toHaveTextContent('Does DESKTOP-1 show this number?482 917');
    await userEvent.click(button('They match'));
    expect(api.confirmSending).toHaveBeenLastCalledWith(true);
    api.confirmSending.mockRejectedValueOnce(new TypeError('Failed to fetch'));
    await userEvent.click(button('They don’t match'));
    expect(api.confirmSending).toHaveBeenLastCalledWith(false);
    expect(notify).toHaveBeenLastCalledWith('Cannot reach the PhotoSense server.', 'error');
  });

  it('shows how far sending has got, and Stop stops it', async () => {
    sendState = sending({ stage: 'sending', computer: 'DESKTOP-1', done: 1, total: 3, bytes: 1_000_000, totalBytes: 4_000_000 });
    await send();
    expect(screen.getByRole('progressbar', { name: 'Sending to DESKTOP-1' })).toHaveAttribute('aria-valuenow', '25');
    expect(dialog('Send to computer')).toHaveTextContent('1 of 3 files · 977 KB of 3.8 MB');
    await userEvent.click(button('Stop'));
    expect([api.stopSending.mock.calls.length, onClose.mock.calls.length]).toEqual([1, 1]);
  });

  it('once sent, says where the files are and deletes them from the phone only after asking, naming what goes and what stays', async () => {
    sendState = sending({ stage: 'sent', computer: 'DESKTOP-1', done: 3, total: 3, where: 'C:\\Photos\\Phone', deletable: ['IMG_0001.JPG', 'IMG_0002.MOV'] });
    api.deleteSent.mockResolvedValue({ deleted: 2 });
    await send();
    expect(dialog('Send to computer')).toHaveTextContent('3 of 3 files are on DESKTOP-1 in C:\\Photos\\Phone. They are still on this phone too.');
    await userEvent.click(button('Delete 1 photo and 1 video from this phone'));
    const asked = screen.getByRole('alertdialog', { name: 'Delete 1 photo and 1 video from this phone?' });
    expect(asked).toHaveTextContent('IMG_0001.JPG and IMG_0002.MOV will go to Recently Deleted on this phone, with any Live Photo videos and edits. The phone keeps them there for 30 days, and asks you first.');
    expect(asked).toHaveTextContent('The copies on DESKTOP-1 in C:\\Photos\\Phone stay.');
    // Escape puts the question away, not the window under it.
    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();

    await userEvent.click(button('Delete 1 photo and 1 video from this phone'));
    await userEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Delete 1 photo and 1 video' }));
    expect(api.deleteSent).toHaveBeenCalledOnce();
    expect(notify).toHaveBeenLastCalledWith('Deleted 1 photo and 1 video from this phone. They are in Recently Deleted for 30 days, and the copies on DESKTOP-1 stay.', 'ok');
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument());
    // Sent is sent: closing stops nothing.
    await userEvent.click(button('Close'));
    expect(onClose).toHaveBeenCalledOnce();
    expect(api.stopSending).not.toHaveBeenCalled();
  });

  it('names one photo by its name, says when there was nothing left, and keeps asking when the phone would not delete', async () => {
    sendState = sending({ stage: 'sent', computer: 'DESKTOP-1', done: 1, total: 1, deletable: ['IMG_0001.JPG'] });
    api.deleteSent.mockRejectedValueOnce(new Error('You chose not to delete these photos.')).mockResolvedValueOnce({ deleted: 0 }).mockResolvedValueOnce({ deleted: 1 });
    await send();
    expect(dialog('Send to computer')).toHaveTextContent('1 of 1 file is on DESKTOP-1. They are still on this phone too.');
    await userEvent.click(button('Delete 1 photo from this phone'));
    const asked = screen.getByRole('alertdialog', { name: 'Delete IMG_0001.JPG from this phone?' });
    expect(asked).toHaveTextContent('The copies on DESKTOP-1 stay.');
    await userEvent.click(within(asked).getByRole('button', { name: 'Delete 1 photo' }));
    expect(notify).toHaveBeenLastCalledWith('You chose not to delete these photos.', 'error');
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
    await userEvent.click(within(asked).getByRole('button', { name: 'Delete 1 photo' }));
    expect(notify).toHaveBeenLastCalledWith('There was nothing left to delete.', 'info');
    await userEvent.click(button('Delete 1 photo from this phone'));
    await userEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Delete 1 photo' }));
    expect(notify).toHaveBeenLastCalledWith('Deleted 1 photo from this phone. It is in Recently Deleted for 30 days, and the copies on DESKTOP-1 stay.', 'ok');
  });

  it('offers nothing to delete until something is safely on the computer, nor once it was deleted', async () => {
    sendState = sending({ stage: 'sent', computer: 'DESKTOP-1', done: 2, total: 2 });
    await send();
    expect(dialog('Send to computer')).toHaveTextContent(/^Send to computer2 of 2 files are on DESKTOP-1\.Close$/);
    expect(screen.queryByRole('button', { name: /^Delete/ })).not.toBeInTheDocument();
  });

  it('says why it could not offer the photos, or why sending failed, and tries again', async () => {
    api.startSending.mockRejectedValueOnce(new Error('None of those files is there to send any more.')).mockRejectedValueOnce('odd');
    render(<SendWindow files={['f1']} notify={notify} onClose={onClose} />);
    expect(await screen.findByRole('alert')).toHaveTextContent('None of those files is there to send any more.');
    await userEvent.click(button('Try again'));
    expect(await screen.findByRole('alert')).toHaveTextContent('odd');
    await userEvent.click(button('Try again'));
    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument());
    expect(api.startSending).toHaveBeenCalledTimes(3);
    await userEvent.click(button('Stop'));
    expect(onClose).toHaveBeenCalledOnce();
  });

  it('after a failure or a stop, says so and offers the files again', async () => {
    sendState = sending({ stage: 'failed', problem: 'The other device stopped answering.' });
    const { redraw } = await send();
    expect(screen.getByRole('alert')).toHaveTextContent('The other device stopped answering.');
    await userEvent.click(button('Try again'));
    expect(api.startSending).toHaveBeenCalledTimes(2);
    sendState = sending({ stage: 'idle' });
    redraw();
    expect(dialog('Send to computer')).toHaveTextContent('This phone is not offering photos now.');
    await userEvent.click(button('Offer them again'));
    expect(api.startSending).toHaveBeenCalledTimes(3);
    await userEvent.click(button('Close'));
    expect(api.stopSending).not.toHaveBeenCalled();
    sendState = sending({ stage: 'failed', problem: 'x' });
    redraw();
    await userEvent.click(button('Close'));
    expect(onClose).toHaveBeenCalledTimes(2);
  });

  it('counts photos and videos by their names, and names a few of them', () => {
    expect([[], ['a.jpg'], ['a.jpg', 'b.HEIC'], ['a.MOV'], ['a.mov', 'b.MP4', 'c.jpg']].map(photosAndVideos)).toEqual(['', '1 photo', '2 photos', '1 video', '1 photo and 2 videos']);
    expect([[], ['a'], ['a', 'b'], ['a', 'b', 'c'], ['a', 'b', 'c', 'd', 'e']].map(someNames)).toEqual(['', 'a', 'a and b', 'a, b and c', 'a, b, c and 2 more']);
  });
});

describe('receiving on the computer', () => {
  function receive(folder?: string) {
    return render(<ReceiveWindow folder={folder} notify={notify} onClose={onClose} />);
  }
  const window_ = () => dialog('Receive from phone');

  it('chooses the only phone found, receives into the folder given, and stops when closed while it connects', async () => {
    receiveState = receiving();
    receive('C:\\Photos');
    expect(screen.getByRole('radio', { name: /Jamie's iPhone/ })).toBeChecked();
    expect(window_()).toHaveTextContent('Wi-Fi');
    await userEvent.click(button('Receive into Photos'));
    expect(api.startReceiving).toHaveBeenCalledExactlyOnceWith('wifi:1', 'C:\\Photos');
    expect(window_()).toHaveTextContent("Connecting to Jamie's iPhone…");
    expect(api.usePhones).toHaveBeenLastCalledWith(false);
    api.stopReceiving.mockRejectedValue(new Error('gone'));
    await userEvent.keyboard('{Escape}');
    expect([api.stopReceiving.mock.calls.length, onClose.mock.calls.length]).toEqual([1, 1]);
  });

  it('says while it looks for phones, and how to make a phone offer photos when none does', () => {
    phones = undefined;
    const { rerender } = receive('C:\\Photos');
    expect(window_()).toHaveTextContent('Looking for phones…');
    phones = [];
    rerender(<ReceiveWindow folder="C:\\Photos" notify={notify} onClose={onClose} />);
    expect(window_()).toHaveTextContent('No phone is offering photos yet.On the phone, open PhotoSense, select photos in Organize and choose Send to computer.');
    expect(button('Receive into Photos')).toBeDisabled();
  });

  it('with several phones, waits for one to be chosen, and says which is on a cable', async () => {
    phones = [IPHONE, IPAD];
    receiveState = receiving();
    receive('C:\\Photos');
    expect(button('Receive into Photos')).toBeDisabled();
    expect(within(screen.getByRole('radio', { name: /on the cable/ }).closest('label')!).getByText('Cable')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('radio', { name: /on the cable/ }));
    await userEvent.click(button('Receive into Photos'));
    expect(api.startReceiving).toHaveBeenCalledExactlyOnceWith('usb:3', 'C:\\Photos');
  });

  it('has a folder chosen first when none is known, and lets it be changed, keeping its keys for the picker meanwhile', async () => {
    receive('');
    expect(button('Receive photos')).toBeDisabled();
    await userEvent.click(button('Choose a folder'));
    await userEvent.keyboard('{Escape}');
    expect(onClose).not.toHaveBeenCalled();
    await userEvent.click(button('Put them here'));
    expect(window_()).toHaveTextContent('D:\\Pictures\\From the phone');
    await userEvent.click(button('Change'));
    await userEvent.click(button('Cancel picking'));
    expect(button('Receive into From the phone')).toBeEnabled();
    await userEvent.click(button('Cancel'));
    expect(onClose).toHaveBeenCalledOnce();
    expect(api.stopReceiving).not.toHaveBeenCalled();
  });

  it('compares numbers with the phone, follows the files in, and says what arrived and what was there already', async () => {
    receiveState = receiving({ stage: 'comparing', code: '482 917' });
    const { rerender } = receive('C:\\Photos');
    const redraw = () => rerender(<ReceiveWindow folder="C:\\Photos" notify={notify} onClose={onClose} />);
    await userEvent.click(button('Receive into Photos'));
    expect(window_()).toHaveTextContent("Does Jamie's iPhone show this number?482 917");
    await userEvent.click(button('They match'));
    expect(api.confirmReceiving).toHaveBeenLastCalledWith(true);
    api.confirmReceiving.mockRejectedValueOnce(new Error('No answer'));
    await userEvent.click(button('They don’t match'));
    expect(notify).toHaveBeenLastCalledWith('No answer', 'error');

    receiveState = receiving({ stage: 'receiving', done: 1, total: 2, bytes: 0, totalBytes: 0 });
    redraw();
    expect(window_()).toHaveTextContent("Receiving from Jamie's iPhone into C:\\Photos.");
    expect(screen.getByRole('progressbar', { name: "Receiving from Jamie's iPhone" })).toHaveAttribute('aria-valuenow', '0');

    receiveState = receiving({ stage: 'done', done: 3, total: 3, alreadyThere: 1 });
    redraw();
    expect(window_()).toHaveTextContent('2 files arrived in C:\\Photos. 1 file was there already, so it was not copied again.');
    expect(window_()).toHaveTextContent('On the phone, PhotoSense now offers to delete what arrived.');
    receiveState = receiving({ stage: 'done', done: 2, total: 2, alreadyThere: 2 });
    redraw();
    expect(window_()).toHaveTextContent('Nothing new arrived in C:\\Photos. 2 files were there already, so they were not copied again.');
    receiveState = receiving({ stage: 'done', done: 1, total: 1 });
    redraw();
    expect(window_()).toHaveTextContent('1 file arrived in C:\\Photos.On the phone');
    await userEvent.click(button('Receive more'));
    expect(button('Receive into Photos')).toBeInTheDocument();

    await userEvent.click(button('Receive into Photos'));
    await userEvent.click(button('Close'));
    expect(onClose).toHaveBeenCalledOnce();
    expect(api.stopReceiving).not.toHaveBeenCalled();
  });

  it('says why it could not start, or why receiving failed, and tries again', async () => {
    api.startReceiving.mockRejectedValueOnce(new Error('That phone is no longer offering photos. Look for it again.'));
    const { rerender } = receive('C:\\Photos');
    await userEvent.click(button('Receive into Photos'));
    expect(screen.getByRole('alert')).toHaveTextContent('That phone is no longer offering photos. Look for it again.');

    receiveState = receiving({ stage: 'failed', problem: 'The phone is already sending to another computer.' });
    await userEvent.click(button('Receive into Photos'));
    expect(screen.getByRole('alert')).toHaveTextContent('The phone is already sending to another computer.');
    await userEvent.click(button('Try again'));
    expect(button('Receive into Photos')).toBeEnabled();

    await userEvent.click(button('Receive into Photos'));
    await userEvent.click(button('Close'));
    expect(api.stopReceiving).not.toHaveBeenCalled();

    // Started, and not heard of yet: it connects to the phone, whatever it is called.
    receiveState = undefined;
    rerender(<ReceiveWindow folder="C:\\Photos" notify={notify} onClose={onClose} />);
    expect(window_()).toHaveTextContent('Connecting to the phone…');
    await userEvent.click(button('Stop'));
    expect(api.stopReceiving).not.toHaveBeenCalled();
    receiveState = receiving({ stage: 'receiving', phone: null, done: 0, total: 1, totalBytes: 10 });
    rerender(<ReceiveWindow folder="C:\\Photos" notify={notify} onClose={onClose} />);
    await userEvent.click(button('Stop'));
    expect(api.stopReceiving).toHaveBeenCalledOnce();
  });
});

describe('the window both happen in', () => {
  it('closes on a press outside it, and shows no progress for nothing', () => {
    const close = vi.fn();
    render(<TransferWindow title="Send to computer" frozen={false} onClose={close}><Progress label="Sending" done={0} total={1} bytes={0} totalBytes={0} /></TransferWindow>);
    expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '0');
    expect(screen.getByText('0 of 1 file · 1 KB of 1 KB')).toBeInTheDocument();
    fireEvent.mouseDown(screen.getByRole('dialog'));
    fireEvent.keyDown(window, { key: 'Enter' });
    expect(close).not.toHaveBeenCalled();
    fireEvent.mouseDown(screen.getByRole('dialog').parentElement!);
    expect(close).toHaveBeenCalledOnce();
  });
});
