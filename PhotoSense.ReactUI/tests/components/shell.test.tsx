import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { act, fireEvent, render, renderHook, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import RootLayout, { metadata } from '../../app/layout';
import { AppMenu } from '../../components/AppMenu';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { ErrorsPanel } from '../../components/ErrorsPanel';
import { MatchChip } from '../../components/MatchChip';
import { Toaster, TOAST_MS, useToasts } from '../../components/Toaster';
import { TopBar } from '../../components/TopBar';

vi.mock('next/font/google', () => ({ DM_Sans: () => ({ variable: 'font-sans-var' }), DM_Mono: () => ({ variable: 'font-mono-var' }) }));

describe('the page frame', () => {
  it('carries the fonts and applies a saved light theme before anything is painted', () => {
    const html = renderToStaticMarkup(<RootLayout><p>the page itself</p></RootLayout>);
    expect(html).toMatch(/^<html lang="en" class="font-sans-var font-mono-var">/);
    expect(html).toContain("localStorage.getItem('photosense-theme')==='light'");
    expect(html.indexOf('<script>')).toBeLessThan(html.indexOf('the page itself'));
    expect(metadata.title).toBe('PhotoSense');
  });
});

describe('TopBar', () => {
  function show(props: Partial<React.ComponentProps<typeof TopBar>> = {}) {
    const handlers = { onChangeFolders: vi.fn(), onToggleErrors: vi.fn(), onToggleMenu: vi.fn() };
    render(<TopBar showScan={false} folders={[]} errorCount={0} menuOpen={false} {...handlers} {...props} />);
    return handlers;
  }

  it('shows the name, that nothing is uploaded, and the menu', async () => {
    const { onToggleMenu } = show();
    expect(screen.getByRole('heading', { name: 'PhotoSense' })).toBeInTheDocument();
    expect(screen.getByText('Runs on this computer. Nothing is uploaded.')).toBeInTheDocument();
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Scanned folders')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /error/ })).not.toBeInTheDocument();

    const menu = screen.getByRole('button', { name: 'Menu' });
    expect(menu).toHaveAttribute('aria-expanded', 'false');
    await userEvent.click(menu);
    expect(onToggleMenu).toHaveBeenCalledOnce();
  });

  it('names the scanned folders by their last part, with how much was scanned and when', async () => {
    const { onChangeFolders } = show({ showScan: true, folders: ['C:\\Users\\jamie\\Phone Pictures', "C:\\Users\\jamie\\Pictures\\Jamie's Phone"], files: 6941, scanned: '2026-10-07T16:25:00' });
    const chip = screen.getByLabelText('Scanned folders');
    expect(chip).toHaveTextContent(/^Phone Pictures \+ Jamie's Phone · 6,941 files · scanned /);
    expect(within(chip).getByText('Phone Pictures')).toHaveAttribute('title', 'C:\\Users\\jamie\\Phone Pictures');

    await userEvent.click(screen.getByRole('button', { name: 'Change or rescan' }));
    expect(onChangeFolders).toHaveBeenCalledOnce();
  });

  it('shows what it knows when the folders or the time are not known', () => {
    show({ showScan: true, files: 1 });
    expect(screen.getByLabelText('Scanned folders')).toHaveTextContent(/^1 file$/);
  });

  it('shows the chip even before the count has arrived', () => {
    show({ showScan: true, folders: ['C:\\photos'], menuOpen: true });
    expect(screen.getByLabelText('Scanned folders')).toHaveTextContent(/^photos$/);
    expect(screen.getByRole('button', { name: 'Menu' })).toHaveAttribute('aria-expanded', 'true');
  });

  it.each([[1, '1 error'], [3, '3 errors']])('counts the errors waiting to be read', async (count, label) => {
    const { onToggleErrors } = show({ errorCount: count });
    await userEvent.click(screen.getByRole('button', { name: label }));
    expect(onToggleErrors).toHaveBeenCalledOnce();
  });
});

describe('AppMenu', () => {
  function show(theme: 'dark' | 'light' = 'dark') {
    const handlers = { onTheme: vi.fn(), onErrors: vi.fn(), onChangeFolders: vi.fn(), onClearResults: vi.fn(), onClose: vi.fn() };
    render(<div><span>elsewhere</span><AppMenu theme={theme} errorCount={2} {...handlers} /></div>);
    return handlers;
  }

  it('switches the theme without closing', async () => {
    const { onTheme, onClose } = show();
    expect(screen.getByRole('button', { name: 'Dark' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: 'Light' })).toHaveAttribute('aria-pressed', 'false');
    await userEvent.click(screen.getByRole('button', { name: 'Light' }));
    expect(onTheme).toHaveBeenCalledExactlyOnceWith('light');
    expect(onClose).not.toHaveBeenCalled();
  });

  it('marks light as chosen when it is', () => {
    show('light');
    expect(screen.getByRole('button', { name: 'Light' })).toHaveAttribute('aria-pressed', 'true');
  });

  it.each([
    [/^Errors/, 'onErrors'], ['Change folders or rescan', 'onChangeFolders'], ['Clear results', 'onClearResults'],
  ] as const)('does what is chosen and closes', async (name, handler) => {
    const handlers = show();
    await userEvent.click(screen.getByRole('menuitem', { name }));
    expect(handlers[handler]).toHaveBeenCalledOnce();
    expect(handlers.onClose).toHaveBeenCalledOnce();
  });

  it('shows the error count and where removed files are', () => {
    show();
    expect(screen.getByRole('menuitem', { name: /^Errors/ })).toHaveTextContent('Errors2');
    expect(screen.getByRole('menu')).toHaveTextContent('Removed files are in _PhotoSense_Removed inside each scanned folder.');
  });

  it('closes on Escape and on a press outside, and on nothing else', () => {
    const { onClose } = show();
    fireEvent.keyDown(window, { key: 'Enter' });
    fireEvent.mouseDown(screen.getByRole('menu'));
    expect(onClose).not.toHaveBeenCalled();
    fireEvent.keyDown(window, { key: 'Escape' });
    fireEvent.mouseDown(screen.getByText('elsewhere'));
    expect(onClose).toHaveBeenCalledTimes(2);
  });
});

describe('ErrorsPanel', () => {
  const errors = [{ id: 2, time: new Date(2026, 9, 7, 16, 25, 5), message: 'Folder not found: C:\\Users\\jamie\\Phone Pics' }, { id: 1, time: new Date(2026, 9, 7, 16, 20), message: 'Cannot reach the PhotoSense server.' }];

  it('lists each problem with its time until it is cleared', async () => {
    const onClear = vi.fn(), onClearAll = vi.fn();
    render(<ErrorsPanel errors={errors} onClear={onClear} onClearAll={onClearAll} onClose={() => undefined} />);
    const rows = within(screen.getByRole('dialog', { name: 'Errors' })).getAllByRole('listitem');
    expect(rows[0]).toHaveTextContent('Folder not found: C:\\Users\\jamie\\Phone Pics');
    expect(rows[0]).toHaveTextContent(errors[0].time.toLocaleTimeString());

    await userEvent.click(within(rows[1]).getByRole('button', { name: 'Clear' }));
    expect(onClear).toHaveBeenCalledExactlyOnceWith(1);
    await userEvent.click(screen.getByRole('button', { name: 'Clear all' }));
    expect(onClearAll).toHaveBeenCalledOnce();
  });

  it('says so when there are none, and closes on Escape', () => {
    const onClose = vi.fn();
    const { unmount } = render(<ErrorsPanel errors={[]} onClear={() => undefined} onClearAll={() => undefined} onClose={onClose} />);
    expect(screen.getByText('No errors. Problems stay listed here until you clear them.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Clear all' })).not.toBeInTheDocument();
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onClose).toHaveBeenCalledOnce();
    unmount();
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onClose).toHaveBeenCalledOnce();
  });
});

