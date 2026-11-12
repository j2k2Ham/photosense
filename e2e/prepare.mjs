// Makes the program the end-to-end tests drive, as publish.ps1 makes it: the service with the page inside it, which it
// hands out itself. The page is built in a copy of PhotoSense.ReactUI, so that a page server running there is left
// alone and that computer's own settings (.env.local, with any map key) stay out; the service's build files go into
// .cache too, never into the repository's bin and obj, from which a running PhotoSense may be started.
import { execSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { cpSync, existsSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { basename, dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const repo = dirname(here);
const cache = join(here, '.cache');
const page = join(cache, 'page');
const server = join(cache, 'server');

// What a copy of the page leaves behind: what is installed or built there, and this computer's own settings.
const LEFT = new Set(['node_modules', '.next', 'out', 'coverage', '.env.local', 'tsconfig.tsbuildinfo']);

// One command line, each part quoted: on Windows npm is a script that only a shell starts.
function run(command, args, cwd, env = {}) {
  const line = [command, ...args.map(a => (/[\s"]/.test(a) ? `"${a}"` : a))].join(' ');
  console.log(`> ${line}`);
  execSync(line, { cwd, stdio: 'inherit', env: { ...process.env, ...env } });
}

console.log('=== The page ===');
mkdirSync(page, { recursive: true });
for (const name of readdirSync(page)) if (name !== 'node_modules' && name !== '.installed') rmSync(join(page, name), { recursive: true, force: true });
const source = join(repo, 'PhotoSense.ReactUI');
cpSync(source, page, { recursive: true, filter: from => !relative(source, from).split(/[\\/]/).some(part => LEFT.has(part)) && basename(from) !== '.env.local' });
// Installed again only when what is to be installed has changed.
const lock = createHash('sha256').update(readFileSync(join(page, 'package-lock.json'))).digest('hex');
const installed = join(page, '.installed');
if (!existsSync(join(page, 'node_modules')) || !existsSync(installed) || readFileSync(installed, 'utf8') !== lock) {
  run('npm', ['ci', '--no-audit', '--no-fund'], page);
  writeFileSync(installed, lock);
}
// Served by the service itself, the page asks the address it came from; no map key, so the plain map is shown.
run('npm', ['run', 'build'], page, { NEXT_PUBLIC_API_BASE: '/api', NEXT_PUBLIC_GOOGLE_MAPS_KEY: '', NEXT_PUBLIC_GOOGLE_MAPS_ID: '' });

console.log('=== The service ===');
run('dotnet', ['publish', join(repo, 'PhotoSense.Server', 'PhotoSense.Server.csproj'), '-c', 'Release', '-o', server,
  '--artifacts-path', join(cache, 'artifacts'), '-nodeReuse:false'], repo);
rmSync(join(server, 'wwwroot'), { recursive: true, force: true });
cpSync(join(page, 'out'), join(server, 'wwwroot'), { recursive: true });
console.log(`\nThe program is in ${server}; npm test drives it.`);
