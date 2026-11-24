import React, { useEffect, useState } from 'react';
import { TrovolveSplash } from './TrovolveBrand';
import { LAUNCH_FADE, LAUNCH_SECONDS, launchedBefore } from '../lib/launch';

/**
 * The launch: the brand's animation on teal black over everything when the app first loads, drawn before anything
 * else, staying `LAUNCH_SECONDS` before it fades to show the app; a reload does not play it again (Jamie, 2026-11-24).
 * On a phone it is the native launch screens' colour, so the opening runs on from them.
 */
export function Launch() {
  const [stage, setStage] = useState<'playing' | 'leaving' | 'gone'>('playing');
  useEffect(() => {
    if (stage === 'gone') return;
    // A reload is hidden before it is painted (`data-launched`, set by the layout); here it goes at once.
    const next = stage === 'leaving' ? 'gone' : launchedBefore() ? 'gone' : 'leaving';
    const wait = stage === 'leaving' ? LAUNCH_FADE : next === 'gone' ? 0 : LAUNCH_SECONDS * 1000 - LAUNCH_FADE;
    const id = setTimeout(() => setStage(next), wait);
    return () => clearTimeout(id);
  }, [stage]);
  if (stage === 'gone') return null;
  return (
    <div data-launch className={`fixed inset-0 z-[60] grid place-items-center bg-[var(--launch)] transition-opacity duration-300 ${stage === 'leaving' ? 'opacity-0' : ''}`}>
      <TrovolveSplash />
    </div>
  );
}
