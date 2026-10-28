// node tools/version.mjs [--part server|iphone] [--release] [--format msbuild|json|text]: see versioning.mjs.
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { main } from './versioning.mjs';

process.exitCode = main(process.argv.slice(2), { root: join(dirname(fileURLToPath(import.meta.url)), '..') });
