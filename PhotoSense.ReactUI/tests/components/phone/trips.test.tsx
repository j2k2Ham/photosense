import { fireEvent, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TripsScreen, TripsTile } from '../../../components/phone/Trips';
import type { Platform } from '../../../lib/phone';
import type { PhotoDto, TownDto, TripDetailDto, TripDto, TripsDto } from '../../../types';
import { organizeFile, photo } from '../../fixtures';
import { deferred } from '../../deferred';
import { frame, inFrame } from './frame';

const api = vi.hoisted(() => ({ useTrips: vi.fn(), useTrip: vi.fn(), useTowns: vi.fn(), chooseHome: vi.fn(), makeTripFolder: vi.fn() }));
vi.mock('../../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../../lib/apiClient')>()), ...api }));

const ROOT = '/Photo Library';
const home: TownDto = { town: 'Bozeman', region: 'Montana', country: 'US', latitude: 45.68, longitude: -111.04, days: 0 };
const pic = (id: string, fileName: string, more: Partial<PhotoDto> = {}) => photo({ id, fileName, sourcePath: `${ROOT}/${fileName}`, ...more });
const trip = (more: Partial<TripDto> = {}): TripDto => ({
  id: 'gla', name: 'Glacier National Park', folder: '2026-07 Glacier National Park', start: '2026-07-18', end: '2026-07-21', photos: 512, videos: 2,
  latitude: 48.68, longitude: -113.8, cover: pic('c1', 'IMG_1.HEIC'), folderMade: false, ...more,
});
const yellowstone = trip({ id: 'yel', name: 'Yellowstone', folder: '2026-08 Yellowstone', start: '2026-08-09', end: '2026-08-12', photos: 388, videos: 0, latitude: 44.43, longitude: -110.59, cover: pic('v1', 'CLIP.MOV', { isVideo: true }) });
const trips = (more: Partial<TripsDto> = {}): TripsDto => ({
  home, homeChosen: false, trips: [yellowstone, trip()], computer: 'Studio iMac', tripsFolder: 'Pictures/PhotoSense', folder: null, ...more,
});
const detail: TripDetailDto = {
  trip: trip(),
  days: [
    { date: '2026-07-18', photos: [pic('p1', 'IMG_1.HEIC'), pic('p2', 'IMG_2.MOV', { isVideo: true, durationSeconds: 42 })] },
    { date: '2026-07-19', photos: [pic('p3', 'IMG_3.HEIC', { sourcePath: '/elsewhere/IMG_3.HEIC' })] },
  ],
};
const files = [organizeFile({ id: 'f1', name: 'IMG_1.HEIC', folder: ROOT })];

let answer: { data?: TripsDto; error?: unknown };
beforeEach(() => {
  answer = { data: trips() };
  api.useTrips.mockReset().mockImplementation(() => answer);
  api.useTrip.mockReset().mockImplementation((id?: string) => (id ? { data: detail } : { data: undefined }));
  api.useTowns.mockReset().mockReturnValue({ data: [{ ...home, days: 120 }, { ...home, town: 'Butte', days: 12 }, { ...home, town: 'Anaconda', days: 1 }] });
  api.chooseHome.mockReset().mockResolvedValue(undefined);
  api.makeTripFolder.mockReset().mockResolvedValue(undefined);
});

function show(platform: Platform = 'ios', more: { frozen?: boolean; tablet?: boolean } = {}) {
  const onOpen = vi.fn(), notify = vi.fn(), onClose = vi.fn();
  const ui = (frozen = more.frozen) => <TripsScreen files={files} unit="miles" frozen={frozen} onOpen={onOpen} notify={notify} onClose={onClose} />;
  const view = inFrame(ui(), frame(platform, { tablet: more.tablet }));
  return { onOpen, notify, onClose, again: (frozen?: boolean) => view.again(ui(frozen)), layer: () => screen.getByRole('dialog') };
}

describe('Library\'s Trips tile', () => {
  it('says how many trips there are, with the latest\'s cover, or that it is still finding them, and opens Trips', async () => {
    const onOpen = vi.fn();
    const { again } = inFrame(<TripsTile trips={undefined} onOpen={onOpen} />);
    expect(screen.getByRole('button', { name: 'Trips, Finding trips…' })).toBeInTheDocument();
    again(<TripsTile trips={trips({ trips: [] })} onOpen={onOpen} />);
    expect(screen.getByRole('button', { name: 'Trips, No trips yet' })).toBeInTheDocument();
    again(<TripsTile trips={trips({ trips: [trip()] })} onOpen={onOpen} />);
    expect(screen.getByRole('button', { name: 'Trips, 1 trip' }).querySelector('img')).toHaveAttribute('src', expect.stringContaining('/photos/c1/thumbnail'));
    again(<TripsTile trips={trips()} onOpen={onOpen} />);
    await userEvent.click(screen.getByRole('button', { name: 'Trips, 2 trips' }));
    expect(onOpen).toHaveBeenCalledOnce();
  });
});

