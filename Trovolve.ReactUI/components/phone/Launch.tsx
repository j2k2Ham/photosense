import React, { useEffect, useState } from 'react';
import { TrovolveSplash } from '../TrovolveBrand';

/** How long the launch may play at most: with reduced motion nothing plays, and no animation says when it ends. */
export const LAUNCH_LONGEST = 1600;

/** How long the launch takes to fade once played. */
export const LAUNCH_FADE = 300;

/**
 * The phone app's launch: the brand's animation on teal black over everything as the app opens, the native launch
 * screens' colour, so the opening runs on from them; then it fades and goes. Played once, as the app opens.
 */
export function Launch() {
  const [stage, setStage] = useState<'playing' | 'leaving' | 'gone'>('playing');
  useEffect(() => {
    if (stage === 'gone') return;
    const id = setTimeout(() => setStage(stage === 'playing' ? 'leaving' : 'gone'), stage === 'playing' ? LAUNCH_LONGEST : LAUNCH_FADE);
    return () => clearTimeout(id);
  }, [stage]);
  if (stage === 'gone') return null;
  return (
    <div className={`fixed inset-0 z-[60] grid place-items-center bg-[var(--launch)] transition-opacity duration-300 ${stage === 'leaving' ? 'opacity-0' : ''}`}>
      <TrovolveSplash onDone={() => setStage('leaving')} />
    </div>
  );
}
