import { describe, expect, it } from 'vitest';
import {
  checkFolderName, clashSummary, clusterPlaces, dateRange, defaultSettings, fileTag, haversineKm, joinPath, mapLayout, nextFreeName, noChoices, pathOf, placeKey,
  placeName, placeOf, relativeFolder, resolveNames, searchText, separatorOf, shortDate, splitName, suggest, totalBytes, unplaced,
  type OrganizeChoices, type OrganizeSettings,
} from '../../lib/organize';
import type { OrganizeFileDto, OrganizeFilesDto, OrganizePlaceDto, OrganizePlanDto } from '../../types';
import { organizeFile as file, organizePlace as place } from '../fixtures';

const ROOT = 'C:\\Users\\jamie\\Phone Pictures';
// The places of the design's own example, with the distances between them that it shows.
const anaconda = place({ town: 'Anaconda', latitude: 46.1283, longitude: -112.9423 });
const opportunity = place({ town: 'Opportunity', latitude: 46.1094, longitude: -112.8156 });
const butte = place({ town: 'Butte', area: 'Uptown Butte', latitude: 46.0038, longitude: -112.5348 });
const westGlacier = place({ town: 'West Glacier', area: 'Glacier National Park', latitude: 48.495, longitude: -113.9819 });
const apgar = place({ town: 'Apgar', area: 'Apgar Village, Glacier National Park', latitude: 48.5268, longitude: -113.9873 });
const oldFaithful = place({ town: 'Old Faithful', area: 'Old Faithful, Yellowstone', state: 'Wyoming', latitude: 44.4605, longitude: -110.8281 });
const PLACES = [anaconda, opportunity, butte, westGlacier, apgar, oldFaithful];

const listing = (files: OrganizeFileDto[], places: OrganizePlaceDto[] = PLACES): OrganizeFilesDto => ({ root: ROOT, fromScan: true, places, files });
const settings = (changes: Partial<OrganizeSettings> = {}): OrganizeSettings => ({ ...defaultSettings, ...changes });
const choices = (changes: Partial<OrganizeChoices> = {}): OrganizeChoices => ({ ...noChoices, ...changes });
const names = (files: OrganizeFileDto[]) => files.map(f => f.name);

describe('distance and clustering', () => {
  it('measures the distance between two positions over the earth', () => {
    expect(haversineKm(anaconda, anaconda)).toBe(0);
    expect(haversineKm(apgar, westGlacier)).toBeCloseTo(3.56, 1);
    expect(haversineKm(anaconda, opportunity)).toBeCloseTo(10.0, 0);
    // A quarter of the way round the equator.
    expect(haversineKm({ latitude: 0, longitude: 0 }, { latitude: 0, longitude: 90 })).toBeCloseTo(10007.5, 0);
  });

  it('gathers places within the distance of each other, and keeps the rest apart', () => {
    expect(clusterPlaces(PLACES, 5)).toEqual([[0], [1], [2], [3, 4], [5]]);
    expect(clusterPlaces(PLACES, 15)).toEqual([[0, 1], [2], [3, 4], [5]]);
    expect(clusterPlaces(PLACES, 1)).toEqual([[0], [1], [2], [3], [4], [5]]);
    expect(clusterPlaces([], 5)).toEqual([]);
  });

  it('joins places through a chain: the ends share a group though they are too far apart themselves', () => {
    // Three places in a row about 4.4 km apart: the outer two are 8.9 km from each other.
    const row = [0, 0.04, 0.08].map(latitude => ({ latitude, longitude: 0 }));
    expect(haversineKm(row[0], row[2])).toBeGreaterThan(5);
    expect(clusterPlaces(row, 5)).toEqual([[0, 1, 2]]);
    expect(clusterPlaces(row, 4)).toEqual([[0], [1], [2]]);
  });
});

