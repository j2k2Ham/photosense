import React from 'react';
import type { PhotoDto } from '../types';
import { differences } from '../lib/format';

/**
 * Spells out what sets a copy apart from its original. Two files of one picture can share a name, a
 * folder and a date, as a phone's HEIC and its JPEG conversion do, and then look like one file listed twice.
 */
export function Differences({ original, copy }: { readonly original: PhotoDto; readonly copy: PhotoDto }) {
  const found = differences(original, copy);
  return (
    <div>
      <div className="text-neutral-500">Differs from the original in</div>
      {found.length === 0
        ? <div>Nothing that is recorded about it.</div>
        : <ul className="list-disc pl-4">{found.map(d => <li key={d} className="break-words">{d}</li>)}</ul>}
    </div>
  );
}
