import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PairWindow } from '../../components/PairWindow';
import type { PairingStateDto } from '../../types';

const api = vi.hoisted(() => ({ usePairing: vi.fn(), openPairing: vi.fn(), closePairing: vi.fn(), confirmPairing: vi.fn(), nameComputer: vi.fn() }));
vi.mock('../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../lib/apiClient')>()), ...api }));

let state: PairingStateDto | undefined;
const at = (more: Partial<PairingStateDto>): PairingStateDto => ({ stage: 'waiting', link: 'trovolve:pair?v=1', code: '482913', expires: null, check: null, other: null, folder: null, problem: null, ...more });

beforeEach(() => {
  for (const mock of Object.values(api)) mock.mockReset();
  state = at({});
  api.usePairing.mockImplementation(() => ({ data: state }));
  api.openPairing.mockResolvedValue(state);
  api.closePairing.mockResolvedValue(undefined);
  api.confirmPairing.mockResolvedValue(state);
  api.nameComputer.mockResolvedValue(state);
});

const window = () => screen.getByRole('dialog', { name: 'Pair a phone' });

describe('Pair a phone, on the computer', () => {
  it('starts waiting as it opens, and shows the QR code, the code to type, how, and that they change', () => {
    render(<PairWindow notify={vi.fn()} onClose={vi.fn()} />);
    expect(api.openPairing).toHaveBeenCalledOnce();
    expect(api.usePairing).toHaveBeenCalledWith(true);
    const qr = within(window()).getByRole('img', { name: 'QR code for the phone to scan' });
    expect(qr.querySelector('path')!.getAttribute('d')).toMatch(/^M\d+ \d+h1v1h-1z/);
    expect(within(window()).getAllByRole('listitem').map(li => li.textContent)).toEqual([
      '1Install Trovolve from the App Store or Google Play.', '2On the phone, choose Connect your computer.', '3Point the phone at this code.',
    ]);
    expect(within(window()).getByLabelText('Code to type on the phone')).toHaveTextContent('482 913');
    expect(window()).toHaveTextContent('The phone and this computer need to be on the same Wi-Fi. The code changes every 5 minutes.');
  });

  it('shows a dot until the code is known, and says when it could not start', async () => {
    const notify = vi.fn();
    state = undefined;
    api.openPairing.mockRejectedValueOnce(new Error('The port is taken.'));
    render(<PairWindow notify={notify} onClose={vi.fn()} />);
    expect(within(window()).getByLabelText('Code to type on the phone')).toHaveTextContent('…');
    expect(within(window()).queryByRole('img')).not.toBeInTheDocument();
    await vi.waitFor(() => expect(notify).toHaveBeenCalledWith('Pair a phone could not start: The port is taken.', 'error'));
  });

  it('asks whether the phone shows the same number, and passes the answer on', async () => {
    const notify = vi.fn();
    state = at({ stage: 'comparing', other: "Jamie's iPhone", check: '271 004' });
    render(<PairWindow notify={notify} onClose={vi.fn()} />);
    expect(window()).toHaveTextContent("Does Jamie's iPhone show this number? Pair only if it does.");
    expect(within(window()).getByLabelText('The number')).toHaveTextContent('271 004');
    await userEvent.click(screen.getByRole('button', { name: "It's the same number" }));
    await userEvent.click(screen.getByRole('button', { name: "It's different" }));
    expect(api.confirmPairing.mock.calls).toEqual([[true], [false]]);
    api.confirmPairing.mockRejectedValueOnce(new Error('Gone.'));
    await userEvent.click(screen.getByRole('button', { name: "It's different" }));
    expect(notify).toHaveBeenCalledWith('Gone.', 'error');
  });

  it('says a phone was paired, and Done closes and stops the waiting', async () => {
    const onClose = vi.fn();
    state = at({ stage: 'paired', other: "Jamie's iPhone" });
    render(<PairWindow notify={vi.fn()} onClose={onClose} />);
    expect(screen.getByRole('status')).toHaveTextContent("Jamie's iPhone is paired with this computer.");
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Done' }));
    expect(api.closePairing).toHaveBeenCalledOnce();
    expect(onClose).toHaveBeenCalledOnce();
  });

  it('says why pairing stopped, and Try again starts afresh', async () => {
    const notify = vi.fn();
    state = at({ stage: 'failed', problem: 'The numbers did not match.' });
    render(<PairWindow notify={notify} onClose={vi.fn()} />);
    expect(screen.getByRole('alert')).toHaveTextContent('The numbers did not match.');
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));
    expect(api.openPairing).toHaveBeenCalledTimes(2);
    api.openPairing.mockRejectedValueOnce('no');
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));
    expect(notify).toHaveBeenCalledWith('no', 'error');
  });

  it('closes by its button, Escape or a click outside, stopping the waiting each time, quietly when that fails', async () => {
    const onClose = vi.fn();
    api.closePairing.mockRejectedValue(new Error('Gone.'));
    render(<PairWindow notify={vi.fn()} onClose={onClose} />);
    await userEvent.click(screen.getByRole('button', { name: 'Close' }));
    await userEvent.keyboard('a');
    await userEvent.keyboard('{Escape}');
    fireEvent.mouseDown(window().parentElement!);
    expect(onClose).toHaveBeenCalledTimes(3);
    expect(api.closePairing).toHaveBeenCalledTimes(3);
  });
});

describe('Naming the computer, in Pair a phone', () => {
  it('says what phones will call it, and Rename names it as the person types, Enter or Save keeping it', async () => {
    state = at({ name: 'DESKTOP-4K2J9' });
    const notify = vi.fn();
    render(<PairWindow notify={notify} onClose={vi.fn()} />);
    expect(window()).toHaveTextContent('Phones will call this computer DESKTOP-4K2J9.');
    await userEvent.click(within(window()).getByRole('button', { name: 'Rename…' }));
    const field = within(window()).getByRole('textbox', { name: 'Name this computer' });
    expect(field).toHaveValue('DESKTOP-4K2J9');
    await userEvent.clear(field);
    await userEvent.type(field, "Jamie's laptop{Enter}");
    expect(api.nameComputer).toHaveBeenCalledWith("Jamie's laptop");
    expect(within(window()).queryByRole('textbox')).not.toBeInTheDocument();

    await userEvent.click(within(window()).getByRole('button', { name: 'Rename…' }));
    await userEvent.click(within(window()).getByRole('button', { name: 'Cancel' }));
    expect(within(window()).queryByRole('textbox')).not.toBeInTheDocument();

    api.nameComputer.mockRejectedValueOnce(new Error('Give this computer a name of 1 to 40 letters.'));
    await userEvent.click(within(window()).getByRole('button', { name: 'Rename…' }));
    await userEvent.clear(within(window()).getByRole('textbox', { name: 'Name this computer' }));
    await userEvent.click(within(window()).getByRole('button', { name: 'Save' }));
    expect(notify).toHaveBeenCalledWith('Give this computer a name of 1 to 40 letters.', 'error');
    expect(within(window()).getByRole('textbox', { name: 'Name this computer' })).toBeInTheDocument();
  });

  it('offers no Rename until the name is known', () => {
    state = at({ name: null });
    render(<PairWindow notify={vi.fn()} onClose={vi.fn()} />);
    expect(window()).toHaveTextContent('Phones will call this computer ….');
    expect(within(window()).getByRole('button', { name: 'Rename…' })).toBeDisabled();
  });
});