describe('names and paths', () => {
  it('names a place by its landmark, by its town and state, or as a state with the landmark inside', () => {
    expect(placeName(butte, 'area')).toBe('Uptown Butte');
    expect(placeName(butte, 'city')).toBe('Butte, Montana');
    expect(placeName(butte, 'nest')).toBe('Montana\\Uptown Butte');
    // No landmark known: the town stands in.
    expect(placeName(anaconda, 'area')).toBe('Anaconda');
    expect(placeName(anaconda, 'nest')).toBe('Montana\\Anaconda');
  });

  it('leaves out of a name what a folder name cannot hold', () => {
    const odd = place({ town: 'A/B: "C"?', area: 'Lake <Tahoe> | North*\\Shore.', state: 'N.Y.' });
    expect(placeName(odd, 'area')).toBe('Lake Tahoe North Shore');
    expect(placeName(odd, 'city')).toBe('A B C, N.Y');
  });

  it('tells places apart by country, state, town and landmark', () => {
    expect(placeKey(butte)).toBe('US|Montana|Butte|Uptown Butte');
    expect(placeKey(anaconda)).toBe('US|Montana|Anaconda|');
  });

  it('joins folders with the separator the service uses, taking either slash for a subfolder', () => {
    expect(separatorOf('C:\\photos')).toBe('\\');
    expect(separatorOf('/home/jamie')).toBe('/');
    expect(separatorOf('photos')).toBe('\\');
    expect(joinPath('C:\\photos\\', 'Trips\\Glacier 2022', undefined, ' 2022 ')).toBe('C:\\photos\\Trips\\Glacier 2022\\2022');
    expect(joinPath('/home/jamie/', 'Trips\\Glacier', '2022')).toBe('/home/jamie/Trips/Glacier/2022');
    expect(joinPath('C:\\', 'a/b')).toBe('C:\\a\\b');
    expect(pathOf(file({ folder: 'C:\\p', name: 'x.JPG' }))).toBe('C:\\p\\x.JPG');
    // The top of a disk already ends with the separator.
    expect(pathOf(file({ folder: 'C:\\', name: 'x.JPG' }))).toBe('C:\\x.JPG');
    expect(pathOf(file({ folder: '/home/p', name: 'x.JPG' }))).toBe('/home/p/x.JPG');
  });

  it('calls a folder by where it is inside the root', () => {
    expect(relativeFolder(ROOT, ROOT)).toBe('Top level');
    expect(relativeFolder(ROOT + '\\', ROOT.toUpperCase())).toBe('Top level');
    expect(relativeFolder(ROOT, `${ROOT}\\2023\\July`)).toBe('2023\\July');
    expect(relativeFolder('/home/p', '/home/p/2023')).toBe('2023');
    // Another folder that merely starts with the same letters, and one elsewhere altogether.
    expect(relativeFolder(ROOT, `${ROOT} old\\2023`)).toBe(`${ROOT} old\\2023`);
    expect(relativeFolder(ROOT, 'D:\\Backup')).toBe('D:\\Backup');
  });

  it('says when files are from', () => {
    expect(shortDate('2023-12-31T14:03:22')).toBe('Dec 31, 2023');
    expect(shortDate('2021-01-05T00:00:00')).toBe('Jan 5, 2021');
    expect(dateRange([])).toBe('–');
    expect(dateRange([file({ date: '2022-07-04T10:00:00' }), file({ date: '2022-07-20T10:00:00' })])).toBe('Jul 2022');
    expect(dateRange([file({ date: '2023-12-31T10:00:00' }), file({ date: '2021-01-02T10:00:00' }), file({ date: '2022-05-02T10:00:00' })])).toBe('Jan 2021 – Dec 2023');
    expect(totalBytes([file({ sizeBytes: 5 }), file({ sizeBytes: 7 })])).toBe(12);
  });

  it('searches a file by name, place, day and month', () => {
    const at = file({ name: 'IMG_1514.JPG', date: '2023-12-31T10:00:00', place: 2 });
    expect(searchText(at, PLACES)).toBe('img_1514.jpg butte uptown butte montana dec 31, 2023 december');
    expect(searchText(file({ name: 'a.MOV', date: '2022-07-04T10:00:00', place: 0 }), PLACES)).toBe('a.mov anaconda montana jul 4, 2022 july');
    expect(searchText(file({ name: 'b.JPG', date: '2022-07-04T10:00:00' }), PLACES)).toBe('b.jpg no location jul 4, 2022 july');
    expect(placeOf(at, PLACES)).toBe('Butte');
    expect(placeOf(file({ place: null }), PLACES)).toBe('No location');
  });

  it('checks a folder name before it is used', () => {
    expect(checkFolderName('  \\Trips\\Glacier 2022\\ ')).toEqual({ name: 'Trips\\Glacier 2022' });
    expect(checkFolderName('  ')).toEqual({ error: 'Give the new folder a name.' });
    expect(checkFolderName('\\/')).toEqual({ error: 'Give the new folder a name.' });
    expect(checkFolderName('What?')).toEqual({ error: 'Folder names cannot contain < > : " / | ? or *' });
    expect(checkFolderName('trips', ['Family', 'Trips'])).toEqual({ error: 'You already have a folder called trips.' });
    expect(checkFolderName('Trips 2', ['Trips'])).toEqual({ name: 'Trips 2' });
  });
});

