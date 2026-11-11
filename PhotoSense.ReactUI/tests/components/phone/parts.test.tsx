import { act, fireEvent, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { describe, expect, it, vi } from 'vitest';
import { Chip, HeaderTextButton, IconButton, ListGroup, ListRow, PhoneButton, Segmented, StickyFooter } from '../../../components/phone/Controls';
import * as Icons from '../../../components/phone/Icons';
import { PhoneToaster } from '../../../components/phone/PhoneToaster';
import { MonthHeader, PhotoGrid, SelectionBar } from '../../../components/phone/PhotoGrid';
import { ConfirmSheet, Sheet } from '../../../components/phone/Sheet';
import type { Toast } from '../../../components/Toaster';
import { frame, inFrame } from './frame';

describe('the phone layout\'s buttons and controls', () => {
  it('draws a button for each thing it does, its label on one line, faint when it cannot be pressed', async () => {
    const pressed = vi.fn();
    inFrame(<>
      <PhoneButton onClick={pressed}>Continue</PhoneButton>
      <PhoneButton kind="danger" onClick={pressed}>Delete IMG_4299.HEIC</PhoneButton>
      <PhoneButton kind="secondary" className="mt-2" onClick={pressed}>Cancel</PhoneButton>
      <PhoneButton kind="text" disabled onClick={pressed}>Keep this copy too</PhoneButton>
      <StickyFooter><span>The foot</span></StickyFooter>
    </>);
    expect(screen.getByRole('button', { name: 'Continue' })).toHaveClass('bg-acc', 'text-on-acc');
    expect(screen.getByRole('button', { name: 'Delete IMG_4299.HEIC' })).toHaveClass('bg-danger');
    expect(screen.getByRole('button', { name: 'Cancel' })).toHaveClass('bg-btn2-bg', 'mt-2');
    expect(screen.getByRole('button', { name: 'Keep this copy too' })).toBeDisabled();
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));
    expect(pressed).toHaveBeenCalledOnce();
    expect(screen.getByText('The foot').parentElement).toHaveClass('sticky');
  });

  it('chooses one of a few side by side, as a sliding pill on an iPhone and outlined segments on Android', async () => {
    const chosen = vi.fn();
    const options = [{ id: 'copy', label: 'Copy' }, { id: 'move', label: 'Move' }, { id: 'both', label: 'Both' }] as const;
    const { again } = inFrame(<Segmented<'copy' | 'move' | 'both'> label="Copy or move" options={options} value="move" onChange={chosen} />);
    const group = screen.getByRole('radiogroup', { name: 'Copy or move' });
    expect(group).toHaveClass('bg-seg-bg');
    expect(within(group).getByRole('radio', { name: 'Move' })).toHaveAttribute('aria-checked', 'true');
    expect(within(group).getByRole('radio', { name: 'Move' })).toHaveClass('shadow-seg', 'bg-seg-on');
    await userEvent.click(within(group).getByRole('radio', { name: 'Copy' }));
    expect(chosen).toHaveBeenCalledWith('copy');

    again(<Segmented<'copy' | 'move' | 'both'> label="Copy or move" options={options} value="copy" onChange={chosen} />, frame('android'));
    const outlined = screen.getByRole('radiogroup', { name: 'Copy or move' });
    expect(outlined).toHaveClass('border-seg-bd');
    expect(within(outlined).getByRole('radio', { name: 'Copy' })).toHaveClass('rounded-l-full');
    expect(within(outlined).getByRole('radio', { name: 'Move' })).toHaveClass('border-l');
    expect(within(outlined).getByRole('radio', { name: 'Both' })).toHaveClass('rounded-r-full');
  });

  it('shows a filter with its count, pressed when it is the one in use', async () => {
    const tapped = vi.fn();
    inFrame(<><Chip label="All" count="6,941" on onClick={tapped} /><Chip label="Videos" on={false} onClick={tapped} /><Chip label="No location" count="381" on={false} onClick={tapped} /></>);
    expect(screen.getByRole('button', { name: 'All 6,941' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByText('381')).toHaveClass('text-t2');
    expect(screen.getByRole('button', { name: 'Videos' })).toHaveAttribute('aria-pressed', 'false');
    await userEvent.click(screen.getByRole('button', { name: 'Videos' }));
    expect(tapped).toHaveBeenCalledOnce();
  });

  it('groups rows in one panel on an iPhone and a row each on Android, in a sheet too', async () => {
    const go = vi.fn();
    const rows = (inSheet?: boolean) => (
      <ListGroup header="Removed files" trailing="37" inSheet={inSheet}>
        <ListRow detail="212 MB" chevron onClick={go}>PhotoSense Removed</ListRow>
        <ListRow danger disabled onClick={go}>Delete permanently…</ListRow>
        <ListRow>Read only</ListRow>
      </ListGroup>
    );
    const { again } = inFrame(rows());
    const group = screen.getByRole('region', { name: 'Removed files' });
    expect(within(group).getByRole('heading', { name: 'Removed files' })).toHaveClass('uppercase');
    expect(within(group).getByText('37')).toBeInTheDocument();
    expect(within(group).getByRole('list')).toHaveClass('bg-cell', 'rounded-cell');
    await userEvent.click(within(group).getByRole('button', { name: /PhotoSense Removed/ }));
    expect(go).toHaveBeenCalledOnce();
    expect(within(group).getByRole('button', { name: 'Delete permanently…' })).toBeDisabled();
    expect(within(group).getByText('Delete permanently…')).toHaveClass('text-danger-t');
    expect(within(group).getByText('Read only').closest('div')).not.toBeNull();

    again(rows(true));
    expect(screen.getByRole('list')).toHaveClass('bg-sheet-cell');
    again(rows(), frame('android'));
    expect(screen.getByRole('heading', { name: 'Removed files' })).toHaveClass('text-acc');
    expect(screen.getByRole('list')).toHaveClass('[&>li]:bg-cell');
    again(rows(true), frame('android'));
    expect(screen.getByRole('list')).toHaveClass('[&>li]:bg-sheet-cell');
    again(<ListGroup><ListRow>Alone</ListRow></ListGroup>);
    expect(screen.queryByRole('heading')).not.toBeInTheDocument();
  });

  it('draws a header\'s buttons as glass on an iPhone and plain or tonal on Android', async () => {
    const pressed = vi.fn();
    const buttons = <><IconButton label="Map or list" onClick={pressed}><Icons.MapIcon /></IconButton><HeaderTextButton onClick={pressed}>Select</HeaderTextButton></>;
    const { again } = inFrame(buttons);
    expect(screen.getByRole('button', { name: 'Map or list' })).toHaveClass('bg-ib-bg', 'backdrop-blur-[12px]');
    expect(screen.getByRole('button', { name: 'Select' })).toHaveClass('h-11');
    await userEvent.click(screen.getByRole('button', { name: 'Select' }));
    expect(pressed).toHaveBeenCalledOnce();
    again(buttons, frame('android'));
    expect(screen.getByRole('button', { name: 'Map or list' })).not.toHaveClass('bg-ib-bg');
    expect(screen.getByRole('button', { name: 'Select' })).toHaveClass('h-10');
  });

  it('draws every icon as a line, out of the way of a screen reader', () => {
    const { container } = inFrame(<>{Object.values(Icons).map((Icon, i) => <Icon key={i} />)}<Icons.LibraryIcon size={22} stroke={2} /></>);
    const drawn = container.querySelectorAll('svg');
    expect(drawn).toHaveLength(Object.keys(Icons).length + 1);
    drawn.forEach(svg => expect(svg).toHaveAttribute('aria-hidden', 'true'));
    expect(drawn[drawn.length - 1]).toHaveAttribute('width', '22');
  });
});

describe('a sheet', () => {
  it('rises from the bottom of a phone with a grabber, and goes on Done, Escape or a press outside it', async () => {
    const closed = vi.fn();
    inFrame(<Sheet title="Settings" onClose={closed}><p>Inside</p></Sheet>);
    const sheet = screen.getByRole('dialog', { name: 'Settings' });
    expect(sheet).toHaveClass('rounded-t-sheet', 'max-h-[calc(100%-56px)]');
    expect(within(sheet).getByRole('heading', { name: 'Settings' })).toBeInTheDocument();
    await userEvent.click(within(sheet).getByRole('button', { name: 'Done' }));
    fireEvent.keyDown(window, { key: 'Escape' });
    fireEvent.keyDown(window, { key: 'Enter' });
    fireEvent.mouseDown(sheet.parentElement!);
    expect(closed).toHaveBeenCalledTimes(3);
  });

  it('is a card in the middle of a tablet, and Android\'s is a little taller', () => {
    const { again } = inFrame(<Sheet title="Name taken" done="Close" onClose={vi.fn()}>x</Sheet>, frame('ios', { tablet: true, form: 'tablet' }));
    expect(screen.getByRole('dialog', { name: 'Name taken' })).toHaveClass('w-[560px]');
    expect(screen.getByRole('button', { name: 'Close' })).toBeInTheDocument();
    again(<Sheet title="Name taken" onClose={vi.fn()}>x</Sheet>, frame('android'));
    expect(screen.getByRole('dialog', { name: 'Name taken' })).toHaveClass('max-h-[calc(100%-24px)]');
  });

  it('closes on Back, through the page\'s history', async () => {
    const closed = vi.fn();
    inFrame(<Sheet title="Settings" onClose={closed}>x</Sheet>);
    await act(async () => { history.back(); await new Promise(r => setTimeout(r, 20)); });
    expect(closed).toHaveBeenCalledOnce();
  });

  it('asks a question with what goes and what stays, the button that does it, and Cancel', async () => {
    const yes = vi.fn(), no = vi.fn();
    const { again } = inFrame(<ConfirmSheet title="Delete IMG_4299.HEIC?" lines={['IMG_4299.JPG stays, in Photo Library.', 'IMG_4299.HEIC goes to PhotoSense Removed.']} confirmLabel="Delete IMG_4299.HEIC" onConfirm={yes} onCancel={no} />);
    const sheet = screen.getByRole('dialog', { name: 'Delete IMG_4299.HEIC?' });
    expect(within(sheet).queryByRole('button', { name: 'Done' })).not.toBeInTheDocument();
    expect(within(sheet).getByText('IMG_4299.JPG stays, in Photo Library.')).toBeInTheDocument();
    expect(within(sheet).getByRole('button', { name: 'Delete IMG_4299.HEIC' })).toHaveClass('bg-danger');
    await userEvent.click(within(sheet).getByRole('button', { name: 'Delete IMG_4299.HEIC' }));
    await userEvent.click(within(sheet).getByRole('button', { name: 'Cancel' }));
    expect([yes.mock.calls.length, no.mock.calls.length]).toEqual([1, 1]);
    again(<ConfirmSheet title="Delete 4,212 from this phone?" lines={[]} confirmLabel="Delete 4,212" kind="primary" busy onConfirm={yes} onCancel={no}><p>More</p></ConfirmSheet>);
    expect(screen.getByRole('button', { name: 'Delete 4,212' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Delete 4,212' })).toHaveClass('bg-acc');
    expect(screen.getByText('More')).toBeInTheDocument();
  });
});

describe('the phone\'s messages', () => {
  const toasts: Toast[] = [
    { id: 1, message: 'Older', kind: 'ok' },
    { id: 2, message: 'IMG_4299.HEIC deleted. IMG_4299.JPG stays.', kind: 'ok', action: { label: 'Undo', run: vi.fn() } },
  ];

  it('show the newest above the tab bar, with what can be done about it', async () => {
    const remove = vi.fn();
    inFrame(<PhoneToaster toasts={toasts} remove={remove} />);
    const log = screen.getByRole('log', { name: 'Messages' });
    expect(log).toHaveClass('bottom-[98px]');
    expect(within(log).queryByText('Older')).not.toBeInTheDocument();
    await userEvent.click(within(log).getByRole('button', { name: 'Undo' }));
    expect(remove).toHaveBeenCalledWith(2);
    expect(toasts[1].action!.run).toHaveBeenCalledOnce();
  });

  it('stand clear of each foot, and an error stays until put away', async () => {
    const remove = vi.fn();
    const error: Toast[] = [{ id: 3, message: 'The disk is full.', kind: 'error' }];
    const { again } = inFrame(<PhoneToaster toasts={error} remove={remove} />, frame('android'));
    expect(screen.getByRole('log')).toHaveClass('bottom-[92px]');
    expect(screen.getByText('Error:')).toHaveClass('sr-only');
    await userEvent.click(screen.getByRole('button', { name: 'OK' }));
    expect(remove).toHaveBeenCalledWith(3);
    again(<PhoneToaster toasts={[]} remove={remove} />, frame('ios', { foot: 'none' }));
    expect(screen.getByRole('log')).toHaveClass('bottom-[calc(var(--safe-b)+112px)]');
    expect(screen.getByRole('log')).toBeEmptyDOMElement();
    again(<PhoneToaster toasts={toasts} remove={remove} />, frame('ios', { tablet: true }));
    expect(screen.getByRole('log')).toHaveClass('bottom-7');
    expect(screen.getByRole('log').firstElementChild).toHaveClass('backdrop-blur-[20px]');
  });
});

describe('the photo grid', () => {
  const items = [
    { id: 'a', name: 'IMG_1.HEIC', thumbnail: '/t/a' },
    { id: 'b', name: 'IMG_2.MOV', thumbnail: '/t/b', duration: '0:42' },
  ];

  it('shows three across on a phone, opens what is tapped, and says how long a video plays', async () => {
    const tapped = vi.fn();
    const { container } = inFrame(<><MonthHeader month="June 2024" count="2 items" /><PhotoGrid items={items} onTap={tapped} /></>);
    expect(container.querySelector('ul')).toHaveClass('grid-cols-3');
    expect(screen.getByRole('heading', { name: 'June 2024' })).toBeInTheDocument();
    expect(screen.getByText('2 items')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'IMG_1.HEIC' })).not.toHaveAttribute('aria-pressed');
    expect(within(screen.getByRole('button', { name: 'IMG_2.MOV' })).getByText('0:42')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'IMG_1.HEIC' }));
    expect(tapped).toHaveBeenCalledWith('a');
  });

  it('while choosing, marks what is chosen: a filled circle on an iPhone, a smaller tile on Android', () => {
    const chosen = new Set(['b']);
    const { container, again } = inFrame(<PhotoGrid items={items} selecting selected={chosen} onTap={vi.fn()} />);
    expect(screen.getByRole('button', { name: 'IMG_2.MOV' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: 'IMG_1.HEIC' })).toHaveAttribute('aria-pressed', 'false');
    expect(container.querySelector('.bg-acc')).toHaveClass('bottom-1.5', 'right-1.5');
    expect(container.querySelectorAll('img')[1]).toHaveClass('h-full');
    again(<PhotoGrid items={items} selecting selected={chosen} onTap={vi.fn()} />, frame('android'));
    expect(container.querySelector('.bg-acc')).toHaveClass('left-1.5', 'top-1.5');
    expect(container.querySelectorAll('img')[1]).toHaveClass('rounded-[14px]');
  });

  it('shows six across on an iPad, eight with its sidebar hidden, and eight on an Android tablet', () => {
    const { container, again } = inFrame(<PhotoGrid items={items} onTap={vi.fn()} />, frame('ios', { tablet: true }));
    expect(container.querySelector('ul')).toHaveClass('grid-cols-6');
    again(<PhotoGrid items={items} onTap={vi.fn()} />, frame('ios', { tablet: true, sidebarHidden: true }));
    expect(container.querySelector('ul')).toHaveClass('grid-cols-8');
    again(<PhotoGrid items={items} onTap={vi.fn()} />, frame('android', { tablet: true }));
    expect(container.querySelector('ul')).toHaveClass('grid-cols-8');
  });
});

