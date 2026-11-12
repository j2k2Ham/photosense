import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { describe, expect, it, vi } from 'vitest';
import { SendScreen } from '../../../components/phone/Send';
import type { Platform } from '../../../lib/phone';
import type { PhoneSend } from '../../../lib/send';
import type { SendStateDto } from '../../../types';
import { frame, inFrame } from './frame';

const GB = 2 ** 30;
const state = (more: Partial<SendStateDto> = {}): SendStateDto => ({
  stage: 'waiting', code: null, computer: 'JAMIE-LAPTOP', done: 0, total: 4212, bytes: 0, totalBytes: Math.round(18.4 * GB), deletable: [], where: null, phone: "Jamie's iPhone", problem: null, current: null, ...more,
});

/** Send as PhoneApp holds it, its actions recorded. */
function sending(more: Partial<PhoneSend> = {}): PhoneSend {
  return {
    what: 'unsorted', mode: 'copy', chosen: [], screen: 'choose', saidDifferent: false, busy: false, moving: false,
    sizes: { unsorted: { count: 4212, bytes: Math.round(18.4 * GB) }, chosen: { count: 0, bytes: 0 }, everything: { count: 6941, bytes: Math.round(31.2 * GB) } },
    setWhat: vi.fn(), setMode: vi.fn(), choose: vi.fn(), offer: vi.fn(), again: vi.fn(), answer: vi.fn(), stop: vi.fn(), remove: vi.fn(), done: vi.fn(),
    ...more,
  };
}

function show(send = sending(), { reading = false, platform = 'ios' as Platform, more = {} } = {}) {
  const onChoose = vi.fn();
  const view = inFrame(<SendScreen send={send} reading={reading} onChoose={onChoose} />, frame(platform, more));
  return { ...view, send, onChoose };
}
const main = () => screen.getByRole('region', { name: 'Send to computer' });
const button = (name: string | RegExp) => screen.getByRole('button', { name });
const at = (stage: SendStateDto['stage'], more: Partial<SendStateDto> = {}, send: Partial<PhoneSend> = {}) => sending({ screen: stage as PhoneSend['screen'], state: state({ stage, ...more }), ...send });

describe('choosing what to send', () => {
  it('offers what is in no album yet, what was chosen in Library, or everything, to copy or move, and says how the computer finds the phone', async () => {
    const { send, onChoose } = show();
    expect(screen.getByRole('heading', { level: 1, name: 'Send to computer' })).toBeInTheDocument();
    const what = screen.getByRole('radiogroup', { name: 'What to send' });
    expect(within(what).getAllByRole('radio').map(r => [r.textContent, r.getAttribute('aria-checked')])).toEqual([
      ['Everything not in an album yet4,212 files · 18.40 GB', 'true'], ['What you chose in LibraryNothing chosen yet. Tap to choose.', 'false'], ['Everything6,941 files · 31.20 GB', 'false'],
    ]);
    await userEvent.click(within(what).getByRole('radio', { name: /^Everything6,941/ }));
    expect(send.setWhat).toHaveBeenCalledWith('everything');
    // Nothing chosen yet: Library opens to choose.
    await userEvent.click(within(what).getByRole('radio', { name: /^What you chose/ }));
    expect([onChoose.mock.calls.length, vi.mocked(send.setWhat).mock.calls.length]).toEqual([1, 1]);
    expect(main()).toHaveTextContent('The photos stay on this phone. Once the computer has them all, you can delete them here.');
    expect(main()).toHaveTextContent('The computer finds this iPhone on the same Wi-Fi, or over a USB cable.');
    await userEvent.click(within(screen.getByRole('radiogroup', { name: 'Copy or move' })).getByRole('radio', { name: 'Move' }));
    expect(send.setMode).toHaveBeenCalledWith('move');
    await userEvent.click(button('Offer 4,212 files to a computer'));
    expect(send.offer).toHaveBeenCalledOnce();
  });

  it('with some chosen in Library offers those, says Move deletes into the bin, and says why an offer could not be made', async () => {
    const send = sending({ what: 'chosen', mode: 'move', chosen: ['a', 'b', 'c'], sizes: { ...sending().sizes, chosen: { count: 3, bytes: 0 } }, refused: 'None of those files is there to send any more.' });
    show(send, { platform: 'android' });
    const what = screen.getByRole('radiogroup', { name: 'What to send' });
    expect(within(what).getAllByRole('radio').map(r => r.textContent)).toEqual(['Everything not in a folder yet4,212 files · 18.40 GB', 'What you chose in Library3 items', 'Everything6,941 files · 31.20 GB']);
    await userEvent.click(within(what).getByRole('radio', { name: /^What you chose/ }));
    expect(send.setWhat).toHaveBeenCalledWith('chosen');
    expect(main()).toHaveTextContent('Each photo is deleted from this phone once the computer has checked every file of it. It goes to the bin.');
    expect(main()).toHaveTextContent('The computer finds this phone on the same Wi-Fi.');
    expect(screen.getByRole('alert')).toHaveTextContent('None of those files is there to send any more.');
    expect(button('Offer 3 items to a computer')).toBeEnabled();
  });

  it('counts one as one, and offers nothing while the library is counted or when there is nothing to send', () => {
    const one = sending({ what: 'chosen', chosen: ['a'], sizes: { unsorted: { count: 1, bytes: 1000 }, chosen: { count: 1, bytes: 0 }, everything: { count: 0, bytes: 0 } } });
    const { again } = show(one, { more: { tablet: true, form: 'tablet' } });
    expect(screen.getByRole('radio', { name: /^What you chose/ })).toHaveTextContent('1 item');
    expect(screen.getByRole('radio', { name: /^Everything not/ })).toHaveTextContent('1 file · 1 KB');
    expect(button('Offer 1 item to a computer')).toBeEnabled();
    expect(main()).toHaveTextContent('The computer finds this iPad on the same Wi-Fi, or over a USB cable.');

    again(<SendScreen send={{ ...one, what: 'unsorted' }} reading onChoose={vi.fn()} />, frame('android', { tablet: true, form: 'tablet' }));
    expect(screen.getByRole('radio', { name: /^Everything not/ })).toHaveTextContent('Counting…');
    expect(button('Choose what to send')).toBeDisabled();
    expect(main()).toHaveTextContent('The computer finds this tablet on the same Wi-Fi.');
    // What was chosen is known while the rest is still counted.
    again(<SendScreen send={one} reading onChoose={vi.fn()} />);
    expect(button('Offer 1 item to a computer')).toBeEnabled();
    again(<SendScreen send={{ ...one, what: 'everything' }} reading={false} onChoose={vi.fn()} />);
    expect(button('Choose what to send')).toBeDisabled();
  });
});