describe('suggested folders by place', () => {
  const at = (id: string, placeIndex: number | null, changes: Partial<OrganizeFileDto> = {}) => file({ id, name: `${id}.JPG`, place: placeIndex, folder: ROOT, date: '2022-07-04T10:00:00', ...changes });
  const files = [at('a1', 0), at('a2', 0), at('a3', 0), at('o1', 1), at('b1', 2), at('b2', 2), at('g1', 3), at('g2', 3), at('p1', 4), at('n1', null)];

  it('gives each group of nearby places one folder, named after the place with the most files, largest first', () => {
    const found = suggest(listing(files), settings(), noChoices);
    expect(found.map(s => [s.name, names(s.files), s.merge, s.fallback])).toEqual([
      ['Anaconda', ['a1.JPG', 'a2.JPG', 'a3.JPG'], '', true],
      ['Glacier National Park', ['g1.JPG', 'g2.JPG', 'p1.JPG'], 'Includes Apgar Village, Glacier National Park (2 mi away)', false],
      ['Uptown Butte', ['b1.JPG', 'b2.JPG'], '', false],
      ['Opportunity', ['o1.JPG'], '', true],
    ]);
    expect(found[1].members).toEqual([3, 4]);
    expect(found[1].dominant).toBe(3);
    expect(found[1].key).toBe('p:US|Montana|West Glacier|Glacier National Park+US|Montana|Apgar|Apgar Village, Glacier National Park');
    expect(found[1].split).toBe('year');
    expect(found[1].destinationOf(files[6])).toBe(`${ROOT}\\Glacier National Park\\2022`);
  });

  it('names a folder after whichever of its places has the most files', () => {
    const more = [...files, at('p2', 4), at('p3', 4)];
    const glacier = suggest(listing(more), settings(), noChoices).find(s => s.members.length === 2)!;
    expect([glacier.name, glacier.dominant, glacier.merge]).toEqual(['Apgar Village, Glacier National Park', 4, 'Includes Glacier National Park (2 mi away)']);
  });

  it('combines more as the distance grows, and nothing when places are kept separate', () => {
    const wide = suggest(listing(files), settings({ radiusMiles: 10 }), noChoices);
    expect(wide.map(s => [s.name, s.files.length, s.merge])).toEqual([
      ['Anaconda', 4, 'Includes Opportunity (6 mi away)'], ['Glacier National Park', 3, 'Includes Apgar Village, Glacier National Park (2 mi away)'], ['Uptown Butte', 2, ''],
    ]);
    const apart = suggest(listing(files), settings({ near: 'separate', radiusMiles: 50 }), noChoices);
    expect(apart.map(s => s.name)).toEqual(['Anaconda', 'Uptown Butte', 'Glacier National Park', 'Opportunity', 'Apgar Village, Glacier National Park']);
    expect(apart.every(s => s.merge === '')).toBe(true);
  });

  it('names at most three of the places a folder takes in, and says a place right beside another is 1 mile away', () => {
    const close = [0, 0.002, 0.004, 0.006, 0.008].map((d, i) => place({ town: `T${i}`, latitude: 46 + d, longitude: -112 }));
    const many = suggest(listing([at('x', 0), at('y', 0), at('z', 1), at('v', 2), at('w', 3), at('u', 4)], close), settings(), noChoices);
    expect(many[0].merge).toBe('Includes T1 (1 mi away), T2 (1 mi away), T3 (1 mi away) and 1 more');
  });

  it('names folders in the chosen style, and by year or flat', () => {
    const cities = suggest(listing(files), settings({ nameFormat: 'city', placeThen: 'none' }), noChoices);
    expect(cities.map(s => [s.name, s.fallback])).toEqual([['Anaconda, Montana', false], ['West Glacier, Montana', false], ['Butte, Montana', false], ['Opportunity, Montana', false]]);
    expect(cities[0].destinationOf(files[0])).toBe(`${ROOT}\\Anaconda, Montana`);
    const nested = suggest(listing(files), settings({ nameFormat: 'nest' }), noChoices);
    expect(nested[2].destinationOf(files[4])).toBe(`${ROOT}\\Montana\\Uptown Butte\\2022`);
    expect(nested[0].fallback).toBe(true);
  });

  it('splits a place\'s folder by year, by month, or not at all', () => {
    const [byYear] = suggest(listing(files), settings(), noChoices);
    expect([byYear.split, byYear.subfolderOf(files[0])]).toEqual(['year', '2022']);
    const [byMonth] = suggest(listing(files), settings({ placeThen: 'month' }), noChoices);
    expect([byMonth.split, byMonth.subfolderOf(files[0]), byMonth.destinationOf(files[0])]).toEqual(['month', '2022-07', `${ROOT}\\Anaconda\\2022-07`]);
    const [whole] = suggest(listing(files), settings({ placeThen: 'none' }), noChoices);
    expect([whole.split, whole.subfolderOf(files[0]), whole.destinationOf(files[0])]).toEqual([undefined, undefined, `${ROOT}\\Anaconda`]);
  });

  it('leaves out files that are already where they would go, and a folder with nothing left to take', () => {
    const settled = [
      at('a1', 0, { folder: `${ROOT}\\Anaconda\\2022` }), at('a2', 0, { folder: `${ROOT.toLowerCase()}\\anaconda\\2022` }), at('a3', 0, { folder: `${ROOT}\\Anaconda` }),
      at('b1', 2, { folder: `${ROOT}\\Uptown Butte\\2022` }),
    ];
    expect(suggest(listing(settled), settings(), noChoices).map(s => [s.name, names(s.files)])).toEqual([['Anaconda', ['a3.JPG']]]);
    // Flat, the one in the place's own folder is settled and the two in its year folders are not.
    expect(suggest(listing(settled), settings({ placeThen: 'none' }), noChoices).map(s => [s.name, names(s.files)])).toEqual([['Anaconda', ['a1.JPG', 'a2.JPG']], ['Uptown Butte', ['b1.JPG']]]);
  });

  it('leaves out files in your own folders and files already organized', () => {
    const arranged = choices({ assigned: { a1: 'f1' }, organized: { [`${ROOT}\\a2.JPG`]: { label: 'Trips', copied: true } } });
    expect(names(suggest(listing(files), settings(), arranged).find(s => s.name === 'Anaconda')!.files)).toEqual(['a3.JPG']);
  });

  it('keeps a file dropped onto a suggested folder with that folder, whatever its own place', () => {
    const glacier = suggest(listing(files), settings(), noChoices)[1].key;
    const dropped = suggest(listing(files), settings(), choices({ added: { a1: glacier, n1: glacier, b1: 'p:gone' } }));
    expect(dropped.map(s => [s.name, names(s.files)])).toEqual([
      ['Glacier National Park', ['a1.JPG', 'g1.JPG', 'g2.JPG', 'p1.JPG', 'n1.JPG']],
      ['Anaconda', ['a2.JPG', 'a3.JPG']],
      // The folder b1 was dropped on is no longer suggested, so it is back with its own place.
      ['Uptown Butte', ['b1.JPG', 'b2.JPG']],
      ['Opportunity', ['o1.JPG']],
    ]);
  });

  it('uses the name a suggested folder was given, without the note about the town standing in', () => {
    const key = suggest(listing(files), settings(), noChoices)[0].key;
    const renamed = suggest(listing(files), settings(), choices({ renames: { [key]: 'Home\\Anaconda ' } }))[0];
    expect([renamed.name, renamed.fallback]).toEqual(['Home\\Anaconda ', false]);
    expect(renamed.destinationOf(files[0])).toBe(`${ROOT}\\Home\\Anaconda\\2022`);
  });

  it('counts the files with no place that nothing has been arranged for', () => {
    const more = [...files, at('n2', null), at('n3', null), at('n4', null)];
    const found = suggest(listing(more), settings(), noChoices);
    const arranged = choices({ assigned: { n2: 'f1' }, added: { n3: found[0].key, n4: 'p:gone' }, organized: { [`${ROOT}\\n1.JPG`]: { label: 'x', copied: false } } });
    expect(names(unplaced(listing(more), noChoices, found))).toEqual(['n1.JPG', 'n2.JPG', 'n3.JPG', 'n4.JPG']);
    expect(names(unplaced(listing(more), arranged, found))).toEqual(['n4.JPG']);
  });
});

