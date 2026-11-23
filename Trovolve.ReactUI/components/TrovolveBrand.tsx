import React from 'react';

// The Trovolve mark: five layers of a library forming one gem, in a 100-unit icon space (drawn here cropped to 17 21 66 60).
const LAYERS = [
  '35,23 28.32,30.52 71.68,30.52 65,23',
  '24.23,35.12 19,41 20.34,42.64 79.66,42.64 81,41 75.77,35.12',
  '24.09,47.24 30.23,54.76 69.77,54.76 75.91,47.24',
  '33.98,59.36 40.11,66.88 59.89,66.88 66.02,59.36',
  '43.87,71.48 50,79 56.13,71.48',
];
// Below 32px three thicker layers stay legible.
const SMALL_LAYERS = [
  '35,23 22.26,37.33 77.74,37.33 65,23',
  '21.31,43.83 33,58.17 67,58.17 78.69,43.83',
  '38.31,64.67 50,79 61.69,64.67',
];
const TONES = {
  color: ['#d1f2f8','#80dbeb','#3ebfc6','#1f9aa3','#007283'],
  deep: ['#3ebfc6','#1f9aa3','#007283','#00616d','#004f58'],
};
const SMALL_TONES = { color: ['#80dbeb', '#3ebfc6', '#007283'], deep: ['#1f9aa3', '#007283', '#004f58'] };

/** color: on teal black or the dark theme. deep: on light surfaces. mono: one color, the text color. */
export type MarkTone = 'color' | 'deep' | 'mono';

interface MarkProps {
  /** The height, in pixels. */
  readonly size?: number;
  readonly tone?: MarkTone;
  /** Said to a screen reader; without it the mark is decoration. */
  readonly label?: string;
  readonly className?: string;
}

/** The Trovolve mark, switching to the three-layer drawing below 32px. */
export function TrovolveMark({ size = 32, tone = 'color', label, className }: MarkProps) {
  const small = size < 32, layers = small ? SMALL_LAYERS : LAYERS, width = small ? 2.8 : 2.2;
  const colors = tone === 'mono' ? undefined : small ? SMALL_TONES[tone] : TONES[tone];
  return (
    <svg role={label ? 'img' : undefined} aria-label={label} aria-hidden={label ? undefined : true} width={(size * 66) / 60} height={size} viewBox="17 21 66 60" className={className}>
      {layers.map((p, i) => { const c = colors ? colors[i] : 'currentColor'; return <polygon key={i} points={p} fill={c} stroke={c} strokeWidth={width} strokeLinejoin="round" />; })}
    </svg>
  );
}

// The lowercase wordmark in Sora 600, outlined, so the app needs no extra font.
const WORDMARK = 'M39.30 0.70L31.50 0.70Q24 0.70 19.15-1.25Q14.30-3.20 11.90-7.85Q9.50-12.50 9.50-20.40L9.50-20.40L9.60-69.60L22.60-69.60L22.50-19.50Q22.50-15.50 24.65-13.35Q26.80-11.20 30.80-11.20L30.80-11.20L39.30-11.20L39.30 0.70ZM39.30-44.10L0.90-44.10L0.90-54.30L39.30-54.30L39.30-44.10ZM61.40 0L47.50 0L47.50-54.30L58.50-54.30L58.50-31.30L58.20-31.30Q58.20-43 63.20-49Q68.20-55 77.90-55L77.90-55L79.90-55L79.90-42.90L76.10-42.90Q69.10-42.90 65.25-39.15Q61.40-35.40 61.40-28.30L61.40-28.30L61.40 0ZM111.40 1.90L111.40 1.90Q104.20 1.90 98.70-0.40Q93.20-2.70 89.40-6.65Q85.60-10.60 83.65-15.60Q81.70-20.60 81.70-26L81.70-26L81.70-28.10Q81.70-33.70 83.75-38.75Q85.80-43.80 89.65-47.75Q93.50-51.70 99-53.95Q104.50-56.20 111.40-56.20L111.40-56.20Q118.30-56.20 123.80-53.95Q129.30-51.70 133.15-47.75Q137-43.80 139-38.75Q141-33.70 141-28.10L141-28.10L141-26Q141-20.60 139.05-15.60Q137.10-10.60 133.30-6.65Q129.50-2.70 124-0.40Q118.50 1.90 111.40 1.90ZM111.40-10L111.40-10Q116.50-10 120-12.25Q123.50-14.50 125.30-18.35Q127.10-22.20 127.10-27.10L127.10-27.10Q127.10-32.10 125.25-35.95Q123.40-39.80 119.85-42.05Q116.30-44.30 111.40-44.30L111.40-44.30Q106.50-44.30 102.95-42.05Q99.40-39.80 97.50-35.95Q95.60-32.10 95.60-27.10L95.60-27.10Q95.60-22.20 97.45-18.35Q99.30-14.50 102.80-12.25Q106.30-10 111.40-10ZM173.00 0L159.40 0L142.40-54.30L156.70-54.30L173.00 0ZM178.20 0L163.50 0L163.50-11.40L178.20-11.40L178.20 0ZM181.70 0L169.00 0L183.00-54.30L196.40-54.30L181.70 0ZM228.30 1.90L228.30 1.90Q221.10 1.90 215.60-0.40Q210.10-2.70 206.30-6.65Q202.50-10.60 200.55-15.60Q198.60-20.60 198.60-26L198.60-26L198.60-28.10Q198.60-33.70 200.65-38.75Q202.70-43.80 206.55-47.75Q210.40-51.70 215.90-53.95Q221.40-56.20 228.30-56.20L228.30-56.20Q235.20-56.20 240.70-53.95Q246.20-51.70 250.05-47.75Q253.90-43.80 255.90-38.75Q257.90-33.70 257.90-28.10L257.90-28.10L257.90-26Q257.90-20.60 255.95-15.60Q254.00-10.60 250.20-6.65Q246.40-2.70 240.90-0.40Q235.40 1.90 228.30 1.90ZM228.30-10L228.30-10Q233.40-10 236.90-12.25Q240.40-14.50 242.20-18.35Q244.00-22.20 244.00-27.10L244.00-27.10Q244.00-32.10 242.15-35.95Q240.30-39.80 236.75-42.05Q233.20-44.30 228.30-44.30L228.30-44.30Q223.40-44.30 219.85-42.05Q216.30-39.80 214.40-35.95Q212.50-32.10 212.50-27.10L212.50-27.10Q212.50-22.20 214.35-18.35Q216.20-14.50 219.70-12.25Q223.20-10 228.30-10ZM281.20 0L267.30 0L267.30-73L281.20-73L281.20 0ZM281.20-62.60L260.50-62.60L260.50-73L281.20-73L281.20-62.60ZM318.10 0L304.50 0L287.50-54.30L301.80-54.30L318.10 0ZM323.30 0L308.60 0L308.60-11.40L323.30-11.40L323.30 0ZM326.80 0L314.10 0L328.10-54.30L341.50-54.30L326.80 0ZM371.60 1.90L371.60 1.90Q364.60 1.90 359.35-0.50Q354.10-2.90 350.65-6.95Q347.20-11 345.45-16Q343.70-21 343.70-26.20L343.70-26.20L343.70-28.10Q343.70-33.50 345.45-38.55Q347.20-43.60 350.65-47.55Q354.10-51.50 359.20-53.85Q364.30-56.20 371.00-56.20L371.00-56.20Q379.80-56.20 385.75-52.35Q391.70-48.50 394.70-42.25Q397.70-36 397.70-28.80L397.70-28.80L397.70-23.80L349.60-23.80L349.60-32.30L389.00-32.30L384.70-28.10Q384.70-33.30 383.20-37Q381.70-40.70 378.65-42.70Q375.60-44.70 371.00-44.70L371.00-44.70Q366.40-44.70 363.20-42.60Q360.00-40.50 358.35-36.55Q356.70-32.60 356.70-27.10L356.70-27.10Q356.70-22 358.30-18.05Q359.90-14.10 363.20-11.85Q366.50-9.60 371.60-9.60L371.60-9.60Q376.70-9.60 379.90-11.65Q383.10-13.70 384.00-16.70L384.00-16.70L396.80-16.70Q395.60-11.10 392.20-6.90Q388.80-2.70 383.55-0.40Q378.30 1.90 371.60 1.90Z';

