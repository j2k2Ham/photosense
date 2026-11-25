import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { FixesScreen, FixesTile } from '../../../components/phone/Fixes';
import type { Platform } from '../../../lib/phone';
import type { ComputerNamesDto, FixesDto, FixGroupDto } from '../../../types';
import { photo, plan } from '../../fixtures';
import { frame, gate, inFrame } from './frame';

const api = vi.hoisted(() => ({ useFixes: vi.fn(), fixDates: vi.fn(), addPlaces: vi.fn(), undoFix: vi.fn(), renameOnComputer: vi.fn() }));
vi.mock('../../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../../lib/apiClient')>()), ...api }));

const group = (more: Partial<FixGroupDto> = {}): FixGroupDto => ({ count: 0, photos: [], date: null, place: null, placeCount: 0, undo: 0, ...more });
const names = (more: Partial<ComputerNamesDto> = {}): ComputerNamesDto => ({
  computer: 'Office PC', files: 6941, style: 'asOnPhone', now: 'IMG_4821.HEIC', after: '2026-03-02 14.31.07.HEIC', stage: null, problem: null, renamed: null, ...more,
});
const fixes = (more: Partial<FixesDto> = {}): FixesDto => ({
  total: 374,
  dates: group({ count: 32, date: '2000-01-01', photos: [photo({ id: 'p1' }), photo({ id: 'v1', isVideo: true })] }),
  places: group({ count: 342, place: 'Glacier National Park', placeCount: 214 }),
  names: names(),
  ...more,
});

let answer: { data?: FixesDto; error?: unknown };
beforeEach(() => {
  answer = { data: fixes() };
  for (const mock of Object.values(api)) mock.mockReset();
  api.useFixes.mockImplementation(() => answer);
  api.fixDates.mockResolvedValue(32);
  api.addPlaces.mockResolvedValue(342);
  api.undoFix.mockResolvedValue(32);
  api.renameOnComputer.mockResolvedValue(undefined);
});

function show(platform: Platform = 'ios', tier: 'free' | 'pro' = 'pro') {
  const notify = vi.fn(), onClose = vi.fn();
  const offered = gate({ plan: plan({ tier }), offer: vi.fn((_reason: string, then?: () => void) => then?.()) });
  const view = inFrame(<FixesScreen notify={notify} onClose={onClose} />, frame(platform), offered);
  return { notify, onClose, offered, again: () => view.again(<FixesScreen notify={notify} onClose={onClose} />), layer: () => screen.getByRole('dialog', { name: 'Fix dates and places' }) };
}

const card = (name: string) => screen.getByRole('region', { name });
const lastAction = (notify: ReturnType<typeof vi.fn>) => (notify.mock.calls.at(-1)![2] as { run(): void }).run;

describe("Library's Needs fixing tile", () => {
  it('says how many need a date or a place, or that it is looking, or that all are fixed, and opens them', async () => {
    const onOpen = vi.fn();
    const { again } = inFrame(<FixesTile fixes={undefined} onOpen={onOpen} />);
    expect(screen.getByRole('button', { name: 'Needs fixing, Looking…' })).toBeInTheDocument();
    again(<FixesTile fixes={fixes({ total: 0 })} onOpen={onOpen} />);
    expect(screen.getByRole('button', { name: 'Needs fixing, All fixed' })).toBeInTheDocument();
    again(<FixesTile fixes={fixes()} onOpen={onOpen} />);
    await userEvent.click(screen.getByRole('button', { name: 'Needs fixing, 374 items' }));
    expect(onOpen).toHaveBeenCalledOnce();
  });
});