describe('suggested folders by date', () => {
  const on = (id: string, date: string, changes: Partial<OrganizeFileDto> = {}) => file({ id, name: `${id}.JPG`, date, folder: ROOT, ...changes });
  const files = [on('a', '2023-12-31T10:00:00'), on('b', '2023-02-01T10:00:00'), on('c', '2022-07-04T10:00:00'), on('d', '2021-01-01T10:00:00', { folder: `${ROOT}\\2021` })];

  it('gives each year one folder, the latest first, and skips files already in theirs', () => {
    const found = suggest(listing(files), settings({ group: 'date' }), noChoices);
    expect(found.map(s => [s.key, s.name, names(s.files), s.split, s.members, s.merge, s.fallback])).toEqual([
      ['y:2023', '2023', ['a.JPG', 'b.JPG'], undefined, [], '', false], ['y:2022', '2022', ['c.JPG'], undefined, [], '', false],
    ]);
    expect(found[0].subfolderOf(files[0])).toBeUndefined();
    expect(found[0].destinationOf(files[0])).toBe(`${ROOT}\\2023`);
  });

  it('splits a year\'s folder by place when asked, leaving a file with no place in the year itself', () => {
    const spread = [
      on('a', '2023-12-31T10:00:00', { place: 3 }), on('b', '2023-02-01T10:00:00', { place: 4 }), on('c', '2023-07-04T10:00:00', { place: 2 }), on('n', '2023-03-03T10:00:00'),
      // Already where they would go: one in its place's folder inside the year, one with no place in the year's own folder.
      on('s', '2023-05-05T10:00:00', { place: 2, folder: `${ROOT}\\2023\\Uptown Butte` }), on('t', '2023-05-06T10:00:00', { folder: `${ROOT}\\2023` }),
    ];
    const [year] = suggest(listing(spread), settings({ group: 'date', dateThen: 'place' }), noChoices);
    expect([year.key, year.name, year.split, names(year.files)]).toEqual(['y:2023', '2023', 'place', ['a.JPG', 'b.JPG', 'c.JPG', 'n.JPG']]);
    // West Glacier and Apgar are within five miles of each other, so inside the year they share a folder as they would on their own.
    expect(spread.slice(0, 4).map(f => year.destinationOf(f))).toEqual([
      `${ROOT}\\2023\\Glacier National Park`, `${ROOT}\\2023\\Glacier National Park`, `${ROOT}\\2023\\Uptown Butte`, `${ROOT}\\2023`,
    ]);
    expect(year.subfolderOf(spread[3])).toBeUndefined();

    // Places kept apart, and named another way.
    const [apart] = suggest(listing(spread), settings({ group: 'date', dateThen: 'place', near: 'separate', nameFormat: 'city' }), noChoices);
    expect([apart.subfolderOf(spread[0]), apart.subfolderOf(spread[1])]).toEqual(['West Glacier, Montana', 'Apgar, Montana']);
    // By date alone nothing is split, so the file in a place's folder is no longer where it would go.
    const [plain] = suggest(listing(spread), settings({ group: 'date' }), noChoices);
    expect([plain.split, plain.subfolderOf(spread[0]), names(plain.files)]).toEqual([undefined, undefined, ['a.JPG', 'b.JPG', 'c.JPG', 'n.JPG', 's.JPG']]);
  });

  it('gives each month one folder when asked', () => {
    expect(suggest(listing(files), settings({ group: 'date', dateGrain: 'month' }), noChoices).map(s => [s.name, names(s.files)]))
      .toEqual([['2023-12', ['a.JPG']], ['2023-02', ['b.JPG']], ['2022-07', ['c.JPG']], ['2021-01', ['d.JPG']]]);
  });

  it('follows renames, drops onto a folder, your own folders and what is already organized', () => {
    const arranged = choices({ renames: { 'y:2023': 'Last year' }, added: { c: 'y:2023', b: 'y:1999' }, assigned: { a: 'f1' }, organized: { [`${ROOT}\\2021\\d.JPG`]: { label: 'x', copied: false } } });
    expect(suggest(listing(files), settings({ group: 'date' }), arranged).map(s => [s.name, names(s.files)])).toEqual([['Last year', ['b.JPG', 'c.JPG']]]);
  });
});

