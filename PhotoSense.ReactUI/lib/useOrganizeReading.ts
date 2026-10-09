import { useEffect, useState } from 'react';
import type { OrganizeFileDto, OrganizeFilesDto, OrganizePlaceDto } from '../types';
import { fetchOrganizeProgress } from './apiClient';

/** How long to wait before asking the service again what it has read, while there is little to show. */
export const ASK_EVERY_MS = 800;
/** A long list takes longer to arrange on screen each time it grows, so it is asked about less often: later by this much at most. */
export const ASK_LATER_BY_MS = 3200;

/** A folder as far as it has been read. */
export interface OrganizeReading {
  /** The service is going through the files now; no longer so once it has been through them all and is putting its answer together. */
  readonly reading: boolean;
  /** Pictures and videos in the folder. */
  readonly total: number;
  /** How many of them have been gone through. */
  readonly done: number;
  /** The files read so far, in the order they were read; absent until there is one. */
  readonly listing?: OrganizeFilesDto;
}

/**
 * What the service has read of a folder so far, for as long as the folder is being read: asked for again
 * and again, each time only for what has been read since. Undefined until the service has begun.
 */
export function useOrganizeReading(root: string | undefined, reading: boolean): OrganizeReading | undefined {
  const [soFar, setSoFar] = useState<OrganizeReading & { root: string }>();

  useEffect(() => {
    // What was read of another folder, or before the whole list came, is of no more use.
    setSoFar(undefined);
    if (!root || !reading) return undefined;
    let stopped = false, timer: ReturnType<typeof setTimeout> | undefined;
    let files: OrganizeFileDto[] = [], places: OrganizePlaceDto[] = [], listing: OrganizeFilesDto | undefined, readingId: string | null | undefined;

    async function ask(folder: string) {
      let wait = ASK_EVERY_MS;
      try {
        const answer = await fetchOrganizeProgress(folder, files.length, places.length);
        if (stopped) return;
        if (!answer.reading) {
          setSoFar(was => was && { ...was, reading: false });
        } else if (answer.readingId !== readingId && files.length > 0) {
          // The service has begun reading the folder anew: what was had is no part of that, so it is asked for from the beginning.
          files = []; places = []; listing = undefined; wait = 0;
        } else {
          readingId = answer.readingId;
          if (answer.files.length > 0) {
            files = files.concat(answer.files);
            places = places.concat(answer.places);
            listing = { root: folder, fromScan: false, places, files };
          }
          setSoFar({ root: folder, reading: true, total: answer.total, done: answer.done, listing });
          wait = answer.more ? 0 : ASK_EVERY_MS + Math.min(ASK_LATER_BY_MS, Math.round(files.length / 15));
        }
      } catch {
        // The listing itself says what went wrong, if something did; this only watches it being made.
      }
      if (!stopped) timer = setTimeout(() => void ask(folder), wait);
    }
    void ask(root);
    return () => { stopped = true; clearTimeout(timer); };
  }, [root, reading]);

  return soFar?.root === root ? soFar : undefined;
}
