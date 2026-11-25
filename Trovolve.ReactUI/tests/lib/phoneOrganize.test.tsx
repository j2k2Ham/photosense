import { act, renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { defaultSettings, suggest, noChoices, type OrganizeSettings } from '../../lib/organize';
import { WORDS } from '../../lib/phone';
import { ago, batchLine, files, goesTo, insideLine, optionsLine, placesFor, rowFacts, rowNote, sections, useOrganize } from '../../lib/phoneOrganize';
import type { OrganizeFileDto, OrganizeFilesDto } from '../../types';
import { organizeFile, organizePlace } from '../fixtures';

const ROOT = '/Photo Library';
const ios = WORDS.ios, android = WORDS.android;
const places = [
  organizePlace(),
  organizePlace({ town: 'Butte', area: 'Uptown Butte', latitude: 46.0038, longitude: -112.5348 }),
  organizePlace({ town: 'Opportunity', latitude: 46.1100, longitude: -112.8700 }),
];
const at = (id: string, more: Partial<OrganizeFileDto> = {}) => organizeFile({ id, name: `${id}.JPG`, folder: ROOT, sizeBytes: 1024 ** 2, ...more });
const all = [
  at('a1', { place: 0, date: '2022-05-01T10:00:00' }), at('a2', { place: 0, date: '2023-06-01T10:00:00' }), at('o1', { place: 2, date: '2023-07-01T10:00:00' }),
  at('b1', { place: 1, date: '2021-01-01T10:00:00' }), at('n1', { date: '2020-01-01T10:00:00' }),
  at('v1', { place: 0, isVideo: true, date: '2023-01-01T10:00:00' }), at('lp', { place: 0, isVideo: true, livePhoto: true, date: '2023-01-01T10:00:01' }),
  at('s1', { place: 0, folder: `${ROOT}/Trips/Montana` }),
];
const listing = (files: OrganizeFileDto[] = all, root = ROOT): OrganizeFilesDto => ({ root, fromScan: true, places, files });

describe('Organize on a phone', () => {
  it('suggests albums only for what is in none yet, largest first, and counts what has no location', () => {
    const { result } = renderHook(() => useOrganize(listing(), 'ios', false, 'miles'));
    const o = result.current;
    expect(o.rows.map(r => [r.suggestion!.name, r.suggestion!.files.map(f => f.id)])).toEqual([['Anaconda', ['a1', 'a2', 'o1', 'v1', 'lp']], ['Uptown Butte', ['b1']]]);
    expect([o.unplaced, o.toMove, o.open, o.view, o.kinds]).toEqual([1, 6, undefined, 'list', 'both']);
  });

  it('on Android also sorts what is still where the camera put it', () => {
    const { result } = renderHook(() => useOrganize(listing([at('c1', { place: 1, folder: '/Phone/DCIM/Camera' }), at('p1', { place: 1, folder: '/Phone/Pictures/Old' })], '/Phone'), 'android', false, 'miles'));
    expect(result.current.rows.map(r => r.suggestion!.files.map(f => f.id))).toEqual([['c1']]);
  });

  it('shows nothing before the library has been read', () => {
    const { result } = renderHook(() => useOrganize(undefined, 'ios', true, 'miles'));
    expect([result.current.rows, result.current.unplaced, result.current.toMove, result.current.open]).toEqual([[], 0, 0, undefined]);
  });

  it('sorts only the kind shown, a Live Photo\'s video never on its own', () => {
    const { result } = renderHook(() => useOrganize(listing(), 'ios', false, 'miles'));
    act(() => result.current.setKinds('videos'));
    expect(result.current.rows.map(r => r.suggestion!.files.map(f => f.id))).toEqual([['v1']]);
    act(() => result.current.setKinds('photos'));
    expect(result.current.rows.map(r => r.suggestion!.files.map(f => f.id))).toEqual([['a1', 'a2', 'o1'], ['b1']]);
  });

  it('by date gives each year an album with places inside, the latest first, and by place each place years inside', () => {
    const { result } = renderHook(() => useOrganize(listing(), 'ios', true, 'miles'));
    act(() => result.current.look('p:x'));
    act(() => result.current.change({ group: 'date' }));
    expect(result.current.settings).toMatchObject({ group: 'date', dateThen: 'place' });
    expect(result.current.rows.map(r => r.key)).toEqual(['y:2023', 'y:2022', 'y:2021', 'y:2020']);
    expect(result.current.unplaced).toBe(0);
    // Looking at nothing in particular, a tablet shows the first.
    expect(result.current.open?.key).toBe('y:2023');
    act(() => result.current.change({ group: 'date' }));
    expect(result.current.settings.dateThen).toBe('place');
    act(() => result.current.change({ group: 'place' }));
    expect(result.current.settings).toMatchObject({ group: 'place', placeThen: 'year' });
    act(() => result.current.change({ radius: 1 }));
    expect(result.current.rows.map(r => r.suggestion!.name)).toEqual(['Anaconda', 'Uptown Butte', 'Opportunity']);
  });

  it('renames a suggestion until the way of naming changes', () => {
    const { result } = renderHook(() => useOrganize(listing(), 'ios', false, 'miles'));
    const key = result.current.rows[0].key;
    act(() => result.current.rename(key, 'Home'));
    expect(result.current.rows[0].suggestion!.name).toBe('Home');
    act(() => result.current.change({ nameFormat: 'town' }));
    expect(result.current.rows.map(r => r.suggestion!.name)).toEqual(['Anaconda', 'Butte']);
  });

  it('keeps a moved suggestion as a row, looks at it on a tablet, and forgets it once undone', () => {
    let files = all;
    const phone = renderHook(() => useOrganize(listing(files), 'ios', false, 'miles'));
    const key = phone.result.current.rows[0].key;
    act(() => phone.result.current.look(key));
    expect(phone.result.current.open?.key).toBe(key);
    files = all.map(f => (['a1', 'a2', 'o1', 'v1', 'lp'].includes(f.id) ? { ...f, folder: `${ROOT}/Anaconda` } : f));
    act(() => phone.result.current.moved(key, { name: 'Anaconda', count: 5, bytes: 9, range: 'May 2022', path: `${ROOT}/Anaconda`, batchId: 'b1' }));
    expect(phone.result.current.open).toBeUndefined();
    // The moved row stays, by its count, with the rest.
    expect(phone.result.current.rows.map(r => [r.key, !!r.moved])).toEqual([[key, true], [phone.result.current.rows[1].key, false]]);
    act(() => phone.result.current.undone('other'));
    expect(phone.result.current.rows).toHaveLength(2);
    act(() => phone.result.current.undone('b1'));
    expect(phone.result.current.rows).toHaveLength(1);

    const tablet = renderHook(() => useOrganize(listing(files), 'ios', true, 'miles'));
    act(() => tablet.result.current.moved('p:gone', { name: 'Gone', count: 1, bytes: 1, range: 'x', path: 'p' }));
    expect(tablet.result.current.open?.moved?.name).toBe('Gone');
    // By date the moved rows go by their key with the rest.
    act(() => tablet.result.current.change({ group: 'date' }));
    expect(tablet.result.current.rows.map(r => r.key)).toEqual(['y:2021', 'y:2020', 'p:gone']);
  });

  it('switches between the list and the map', () => {
    const { result } = renderHook(() => useOrganize(listing(), 'ios', false, 'miles'));
    act(() => result.current.setView('map'));
    expect(result.current.view).toBe('map');
  });
});

describe('what Organize says on a phone', () => {
  const s = (more: Partial<OrganizeSettings> = {}): OrganizeSettings => ({ ...defaultSettings, ...more });

  it('sums up the options in a line, in the platform\'s words', () => {
    expect(optionsLine(s(), 'both', ios)).toBe('Within 5 mi · Landmark or area · Place › Year');
    expect(optionsLine(s({ near: 'separate', nameFormat: 'town', placeThen: 'month' }), 'photos', android)).toBe('Keep separate · Town · Place/Month · Photos only');
    expect(optionsLine(s({ placeThen: 'none' }), 'videos', ios)).toBe('Within 5 mi · Landmark or area · Place · Videos only');
    expect(optionsLine(s({ group: 'date', dateThen: 'place' }), 'both', ios)).toBe('By year · Year › Place');
    expect(optionsLine(s({ group: 'date', dateThen: 'none' }), 'both', ios)).toBe('By year · Year');
  });

  it('says what an album holds inside', () => {
    expect([insideLine(s(), ios), insideLine(s({ placeThen: 'none' }), ios), insideLine(s({ group: 'date', dateThen: 'place' }), android), insideLine(s({ group: 'date', dateThen: 'none' }), ios)])
      .toEqual(['Place › Year', 'Place', 'Year/Place', 'Nothing']);
  });

  it('notes why a suggestion is named as it is, what it takes in, or the places inside a year', () => {
    const [anaconda, butte] = suggest(listing(all.filter(f => f.folder === ROOT)), s(), noChoices);
    expect(rowNote(anaconda)).toBe('No landmark known here, so it is named after the town.');
    expect(rowNote(butte)).toBeUndefined();
    const [merged] = suggest(listing([at('o1', { place: 2 }), at('o2', { place: 2 }), at('a1', { place: 0 })]), s({ nameFormat: 'town' }), noChoices);
    expect(rowNote(merged)).toBe('Includes Anaconda (4 mi away).');
    const years = suggest(listing([at('a1', { place: 0 }), at('b1', { place: 1 }), at('n1'), at('x', { date: '2020-01-01T00:00:00' }), at('y', { date: '2020-02-01T00:00:00' }), at('z', { place: 0, date: '2020-03-01T00:00:00' }), at('w', { place: 2, date: '2019-01-01T00:00:00' })]), s({ group: 'date', dateThen: 'place' }), noChoices);
    expect(years.map(rowNote)).toEqual(['2 places inside', 'No location on most', '1 place inside']);
  });

  it('says a row\'s size and dates, moved or not', () => {
    const [anaconda] = suggest(listing(), s(), noChoices);
    expect(rowFacts({ key: anaconda.key, suggestion: anaconda })).toEqual({ count: 6, bytes: 6 * 1024 ** 2, range: 'May 2022 – Jul 2023' });
    const moved = { name: 'A', count: 2, bytes: 3, range: 'May 2022', path: 'p' };
    expect(rowFacts({ key: 'k', moved })).toBe(moved);
    expect([files(1), files(1234)]).toEqual(['1 file', '1,234 files']);
  });

  it('offers the top of an iPhone\'s library or a folder of albums made earlier', () => {
    const folders = [ROOT, `${ROOT}/Anaconda`, `${ROOT}/Trips/Montana`, `${ROOT}/Trips/Idaho`, `${ROOT}/Family/2023/June`, '/Elsewhere/x'];
    expect(placesFor(ROOT, folders, 'ios')).toEqual([
      { path: ROOT, title: 'Photo Library', detail: 'At the top level of your albums' },
      { path: `${ROOT}/Family`, title: 'Family', detail: 'A folder of albums made earlier' },
      { path: `${ROOT}/Family/2023`, title: '2023', detail: 'A folder of albums made earlier' },
      { path: `${ROOT}/Trips`, title: 'Trips', detail: 'A folder of albums made earlier' },
    ]);
  });

  it('offers Pictures, DCIM, or a folder made earlier inside them on Android, where apps may make folders only there', () => {
    const folders = ['/Phone/DCIM/Camera', '/Phone/Pictures/Trips/Montana', '/Phone/Movies/Clips/a', '/Phone/Download/x/y', '/Phone/Pictures/Screenshots'];
    expect(placesFor('/Phone', folders, 'android')).toEqual([
      { path: '/Phone/Pictures', title: 'Pictures', detail: 'Pictures, on this phone' },
      { path: '/Phone/DCIM', title: 'DCIM', detail: 'Beside the camera\'s own folder' },
      { path: '/Phone/Movies/Clips', title: 'Clips', detail: 'Movies/Clips, made earlier' },
      { path: '/Phone/Pictures/Trips', title: 'Trips', detail: 'Pictures/Trips, made earlier' },
    ]);
  });

  it('puts the preview\'s files in sections by year, month or place, those with nothing to go by last', () => {
    const f = [at('a', { date: '2023-02-01T00:00:00' }), at('b', { date: '2022-12-01T00:00:00' }), at('c'), at('d', { date: '2023-02-03T00:00:00' })];
    const year = (x: OrganizeFileDto) => x.date.slice(0, 4), month = (x: OrganizeFileDto) => x.date.slice(0, 7);
    expect(sections(f, year).map(x => x.title)).toEqual(['All files']);
    expect(sections(f, year, 'year').map(x => [x.title, x.files.map(y => y.id)])).toEqual([['2022', ['b']], ['2023', ['a', 'c', 'd']]]);
    expect(sections(f, month, 'month').map(x => x.title)).toEqual(['December 2022', 'February 2023', 'June 2023']);
    const byPlace = (x: OrganizeFileDto) => (x.id === 'a' ? 'Butte' : x.id === 'b' ? undefined : 'Anaconda');
    expect(sections(f, byPlace, 'place').map(x => x.title)).toEqual(['Anaconda', 'Butte', 'No location']);
    expect(sections([at('q'), at('r')], () => undefined, 'year').map(x => x.title)).toEqual(['Other']);
    expect(sections([at('q'), at('r', { date: '2020-01-01T00:00:00' })], x => (x.id === 'q' ? undefined : '2020'), 'year').map(x => x.title)).toEqual(['2020', 'Other']);
  });

  it('says how long ago something was done', () => {
    const now = new Date(2026, 10, 11, 18, 0);
    const back = (minutes: number) => new Date(now.getTime() - minutes * 60_000).toISOString();
    expect(ago(back(0), now)).toBe('Just now');
    expect(ago(back(2), now)).toBe('2 min ago');
    expect(ago(back(130), now)).toBe('2 h ago');
    expect(ago(new Date(2026, 10, 10, 18, 12).toISOString(), now)).toBe('Yesterday, 6:12 PM');
    expect(ago(new Date(2026, 9, 28, 9, 5).toISOString(), now)).toBe('Oct 28, 9:05 AM');
    expect(ago(new Date(2026, 9, 28, 0, 5).toISOString(), now)).toBe('Oct 28, 12:05 AM');
  });

  it('says what a change in Recently moved did', () => {
    const b = { id: 'b', label: 'Anaconda', mode: 'move' as const, count: 724, bytes: 1, utc: '' };
    expect(batchLine(b, ROOT, ios)).toBe('Moved 724 files to Anaconda');
    expect(batchLine({ ...b, mode: 'copy', count: 1 }, ROOT, ios)).toBe('Copied 1 file to Anaconda');
    expect(batchLine({ ...b, mode: 'remove', count: 3, label: `${ROOT}/Trips` }, ROOT, ios)).toBe('Deleted 3 files from Photo Library › Trips');
    expect(batchLine({ ...b, mode: 'remove', count: 3, label: `${ROOT}/Trips` }, undefined, ios)).toBe('Deleted 3 files from Trips');
  });
});

describe('where an album goes, as the design names it', () => {
  it('names the library on an iPhone, and on Android leaves the phone unsaid', () => {
    expect(goesTo(`${ROOT}/Trips/Anaconda`, ROOT, ios, 'ios')).toBe('Photo Library › Trips › Anaconda');
    expect(goesTo('/Phone/Pictures/Anaconda', '/Phone', android, 'android')).toBe('Pictures/Anaconda');
    expect(goesTo('C:\\Phone\\Pictures', 'C:\\Phone', android, 'android')).toBe('Pictures');
    expect(goesTo('/Phone', '/Phone', android, 'android')).toBe('Phone');
  });
});