describe('Trips (29–31)', () => {
  it('lists the trips, the latest first, with home and how they were found, and closes', async () => {
    const { layer, onClose } = show();
    expect(layer()).toHaveAccessibleName('Trips');
    expect(layer()).toHaveTextContent('Grouped by place and date from photos taken away from home in Bozeman, Montana.');
    const list = within(layer()).getByRole('list', { name: 'Trips' });
    expect(within(list).getAllByRole('button').map(b => b.getAttribute('aria-label'))).toEqual([
      'Yellowstone, Aug 9 – 12, 2026 · 388 photos', 'Glacier National Park, Jul 18 – 21, 2026 · 510 photos and 2 videos',
    ]);
    await userEvent.click(within(layer()).getByRole('button', { name: 'Back' }));
    expect(onClose).toHaveBeenCalledOnce();
  });

  it('says so while finding them, when they could not be found, when no photo has a place, and when there are none', () => {
    answer = { data: undefined };
    const { again, layer } = show();
    expect(layer()).toHaveTextContent('Finding trips…');
    answer = { error: new Error('The library could not be read.') };
    again();
    expect(within(layer()).getByRole('alert')).toHaveTextContent('Trips could not be found: The library could not be read.');
    answer = { error: 'gone' };
    again();
    expect(within(layer()).getByRole('alert')).toHaveTextContent('Trips could not be found: gone');
    answer = { data: trips({ home: null, trips: [] }) };
    again();
    expect(layer()).toHaveTextContent('None of your photos say where they were taken, so there are no trips yet.');
    answer = { data: trips({ trips: [] }) };
    again();
    expect(layer()).toHaveTextContent('No trips yet. A trip is at least 10 photos and videos taken more than about 30 miles from home');
  });

  it('shows the trips as pins on a plain map, with home, and a pin opens its trip', async () => {
    const { layer } = show('android');
    await userEvent.click(within(layer()).getByRole('radio', { name: 'Map' }));
    const map = within(layer()).getByRole('group', { name: 'Map' });
    expect(within(map).getByRole('img', { name: 'Home, Bozeman, Montana' })).toBeInTheDocument();
    expect(layer()).toHaveTextContent('Each pin is a trip, with its photo count. The map is drawn on this phone; where photos were taken is never sent anywhere.');
    await userEvent.click(within(map).getByRole('button', { name: 'Glacier National Park, 510 photos and 2 videos' }));
    expect(layer()).toHaveAccessibleName('Glacier National Park');
  });

  it('opens a trip with its cover, dates and each day\'s grid; a photo the library holds opens, and Back goes back to the list', async () => {
    const { layer, onOpen, notify } = show();
    await userEvent.click(within(layer()).getByRole('button', { name: /^Glacier National Park/ }));
    expect(layer()).toHaveTextContent('Jul 18 – 21, 2026 · 510 photos and 2 videos');
    const first = within(layer()).getByRole('region', { name: 'Day 1 · Jul 18' });
    expect(within(first).getByText('0:42')).toBeInTheDocument();
    await userEvent.click(within(first).getByRole('button', { name: 'IMG_1.HEIC' }));
    expect(onOpen).toHaveBeenCalledWith(files[0]);
    await userEvent.click(within(within(layer()).getByRole('region', { name: 'Day 2 · Jul 19' })).getByRole('button', { name: 'IMG_3.HEIC' }));
    expect(notify).toHaveBeenCalledWith('PhotoSense is still reading IMG_3.HEIC. Try again in a moment.', 'info');
    // Escape takes the trip away first, then Trips.
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(layer()).toHaveAccessibleName('Trips');
  });

  it('says when a trip could not be read', async () => {
    api.useTrip.mockReturnValue({ error: new Error('Gone.') });
    const { layer } = show();
    await userEvent.click(within(layer()).getByRole('button', { name: /^Yellowstone/ }));
    expect(within(layer()).getByRole('alert')).toHaveTextContent('The trip could not be read: Gone.');
  });

  it('ignores Escape while the viewer is over it, and closes on it after', () => {
    const { again, onClose } = show('ios', { frozen: true });
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onClose).not.toHaveBeenCalled();
    again(false);
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onClose).toHaveBeenCalledOnce();
  });
});

