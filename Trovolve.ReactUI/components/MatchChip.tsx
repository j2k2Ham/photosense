import React from 'react';
import type { MatchKind } from '../types';
import { matchLabel } from '../lib/format';

const tone: Record<MatchKind | 'keeping', string> = {
  identical: 'bg-ident-bg text-ident',
  samePicture: 'bg-same-bg text-same',
  similar: 'bg-amber-bg text-amber',
  keeping: 'bg-keep-bg text-keep',
};

/** How sure a match is, or that the copy is being kept, as a small coloured label. */
export function MatchChip({ match, kept = false }: { readonly match: MatchKind; readonly kept?: boolean }) {
  return <span className={`badge ${tone[kept ? 'keeping' : match]}`}>{kept ? 'Keeping' : matchLabel[match]}</span>;
}
