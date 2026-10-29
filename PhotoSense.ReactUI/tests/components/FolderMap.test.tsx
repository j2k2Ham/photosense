import React from 'react';
import { act, fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { FolderMap } from '../../components/organize/FolderMap';
import { GoogleFolderMap } from '../../components/organize/GoogleFolderMap';
import type { GoogleMaps, LatLng } from '../../lib/googleMaps';
import type { MapDot, MapLayout } from '../../lib/organize';
import { deferred } from '../fakeSignalR';

const google = vi.hoisted(() => ({ googleMapsKey: vi.fn(), loadGoogleMaps: vi.fn(), onGoogleMapsRefused: vi.fn() }));
vi.mock('../../lib/googleMaps', () => google);

// Stand-ins for Google's own classes, which keep what they were made with and told to do.
class FakeMap {
  static made: FakeMap[] = [];
  fitBounds = vi.fn();
  setCenter = vi.fn();
  setZoom = vi.fn();
  setMapTypeId = vi.fn();
  /** A click on the map itself, as Google would report it. */
  click!: () => void;
  constructor(public element: HTMLElement, public options: Record<string, unknown>) { FakeMap.made.push(this); }
  addListener(_event: string, handler: () => void) { this.click = handler; }
}
class FakeMarker {
  static made: FakeMarker[] = [];
  map: unknown;
  click!: () => void;
  constructor(public options: { map: unknown; position: LatLng; title: string; zIndex: number; icon: Record<string, unknown> }) { this.map = options.map; FakeMarker.made.push(this); }
  setMap(map: unknown) { this.map = map; }
  addListener(_event: string, handler: () => void) { this.click = handler; }
}
class FakeBounds {
  points: LatLng[] = [];
  extend(at: LatLng) { this.points.push(at); }
}
const LIBRARY = { Map: FakeMap, Marker: FakeMarker, LatLngBounds: FakeBounds, SymbolPath: { CIRCLE: 'circle' } } as unknown as GoogleMaps;

const dot = (key: string, latitude: number, longitude: number, size = 14): MapDot => ({ key, x: 40, y: 60, size, label: key, labelled: true, labelSide: 'right', tip: `${key} · 3 files`, latitude, longitude });
const layout = (...dots: MapDot[]): MapLayout => ({ dots, rings: [], merged: [], aspect: 1, scale: { miles: 5, percent: 20 } });
const BUTTE = dot('Butte', 46.0038, -112.5348, 20), GLACIER = dot('Glacier', 48.495, -113.9819);
const NOTE = 'Places within 5 miles of each other share a folder.';
// The markers on a map now: one that was taken off is no longer on any.
const shown = (on?: FakeMap) => FakeMarker.made.filter(m => (on ? m.map === on : m.map !== null));
const map = () => screen.getByRole('group', { name: 'Map of the suggested folders' });
const large = () => screen.getByRole('group', { name: 'Large map of the suggested folders' });
const mapWindow = () => screen.queryByRole('dialog', { name: 'Map' });
const enlarge = () => within(map()).queryByRole('button', { name: 'Enlarge the map' });
let refuse: () => void;
const stopWatching = vi.fn();

beforeEach(() => {
  FakeMap.made = [];
  FakeMarker.made = [];
  for (const mock of Object.values(google)) mock.mockReset();
  stopWatching.mockReset();
  google.googleMapsKey.mockReturnValue('key-1');
  google.loadGoogleMaps.mockResolvedValue(LIBRARY);
  google.onGoogleMapsRefused.mockImplementation((tell: () => void) => { refuse = tell; return stopWatching; });
});

async function open(map: MapLayout = layout(BUTTE, GLACIER), focus?: string) {
  const onFocus = vi.fn();
  const view = render(<FolderMap map={map} focus={focus} note={NOTE} onFocus={onFocus} />);
  await act(async () => {});
  return { onFocus, ...view, show: (next: MapLayout, nextFocus?: string) => view.rerender(<FolderMap map={next} focus={nextFocus} note={NOTE} onFocus={onFocus} />) };
}

describe('the map of the suggested folders', () => {
  it('is a plain one, asking Google for nothing, when no key is set', async () => {
    google.googleMapsKey.mockReturnValue(undefined);
    const { onFocus } = await open();
    expect(google.loadGoogleMaps).not.toHaveBeenCalled();
    expect(map().parentElement).toHaveTextContent(`${NOTE} Larger places are labeled. Hover a dot for its name, click to see its files. Click the map itself to open it large. This is a plain map. To see streets and satellite pictures, set a Google Maps key: the README says how.`);
    await userEvent.click(within(map()).getByRole('button', { name: 'Butte · 3 files' }));
    expect(onFocus).toHaveBeenCalledExactlyOnceWith('Butte');
  });

  it('is Google\'s when a key is set: streets or satellite pictures, with a dot for each folder and all of them in view', async () => {
    const waiting = deferred<GoogleMaps>();
    google.loadGoogleMaps.mockReturnValue(waiting.promise);
    await open();
    expect(google.loadGoogleMaps).toHaveBeenCalledExactlyOnceWith('key-1');
    expect(within(map()).getByRole('status')).toHaveTextContent('Loading the map…');
    // Until there is a map there is nothing to open large.
    expect(enlarge()).not.toBeInTheDocument();
    await act(async () => waiting.resolve(LIBRARY));

    expect(within(map()).queryByRole('status')).not.toBeInTheDocument();
    expect(enlarge()).toBeInTheDocument();
    const [made] = FakeMap.made;
    expect(FakeMap.made).toHaveLength(1);
    expect(map()).toContainElement(made.element);
    expect(map()).toHaveClass('h-[380px]');
    // The small map is opened large by a click, so nothing on it but the dots answers one; its own button stands in for Google's way to the whole screen.
    expect(made.options).toMatchObject({ mapTypeId: 'roadmap', mapTypeControl: false, zoomControl: true, fullscreenControl: false, clickableIcons: false, streetViewControl: false });
    expect(shown().map(m => [m.options.title, m.options.position, m.options.icon.scale, m.options.zIndex])).toEqual([
      ['Butte · 3 files', { lat: 46.0038, lng: -112.5348 }, 10, 1], ['Glacier · 3 files', { lat: 48.495, lng: -113.9819 }, 7, 1],
    ]);
    expect(shown().every(m => m.options.map === made && m.options.icon.path === 'circle')).toBe(true);
    const [bounds, padding] = made.fitBounds.mock.calls[0] as [FakeBounds, number];
    expect([bounds.points, padding]).toEqual([[{ lat: 46.0038, lng: -112.5348 }, { lat: 48.495, lng: -113.9819 }], 36]);
    expect(map().parentElement).toHaveTextContent(`${NOTE} Each dot is a suggested folder: hover for its name, click to see its files. Click the map itself to open it large. Switch between Map and Satellite at its top left, and zoom with the wheel or the buttons. The map comes from Google; your pictures are not sent to it.`);
  });

  it('switches between the map with streets and satellite pictures', async () => {
    await open();
    const [made] = FakeMap.made, view = within(map()).getByRole('group', { name: 'Map view' });
    expect(within(view).getAllByRole('button').map(b => [b.textContent, b.getAttribute('aria-pressed')])).toEqual([['Map', 'true'], ['Satellite', 'false']]);
    await userEvent.click(within(view).getByRole('button', { name: 'Satellite' }));
    // Satellite pictures with the streets and names drawn over them.
    expect(made.setMapTypeId).toHaveBeenLastCalledWith('hybrid');
    expect(within(view).getByRole('button', { name: 'Satellite' })).toHaveAttribute('aria-pressed', 'true');
    await userEvent.click(within(view).getByRole('button', { name: 'Map' }));
    expect(made.setMapTypeId).toHaveBeenLastCalledWith('roadmap');
    // The dots stay as they are, and so does what is in view.
    expect(FakeMarker.made).toHaveLength(2);
    expect(made.fitBounds).toHaveBeenCalledOnce();
  });

  it('shows a folder\'s files on a click of its dot, marks the one picked, and lets it go on a second click', async () => {
    const { onFocus, show } = await open();
    act(() => shown()[1].click());
    expect(onFocus).toHaveBeenLastCalledWith('Glacier');
    // A click on a dot is the dot's own: the map is not opened large by it.
    expect(mapWindow()).not.toBeInTheDocument();

    show(layout(BUTTE, GLACIER), 'Glacier');
    // The dots are drawn again, the picked one on top with a heavier edge; what is in view is left as the person has it.
    expect(shown()).toHaveLength(2);
    expect(FakeMarker.made).toHaveLength(4);
    expect(shown().map(m => [m.options.zIndex, m.options.icon.strokeWeight, m.options.icon.fillOpacity])).toEqual([[1, 1.5, 0.85], [2, 3, 1]]);
    expect(FakeMap.made[0].fitBounds).toHaveBeenCalledOnce();
    act(() => shown()[1].click());
    expect(onFocus).toHaveBeenLastCalledWith(undefined);
  });

  it('brings the folders into view again when they are other folders, and draws a single one with what is around it', async () => {
    const { show } = await open();
    const [made] = FakeMap.made;
    show(layout(GLACIER));
    expect(shown().map(m => m.options.title)).toEqual(['Glacier · 3 files']);
    expect(made.fitBounds).toHaveBeenCalledOnce();
    expect(made.setCenter).toHaveBeenCalledExactlyOnceWith({ lat: 48.495, lng: -113.9819 });
    expect(made.setZoom).toHaveBeenCalledExactlyOnceWith(11);
    show(layout(BUTTE, GLACIER));
    expect(made.fitBounds).toHaveBeenCalledTimes(2);
    expect(FakeMap.made).toHaveLength(1);
  });

  it('falls back to the plain map, saying why, when Google cannot be reached or refuses the key', async () => {
    google.loadGoogleMaps.mockRejectedValue(new Error('Google Maps could not be reached.'));
    const first = await open();
    expect(within(map()).getByRole('button', { name: 'Butte · 3 files' })).toBeInTheDocument();
    expect(map().parentElement).toHaveTextContent('Click the map itself to open it large. Google Maps could not be reached. This plain map is shown instead.');
    expect(stopWatching).toHaveBeenCalledOnce();
    first.unmount();

    google.loadGoogleMaps.mockResolvedValue(LIBRARY);
    await open();
    expect(FakeMap.made).toHaveLength(1);
    act(() => refuse());
    expect(within(map()).getByRole('button', { name: 'Butte · 3 files' })).toBeInTheDocument();
    expect(map().parentElement).toHaveTextContent('Google did not accept the map key. This plain map is shown instead.');
  });

  it('takes no notice of the library arriving, or failing to, after the map has gone', async () => {
    for (const settle of ['resolve', 'reject'] as const) {
      const waiting = deferred<GoogleMaps>();
      google.loadGoogleMaps.mockReturnValue(waiting.promise);
      const onUnavailable = vi.fn();
      const view = render(<GoogleFolderMap mapsKey="key-1" dots={[BUTTE]} view="roadmap" onView={vi.fn()} onFocus={vi.fn()} onUnavailable={onUnavailable} />);
      view.unmount();
      await act(async () => { if (settle === 'resolve') waiting.resolve(LIBRARY); else waiting.reject(new Error('Google Maps could not be reached.')); });
      expect(FakeMap.made).toHaveLength(0);
      expect(onUnavailable).not.toHaveBeenCalled();
    }
  });
});

describe('the map opened large', () => {
  it('opens in a window of its own on a click of the plain map or of the button in its corner, and does there what it does small', async () => {
    google.googleMapsKey.mockReturnValue(undefined);
    // Only the larger places are labeled on the small map.
    const { onFocus, show } = await open(layout(BUTTE, { ...GLACIER, labelled: false }));
    expect(map()).toHaveTextContent(/^Butte5 mi$/);
    expect(map()).toHaveClass('w-full', 'cursor-zoom-in');
    // A click on a dot is the dot's own.
    await userEvent.click(within(map()).getByRole('button', { name: 'Glacier · 3 files' }));
    expect(onFocus).toHaveBeenLastCalledWith('Glacier');
    expect(mapWindow()).not.toBeInTheDocument();

    await userEvent.click(map());
    expect(mapWindow()).toHaveTextContent(/^Map2 suggested folders · click a dot to see that folder's filesEsc×/);
    expect(mapWindow()).toContainElement(large());
    // With room for it, every place is labeled; and the large map is not opened larger still.
    expect(large()).toHaveTextContent(/^ButteGlacier5 mi$/);
    expect(large()).not.toHaveClass('w-full', 'cursor-zoom-in');
    expect(large().parentElement).toHaveClass('h-full', '[container-type:size]');
    expect(within(large()).getAllByRole('button').map(b => b.getAttribute('aria-label'))).toEqual(['Butte · 3 files', 'Glacier · 3 files']);
    expect(mapWindow()).not.toHaveTextContent('Larger places are labeled');
    await userEvent.click(large());
    expect(mapWindow()).toBeInTheDocument();

    // A dot shows its folder's files, which are behind the window: the window is put away.
    await userEvent.click(within(large()).getByRole('button', { name: 'Butte · 3 files' }));
    expect(onFocus).toHaveBeenLastCalledWith('Butte');
    expect(mapWindow()).not.toBeInTheDocument();

    // The picked folder is marked there too, and a click on it lets it go, as on the small map.
    show(layout(BUTTE), 'Butte');
    await userEvent.click(enlarge()!);
    expect(mapWindow()).toHaveTextContent('1 suggested folder · click a dot');
    expect(within(large()).getByRole('button', { name: 'Butte · 3 files' })).toHaveAttribute('aria-pressed', 'true');
    await userEvent.click(within(large()).getByRole('button', { name: 'Butte · 3 files' }));
    expect(onFocus).toHaveBeenLastCalledWith(undefined);
    expect(mapWindow()).not.toBeInTheDocument();
  });

  it('closes on Escape, on its own button and on a press outside it, and on nothing else', async () => {
    google.googleMapsKey.mockReturnValue(undefined);
    const { onFocus } = await open();
    for (const close of [() => fireEvent.keyDown(window, { key: 'Escape' }), () => fireEvent.click(within(mapWindow()!).getByRole('button', { name: 'Close' })), () => fireEvent.mouseDown(mapWindow()!.parentElement!)]) {
      await userEvent.click(enlarge()!);
      fireEvent.keyDown(window, { key: 'Enter' });
      fireEvent.mouseDown(mapWindow()!);
      expect(mapWindow()).toBeInTheDocument();
      close();
      expect(mapWindow()).not.toBeInTheDocument();
    }
    // Once it is closed the keys are no longer its own.
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onFocus).not.toHaveBeenCalled();
  });

  it('is a second map of Google\'s when the small one is: larger, showing what the small one shows, and able to take the whole screen', async () => {
    const { onFocus } = await open(layout(BUTTE, GLACIER), 'Butte');
    const [small] = FakeMap.made;
    await userEvent.click(within(map()).getByRole('button', { name: 'Satellite' }));
    // A click on the map itself, which Google reports apart from a click on a dot or the end of a drag.
    // The library is already there, so the large map is drawn as soon as its window is.
    await act(async () => small.click());

    expect(mapWindow()).toContainElement(large());
    expect(FakeMap.made).toHaveLength(2);
    const big = FakeMap.made[1];
    expect(large()).toContainElement(big.element);
    expect(large()).toHaveClass('h-full');
    expect(big.options).toMatchObject({ mapTypeControl: false, zoomControl: true, fullscreenControl: true, clickableIcons: true, streetViewControl: false });
    // It opens on satellite pictures, as the small one was left, and takes in every folder with more room around them.
    expect(big.setMapTypeId).toHaveBeenLastCalledWith('hybrid');
    expect(big.fitBounds.mock.calls[0][1]).toBe(72);
    expect(shown(big).map(m => [m.options.title, m.options.zIndex])).toEqual([['Butte · 3 files', 2], ['Glacier · 3 files', 1]]);
    // Switched back to streets there, the small one follows.
    await userEvent.click(within(large()).getByRole('button', { name: 'Map' }));
    expect([small.setMapTypeId.mock.lastCall, big.setMapTypeId.mock.lastCall]).toEqual([['roadmap'], ['roadmap']]);

    // It has no way to a still larger one, by a click or by a button.
    expect(within(large()).queryByRole('button', { name: 'Enlarge the map' })).not.toBeInTheDocument();
    act(() => big.click());
    expect(FakeMap.made).toHaveLength(2);

    // A dot shows its folder's files and puts the window away, and its dots with it.
    act(() => shown(big)[1].click());
    expect(onFocus).toHaveBeenLastCalledWith('Glacier');
    expect(mapWindow()).not.toBeInTheDocument();
    expect(shown(big)).toHaveLength(0);
    expect(shown(small)).toHaveLength(2);

    // By the button in the small map's corner it opens as well: as another map, drawn afresh.
    await userEvent.click(enlarge()!);
    expect(mapWindow()).toBeInTheDocument();
    expect(FakeMap.made).toHaveLength(3);
  });

  it('turns into the plain map, as the small one does, when Google stops being of use while it is open', async () => {
    await open();
    await act(async () => FakeMap.made[0].click());
    expect(FakeMap.made).toHaveLength(2);
    act(() => refuse());
    expect(within(large()).getByRole('button', { name: 'Butte · 3 files' })).toBeInTheDocument();
    expect(within(map()).getByRole('button', { name: 'Butte · 3 files' })).toBeInTheDocument();
    expect(map().parentElement).toHaveTextContent('Google did not accept the map key. This plain map is shown instead.');
  });
});