describe('Make a folder on the computer', () => {
  async function opened(platform: Platform = 'ios', tablet = false) {
    const shown = show(platform, { tablet });
    await userEvent.click(within(shown.layer()).getByRole('button', { name: /^Glacier National Park/ }));
    return shown;
  }

  it('makes the folder, says it is being made, and says where once it is; then shows Folder created', async () => {
    const { layer, notify, again } = await opened();
    expect(layer()).toHaveTextContent('Puts this trip’s copies in Pictures/PhotoSense/2026-07 Glacier National Park. Nothing changes on this iPhone.');
    const asked = deferred<void>();
    api.makeTripFolder.mockReturnValue(asked.promise);
    await userEvent.click(within(layer()).getByRole('button', { name: 'Make a folder on Studio iMac' }));
    expect(api.makeTripFolder).toHaveBeenCalledWith('gla');
    expect(within(layer()).getByRole('button', { name: 'Making the folder…' })).toBeDisabled();
    asked.resolve();
    answer = { data: trips({ folder: { id: 'gla', stage: 'running', problem: null, made: null } }) };
    again();
    expect(await within(layer()).findByRole('button', { name: 'Making the folder…' })).toBeDisabled();
    answer = { data: trips({ trips: [yellowstone, trip({ folderMade: true })], folder: { id: 'gla', stage: 'ready', problem: null, made: 'Pictures/PhotoSense/2026-07 Glacier National Park' } }) };
    again();
    expect(notify).toHaveBeenCalledWith('Created Pictures/PhotoSense/2026-07 Glacier National Park on Studio iMac.', 'ok');
    expect(within(layer()).getByRole('button', { name: 'Folder created' })).toBeDisabled();
    // Said once.
    again();
    expect(notify).toHaveBeenCalledOnce();
  });

  it('says why the computer did not make it, and why it could not be asked', async () => {
    const { layer, notify, again } = await opened('android', true);
    expect(layer()).toHaveTextContent('Nothing changes on this tablet.');
    answer = { data: trips({ folder: { id: 'gla', stage: 'running', problem: null, made: null } }) };
    again();
    answer = { data: trips({ folder: { id: 'gla', stage: 'failed', problem: 'The disk is full.', made: null } }) };
    again();
    expect(notify).toHaveBeenCalledWith('The disk is full.', 'error');
    api.makeTripFolder.mockRejectedValue(new Error('A backup to Studio iMac is running. Make the folder once it ends.'));
    await userEvent.click(within(layer()).getByRole('button', { name: 'Make a folder on Studio iMac' }));
    expect(notify).toHaveBeenLastCalledWith('A backup to Studio iMac is running. Make the folder once it ends.', 'error');
    expect(within(layer()).getByRole('button', { name: 'Make a folder on Studio iMac' })).toBeEnabled();
  });

  it('with no computer, says to pair one; before the computer has said where, names the folder', async () => {
    answer = { data: trips({ computer: null }) };
    const { layer, again } = await opened();
    expect(within(layer()).queryByRole('button', { name: /Make a folder/ })).not.toBeInTheDocument();
    expect(layer()).toHaveTextContent('Pair with a computer to make this trip a folder on it: in Settings, choose Computer.');
    answer = { data: trips({ tripsFolder: null }) };
    again();
    expect(layer()).toHaveTextContent('Puts this trip’s copies in a folder named 2026-07 Glacier National Park on Studio iMac.');
  });
});

describe('Home', () => {
  it('Change lists the towns with their days, and one chosen becomes home', async () => {
    const { layer } = show();
    await userEvent.click(within(layer()).getByRole('button', { name: 'Change' }));
    const sheet = screen.getByRole('dialog', { name: 'Home' });
    expect(sheet).toHaveTextContent('Photos taken more than 50 km (about 30 miles) from home make trips.');
    expect(within(sheet).getByRole('button', { name: /^Where most photos are taken/ })).toHaveTextContent('Bozeman, Montana');
    expect(within(sheet).getByRole('button', { name: /^Butte, Montana/ })).toHaveTextContent('12 days');
    expect(within(sheet).getByRole('button', { name: /^Anaconda, Montana/ })).toHaveTextContent('1 day');
    await userEvent.click(within(sheet).getByRole('button', { name: /^Butte, Montana/ }));
    expect(api.chooseHome).toHaveBeenCalledWith(expect.objectContaining({ town: 'Butte' }));
    expect(screen.queryByRole('dialog', { name: 'Home' })).not.toBeInTheDocument();
  });

  it('a home chosen is marked, PhotoSense\'s own can be had again, and a failure is said', async () => {
    answer = { data: trips({ homeChosen: true, home: { ...home, town: 'Butte' } }) };
    api.useTowns.mockReturnValue({ data: undefined });
    const { layer, notify } = show();
    await userEvent.click(within(layer()).getByRole('button', { name: 'Change' }));
    let sheet = screen.getByRole('dialog', { name: 'Home' });
    expect(within(sheet).getByRole('button', { name: /^Where most photos are taken/ })).not.toHaveTextContent('Butte');
    api.useTowns.mockReturnValue({ data: [{ ...home, town: 'Butte', days: 3 }, { ...home, days: 9 }] });
    api.chooseHome.mockRejectedValueOnce(new Error('No.'));
    await userEvent.click(within(sheet).getByRole('button', { name: /^Where most photos are taken/ }));
    expect(notify).toHaveBeenCalledWith('No.', 'error');
    sheet = screen.getByRole('dialog', { name: 'Home' });
    await userEvent.click(within(sheet).getByRole('button', { name: /^Where most photos are taken/ }));
    expect(api.chooseHome).toHaveBeenLastCalledWith(null);
  });
});
