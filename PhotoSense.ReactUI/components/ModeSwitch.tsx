import React from 'react';

/** The two things PhotoSense does: clearing out duplicates, and putting files into folders. */
export type Area = 'clean' | 'organize';

const areas: { id: Area; name: string }[] = [{ id: 'clean', name: 'Clean up' }, { id: 'organize', name: 'Organize' }];

/** The switch in the header between the two areas. */
export function ModeSwitch({ area, onArea }: { readonly area: Area; onArea(area: Area): void }) {
  return (
    <div role="group" aria-label="Area" className="flex shrink-0 rounded-full border border-line bg-s2 p-[3px]">
      {areas.map(a => (
        <button key={a.id} type="button" aria-pressed={area === a.id} onClick={() => onArea(a.id)}
          className={`pill h-8 px-4 text-[13.5px] ${area === a.id ? 'bg-t1 text-bg' : 'text-t2'}`}>{a.name}</button>
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
  const shape = { small: 'h-7 px-[13px] text-[12.5px]', medium: 'h-8 px-3.5 text-[13px]', wide: 'h-[34px] flex-1 text-[14px]' }[size];
  return (
    <div role="group" aria-label={label} className={`flex rounded-full border border-line p-[3px] ${size === 'small' ? 'bg-bg' : 'bg-s2'} ${size === 'wide' ? '' : 'shrink-0'}`}>
      {options.map(o => (
        <button key={o.value} type="button" aria-pressed={value === o.value} onClick={() => onChange(o.value)}
          className={`pill gap-[7px] ${shape} ${value === o.value ? 'bg-t1 text-bg' : 'text-t2'}`}>{o.name}</button>
      ))}
    </div>
  );
}
