// Writes the third-party notices each program builds in and shows under About (notices/server.txt, iphone.txt, android.txt):
// everything it ships that others made, with each licence in full. Run it after adding or updating a package;
// --check fails while the notices are out of date, so that a program is never shipped with notices that are wrong.
import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const DOTNET = 'Copyright (c) .NET Foundation and Contributors';

/** What each program is built from: the projects whose packages it ships, and the platform it runs on. */
export const PROGRAMS = {
  server: { title: 'PhotoSense for Windows and Mac', projects: ['PhotoSense.Server', 'PhotoSense.Desktop', 'PhotoSense.Infrastructure', 'PhotoSense.Api'], platform: ['dotnet', 'aspnetcore'] },
  iphone: { title: 'PhotoSense for iPhone and iPad', projects: ['PhotoSense.Infrastructure', 'PhotoSense.Api'], references: 'PhotoSense.iOS/PhotoSense.iOS.csproj', platform: ['dotnet', 'ios'] },
  android: { title: 'PhotoSense for Android', projects: ['PhotoSense.Infrastructure', 'PhotoSense.Api'], references: 'PhotoSense.Android/PhotoSense.Android.csproj', platform: ['dotnet', 'android'] },
};

const PLATFORM = {
  dotnet: { name: '.NET', license: 'MIT', copyright: DOTNET, more: 'https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT' },
  aspnetcore: { name: 'ASP.NET Core', license: 'MIT', copyright: DOTNET, more: 'https://github.com/dotnet/aspnetcore/blob/main/THIRD-PARTY-NOTICES.txt' },
  ios: { name: '.NET for iOS', license: 'MIT', copyright: DOTNET, more: 'https://github.com/dotnet/macios/blob/main/LICENSE' },
  android: { name: '.NET for Android', license: 'MIT', copyright: DOTNET, more: 'https://github.com/dotnet/android/blob/main/LICENSE' },
};

// Packages that name their licence without carrying its text, or carry a text that names no copyright holder.
const NAMED_ONLY = {
  xmpcore: { license: 'BSD-3-Clause', copyright: 'Copyright 2006-2007 Adobe Systems Incorporated (the XMP library it is ported from); copyright 2015-2021 Drew Noakes (the port)' },
};
const isMicrosoft = id => /^microsoft\./i.test(id);

// Tailwind is a build tool, but the page's styles are written by it, its reset included.
const SHIPPED_FROM_BUILD = ['tailwindcss'];

const LICENCE_FILE = /^(licen[cs]e|copying)(\.(md|txt))?$/i;
const NOTICE_FILE = /^notice(\.(md|txt))?$/i;

const read = path => readFileSync(path, 'utf8').replace(/\r\n/g, '\n').trim();
const fileIn = (dir, pattern) => (existsSync(dir) ? readdirSync(dir).find(f => pattern.test(f)) : undefined);

/** The licence's own text from beside the package, or the standard text of the licence it names, with its copyright. */
export function licenceText({ dir, license, copyright, templates }) {
  const file = fileIn(dir, LICENCE_FILE);
  if (file) return read(join(dir, file));
  if (!templates[license]) throw new Error(`No text for the licence "${license}" of ${dir}: add tools/licenses/${license}.txt or a NAMED_ONLY entry.`);
  return templates[license].replace('{{copyright}}', copyright ?? '');
}

/** The packages a project ships: those with files that run, from its restore (obj/project.assets.json). */
export function nugetPackages(assets) {
  const runs = lib => [...Object.keys(lib.runtime ?? {}), ...Object.keys(lib.native ?? {})].some(file => !file.endsWith('_._'));
  return Object.entries(Object.values(assets.targets)[0])
    .filter(([, lib]) => lib.type === 'package' && runs(lib))
    .map(([key]) => { const [id, version] = key.split('/'); return { id, version }; });
}

/** The packages a project file names itself, for a project that cannot be restored here (the iPhone app's). */
export function referencedPackages(csproj) {
  return [...csproj.matchAll(/<PackageReference Include="([^"]+)" Version="([^"]+)"/g)].map(([, id, version]) => ({ id, version }));
}

