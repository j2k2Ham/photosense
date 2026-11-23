import React from 'react';

/**
 * One of Library's collections (26): a square, holding a cover or an icon, with its name and how many under it, as
 * the design draws Trips, Needs fixing and Hidden side by side.
 */
export function CollectionTile({ label, line, tone = 'fill', onOpen, children }: {
  readonly label: string;
  readonly line: string;
  /** The square's colour behind its icon: Hidden's is the deeper one. */
  readonly tone?: 'fill' | 'fill2';
  onOpen(): void;
  readonly children: React.ReactNode;
}) {
  return (
    <button type="button" onClick={onOpen} aria-label={`${label}, ${line}`} className="flex w-full min-w-0 flex-col gap-1.5 text-left">
      <span aria-hidden className={`relative grid aspect-square w-full place-items-center overflow-hidden rounded-[16px] ${tone === 'fill' ? 'bg-fill' : 'bg-fill2'}`}>{children}</span>
      <span className="flex min-w-0 flex-col">
        <span className="truncate text-[15px] font-semibold">{label}</span>
        <span className="truncate text-[13px] text-t2">{line}</span>
      </span>
    </button>
  );
}