describe('the tag on a file', () => {
  const f = file({ id: 'a', name: 'a.JPG', folder: ROOT, place: 0 });
  const suggestions = new Map(suggest(listing([f]), settings(), noChoices).map(s => [s.key, s]));
  const key = [...suggestions.keys()][0];

  it('names the folder it is set to go to, or the one it went to', () => {
    expect(fileTag(f, noChoices, {}, suggestions)).toBeUndefined();
    expect(fileTag(f, choices({ assigned: { a: 'f1' } }), { f1: 'Trips\\Glacier 2022' }, suggestions)).toEqual({ text: 'For Trips\\Glacier 2022', done: false });
    expect(fileTag(f, choices({ added: { a: key } }), {}, suggestions)).toEqual({ text: 'For Anaconda', done: false });
    expect(fileTag(f, choices({ added: { a: 'p:gone' } }), {}, suggestions)).toBeUndefined();
    const moved = { [`${ROOT}\\a.JPG`]: { label: 'Anaconda', copied: false } }, copied = { [`${ROOT}\\a.JPG`]: { label: 'Anaconda', copied: true } };
    expect(fileTag(f, choices({ organized: moved }), {}, suggestions)).toEqual({ text: 'In Anaconda', done: true });
    expect(fileTag(f, choices({ organized: copied }), {}, suggestions)).toEqual({ text: 'Copied to Anaconda', done: true });
    // Once it has gone, having been dropped on a suggestion no longer counts; being put in a folder of your own still does.
    expect(fileTag(f, choices({ organized: moved, added: { a: key } }), {}, suggestions)).toEqual({ text: 'In Anaconda', done: true });
    expect(fileTag(f, choices({ organized: copied, assigned: { a: 'f1' } }), { f1: 'Trips' }, suggestions)).toEqual({ text: 'For Trips', done: false });
  });
});

