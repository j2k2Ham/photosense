import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SettingsPanel } from '../../components/SettingsPanel';
import { deferred } from '../fakeSignalR';

const api = vi.hoisted(() => ({ startScan: vi.fn() }));
vi.mock('../../lib/apiClient', () => ({ startScan: api.startScan }));

beforeEach(() => {
  api.startScan.mockReset();
  api.startScan.mockResolvedValue({ instanceId: 'scan-7' });
});

function show() {
  const onStarted = vi.fn();
  const view = render(<SettingsPanel onStarted={onStarted} />);
  const [primaryPicker, secondaryPicker] = [...view.container.querySelectorAll<HTMLInputElement>('input[type=file]')];
  const [browsePrimary, browseSecondary] = screen.getAllByTitle('Browse...');
  return { onStarted, primaryPicker, secondaryPicker, browsePrimary, browseSecondary, ...view };
}

const primary = () => screen.getByLabelText<HTMLInputElement>('Root folder path');
const secondary = () => screen.getByLabelText<HTMLInputElement>('Secondary folder path');
const scan = () => screen.getByRole('button', { name: /Scan|Starting/ });

// Records which of the hidden folder inputs was asked to open its dialog.
function watchPickers() {
  const opened: HTMLInputElement[] = [];
  vi.spyOn(HTMLInputElement.prototype, 'click').mockImplementation(function (this: HTMLInputElement) { opened.push(this); });
  return opened;
}