/** What the page ships: its packages and theirs, but none of the tools that only build it, and the styles Tailwind writes. */
export function pagePackages(lock) {
  return Object.entries(lock.packages)
    .filter(([path, entry]) => path && ((!entry.dev && !entry.optional && !entry.devOptional) || SHIPPED_FROM_BUILD.includes(path.replace(/^.*node_modules\//, ''))))
    .map(([path, entry]) => ({ name: path.replace(/^.*node_modules\//, ''), path, version: entry.version, license: entry.license }));
}

function nugetEntry({ id, version }, nugetRoot, templates) {
  const dir = join(nugetRoot, id.toLowerCase(), version);
  const nuspecPath = join(dir, `${id.toLowerCase()}.nuspec`);
  const nuspec = existsSync(nuspecPath) ? readFileSync(nuspecPath, 'utf8') : '';
  const own = NAMED_ONLY[id.toLowerCase()];
  const declared = /<license type="expression">([^<]+)</.exec(nuspec)?.[1] ?? /<licenseUrl>([^<]+)</.exec(nuspec)?.[1] ?? 'its own licence';
  const license = own?.license ?? (isMicrosoft(id) ? 'MIT' : declared);
  const copyright = own?.copyright ?? (isMicrosoft(id) ? DOTNET : /<copyright>([^<]*)</.exec(nuspec)?.[1]);
  // Microsoft's packages carry the .NET notices again; they are named once, under .NET.
  const text = isMicrosoft(id) || own ? templates[license].replace('{{copyright}}', copyright) : licenceText({ dir, license, copyright, templates });
  const notice = isMicrosoft(id) ? undefined : fileIn(dir, NOTICE_FILE);
  return { name: `${id} ${version}`, license, copyright, text, notice: notice && read(join(dir, notice)) };
}

/** Every notice of one program, as plain text. */
export function noticesFor(program, { root, nugetRoot, templates }) {
  const { title, projects, references, platform } = PROGRAMS[program];
  const packages = new Map();
  for (const project of projects) for (const p of nugetPackages(JSON.parse(read(join(root, project, 'obj', 'project.assets.json'))))) packages.set(`${p.id}/${p.version}`.toLowerCase(), p);
  if (references) for (const p of referencedPackages(read(join(root, references)))) packages.set(`${p.id}/${p.version}`.toLowerCase(), p);
  const reactRoot = join(root, 'PhotoSense.ReactUI');
  const entries = [
    ...platform.map(key => ({ ...PLATFORM[key], name: PLATFORM[key].name, text: templates[PLATFORM[key].license].replace('{{copyright}}', PLATFORM[key].copyright) })),
    ...[...packages.values()].sort((a, b) => a.id.localeCompare(b.id)).map(p => nugetEntry(p, nugetRoot, templates)),
    ...pagePackages(JSON.parse(read(join(reactRoot, 'package-lock.json')))).sort((a, b) => a.name.localeCompare(b.name))
      .map(p => ({ name: `${p.name} ${p.version}`, license: p.license, text: licenceText({ dir: join(reactRoot, p.path), license: p.license, templates }) })),
  ];
  // The page's fonts, which Next downloads when the page is built and the page then carries (app/layout.tsx).
  const fontsDir = join(root, 'tools', 'fonts');
  const fonts = readdirSync(fontsDir).sort().map(f => ({ name: f.replace(/\.txt$/, ''), text: read(join(fontsDir, f)) }));
  const rule = '-'.repeat(72);
  return [
    `Third-party notices for ${title}`,
    '',
    'PhotoSense includes the work of others, listed here with the licence each is used under. Each licence is given in full below.',
    '',
    'PLACE NAMES',
    '',
    'GeoNames (https://www.geonames.org/), under the Creative Commons Attribution 4.0 International licence',
    '(https://creativecommons.org/licenses/by/4.0/). PhotoSense uses extracts of it, changed by PhotoSense: towns of',
    '1,000 people or more with the names of their regions, and sights, parks, beaches and districts, each with its position.',
    '',
    'FONTS',
    '',
    ...fonts.map(f => `${f.name} (SIL Open Font License 1.1)`),
    ...fonts.flatMap(f => ['', rule, `${f.name} (SIL Open Font License 1.1)`, rule, '', f.text]),
    '',
    'SOFTWARE',
    '',
    ...entries.map(e => `${e.name} (${e.license})`),
    ...entries.flatMap(e => [
      '', rule, `${e.name} (${e.license})`,
      // A licence such as Apache 2.0 has no line for the copyright, which the package gives elsewhere.
      ...(e.copyright && !e.text.includes(e.copyright) ? [e.copyright] : []),
      ...(e.more ? [`Its own third-party notices: ${e.more}`] : []),
      rule, '', e.text, ...(e.notice ? ['', e.notice] : []),
    ]),
    '',
  ].join('\n');
}

export function readTemplates(dir) {
  return Object.fromEntries(readdirSync(dir).map(f => [f.replace(/\.txt$/, ''), read(join(dir, f))]));
}

/** node tools/notices.mjs [--check] */
export function main(argv, { root, nugetRoot = process.env.NUGET_PACKAGES || join(homedir(), '.nuget', 'packages'), print = console.log, fail = console.error }) {
  const check = argv.includes('--check');
  const templates = readTemplates(join(root, 'tools', 'licenses'));
  const stale = [];
  try {
    for (const program of Object.keys(PROGRAMS)) {
      const path = join(root, 'notices', `${program}.txt`);
      const text = noticesFor(program, { root, nugetRoot, templates });
      if (check) { if (!existsSync(path) || read(path) !== text.trim()) stale.push(path); }
      else { mkdirSync(join(root, 'notices'), { recursive: true }); writeFileSync(path, text); print(`Wrote ${path}`); }
    }
  } catch (error) {
    fail(`The third-party notices: ${error.message}`);
    return 1;
  }
  if (stale.length) { fail(`The third-party notices are out of date (${stale.join(', ')}): run node tools/notices.mjs and commit them.`); return 1; }
  return 0;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.exitCode = main(process.argv.slice(2), { root: join(dirname(fileURLToPath(import.meta.url)), '..') });
}
