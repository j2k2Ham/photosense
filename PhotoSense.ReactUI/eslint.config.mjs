import jsxA11y from 'eslint-plugin-jsx-a11y';
import nextVitals from 'eslint-config-next/core-web-vitals';
import nextTypescript from 'eslint-config-next/typescript';

const config = [
  ...nextVitals,
  ...nextTypescript,
  // Next turns on a handful of the accessibility rules; the page is held to all of them (the plugin is Next's own).
  { rules: jsxA11y.flatConfigs.strict.rules },
  {
    rules: {
      // Pictures come from the service or the phone, which next/image cannot reach in a page made of plain files.
      '@next/next/no-img-element': 'off',
      // A label's own text may sit in the spans that lay out its title and explanation.
      'jsx-a11y/label-has-associated-control': ['error', { depth: 3 }],
      // Taking a value out of an object with ...rest names it without using it, to leave it out of the rest.
      '@typescript-eslint/no-unused-vars': ['warn', { ignoreRestSiblings: true }],
      // These two keep a component ready for React's compiler: state is not set straight from an effect, nor a ref
      // read or written while drawing (Jamie, 2026-11-17: every warning fixed, and none let back in).
      'react-hooks/set-state-in-effect': 'error',
      'react-hooks/refs': 'error',
    },
  },
  { ignores: ['.next/**', 'out/**', 'coverage/**', 'next-env.d.ts'] },
];

export default config;