describe('MatchChip', () => {
  it.each([
    ['identical', false, 'Identical file', 'text-ident'], ['samePicture', false, 'Same picture', 'text-same'],
    ['similar', false, 'Similar', 'text-amber'], ['identical', true, 'Keeping', 'text-keep'],
  ] as const)('labels a %s match (kept: %s) as %s', (match, kept, label, tone) => {
    render(<MatchChip match={match} kept={kept} />);
    expect(screen.getByText(label)).toHaveClass(tone);
  });

  it('takes a copy as not kept unless told', () => {
    render(<MatchChip match="similar" />);
    expect(screen.getByText('Similar')).toBeInTheDocument();
  });
});

describe('ConfirmDialog', () => {
  const show = (props: Partial<React.ComponentProps<typeof ConfirmDialog>> = {}) => {
    const onConfirm = vi.fn(), onCancel = vi.fn();
    const view = render(<ConfirmDialog title="Delete all duplicates?" confirmLabel="Delete 3 files" busy={false} onConfirm={onConfirm} onCancel={onCancel} {...props}><p>3 files will be removed.</p></ConfirmDialog>);
    return { onConfirm, onCancel, ...view };
  };

  it('asks its question and reports the answer', async () => {
    const { onConfirm, onCancel } = show({ note: 'Files go to a holding folder.' });
    const dialog = screen.getByRole('alertdialog', { name: 'Delete all duplicates?' });
    expect(dialog).toHaveTextContent('3 files will be removed.');
    expect(dialog).toHaveTextContent('Files go to a holding folder.');
    expect(screen.getByRole('button', { name: 'Delete 3 files' })).toHaveClass('pill-rose');

    await userEvent.click(screen.getByRole('button', { name: 'Delete 3 files' }));
    expect(onConfirm).toHaveBeenCalledOnce();
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onCancel).toHaveBeenCalledOnce();
  });

  it('is dismissed by Escape and by a press on the backdrop, and by nothing else', () => {
    const { onCancel, container, unmount } = show({ tone: 'brand' });
    expect(screen.getByRole('button', { name: 'Delete 3 files' })).toHaveClass('pill-brand');
    fireEvent.keyDown(window, { key: 'Enter' });
    fireEvent.mouseDown(screen.getByRole('alertdialog'));
    expect(onCancel).not.toHaveBeenCalled();
    fireEvent.keyDown(window, { key: 'Escape' });
    fireEvent.mouseDown(container.firstElementChild!);
    expect(onCancel).toHaveBeenCalledTimes(2);
    unmount();
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onCancel).toHaveBeenCalledTimes(2);
  });

  it('cannot be answered or dismissed while the work is under way', () => {
    const { onCancel, container } = show({ busy: true });
    expect(screen.getByRole('button', { name: 'Working…' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
    fireEvent.keyDown(window, { key: 'Escape' });
    fireEvent.mouseDown(container.firstElementChild!);
    expect(onCancel).not.toHaveBeenCalled();
  });
});

describe('messages', () => {
  it('go after four and a half seconds, except errors, which stay and are kept in a list', () => {
    vi.useFakeTimers();
    const { result } = renderHook(() => useToasts());

    act(() => {
      result.current.push('Scan started');
      result.current.push('Moved 3 duplicates', 'ok');
      result.current.push('Cannot reach the PhotoSense server.', 'error');
    });
    expect(result.current.toasts.map(t => [t.message, t.kind])).toEqual([['Scan started', 'info'], ['Moved 3 duplicates', 'ok'], ['Cannot reach the PhotoSense server.', 'error']]);
    expect(result.current.errors.map(e => e.message)).toEqual(['Cannot reach the PhotoSense server.']);

    act(() => { vi.advanceTimersByTime(TOAST_MS - 1); });
    expect(result.current.toasts).toHaveLength(3);
    act(() => { vi.advanceTimersByTime(1); });
    expect(result.current.toasts.map(t => t.message)).toEqual(['Cannot reach the PhotoSense server.']);

    // Dismissing the toast leaves the error on the list; clearing the list is separate.
    act(() => result.current.remove(result.current.toasts[0].id));
    expect(result.current.toasts).toEqual([]);
    expect(result.current.errors).toHaveLength(1);
    act(() => result.current.push('Folder not found: X', 'error'));
    expect(result.current.errors.map(e => e.message)).toEqual(['Folder not found: X', 'Cannot reach the PhotoSense server.']);
    act(() => result.current.clearError(result.current.errors[1].id));
    expect(result.current.errors.map(e => e.message)).toEqual(['Folder not found: X']);
    act(() => result.current.clearErrors());
    expect(result.current.errors).toEqual([]);
  });

  it('show as pills, the newest four, with errors marked as kept', async () => {
    const remove = vi.fn();
    const toasts = [1, 2, 3, 4, 5].map(id => ({ id, message: `message ${id}`, kind: 'info' as const }));
    render(<Toaster remove={remove} toasts={[...toasts, { id: 6, message: 'done', kind: 'ok' }, { id: 7, message: 'failed', kind: 'error' }]} />);

    expect(screen.queryByText('message 3')).not.toBeInTheDocument();
    expect(screen.getAllByRole('status').map(s => s.textContent)).toEqual(['message 4×', 'message 5×', 'done×']);
    const error = screen.getByRole('alert');
    expect(error).toHaveTextContent('failedKept in Errors');

    await userEvent.click(within(error).getByRole('button', { name: 'Dismiss' }));
    expect(remove).toHaveBeenCalledExactlyOnceWith(7);
  });
});