describe('the selection bar', () => {
  it('offers Send, the platform\'s word for putting files somewhere, and Delete, faint while nothing is chosen', async () => {
    const send = vi.fn(), put = vi.fn(), del = vi.fn();
    const { again } = inFrame(<SelectionBar count={0} onSend={send} onPut={put} onDelete={del} />);
    const bar = screen.getByRole('toolbar', { name: 'What is selected' });
    expect(bar).toHaveClass('inset-x-4', 'rounded-full');
    expect(within(bar).getByRole('button', { name: 'Add to album' })).toBeDisabled();
    again(<SelectionBar count={3} onSend={send} onPut={put} onDelete={del} />, frame('android'));
    const across = screen.getByRole('toolbar');
    expect(across).toHaveClass('inset-x-0');
    await userEvent.click(within(across).getByRole('button', { name: 'Send' }));
    await userEvent.click(within(across).getByRole('button', { name: 'Move to folder' }));
    await userEvent.click(within(across).getByRole('button', { name: 'Delete' }));
    expect([send, put, del].map(f => f.mock.calls.length)).toEqual([1, 1, 1]);
    again(<SelectionBar count={3} onSend={send} onPut={put} onDelete={del} />, frame('ios', { tablet: true }));
    expect(screen.getByRole('toolbar')).toHaveClass('bottom-7', 'mx-auto');
  });
});
