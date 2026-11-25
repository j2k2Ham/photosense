import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SearchScreen } from '../../../components/phone/Search';
import type { Platform } from '../../../lib/phone';
import type { PhotoDto, SearchResultDto } from '../../../types';
import { organizeFile } from '../../fixtures';
import { frame, gate, inFrame } from './frame';

const api = vi.hoisted(() => ({ useSearch: vi.fn(), refreshSearch: vi.fn() }));
vi.mock('../../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../../lib/apiClient')>()), ...api }));

const ROOT = '/Photo Library';
const files = [organizeFile({ id: 'f1', name: 'IMG_1.HEIC', folder: ROOT })];
const photo = (id: string, fileName: string, more: Partial<PhotoDto> = {}) => ({ id, fileName, sourcePath: `${ROOT}/${fileName}`, isVideo: false, ...more }) as PhotoDto;
const result = (more: Partial<SearchResultDto> = {}): SearchResultDto => ({
  parts: [{ kind: 'inPhoto', text: 'dog' }, { kind: 'inPhoto', text: 'beach' }, { kind: 'year', text: '2023' }],
  photos: [photo('p1', 'IMG_1.HEIC'), photo('p2', 'IMG_2.MOV', { isVideo: true }), photo('p3', 'IMG_3.HEIC', { sourcePath: '/elsewhere/IMG_3.HEIC' })],
  total: 360, videos: 0, proNeeded: true, ...more,
});

let answer: { data?: SearchResultDto; error?: unknown };
beforeEach(() => {
  localStorage.clear();
  answer = { data: result() };
  api.useSearch.mockReset().mockImplementation((q: string) => (q ? answer : { data: undefined }));
  api.refreshSearch.mockReset();
});

function show(platform: Platform = 'ios') {
  const onOpen = vi.fn(), notify = vi.fn(), onClose = vi.fn();
  const plan = gate();
  inFrame(<SearchScreen files={files} onOpen={onOpen} notify={notify} onClose={onClose} />, frame(platform), plan);
  const search = screen.getByRole('dialog', { name: 'Search' });
  return { onOpen, notify, onClose, plan, search, field: within(search).getByRole('searchbox', { name: 'Search photos' }) };
}

