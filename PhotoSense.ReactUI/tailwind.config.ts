import type { Config } from 'tailwindcss';

// Every colour is a design token defined in app/globals.css, once for the dark theme and once for the light.
const tokens = ['bg', 's1', 's2', 's3', 'pop', 'line', 't1', 't2', 't3', 'stage', 'brand', 'on-brand', 'sel', 'keep', 'keep-bg', 'keep-line',
  'rose', 'rose-t', 'rose-bg', 'rose-line', 'amber', 'amber-bg', 'diff', 'ident', 'ident-bg', 'same', 'same-bg', 'scrim'];
// The phone and tablet skins' own colours, set only where a skin is (data-skin on <html>); the names they share with
// the desktop's (bg, t1 to t3, stage, sel, keep, amber, ident, ident-bg, scrim) are in the list above.
const skin = ['cell', 'fill', 'fill2', 'sep', 'acc', 'on-acc', 'acc-soft', 'on-acc-soft', 'danger', 'on-danger', 'danger-t', 'danger-soft',
  'on-keep', 'keep-soft', 'amber-soft', 'bar', 'bar-hi', 'bar-on', 'bar-off', 'side', 'rail', 'sheet', 'sheet-cell',
  'toast', 'on-toast', 'toast-act', 'btn2-bg', 'btn2-fg', 'seg-bg', 'seg-bd', 'seg-on', 'seg-on-fg', 'chip-bg', 'chip-bd', 'chip-on-bg', 'chip-on-fg',
  'ib-bg', 'tb-fg', 'grab', 'sw-on', 'sw-off', 'map', 'map-line', 'input', 'alert'];

const config: Config = {
  darkMode: 'class',
  content: [
    './app/**/*.{js,ts,jsx,tsx}',
    './components/**/*.{js,ts,jsx,tsx}',
    './lib/**/*.{js,ts,jsx,tsx}'
  ],
  theme: {
    extend: {
      colors: Object.fromEntries([...tokens, ...skin].map(t => [t, `var(--${t})`])),
      fontFamily: {
        sans: ['var(--font-sans)', 'system-ui', 'sans-serif'],
        mono: ['var(--font-mono)', 'ui-monospace', 'monospace'],
        // A skin's own type: its text, its titles and its file names.
        ui: 'var(--font-ui)',
        title: 'var(--font-title)',
        code: 'var(--font-code)'
      },
      borderRadius: {
        cell: 'var(--r-cell)',
        sheet: 'var(--r-sheet)',
        toast: 'var(--r-toast)',
        dialog: 'var(--r-dialog)',
        chip: 'var(--r-chip)'
      },
      // The phone layout's motion: a screen pushed in, a sheet or toast rising, a fade, a question popping up, a pulse.
      keyframes: {
        'push-in': { from: { transform: 'translateX(44px)', opacity: '.3' }, to: { transform: 'none', opacity: '1' } },
        'rise': { from: { transform: 'translateY(40px)', opacity: '0' }, to: { transform: 'none', opacity: '1' } },
        'fade-in': { from: { opacity: '0' }, to: { opacity: '1' } },
        'pop': { from: { transform: 'scale(1.06)', opacity: '0' }, to: { transform: 'none', opacity: '1' } },
        'pulse-ring': { '0%': { transform: 'scale(.6)', opacity: '.6' }, '100%': { transform: 'scale(1.45)', opacity: '0' } }
      },
      animation: {
        'push-in': 'push-in .28s cubic-bezier(.2,.8,.2,1)',
        'sheet-in': 'rise .3s cubic-bezier(.2,.8,.2,1)',
        'toast-in': 'rise .25s cubic-bezier(.2,.8,.2,1)',
        'fade-in': 'fade-in .2s',
        'pop': 'pop .2s',
        'pulse-ring': 'pulse-ring 2.2s ease-out infinite'
      },
      boxShadow: {
        pop: '0 18px 50px var(--shadow)',
        dialog: '0 30px 90px var(--shadow)',
        toast: '0 14px 40px var(--shadow)',
        setup: '0 10px 40px var(--shadow)',
        selected: '0 0 0 3px var(--s1), 0 0 0 5px var(--brand)',
        bar: 'var(--bar-sh)',
        ib: 'var(--ib-sh)',
        seg: 'var(--seg-sh)'
      }
    }
  },
  plugins: []
};

export default config;