/** The wordmark, in the text color. */
export function TrovolveWordmark({ height = 24, className }: { readonly height?: number; readonly className?: string }) {
  return (
    <svg role="img" aria-label="trovolve" height={height} width={(height * 396.8) / 74.9} viewBox="0.9 -73 396.8 74.9" className={className}>
      <path d={WORDMARK} fill="currentColor" />
    </svg>
  );
}

// The launch animation: each layer flies in from a loose pile and settles, then the name fades up.
const SPREAD = ['translate(-10px,-8px) rotate(-7deg)','translate(9px,-4px) rotate(5deg)','translate(-7px,0px) rotate(-3deg)','translate(8px,4px) rotate(6deg)','translate(-6px,9px) rotate(-10deg)'];
const SPLASH_CSS = `
.tv-layer{transform-box:fill-box;transform-origin:center;animation:tv-settle .9s cubic-bezier(.2,.8,.2,1) both}
@keyframes tv-settle{from{opacity:0;transform:var(--tv-from)}to{opacity:1;transform:none}}
.tv-name{animation:tv-rise .6s cubic-bezier(.2,.8,.2,1) .55s both}
@keyframes tv-rise{from{opacity:0;transform:translateY(8px)}to{opacity:1;transform:none}}
@media (prefers-reduced-motion:reduce){.tv-layer,.tv-name{animation:none}}`;

/** The splash: the stacked lockup on teal black, played once. onDone is called when the name has faded up. */
export function TrovolveSplash({ size = 120, onDone }: { readonly size?: number; onDone?(): void }) {
  return (
    <div role="img" aria-label="trovolve. Your memories. Evolved." style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: size * 0.22 }}>
      <style>{SPLASH_CSS}</style>
      <svg aria-hidden width={(size * 66) / 60} height={size} viewBox="17 21 66 60" style={{ overflow: 'visible' }}>
        {LAYERS.map((p, i) => (
          <polygon key={i} className="tv-layer" points={p} fill={TONES.color[i]} stroke={TONES.color[i]} strokeWidth={2.2} strokeLinejoin="round"
            style={{ ['--tv-from' as string]: SPREAD[i], animationDelay: `${(4 - i) * 80}ms` } as React.CSSProperties} />
        ))}
      </svg>
      <div className="tv-name" onAnimationEnd={onDone} style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: size * 0.09, color: '#eff2f4' }}>
        <TrovolveWordmark height={size * 0.3} />
        <span className="font-sans" style={{ fontSize: size * 0.125, fontWeight: 500, color: '#80dbeb' }}>Your memories. Evolved.</span>
      </div>
    </div>
  );
}