describe('starting a scan', () => {
  it('needs a root folder first', async () => {
    show();
    expect(scan()).toBeDisabled();
    await userEvent.type(primary(), '   ');
    expect(scan()).toBeDisabled();
    await userEvent.type(primary(), 'C:\\photos');
    expect(scan()).toBeEnabled();
  });

  it('scans the folder as typed, subfolders included, and reports the scan it started', async () => {
    const { onStarted } = show();
    await userEvent.type(primary(), '  C:\\photos  ');
    await userEvent.type(secondary(), '   ');

    await userEvent.click(scan());

    expect(api.startScan).toHaveBeenCalledExactlyOnceWith({ primaryLocation: 'C:\\photos', secondaryLocation: undefined, recursive: true });
    expect(onStarted).toHaveBeenCalledExactlyOnceWith('scan-7');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('passes on a second folder and the choice to stay out of subfolders', async () => {
    show();
    await userEvent.type(primary(), 'C:\\photos');
    await userEvent.type(secondary(), ' D:\\backup ');
    await userEvent.click(screen.getByLabelText('Recursive'));

    await userEvent.click(scan());

    expect(api.startScan).toHaveBeenCalledExactlyOnceWith({ primaryLocation: 'C:\\photos', secondaryLocation: 'D:\\backup', recursive: false });
  });

  it('cannot be started twice while the first is being started', async () => {
    const starting = deferred<{ instanceId: string }>();
    api.startScan.mockReturnValue(starting.promise);
    const { onStarted } = show();
    await userEvent.type(primary(), 'C:\\photos');

    await userEvent.click(scan());
    expect(scan()).toHaveTextContent('Starting...');
    expect(scan()).toBeDisabled();

    starting.resolve({ instanceId: 'scan-8' });
    expect(await screen.findByRole('button', { name: 'Scan' })).toBeEnabled();
    expect(onStarted).toHaveBeenCalledExactlyOnceWith('scan-8');
  });

  it.each([
    [new TypeError('Failed to fetch'), 'Cannot reach the PhotoSense server.'],
    [new Error('Folder not found: C:\\nowhere'), 'Folder not found: C:\\nowhere'],
    ['A scan is already running', 'A scan is already running'],
  ])('says why when it could not be started', async (failure, message) => {
    api.startScan.mockRejectedValueOnce(failure);
    const { onStarted } = show();
    await userEvent.type(primary(), 'C:\\nowhere');

    await userEvent.click(scan());
    expect(await screen.findByRole('alert')).toHaveTextContent(message);
    expect(onStarted).not.toHaveBeenCalled();
    expect(scan()).toBeEnabled();

    // The complaint goes once the next attempt is made.
    await userEvent.click(scan());
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(onStarted).toHaveBeenCalledExactlyOnceWith('scan-7');
  });
});

describe('browsing for a folder', () => {
  it('has a hidden input for each folder that offers folders rather than files', () => {
    const { primaryPicker, secondaryPicker } = show();
    for (const picker of [primaryPicker, secondaryPicker]) {
      expect(picker).toHaveAttribute('webkitdirectory', '');
      expect(picker).toHaveAttribute('directory', '');
      expect(picker).not.toBeVisible();
    }
  });

  it('fills in the name of the folder picked in the browser\'s own dialog', async () => {
    const showDirectoryPicker = vi.fn().mockResolvedValueOnce({ name: 'Vacation' }).mockResolvedValueOnce({ name: 'Backup' });
    vi.stubGlobal('showDirectoryPicker', showDirectoryPicker);
    const opened = watchPickers();
    const { browsePrimary, browseSecondary } = show();

    await userEvent.click(browsePrimary);
    expect(primary()).toHaveValue('Vacation');
    expect(secondary()).toHaveValue('');

    await userEvent.click(browseSecondary);
    expect(secondary()).toHaveValue('Backup');
    expect(opened).toEqual([]);          // the older dialog is not opened as well
  });

  it('fills in nothing for a folder that has no name', async () => {
    vi.stubGlobal('showDirectoryPicker', vi.fn().mockResolvedValue({}));
    const { browsePrimary } = show();
    await userEvent.type(primary(), 'C:\\photos');

    await userEvent.click(browsePrimary);

    expect(primary()).toHaveValue('');
  });

  it('falls back to the older folder dialog where the browser has no picker', async () => {
    const opened = watchPickers();
    const { browsePrimary, browseSecondary, primaryPicker, secondaryPicker } = show();

    await userEvent.click(browsePrimary);
    expect(opened).toEqual([primaryPicker]);
    await userEvent.click(browseSecondary);
    expect(opened).toEqual([primaryPicker, secondaryPicker]);
  });

  it('falls back quietly when the picker was dismissed or not allowed', async () => {
    const debug = vi.spyOn(console, 'debug').mockImplementation(() => undefined);
    const opened = watchPickers();
    vi.stubGlobal('showDirectoryPicker', vi.fn()
      .mockRejectedValueOnce(new DOMException('The user aborted a request.', 'AbortError'))
      .mockRejectedValueOnce(new DOMException('Not allowed here.', 'NotAllowedError'))
      .mockRejectedValueOnce(new DOMException('Blocked.', 'SecurityError')));
    const { browsePrimary, browseSecondary, primaryPicker, secondaryPicker } = show();

    await userEvent.click(browsePrimary);
    await userEvent.click(browseSecondary);
    await userEvent.click(browsePrimary);

    expect(opened).toEqual([primaryPicker, secondaryPicker, primaryPicker]);
    expect(debug).not.toHaveBeenCalled();
  });

  it.each([
    ['an error of another kind', new Error('The picker crashed')],
    ['an object that is no error', {}],
    ['a bare message', 'not supported'],
    ['nothing at all', null],
  ])('falls back and notes the reason when the picker fails with %s', async (_what, failure) => {
    const debug = vi.spyOn(console, 'debug').mockImplementation(() => undefined);
    const opened = watchPickers();
    vi.stubGlobal('showDirectoryPicker', vi.fn().mockRejectedValue(failure));
    const { browseSecondary, secondaryPicker } = show();

    await userEvent.click(browseSecondary);

    expect(opened).toEqual([secondaryPicker]);
    expect(debug).toHaveBeenCalledExactlyOnceWith('Directory picker fallback reason:', failure);
  });
});

describe('the older folder dialog', () => {
  const choose = (picker: HTMLInputElement, files: unknown) => fireEvent.change(picker, { target: { files } });

  it('fills in the top folder of what was chosen', () => {
    const { primaryPicker, secondaryPicker } = show();

    choose(primaryPicker, [{ webkitRelativePath: 'Vacation/2024/IMG_1.JPG' }, { webkitRelativePath: 'Vacation/2024/IMG_2.JPG' }]);
    choose(secondaryPicker, [{ webkitRelativePath: 'Backup\\IMG_1.JPG' }]);

    expect(primary()).toHaveValue('Vacation');
    expect(secondary()).toHaveValue('Backup');
  });

  it('leaves a path that was already typed as it is', async () => {
    const { primaryPicker, secondaryPicker } = show();
    await userEvent.type(primary(), 'C:\\photos');
    await userEvent.type(secondary(), 'D:\\backup');

    choose(primaryPicker, [{ webkitRelativePath: 'Vacation/IMG_1.JPG' }]);
    choose(secondaryPicker, [{ webkitRelativePath: 'Backup/IMG_1.JPG' }]);

    expect(primary()).toHaveValue('C:\\photos');
    expect(secondary()).toHaveValue('D:\\backup');
  });

  it.each([
    ['an empty folder', []],
    ['no selection', null],
    ['a file that does not say where it came from', [{ name: 'IMG_1.JPG' }]],
  ])('fills in nothing for %s', (_what, files) => {
    const { primaryPicker } = show();
    choose(primaryPicker, files);
    expect(primary()).toHaveValue('');
  });
});