describe('the offer', () => {
  it('waits for a computer, saying what to do on it, and stops offering', async () => {
    const { send } = show(at('waiting'));
    expect(screen.getByRole('heading', { level: 2, name: 'Waiting for a computer' })).toBeInTheDocument();
    expect(main()).toHaveTextContent("Jamie's iPhone is offering 4,212 files. Keep PhotoSense open on this screen.");
    expect(within(screen.getByRole('list', { name: 'On the computer' })).getAllByRole('listitem').map(li => li.textContent)).toEqual([
      '1Open PhotoSense on the computer.', '2Choose Receive from phone in its menu.', "3Pick Jamie's iPhone.",
    ]);
    await userEvent.click(button('Stop offering'));
    expect(send.stop).toHaveBeenCalledOnce();
  });

  it('asks whether the computer shows the same number, and holds the answers while one is given', async () => {
    const { send, again } = show(at('comparing', { code: '482 913' }));
    expect(screen.getByRole('heading', { level: 2, name: 'Does JAMIE-LAPTOP show this number?' })).toBeInTheDocument();
    expect(screen.getByLabelText('The number: 482 913')).toHaveTextContent('482 913');
    expect(main()).toHaveTextContent('You compare it once for each computer. After that, everything sent between them is encrypted.');
    await userEvent.click(button("It's the same number"));
    await userEvent.click(button("It's different"));
    expect(vi.mocked(send.answer).mock.calls).toEqual([[true], [false]]);
    again(<SendScreen send={at('comparing', { code: '482 913', computer: null }, { busy: true })} reading={false} onChoose={vi.fn()} />);
    expect(screen.getByRole('heading', { level: 2, name: 'Does the computer show this number?' })).toBeInTheDocument();
    expect(button("It's the same number")).toBeDisabled();
    expect(button("It's different")).toBeDisabled();
  });

  it('shows how far sending has got, where the files go, the file going now, and stops', async () => {
    const { send, again } = show(at('sending', { done: 1938, bytes: Math.round(8.5 * GB), where: 'D:\\Phone Pictures', current: 'IMG_9080.MOV' }));
    expect(main()).toHaveTextContent('46%');
    expect(screen.getByRole('progressbar', { name: 'Sending to JAMIE-LAPTOP' })).toHaveAttribute('aria-valuenow', '46');
    expect(main()).toHaveTextContent('1,938 of 4,212 files · 8.50 GB of 18.40 GB');
    expect(main()).toHaveTextContent('To JAMIE-LAPTOP, in D:\\Phone Pictures');
    expect(screen.getByText('IMG_9080.MOV')).toHaveClass('font-code');
    expect(main()).toHaveTextContent('Keep PhotoSense open and the screen on until it finishes. Nothing is deleted from this phone while sending.');
    await userEvent.click(button('Stop sending'));
    expect(send.stop).toHaveBeenCalledOnce();
    again(<SendScreen send={at('sending', { total: 0, totalBytes: 0 })} reading={false} onChoose={vi.fn()} />);
    expect(main()).toHaveTextContent('0%');
    expect(main()).toHaveTextContent('To JAMIE-LAPTOP');
    expect(main()).not.toHaveTextContent('IMG_9080.MOV');
  });
});