describe('Fix dates and places (32)', () => {
  it('says while it looks, what went wrong, and that all is fixed; Back closes it', async () => {
    answer = { data: undefined };
    const { layer, again, onClose } = show();
    expect(layer()).toHaveTextContent('Looking at dates and places…');
    answer = { error: new Error('No.') };
    again();
    expect(within(layer()).getByRole('alert')).toHaveTextContent('What needs fixing could not be found: No.');
    answer = { error: 'gone' };
    again();
    expect(within(layer()).getByRole('alert')).toHaveTextContent('could not be found: gone');
    answer = { data: fixes({ total: 0, dates: group(), places: group(), names: null }) };
    again();
    expect(layer()).toHaveTextContent('All fixedDates and places look right.');
    expect(within(layer()).queryByRole('region')).not.toBeInTheDocument();
    await userEvent.click(within(layer()).getByRole('button', { name: 'Back' }));
    expect(onClose).toHaveBeenCalledOnce();
  });

  it('on the free plan, offers Pro first, then fixes the dates to the day chosen, with Undo', async () => {
    const { notify, offered } = show('ios', 'free');
    const dates = card('Camera clock not set');
    expect(dates).toHaveTextContent('32 items dated Jan 1, 2000');
    expect(dates).toHaveTextContent('Jan 1, 2000');
    expect(dates).toHaveTextContent('Each keeps its time of day, and your iPhone asks before they change.');
    const fix = within(dates).getByRole('button', { name: 'Fix 32 dates PRO' });
    expect(fix).toBeDisabled();
    await userEvent.type(within(dates).getByLabelText('The date they were taken'), '2026-03-02');
    await userEvent.click(fix);
    expect(offered.offer).toHaveBeenCalledWith('fix', expect.any(Function));
    expect(api.fixDates).toHaveBeenCalledWith('2026-03-02');
    expect(notify).toHaveBeenCalledWith('32 dates fixed.', 'ok', expect.objectContaining({ label: 'Undo' }));
    lastAction(notify)();
    await vi.waitFor(() => expect(notify).toHaveBeenLastCalledWith('Put back 32 dates.', 'ok'));
    expect(api.undoFix).toHaveBeenCalledWith('dates');
  });

  it('gives places from the nearest photo, with Undo, and says why the phone did not', async () => {
    const { notify } = show('android');
    const places = card('Missing location');
    expect(places).toHaveTextContent('342 items with no place');
    expect(places).toHaveTextContent('No location→Place of the nearest photo');
    expect(places).toHaveTextContent('214 of them get Glacier National Park.');
    expect(card('Camera clock not set')).toHaveTextContent('your phone asks');
    await userEvent.click(within(places).getByRole('button', { name: 'Add 342 locations' }));
    expect(notify).toHaveBeenCalledWith('342 locations added.', 'ok', expect.objectContaining({ label: 'Undo' }));
    api.undoFix.mockResolvedValueOnce(1);
    lastAction(notify)();
    await vi.waitFor(() => expect(notify).toHaveBeenLastCalledWith('Took back 1 location.', 'ok'));
    api.addPlaces.mockRejectedValueOnce(new Error('You chose not to let Trovolve change these photos.'));
    await userEvent.click(within(places).getByRole('button', { name: 'Add 342 locations' }));
    expect(notify).toHaveBeenLastCalledWith('You chose not to let Trovolve change these photos.', 'error');
  });

  it('says one of each in the singular, and once fixed offers Undo on the card', async () => {
    answer = { data: fixes({ total: 2, dates: group({ count: 1, date: '1970-01-01', undo: 1 }), places: group({ count: 1, place: 'Butte', placeCount: 1, undo: 1 }) }) };
    api.fixDates.mockResolvedValue(1);
    api.addPlaces.mockResolvedValue(1);
    api.undoFix.mockResolvedValue(1);
    const { notify } = show();
    await userEvent.type(within(card('Camera clock not set')).getByLabelText('The date they were taken'), '2026-03-02');
    await userEvent.click(within(card('Camera clock not set')).getByRole('button', { name: 'Fix 1 date' }));
    expect(notify).toHaveBeenLastCalledWith('1 date fixed.', 'ok', expect.anything());
    await userEvent.click(within(card('Missing location')).getByRole('button', { name: 'Add 1 location' }));
    expect(notify).toHaveBeenLastCalledWith('1 location added.', 'ok', expect.anything());
    expect(card('Missing location')).toHaveTextContent('It gets Butte.');
    await userEvent.click(within(card('Camera clock not set')).getByRole('button', { name: 'Undo' }));
    expect(notify).toHaveBeenLastCalledWith('Put back 1 date.', 'ok');
    expect(card('Camera clock not set')).toHaveTextContent('1 date fixed');
    expect(card('Missing location')).toHaveTextContent('1 location added');
  });

  it('after a fix with nothing left, says each kind looks right and keeps Undo', async () => {
    answer = { data: fixes({ total: 0, dates: group({ undo: 3 }), places: group({ undo: 5 }), names: null }) };
    show();
    expect(card('Camera clock not set')).toHaveTextContent('Every date looks right.3 dates fixedUndo');
    expect(card('Missing location')).toHaveTextContent('Every photo that can have a place has one.5 locations addedUndo');
    await userEvent.click(within(card('Missing location')).getByRole('button', { name: 'Undo' }));
    expect(api.undoFix).toHaveBeenCalledWith('places');
  });
});