describe('Search', () => {
  it('before a search, offers some to try, says it stays on the phone, and Cancel closes it', async () => {
    const { search, onClose } = show();
    expect(within(search).queryByRole('region', { name: 'Recent' })).not.toBeInTheDocument();
    expect(within(search).getByRole('region', { name: 'Try' })).toHaveTextContent('dog at the beachsunset over mountainsbirthday cake');
    expect(search).toHaveTextContent('Search runs on this iPhone. Your photos and searches are not uploaded.');
    expect(within(search).queryByRole('button', { name: 'Back' })).not.toBeInTheDocument();
    await userEvent.click(within(search).getByRole('button', { name: 'Cancel' }));
    expect(onClose).toHaveBeenCalledOnce();
  });

  it('on Android, Back closes it, and it says this phone', async () => {
    const { search, onClose } = show('android');
    expect(search).toHaveTextContent('Search runs on this phone.');
    expect(within(search).queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument();
    await userEvent.click(within(search).getByRole('button', { name: 'Back' }));
    expect(onClose).toHaveBeenCalledOnce();
  });

  it('shows the search in its parts, the free plan\'s card, how many match and each one, and opens one the library holds', async () => {
    const { search, field, onOpen, notify, plan } = show();
    await userEvent.type(field, '  dog at the beach 2023 {Enter}');
    expect(api.useSearch).toHaveBeenLastCalledWith('dog at the beach 2023');
    expect(field).toHaveValue('dog at the beach 2023');
    expect(within(search).getByRole('list', { name: 'Searched for' })).toHaveTextContent('In photodogIn photobeachYear2023');
    expect(within(search).getByRole('note')).toHaveTextContent('Searching for what is in photos, like “dog” and “beach”, is part of Pro. Showing photos that match 2023.');
    await userEvent.click(within(search).getByRole('button', { name: 'Get Pro' }));
    expect(plan.offer).toHaveBeenCalledWith('search', expect.any(Function));
    (plan.offer as ReturnType<typeof vi.fn>).mock.calls[0][1]();
    expect(api.refreshSearch).toHaveBeenCalledOnce();
    expect(within(search).getByRole('heading', { name: '360 photos' })).toBeInTheDocument();
    const matches = within(search).getByRole('list', { name: 'Matches' });
    expect(within(matches).getByRole('button', { name: 'IMG_1.HEIC' }).querySelector('img')).toHaveAttribute('src', 'http://localhost:7071/api/photos/p1/thumbnail');
    expect(within(matches).getByRole('button', { name: 'IMG_2.MOV' }).querySelector('img')).toBeNull();
    await userEvent.click(within(matches).getByRole('button', { name: 'IMG_1.HEIC' }));
    expect(onOpen).toHaveBeenCalledWith(files[0]);
    await userEvent.click(within(matches).getByRole('button', { name: 'IMG_3.HEIC' }));
    expect(notify).toHaveBeenCalledWith('Trovolve is still reading IMG_3.HEIC. Try again in a moment.', 'info');
  });

  it('on Pro, has no card; says one match, or none; says what went wrong; and says it is searching meanwhile', async () => {
    const { search, field } = show();
    answer = { data: result({ proNeeded: false, total: 1, photos: [photo('p1', 'IMG_1.HEIC')] }) };
    await userEvent.type(field, 'dog{Enter}');
    expect(within(search).queryByRole('note')).not.toBeInTheDocument();
    expect(within(search).getByRole('heading', { name: '1 photo' })).toBeInTheDocument();
    // Videos found with photos are said apart from them.
    answer = { data: result({ proNeeded: false, total: 3, videos: 1 }) };
    await userEvent.type(field, 'g{Enter}');
    expect(within(search).getByRole('heading', { name: '2 photos and 1 video' })).toBeInTheDocument();
    answer = { data: result({ proNeeded: true, parts: [{ kind: 'inPhoto', text: 'cat' }], total: 0, photos: [] }) };
    await userEvent.type(field, 's{Enter}');
    expect(within(search).getByRole('note')).toHaveTextContent('Searching for what is in photos, like “cat”, is part of Pro.');
    expect(within(search).getByRole('heading', { name: 'No matches' })).toBeInTheDocument();
    expect(within(search).queryByRole('list', { name: 'Matches' })).not.toBeInTheDocument();
    answer = { error: new Error('The phone is busy.') };
    await userEvent.type(field, 'x{Enter}');
    expect(within(search).getByRole('alert')).toHaveTextContent('The search could not be made: The phone is busy.');
    answer = { error: 'busy' };
    await userEvent.type(field, 'y{Enter}');
    expect(within(search).getByRole('alert')).toHaveTextContent('The search could not be made: busy');
    answer = {};
    await userEvent.type(field, 'z{Enter}');
    expect(search).toHaveTextContent('Searching…');
  });

  it('keeps the searches made, latest first, which run again when tapped; Clear and an emptied field start again', async () => {
    const { search, field } = show();
    await userEvent.type(field, 'Butte{Enter}');
    await userEvent.click(within(search).getByRole('button', { name: 'Clear' }));
    expect(field).toHaveValue('');
    expect(within(search).getByRole('region', { name: 'Recent' })).toHaveTextContent('Butte');
    await userEvent.click(within(within(search).getByRole('region', { name: 'Try' })).getByRole('button', { name: 'birthday cake' }));
    expect(api.useSearch).toHaveBeenLastCalledWith('birthday cake');
    await userEvent.clear(field);
    expect(within(search).getByRole('region', { name: 'Recent' })).toHaveTextContent('birthday cakeButte');
    await userEvent.click(within(within(search).getByRole('region', { name: 'Recent' })).getByRole('button', { name: 'Butte' }));
    expect(field).toHaveValue('Butte');
    // An empty search is not kept.
    await userEvent.clear(field);
    await userEvent.type(field, '   {Enter}');
    expect(api.useSearch).toHaveBeenLastCalledWith('');
    expect(within(search).getByRole('region', { name: 'Recent' })).toHaveTextContent(/^RecentButtebirthday cake$/);
  });
});
