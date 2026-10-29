import type { Config } from 'tailwindcss';

// Every colour is a design token defined in app/globals.css, once for the dark theme and once for the light.
const tokens = ['bg', 's1', 's2', 's3', 'pop', 'line', 't1', 't2', 't3', 'stage', 'brand', 'on-brand', 'sel', 'keep', 'keep-bg', 'keep-line',
  'rose', 'rose-t', 'rose-bg', 'rose-line', 'amber', 'amber-bg', 'diff', 'ident', 'ident-bg', 'same', 'same-bg', 'scrim'];

const config: Config = {
  darkMode: 'class',
  content: [
    './app/**/*.{js,ts,jsx,tsx}',
    './components/**/*.{js,ts,jsx,tsx}',
    './lib/**/*.{js,ts,jsx,tsx}'
  ],
  theme: {
    extend: {
      colors: Object.fromEntries(tokens.map(t => [t, `var(--${t})`])),
      fontFamily: {
        sans: ['var(--font-sans)', 'system-ui', 'sans-serif'],
        mono: ['var(--font-mono)', 'ui-monospace', 'monospace']
      },
      boxShadow: {
        pop: '0 18px 50px var(--shadow)',
        dialog: '0 30px 90px var(--shadow)',
        toast: '0 14px 40px var(--shadow)',
        setup: '0 10px 40px var(--shadow)',
        selected: '0 0 0 3px var(--s1), 0 0 0 5px var(--brand)'
      }
    }
  },
  plugins: []
};

export default config;