describe("The copies' names on the computer", () => {
  it('shows a name now and by date, and asks the computer to rename them, Pro first', async () => {
    const { notify, offered, again } = show('ios', 'free');
    const files = card('File names on Office PC');
    expect(files).toHaveTextContent('6,941 files named like IMG_4821');
    expect(files).toHaveTextContent('IMG_4821.HEIC→2026-03-02 14.31.07.HEIC');
    expect(files).toHaveTextContent('Files on this iPhone keep their names.');
    await userEvent.click(within(files).getByRole('button', { name: 'Rename 6,941 files PRO' }));
    expect(offered.offer).toHaveBeenCalledWith('fix', expect.any(Function));
    expect(api.renameOnComputer).toHaveBeenCalledWith('date');

    answer = { data: fixes({ names: names({ stage: 'running' }) }) };
    again();
    expect(within(card('File names on Office PC')).getByRole('button', { name: 'Renaming… PRO' })).toBeDisabled();
    answer = { data: fixes({ names: names({ stage: 'ready', style: 'date', renamed: 6941 }) }) };
    again();
    expect(notify).toHaveBeenCalledWith('Renamed 6,941 files on Office PC.', 'ok');
    expect(card('File names on Office PC')).toHaveTextContent('6,941 files renamedTo undo it, open Organize on Office PC and choose Recently moved.');
  });

  it('says why the computer did not rename them, one file in the singular, and when they are named by date already', () => {
    answer = { data: fixes({ names: names({ stage: 'running', files: 1, now: null }) }) };
    const { notify, again } = show();
    expect(card('File names on Office PC')).toHaveTextContent('1 file');
    expect(card('File names on Office PC')).not.toHaveTextContent('→');
    answer = { data: fixes({ names: names({ stage: 'failed', problem: 'The disk is full.', files: 1, now: null }) }) };
    again();
    expect(notify).toHaveBeenCalledWith('The disk is full.', 'error');
    expect(within(card('File names on Office PC')).getByRole('button', { name: 'Rename 1 file' })).toBeEnabled();
    answer = { data: fixes({ names: names({ files: 0 }) }) };
    again();
    expect(within(card('File names on Office PC')).getByRole('button', { name: 'Rename 0 files' })).toBeDisabled();
    answer = { data: fixes({ names: names({ style: 'datePlace', renamed: 1 }) }) };
    again();
    expect(card('File names on Office PC')).toHaveTextContent('1 file renamed');
    answer = { data: fixes({ names: names({ style: 'date' }) }) };
    again();
    expect(card('File names on Office PC')).toHaveTextContent('Named by date');
    answer = { data: fixes({ names: names({ stage: 'running', style: 'date' }) }) };
    again();
    answer = { data: fixes({ names: names({ stage: 'ready', style: 'date', renamed: 1 }) }) };
    again();
    expect(notify).toHaveBeenLastCalledWith('Renamed 1 file on Office PC.', 'ok');
  });
});
