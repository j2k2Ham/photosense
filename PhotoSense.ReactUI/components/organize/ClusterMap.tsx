import React from 'react';
import type { MapLayout } from '../../lib/organize';
import { EnlargeIcon } from './shared';

interface Props {
  readonly map: MapLayout;
  readonly focus?: string;
  /** What is said under the map; nothing when the window it is in says it. */
  readonly note?: string;
  /** Drawn as large as the space it is in allows, with every dot labeled. */
  readonly large?: boolean;
  onFocus(key?: string): void;
  /** Opens the map large: on a click of the map itself, or of the button in its corner. */
  onEnlarge?(): void;
}

/** The suggested folders as dots on a plain map, with the reach places were combined within. */
export function ClusterMap({ map, focus, note, large = false, onFocus, onEnlarge }: Props) {
  const at = (p: { x: number; y: number }) => ({ left: `${p.x}%`, top: `${p.y}%` });
  // Width over height, kept within what still looks like a map.
  const shape = 1 / Math.min(1.45, Math.max(0.6, map.aspect));
  return (
    <div className={large ? 'flex h-full w-full items-center justify-center [container-type:size]' : 'flex flex-col items-center gap-2.5 pb-2'}>
      {/* A click on the map itself, as well as the button in its corner, opens it large. */}
      {/* eslint-disable-next-line jsx-a11y/click-events-have-key-events, jsx-a11y/no-static-element-interactions */}
      <div role="group" aria-label={large ? 'Large map of the suggested folders' : 'Map of the suggested folders'}
        // As wide as the space allows without growing taller than it.
        style={large ? { aspectRatio: shape, width: `min(100cqw, calc(100cqh * ${shape}))` } : { aspectRatio: shape }}
        onClick={e => { if (onEnlarge && !(e.target as Element).closest('button')) onEnlarge(); }}
        className={`relative overflow-hidden rounded-[14px] border border-line bg-stage bg-[linear-gradient(var(--line)_1px,transparent_1px),linear-gradient(90deg,var(--line)_1px,transparent_1px)] bg-[length:20%_20%] ${large ? '' : 'w-full'} ${onEnlarge ? 'cursor-zoom-in' : ''}`}>
        {map.rings.map((r, i) => (
          <span key={i} aria-hidden style={{ ...at(r), width: `${r.diameter}%` }} className="absolute aspect-square -translate-x-1/2 -translate-y-1/2 rounded-full border border-dashed border-brand bg-ident-bg" />
        ))}
        {map.merged.map((m, i) => <span key={i} aria-hidden style={at(m)} className="absolute -ml-[3px] -mt-[3px] h-1.5 w-1.5 rounded-full bg-t2" />)}
        {map.dots.map(d => {
          const on = d.key === focus;
          return (
            <span key={d.key} style={{ ...at(d), zIndex: on ? 3 : 2 }} className="absolute h-0 w-0">
              <button type="button" title={d.tip} aria-label={d.tip} aria-pressed={on} onClick={() => onFocus(on ? undefined : d.key)}
                style={{ width: d.size, height: d.size, left: -d.size / 2, top: -d.size / 2 }}
                className={`absolute rounded-full bg-brand ${on ? 'shadow-[0_0_0_3px_var(--stage),0_0_0_5px_var(--brand)]' : 'opacity-75 shadow-[0_0_0_2px_var(--stage)]'}`} />
              {(on || d.labelled || large) && (
                <span aria-hidden style={{ [d.labelSide === 'left' ? 'right' : 'left']: d.size / 2 + 6, top: -13 }}
                  className={`pointer-events-none absolute whitespace-nowrap rounded-full border bg-pop px-[9px] py-[3px] text-[12px] shadow-[0_4px_14px_var(--shadow)] ${on ? 'border-brand font-semibold' : 'border-line font-medium'}`}>{d.label}</span>
              )}
            </span>
          );
        })}
        <span aria-hidden style={{ width: `${map.scale.percent}%` }} className="absolute bottom-3 left-3 flex flex-col gap-1 text-[11px] text-t3">
          <span className="h-1 border border-t-0 border-t3" />{map.scale.miles} mi
        </span>
        {onEnlarge && (
          <button type="button" aria-label="Enlarge the map" title="Enlarge the map" onClick={onEnlarge}
            className="absolute right-2.5 top-2.5 z-[4] flex h-8 w-8 items-center justify-center rounded-full border border-line bg-pop text-t1 shadow-pop hover:bg-s2"><EnlargeIcon /></button>
        )}
      </div>
      {note && <p className="text-center text-[12.5px] text-t3">{note}</p>}
    </div>
  );
}
