import type { Config } from 'tailwindcss';

const config: Config = {
  darkMode: 'class',
  content: [
    './app/**/*.{js,ts,jsx,tsx}',
    './components/**/*.{js,ts,jsx,tsx}'
  ],
  theme: {
    extend: {
      colors: {
        brand: {
          50: '#eef7ff',
          100: '#d9edff',
          200: '#b8ddff',
          300: '#85c7ff',
          400: '#4da9ff',
          500: '#1d86ff',
          600: '#0065e4',
          700: '#004fb6',
          800: '#003f8f',
          900: '#003571'
        }
      }
    }
  },
  plugins: []
};

export default config;
