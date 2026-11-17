import React from 'react';

/** What PhotoSense does on a computer: clearing out duplicates, putting files into folders, and keeping phones' backups. */
export type Area = 'clean' | 'organize' | 'phones';

const areas: { id: Area; name: string }[] = [{ id: 'clean', name: 'Clean up' }, { id: 'organize', name: 'Organize' }, { id: 'phones', name: 'Phones' }];

/** The switch in the header between the areas; one that is new has a dot until it is first opened. */
export function ModeSwitch({ area, fresh = [], onArea }: { readonly area: Area; readonly fresh?: readonly Area[]; onArea(area: Area): void }) {
  return (
    <div role="group" aria-label="Area" className="flex shrink-0 rounded-full border border-line bg-s2 p-[3px]">
      {areas.map(a => (
        <button key={a.id} type="button" aria-pressed={area === a.id} onClick={() => onArea(a.id)}
          className={`pill h-8 gap-1.5 px-4 text-[13.5px] max-sm:px-3 ${area === a.id ? 'bg-t1 text-bg' : 'text-t2'}`}>
          {a.name}
          {fresh.includes(a.id) && <span role="img" aria-label="New" className="h-1.5 w-1.5 rounded-full bg-brand" />}
        </button>
      ))}
    </div>
  );
}

/** A row of choices of which one is on, in the same style as the header's switch. */
export function Segmented<T extends string | number>({ label, value, options, size = 'small', onChange }: {
  readonly label: string;
  readonly value: T;
  readonly options: readonly { readonly value: T; readonly name: React.ReactNode }[];
  readonly size?: 'small' | 'medium' | 'wide';
  onChange(value: T): void;
}) {
  // The medium size closes up on the smallest screens, so that a row of three choices still fits.
  const shape = { small: 'h-7 px-[13px] text-[12.5px]', medium: 'h-8 px-2.5 text-[12.5px] sm:px-3.5 sm:text-[13px]', wide: 'h-[34px] flex-1 text-[14px]' }[size];
  return (
    <div role="group" aria-label={label} className={`flex rounded-full border border-line p-[3px] ${size === 'small' ? 'bg-bg' : 'bg-s2'} ${size === 'wide' ? '' : 'shrink-0'}`}>
      {options.map(o => (
        <button key={o.value} type="button" aria-pressed={value === o.value} onClick={() => onChange(o.value)}
          className={`pill gap-1.5 sm:gap-[7px] ${shape} ${value === o.value ? 'bg-t1 text-bg' : 'text-t2'}`}>{o.name}</button>
      ))}
    </div>
  );
}
