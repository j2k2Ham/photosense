import React, { useEffect, useRef } from 'react';

interface Props {
  readonly label: string;
  /** Everything it stands for is selected. */
  readonly checked: boolean;
  /** Some of it is, and some is not: the box shows neither a tick nor nothing. */
  readonly partial: boolean;
  /** What it selects, said on hover. */
  readonly title: string;
  /** Asked to select everything (true) or to let everything go (false). */
  onChange(all: boolean): void;
}

/** The box that selects everything shown at once, or lets it all go. */
export function SelectAll({ label, checked, partial, title, onChange }: Props) {
  const box = useRef<HTMLInputElement>(null);
  // A box that is half on has no attribute to say so: it is told directly.
  useEffect(() => { box.current!.indeterminate = partial && !checked; }, [partial, checked]);
  return (
    <label title={title} className="flex cursor-pointer select-none items-center gap-2 whitespace-nowrap text-[13px] text-t2">
      <input ref={box} type="checkbox" checked={checked} onChange={e => onChange(e.target.checked)} className="h-4 w-4 accent-[var(--brand)]" />
      {label}
    </label>
  );
}
