import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { cpSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { after, describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';
import { licenceText, main, nugetPackages, pagePackages, referencedPackages } from '../notices.mjs';

const repo = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const made = [];
after(() => { for (const dir of made) rmSync(dir, { recursive: true, force: true }); });

function write(path, text) {
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, text);
}

const assets = packages => JSON.stringify({ targets: { 'net10.0': packages } });
const dll = { type: 'package', runtime: { 'lib/net10.0/x.dll': {} } };

/** A repository and NuGet folder of made-up packages, one for each way a package can give its licence. */
function fixture({ unknownLicence = false } = {}) {
  const root = mkdtempSync(join(tmpdir(), 'photosense-notices-'));
  made.push(root);
  const nuget = join(root, 'nuget');
  cpSync(join(repo, 'tools', 'licenses'), join(root, 'tools', 'licenses'), { recursive: true });
  write(join(root, 'tools', 'fonts', 'Sans.txt'), 'Copyright the Sans authors.\nSIL Open Font License');
  write(join(root, 'tools', 'fonts', 'Mono.txt'), 'Copyright the Mono authors.\nSIL Open Font License');
  const server = {
    'Alpha/1.0.0': dll,
    'Beta/2.0.0': dll,
    'Microsoft.Gamma/3.0.0': dll,
    'XmpCore/6.1.10.1': dll,
    'Native.Thing/1.0.0': { type: 'package', native: { 'runtimes/win-x64/native/thing.dll': {} } },
    'Linked/1.0.0': dll,
    'Refs.Only/1.0.0': { type: 'package', runtime: { 'lib/net10.0/_._': {} } },
    'Build.Only/1.0.0': { type: 'package' },
    'PhotoSense.Domain/1.0.0': { type: 'project' },
    ...(unknownLicence ? { 'Odd/1.0.0': dll } : {}),
  };
  for (const project of ['PhotoSense.Server', 'PhotoSense.Desktop']) write(join(root, project, 'obj', 'project.assets.json'), assets(server));
  for (const project of ['PhotoSense.Infrastructure', 'PhotoSense.Api']) write(join(root, project, 'obj', 'project.assets.json'), assets({ 'Alpha/1.0.0': dll }));
  write(join(root, 'PhotoSense.iOS', 'PhotoSense.iOS.csproj'), '<PackageReference Include="Microsoft.Missing" Version="1.0.0" />');
  write(join(nuget, 'alpha', '1.0.0', 'alpha.nuspec'), '<license type="expression">MIT</license><copyright>Copyright Alpha</copyright>');
  write(join(nuget, 'beta', '2.0.0', 'beta.nuspec'), '<license type="expression">Apache-2.0</license><copyright>Copyright Beta</copyright>');
  write(join(nuget, 'beta', '2.0.0', 'LICENSE.txt'), 'The Beta licence.\r\n');
  write(join(nuget, 'beta', '2.0.0', 'NOTICE.txt'), 'Beta includes Delta.');
  write(join(nuget, 'microsoft.gamma', '3.0.0', 'THIRD-PARTY-NOTICES.TXT'), 'Not repeated.');
  write(join(nuget, 'linked', '1.0.0', 'linked.nuspec'), '<licenseUrl>https://linked.example</licenseUrl>');
  write(join(nuget, 'linked', '1.0.0', 'LICENSE.md'), 'The Linked licence.');
  write(join(nuget, 'native.thing', '1.0.0', 'License'), 'The Thing licence.');
  write(join(nuget, 'odd', '1.0.0', 'odd.nuspec'), '<license type="expression">WTFPL</license>');
  const page = join(root, 'PhotoSense.ReactUI');
  write(join(page, 'package-lock.json'), JSON.stringify({ packages: {
    '': { name: 'page' },
    'node_modules/react': { version: '19.0.0', license: 'MIT' },
    'node_modules/next/node_modules/postcss': { version: '8.5.23', license: 'MIT' },
    'node_modules/tailwindcss': { version: '3.4.19', license: 'MIT', dev: true },
    'node_modules/vitest': { version: '4.1.12', license: 'MIT', dev: true },
    'node_modules/sharp': { version: '0.34.0', license: 'Apache-2.0', optional: true },
    'node_modules/fsevents': { version: '2.3.3', license: 'MIT', devOptional: true },
  } }));
  for (const [path, text] of [['react', 'React licence'], ['next/node_modules/postcss', 'PostCSS licence'], ['tailwindcss', 'Tailwind licence']]) write(join(page, 'node_modules', ...path.split('/'), 'LICENSE'), text);
  return { root, nuget };
}

const quiet = () => {};

describe('the packages a program ships', () => {
  it('are those with files that run, not reference-only packages, build tools or the projects themselves', () => {
    const { root } = fixture();
    const listed = nugetPackages(JSON.parse(readFileSync(join(root, 'PhotoSense.Server', 'obj', 'project.assets.json'), 'utf8')));
    assert.deepEqual(listed.map(p => p.id), ['Alpha', 'Beta', 'Microsoft.Gamma', 'XmpCore', 'Native.Thing', 'Linked']);
  });

  it('include those a project names itself when it cannot be restored here', () => {
    assert.deepEqual(referencedPackages('<PackageReference Include="A.B" Version="1.2.3" /> <PackageReference Include="C" Version="4.0.0" />'),
      [{ id: 'A.B', version: '1.2.3' }, { id: 'C', version: '4.0.0' }]);
  });

  it('for the page, are its own and theirs, and the styles Tailwind writes, but no tool that only builds or tests it', () => {
    const { root } = fixture();
    const names = pagePackages(JSON.parse(readFileSync(join(root, 'PhotoSense.ReactUI', 'package-lock.json'), 'utf8'))).map(p => p.name);
    assert.deepEqual(names, ['react', 'postcss', 'tailwindcss']);
  });
});

describe('a licence', () => {
  const templates = { MIT: 'MIT\n\n{{copyright}}\n\nPermission...' };

  it('is the text the package carries, or the standard text of the licence it names with its copyright', () => {
    const { nuget } = fixture();
    assert.equal(licenceText({ dir: join(nuget, 'beta', '2.0.0'), license: 'Apache-2.0', templates }), 'The Beta licence.');
    assert.equal(licenceText({ dir: join(nuget, 'nowhere'), license: 'MIT', copyright: 'Copyright Alpha', templates }), 'MIT\n\nCopyright Alpha\n\nPermission...');
    assert.equal(licenceText({ dir: join(nuget, 'nowhere'), license: 'MIT', templates }), 'MIT\n\n\n\nPermission...');
  });

  it('that has no text anywhere stops the notices being written, saying what to add', () => {
    assert.throws(() => licenceText({ dir: 'nowhere', license: 'WTFPL', templates }), /No text for the licence "WTFPL" of nowhere: add tools\/licenses\/WTFPL.txt/);
  });
});

describe('the notices', () => {
  it('are written for each program: the place names, then every package with its licence in full', () => {
    const { root, nuget } = fixture();
    const said = [];
    assert.equal(main([], { root, nugetRoot: nuget, print: s => said.push(s), fail: quiet }), 0);
    assert.deepEqual(said, [`Wrote ${join(root, 'notices', 'server.txt')}`, `Wrote ${join(root, 'notices', 'iphone.txt')}`]);

    const server = readFileSync(join(root, 'notices', 'server.txt'), 'utf8');
    assert.match(server, /^Third-party notices for PhotoSense for Windows and Mac\n/);
    assert.match(server, /GeoNames \(https:\/\/www\.geonames\.org\/\)/);
    assert.match(server, /\nFONTS\n\nMono \(SIL Open Font License 1\.1\)\nSans \(SIL Open Font License 1\.1\)\n/);
    assert.match(server, /\nSans \(SIL Open Font License 1\.1\)\n-+\n\nCopyright the Sans authors\./);
    assert.match(server, /\nASP\.NET Core \(MIT\)\n/);
    // Named only: the standard text with the package's copyright, which is not then said twice.
    assert.match(server, /\nAlpha 1\.0\.0 \(MIT\)\n-+\n\nMIT License\n\nCopyright Alpha\n/);
    // Its own text, and the copyright that text does not give, and its NOTICE.
    assert.match(server, /\nBeta 2\.0\.0 \(Apache-2\.0\)\nCopyright Beta\n-+\n\nThe Beta licence\.\n\nBeta includes Delta\.\n/);
    assert.match(server, /\nMicrosoft\.Gamma 3\.0\.0 \(MIT\)\n-+\n\nMIT License\n\nCopyright \(c\) \.NET Foundation and Contributors\n/);
    assert.doesNotMatch(server, /Not repeated/);
    assert.match(server, /\nXmpCore 6\.1\.10\.1 \(BSD-3-Clause\)\n-+\n\nBSD 3-Clause License\n\nCopyright 2006-2007 Adobe Systems Incorporated/);
    assert.match(server, /\nNative\.Thing 1\.0\.0 \(its own licence\)\n-+\n\nThe Thing licence\.\n/);
    assert.match(server, /\nLinked 1\.0\.0 \(https:\/\/linked\.example\)\n-+\n\nThe Linked licence\.\n/);
    assert.match(server, /\ntailwindcss 3\.4\.19 \(MIT\)\n-+\n\nTailwind licence\n/);
    assert.match(server, /Its own third-party notices: https:\/\/github\.com\/dotnet\/runtime/);

    const iphone = readFileSync(join(root, 'notices', 'iphone.txt'), 'utf8');
    assert.match(iphone, /^Third-party notices for PhotoSense for iPhone and iPad\n/);
    assert.match(iphone, /\n\.NET for iOS \(MIT\)\n/);
    assert.match(iphone, /\nMicrosoft\.Missing 1\.0\.0 \(MIT\)\n/);
    assert.doesNotMatch(iphone, /Beta|ASP\.NET/);
  });

  it('are found up to date by --check once written, and out of date when one is changed or missing', () => {
    const { root, nuget } = fixture();
    const failed = [];
    const check = () => main(['--check'], { root, nugetRoot: nuget, print: quiet, fail: s => failed.push(s) });
    assert.equal(check(), 1);
    main([], { root, nugetRoot: nuget, print: quiet, fail: quiet });
    assert.equal(check(), 0);
    writeFileSync(join(root, 'notices', 'iphone.txt'), 'old');
    assert.equal(check(), 1);
    assert.equal(failed.at(-1), `The third-party notices are out of date (${join(root, 'notices', 'iphone.txt')}): run node tools/notices.mjs and commit them.`);
  });

  it('are not written when a package\'s licence has no text, which says why', () => {
    const { root, nuget } = fixture({ unknownLicence: true });
    const failed = [];
    assert.equal(main([], { root, nugetRoot: nuget, print: quiet, fail: s => failed.push(s) }), 1);
    assert.match(failed[0], /^The third-party notices: No text for the licence "WTFPL"/);
  });

  it('read the packages from NUGET_PACKAGES or the user\'s own NuGet folder, and write to the console, unless told otherwise', t => {
    const said = t.mock.method(console, 'log', quiet);
    t.mock.method(console, 'error', quiet);
    const saved = process.env.NUGET_PACKAGES;
    try {
      const { root, nuget } = fixture();
      process.env.NUGET_PACKAGES = nuget;
      assert.equal(main([], { root }), 0);
      assert.equal(said.mock.calls.length, 2);
      delete process.env.NUGET_PACKAGES;
      assert.equal(main(['--check'], { root: repo }), 0);
    } finally {
      if (saved === undefined) delete process.env.NUGET_PACKAGES; else process.env.NUGET_PACKAGES = saved;
    }
  });

  it('name every font the page uses, as app/layout.tsx asks Next for them', () => {
    const layout = readFileSync(join(repo, 'PhotoSense.ReactUI', 'app', 'layout.tsx'), 'utf8');
    const used = /import \{([^}]+)\} from 'next\/font\/google'/.exec(layout)[1].split(',').map(name => name.trim().replace(/_/g, ' ')).sort();
    assert.deepEqual(readdirSync(join(repo, 'tools', 'fonts')).map(f => f.replace(/\.txt$/, '')).sort(), used);
  });

  it('of this repository are up to date, checked as a program', () => {
    execFileSync(process.execPath, [join(repo, 'tools', 'notices.mjs'), '--check'], { encoding: 'utf8' });
  });
});
