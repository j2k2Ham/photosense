import { act, renderHook } from '@testing-library/react';
import { expect, it, vi } from 'vitest';
import { reply } from '../fixtures';
import { useScanLogs } from '../../lib/useScanLogs';

// The service's address is read once, when the module loads, so it is set before anything is imported.
vi.hoisted(() => vi.stubEnv('NEXT_PUBLIC_API_BASE', 'https://photos.example/api'));

it('asks for logs beside wherever the service lives', async () => {
  vi.useFakeTimers();
  const fetchMock = vi.fn<typeof fetch>(async () => reply({ items: [] }));
  vi.stubGlobal('fetch', fetchMock);

  const { unmount } = renderHook(() => useScanLogs({ disableSignalR: true }));
  await act(async () => { await vi.advanceTimersByTimeAsync(0); });
  unmount();

  expect(fetchMock.mock.calls.map(c => c[0])).toEqual(['https://photos.example/api/scan/logs?limit=200']);
});