describe('names that are already taken', () => {
  const DEST = `${ROOT}\\Anaconda\\2023`;
  const going = (id: string, name: string, changes: Partial<OrganizeFileDto> = {}) => file({ id, name, folder: `${ROOT}\\${id}`, ...changes });
  const plan = (files: OrganizeFileDto[], changes: Partial<OrganizePlanDto> = {}): OrganizePlanDto =>
    ({ destination: `${ROOT}\\Anaconda`, exists: true, items: files.map(f => ({ id: f.id, folder: DEST })), clashes: [], taken: {}, companions: 0, ...changes });
  const there = (name: string, identical = false) => ({ id: `disk-${name}`, name, sizeBytes: 2_000_000, date: '2023-05-17T10:00:00', width: 3024, height: 4032, isVideo: false, placeName: null, identical });

  it('splits a name from its extension, and finds the first free number', () => {
    expect(splitName('IMG_6435.JPG')).toEqual({ base: 'IMG_6435', ext: '.JPG' });
    expect(splitName('archive.tar.gz')).toEqual({ base: 'archive.tar', ext: '.gz' });
    expect(splitName('README')).toEqual({ base: 'README', ext: '' });
    expect(splitName('.hidden')).toEqual({ base: '.hidden', ext: '' });
    expect(nextFreeName('IMG_1', '.JPG', new Set())).toBe('IMG_1 (1).JPG');
    expect(nextFreeName('IMG_1', '.JPG', new Set(['img_1 (1).jpg', 'img_1 (2).jpg']))).toBe('IMG_1 (3).JPG');
  });

  it('lets every file keep its name when none is taken', () => {
    const files = [going('a', 'IMG_1.JPG'), going('b', 'IMG_2.JPG')];
    expect(resolveNames(files, plan(files), {}, {})).toEqual({ clashes: [], names: { a: 'IMG_1.JPG', b: 'IMG_2.JPG' } });
  });

  it('gives a file whose name is taken the first free number, counting the numbered copies already there', () => {
    const files = [going('a', 'IMG_6435.JPG'), going('b', 'MOV_8203.MP4')];
    const p = plan(files, {
      clashes: [{ id: 'a', existing: [there('IMG_6435.JPG'), there('IMG_6435 (1).JPG')], nextFree: 'IMG_6435 (2).JPG' }, { id: 'b', existing: [there('MOV_8203.MP4', true)], nextFree: 'MOV_8203 (1).MP4' }],
      taken: { [DEST]: ['IMG_6435.JPG', 'IMG_6435 (1).JPG', 'MOV_8203.MP4', 'other.JPG'] },
    });
    const { clashes, names: arriving } = resolveNames(files, p, {}, {});
    expect(arriving).toEqual({ a: 'IMG_6435 (2).JPG', b: 'MOV_8203 (1).MP4' });
    expect(clashes.map(c => [c.file.id, c.folder, c.base, c.ext, c.mode, c.bad, c.auto, c.final, c.wanted])).toEqual([
      ['a', DEST, 'IMG_6435', '.JPG', 'auto', false, 'IMG_6435 (2).JPG', 'IMG_6435 (2).JPG', 'IMG_6435'],
      ['b', DEST, 'MOV_8203', '.MP4', 'auto', false, 'MOV_8203 (1).MP4', 'MOV_8203 (1).MP4', 'MOV_8203'],
    ]);
    // Each is in the folder the file is going to, under the name it is listed by.
    expect(clashes[0].takenBy.map(t => [t.name, t.incoming, t.identical, t.folder, t.ownName])).toEqual([
      ['IMG_6435.JPG', false, false, DEST, 'IMG_6435.JPG'], ['IMG_6435 (1).JPG', false, false, DEST, 'IMG_6435 (1).JPG'],
    ]);
    expect(clashes[1].takenBy[0].identical).toBe(true);
  });

  it('uses the name typed for a file, keeping its extension, unless that name cannot be used', () => {
    const files = [going('a', 'IMG_6435.JPG')];
    const p = plan(files, { clashes: [{ id: 'a', existing: [there('IMG_6435.JPG')], nextFree: 'IMG_6435 (1).JPG' }], taken: { [DEST]: ['IMG_6435.JPG', 'Other.jpg'] } });
    const typed = (name: string) => resolveNames(files, p, { a: { mode: 'rename', name } }, {}).clashes[0];
    expect([typed(' IMG_6435 2023-06 ').mode, typed(' IMG_6435 2023-06 ').bad, typed(' IMG_6435 2023-06 ').final, typed(' IMG_6435 2023-06 ').wanted]).toEqual(['rename', false, 'IMG_6435 2023-06.JPG', ' IMG_6435 2023-06 ']);
    // Empty, holding a character a name cannot have, or taken by another file there: a number instead.
    for (const name of ['', '   ', 'a/b', 'a\\b', 'what?', 'other', 'IMG_6435']) expect([typed(name).bad, typed(name).final]).toEqual([true, 'IMG_6435 (1).JPG']);
  });

  it('leaves a file that is left out where it is: it takes no name, and still counts as a name that is taken', () => {
    const files = [going('a', 'IMG_1.JPG'), going('b', 'IMG_2.JPG')];
    const p = plan(files, { clashes: [{ id: 'a', existing: [there('IMG_1.JPG')], nextFree: 'IMG_1 (1).JPG' }], taken: { [DEST]: ['IMG_1.JPG'] } });
    const { clashes, names: arriving } = resolveNames(files, p, { a: { mode: 'rename', name: 'kept' } }, { a: true, b: true });
    expect(arriving).toEqual({});
    expect(clashes.map(c => [c.file.id, c.mode, c.final, c.bad])).toEqual([['a', 'skip', undefined, false]]);
  });

  it('does not let two files of one move arrive under the same name', () => {
    const files = [going('a', 'IMG_1.JPG', { sizeBytes: 11 }), going('b', 'IMG_1.JPG'), going('c', 'img_1.jpg'), going('d', 'IMG_1 (2).JPG')];
    const { clashes, names: arriving } = resolveNames(files, plan(files), {}, {});
    expect(arriving).toEqual({ a: 'IMG_1.JPG', b: 'IMG_1 (1).JPG', c: 'img_1 (2).jpg', d: 'IMG_1 (2) (1).JPG' });
    expect(clashes.map(c => [c.file.id, c.takenBy.map(t => [t.id, t.name, t.incoming, t.sizeBytes])])).toEqual([
      ['b', [['a', 'IMG_1.JPG', true, 11]]],
      ['c', [['a', 'IMG_1.JPG', true, 11], ['b', 'IMG_1 (1).JPG', true, 6_093_000]]],
      ['d', [['c', 'img_1 (2).jpg', true, 6_093_000]]],
    ]);
    // Another file of the move is still in its own folder under its own name, whatever name it will arrive by.
    expect(clashes[1].takenBy.map(t => [t.ownName, t.folder])).toEqual([['IMG_1.JPG', `${ROOT}\\a`], ['IMG_1.JPG', `${ROOT}\\b`]]);
    // With the first of them left out, the second keeps its name.
    expect(resolveNames(files, plan(files), {}, { a: true }).names).toEqual({ b: 'IMG_1.JPG', c: 'img_1 (1).jpg', d: 'IMG_1 (2).JPG' });
  });

  it('keeps folders apart, and passes over a file the plan says nothing about', () => {
    const files = [going('a', 'IMG_1.JPG'), going('b', 'IMG_1.JPG'), going('c', 'IMG_1.JPG')];
    const p = plan(files, { items: [{ id: 'a', folder: `${DEST}\\x` }, { id: 'b', folder: `${DEST}\\y` }] });
    expect(resolveNames(files, p, {}, {})).toEqual({ clashes: [], names: { a: 'IMG_1.JPG', b: 'IMG_1.JPG' } });
  });

  it('sums up what will happen to the files whose names are taken', () => {
    const files = ['a', 'b', 'c', 'd'].map(id => going(id, `${id}.JPG`));
    const p = plan(files, { taken: { [DEST]: files.map(f => f.name) } });
    const of = (choicesFor: Parameters<typeof resolveNames>[2], excluded: Record<string, boolean>) => clashSummary(resolveNames(files, p, choicesFor, excluded).clashes);
    expect(of({}, {})).toBe('4 get a number');
    expect(of({ a: { mode: 'rename', name: 'new' }, b: { mode: 'rename', name: '' } }, { c: true })).toBe('2 get a number · 1 renamed · 1 left where it is');
    expect(of({ a: { mode: 'rename', name: 'new' }, b: { mode: 'rename', name: 'newer' } }, { c: true, d: true })).toBe('2 renamed · 2 left where they are');
    expect(of({}, { b: true, c: true, d: true })).toBe('1 gets a number · 3 left where they are');
    expect(clashSummary([])).toBe('');
  });
});