describe('what was sent', () => {
  it('says the files are on the computer, checked, and with Copy deletes them from the phone after a question naming what goes and what stays', async () => {
    const { send } = show(at('sent', { done: 4212, where: 'D:\\Phone Pictures', deletable: ['IMG_1.HEIC', 'IMG_2.HEIC', 'IMG_3.MOV', 'IMG_4.JPG'] }));
    expect(screen.getByRole('heading', { level: 2, name: '4,212 files are on JAMIE-LAPTOP' })).toBeInTheDocument();
    expect(main()).toHaveTextContent('In D:\\Phone Pictures. Each file was checked against the one on this phone.');
    expect(main()).toHaveTextContent('Free up space on this phoneThe copies on JAMIE-LAPTOP stay. Deleted photos wait in Recently Deleted for 30 days.');
    await userEvent.click(button('Delete 4 from this phone'));
    const question = screen.getByRole('dialog', { name: 'Delete 3 photos and 1 video from this phone?' });
    expect(question).toHaveTextContent('IMG_1.HEIC, IMG_2.HEIC, IMG_3.MOV and 1 more go to Recently Deleted on this phone, with any Live Photo videos and edits.');
    expect(question).toHaveTextContent('The copies on JAMIE-LAPTOP stay, in D:\\Phone Pictures.');
    expect(question).toHaveTextContent('Only files the computer has checked are deleted. They wait in Recently Deleted for 30 days.');
    await userEvent.click(within(question).getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await userEvent.click(button('Delete 4 from this phone'));
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete 3 photos and 1 video' }));
    expect(send.remove).toHaveBeenCalledOnce();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await userEvent.click(button('Done'));
    expect(send.done).toHaveBeenCalledOnce();
  });

  it('names one going by its name, and says where the copies are when they are in several folders', async () => {
    show(at('sent', { done: 1, total: 1, deletable: ['IMG_1.HEIC'] }), { platform: 'android' });
    expect(screen.getByRole('heading', { level: 2, name: '1 file is on JAMIE-LAPTOP' })).toBeInTheDocument();
    expect(main()).toHaveTextContent('Each file was checked against the one on this phone.');
    await userEvent.click(button('Delete 1 from this phone'));
    const question = screen.getByRole('dialog', { name: 'Delete IMG_1.HEIC from this phone?' });
    expect(within(question).getByRole('button', { name: 'Delete 1 photo' })).toBeInTheDocument();
    expect(question).toHaveTextContent('IMG_1.HEIC goes to the bin on this phone, with any Live Photo videos and edits.');
    expect(question).toHaveTextContent('The copies on JAMIE-LAPTOP stay.');
  });

  it('says some were sent when not all were, and while moving or once deleted offers no deleting', () => {
    const { again } = show(at('sent', { done: 3, total: 4, computer: null, deletable: ['IMG_1.HEIC'] }, { moving: true }));
    expect(screen.getByRole('heading', { level: 2, name: '3 of 4 files are on the computer' })).toBeInTheDocument();
    expect(main()).toHaveTextContent('Deleting them from this phone. The phone asks you first.');
    expect(screen.queryByRole('button', { name: /from this phone$/ })).not.toBeInTheDocument();
    again(<SendScreen send={at('sent', { done: 4, total: 4, deletable: ['IMG_1.HEIC'] }, { deleted: 1 })} reading={false} onChoose={vi.fn()} />);
    expect(main()).toHaveTextContent('Deleted from this phone. They wait in Recently Deleted for 30 days.');
    expect(screen.queryByRole('button', { name: /from this phone$/ })).not.toBeInTheDocument();
    again(<SendScreen send={at('sent', { done: 4, total: 4, deletable: ['IMG_1.HEIC'] }, { deleted: 0 })} reading={false} onChoose={vi.fn()} />);
    expect(main()).not.toHaveTextContent('Deleted from this phone.');
    again(<SendScreen send={at('sent', { done: 4, total: 4 })} reading={false} onChoose={vi.fn()} />);
    expect(main()).not.toHaveTextContent('Free up space');
  });
});

describe('an offer that stopped', () => {
  it('says nothing was sent and why, the numbers when they were said to be different, and tries again', async () => {
    const { send, again } = show(at('failed', { problem: 'The other device stopped answering. Try again.' }));
    expect(screen.getByRole('heading', { level: 2, name: 'Stopped. Nothing was sent.' })).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('The other device stopped answering. Try again.');
    await userEvent.click(button('Try again'));
    await userEvent.click(button('Done'));
    expect([vi.mocked(send.again).mock.calls.length, vi.mocked(send.done).mock.calls.length]).toEqual([1, 1]);
    again(<SendScreen send={at('failed', { problem: 'The numbers did not match, so nothing was sent.' }, { saidDifferent: true })} reading={false} onChoose={vi.fn()} />);
    expect(screen.getByRole('alert')).toHaveTextContent('The numbers did not match. Check that you picked this phone on the computer, then try again.');
    again(<SendScreen send={at('failed', { done: 12, total: 40, problem: 'Sending stopped. Try again.' })} reading={false} onChoose={vi.fn()} />);
    expect(screen.getByRole('heading', { level: 2, name: 'Stopped. 12 of 40 files are on JAMIE-LAPTOP.' })).toBeInTheDocument();
  });
});