describe('the map', () => {
  const at = (id: string, placeIndex: number) => file({ id, name: `${id}.JPG`, place: placeIndex, folder: ROOT });
  const files = [...Array.from({ length: 9 }, (_, i) => at(`a${i}`, 0)), at('g1', 3), at('g2', 3), at('p1', 4), at('y1', 5)];

  it('has nothing to show without suggestions by place', () => {
    expect(mapLayout([], PLACES, settings())).toBeUndefined();
    expect(mapLayout(suggest(listing(files), settings({ group: 'date' }), noChoices), PLACES, settings({ group: 'date' }))).toBeUndefined();
  });

  it('puts each folder at the middle of its files, north up and east right, sized by how many it holds', () => {
    const found = suggest(listing(files), settings(), noChoices);
    const map = mapLayout(found, PLACES, settings())!;
    expect(map.dots.map(d => [d.label, d.size, d.labelled, d.tip])).toEqual([
      ['Anaconda', 14, true, 'Anaconda · 9 files'], ['West Glacier', 13, true, 'Glacier National Park · 3 files'], ['Old Faithful', 13, true, 'Old Faithful, Yellowstone · 1 file'],
    ]);
    const [anacondaDot, glacierDot, yellowstoneDot] = map.dots;
    // Glacier is north of Anaconda, and Yellowstone south-east of both.
    expect(glacierDot.y).toBeLessThan(anacondaDot.y);
    expect(yellowstoneDot.y).toBeGreaterThan(anacondaDot.y);
    expect(yellowstoneDot.x).toBeGreaterThan(anacondaDot.x);
    expect([anacondaDot.labelSide, yellowstoneDot.labelSide]).toEqual(['right', 'left']);
    for (const d of map.dots) for (const v of [d.x, d.y]) { expect(v).toBeGreaterThan(5); expect(v).toBeLessThan(95); }
    // Two of the three files are at West Glacier, so the dot sits a third of the way from it to Apgar.
    const between = (a: number, b: number) => a + (b - a) / 3;
    expect(glacierDot.y).toBeCloseTo(between(map.merged[0].y, map.merged[1].y), 5);
    // The two places it stands for are marked, and every place has the reach it was combined within drawn round it.
    expect(map.merged).toHaveLength(2);
    expect(map.rings).toHaveLength(4);
    // Five miles, against a map whose width the scale gives in miles.
    expect(map.rings[0].diameter).toBeCloseTo(5 / (map.scale.miles / map.scale.percent * 100) * 100, 5);
    // Each dot also says where on the earth it is, for a map that is drawn from that.
    expect([anacondaDot.latitude, anacondaDot.longitude]).toEqual([anaconda.latitude, anaconda.longitude]);
    expect(glacierDot.latitude).toBeCloseTo(westGlacier.latitude + (apgar.latitude - westGlacier.latitude) / 3, 6);
    expect(map.aspect).toBeGreaterThan(1);
    expect(map.scale.miles).toBe(50);
  });

  it('draws no reach when places are kept separate, and labels only the six largest folders', () => {
    const towns = Array.from({ length: 8 }, (_, i) => place({ town: `T${i}`, latitude: 40 + i, longitude: -100 }));
    const spread = towns.flatMap((_, i) => Array.from({ length: 8 - i }, (__, n) => at(`t${i}-${n}`, i)));
    const apart = settings({ near: 'separate' });
    const map = mapLayout(suggest(listing(spread, towns), apart, noChoices), towns, apart)!;
    expect(map.rings).toEqual([]);
    expect(map.merged).toEqual([]);
    expect(map.dots.map(d => d.labelled)).toEqual([true, true, true, true, true, true, false, false]);
  });

  it('does not blow a single town up to fill the map, and sits a folder holding only dropped files at its place', () => {
    const one = [at('a', 0)];
    const key = suggest(listing(one), settings(), noChoices)[0].key;
    const dropped = choices({ added: { n: key }, assigned: { a: 'f1' } });
    const found = suggest(listing([...one, file({ id: 'n', name: 'n.JPG', folder: ROOT })]), settings(), dropped);
    expect(names(found[0].files)).toEqual(['n.JPG']);
    const map = mapLayout(found, PLACES, settings())!;
    expect(map.dots[0].x).toBeCloseTo(50, 6);
    expect(map.dots[0].y).toBeCloseTo(50, 6);
    expect(map.scale.miles).toBe(5);
    expect(map.aspect).toBe(1);
  });

  it('still has a scale on the smallest map', () => {
    const map = mapLayout(suggest(listing([at('a', 0)]), settings({ near: 'separate' }), noChoices), PLACES, settings({ near: 'separate' }))!;
    // Some sixteen miles across: two miles is the longest round distance that fits a quarter of it.
    expect(map.scale.miles).toBe(2);
  });
});
