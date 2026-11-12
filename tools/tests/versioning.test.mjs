import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { dirname, join } from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';
import { bump, git, main, parseTag, readConfig, readRepository, versionOf } from '../versioning.mjs';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const linked = { linked: true, release: 'minor', parts: { server: { release: 'patch' }, iphone: { release: 'patch' } } };
const unlinked = { ...linked, linked: false };
const version = (config, more = {}) => versionOf({ config, part: null, tags: [], headTags: [], commits: 57, release: false, ...more });

describe('version.json', () => {
  it('is read when it says whether the programs are linked and what each next release is', () => {
    assert.deepEqual(readConfig(JSON.stringify(linked)), linked);
    assert.deepEqual(readConfig('{"linked": false, "release": "major"}'), { linked: false, release: 'major' });
  });

  it('stops the build when a value is wrong, naming it', () => {
    assert.throws(() => readConfig('{"linked": "yes", "release": "minor"}'), /"linked" must be true or false/);
    assert.throws(() => readConfig('{"linked": true, "release": "big"}'), /"release" must be "major", "minor" or "patch", not "big"/);
    assert.throws(() => readConfig('{"linked": true, "release": "minor", "parts": {"iphone": {}}}'), /"parts.iphone.release" must be/);
  });
});

describe('release tags', () => {
  it('are a shared release or one program\'s own, and anything else is not a release', () => {
    assert.deepEqual(parseTag('v1.2.3'), { part: null, version: [1, 2, 3] });
    assert.deepEqual(parseTag('iphone-v10.0.4'), { part: 'iphone', version: [10, 0, 4] });
    for (const other of ['v1.2', 'v1.2.3-beta', 'latest', 'Server-v1.0.0']) assert.equal(parseTag(other), null);
  });

  it('are raised by the kind of release that comes next', () => {
    assert.deepEqual(bump([1, 2, 3], 'major'), [2, 0, 0]);
    assert.deepEqual(bump([1, 2, 3], 'minor'), [1, 3, 0]);
    assert.deepEqual(bump([1, 2, 3], 'patch'), [1, 2, 4]);
  });
});

describe('a program\'s version', () => {
  it('starts from nothing released, with the number of commits as its build', () => {
    assert.deepEqual(version(linked), { version: '0.1.0', build: 57, display: '0.1.0.57' });
  });

  it('is the bare release when built as the release', () => {
    assert.deepEqual(version(linked, { release: true }), { version: '0.1.0', build: 57, display: '0.1.0' });
  });

  it('carries on above the highest release of any program while they are linked', () => {
    const tags = ['v1.0.0', 'iphone-v1.3.0', 'server-v1.2.9', 'v1.2.0', 'notes'];
    assert.equal(version(linked, { part: 'server', tags }).display, '1.4.0.57');
    assert.equal(version(linked, { part: 'server', tags: ['v2.0.1', 'v2.0.0', 'v1.9.9'] }).display, '2.1.0.57');
  });

  it('counts only the shared releases and its own once unlinked, raised as its own entry says', () => {
    const tags = ['v1.0.0', 'iphone-v1.1.0', 'server-v2.0.0'];
    assert.equal(version(unlinked, { part: 'iphone', tags }).display, '1.1.1.57');
    assert.equal(version(unlinked, { part: 'server', tags }).display, '2.0.1.57');
  });

  it('for the libraries every program shares, is the linked version even while the programs are unlinked', () => {
    assert.equal(version(unlinked, { tags: ['v1.0.0', 'iphone-v1.1.0'] }).display, '1.2.0.57');
  });

  it('cannot be worked out for a program version.json does not name once they are unlinked', () => {
    assert.throws(() => version(unlinked, { part: 'android' }), /no part named "android"/);
    assert.throws(() => version({ linked: false, release: 'minor' }, { part: 'iphone' }), /no part named "iphone"/);
  });

  it('is the release itself on the commit tagged with it', () => {
    assert.deepEqual(version(linked, { part: 'iphone', headTags: ['v1.2.0', 'notes'], tags: ['v1.2.0'] }), { version: '1.2.0', build: 57, display: '1.2.0' });
    assert.equal(version(linked, { part: 'iphone', headTags: ['v1.2.0', 'v1.3.0'] }).display, '1.3.0');
    assert.equal(version(unlinked, { part: 'iphone', headTags: ['iphone-v1.1.0'] }).display, '1.1.0');
  });

  it('is not one program\'s own release while they are linked, nor another program\'s', () => {
    assert.equal(version(linked, { part: 'iphone', headTags: ['iphone-v1.1.0'] }).display, '0.1.0.57');
    assert.equal(version(unlinked, { part: 'server', headTags: ['iphone-v1.1.0'] }).display, '0.0.1.57');
  });
});

describe('the repository', () => {
  it('gives its tags, those on the commit being built and the commits leading to it', () => {
    const answers = { 'tag --list': 'v1.0.0\r\niphone-v1.1.0\r\n', 'tag --points-at HEAD': '', 'rev-list --count HEAD': '57\n' };
    assert.deepEqual(readRepository(args => answers[args.join(' ')]), { tags: ['v1.0.0', 'iphone-v1.1.0'], headTags: [], commits: 57 });
  });

  it('gives nothing when there is no history to read', () => {
    assert.deepEqual(readRepository(() => null), { tags: [], headTags: [], commits: 0 });
  });

  it('is read with git, and a command git refuses gives nothing', () => {
    assert.match(git(root)(['rev-list', '--count', 'HEAD']), /^\d+\s*$/);
    assert.equal(git(root)(['no-such-command']), null);
  });
});

describe('the command', () => {
  const config = JSON.stringify(linked);
  const run = args => ({ 'rev-list --count HEAD': '57' })[args.join(' ')] ?? '';
  const call = (argv, more = {}) => {
    const said = [], failed = [];
    const code = main(argv, { root, read: () => config, run, print: s => said.push(s), fail: s => failed.push(s), ...more });
    return { code, said, failed };
  };

  it('prints the version for MSBuild, as JSON or as text, for a program or the libraries', () => {
    assert.deepEqual(call(['--part', 'iphone', '--format', 'msbuild']), { code: 0, said: ['0.1.0;57;0.1.0.57'], failed: [] });
    assert.deepEqual(call(['--format', 'json', '--release']).said, ['{"version":"0.1.0","build":57,"display":"0.1.0"}']);
    assert.deepEqual(call(['--part', '']).said, ['0.1.0.57']);
  });

  it('says what is wrong and fails, for a format it does not know or a mistake in version.json', () => {
    assert.deepEqual(call(['--format', 'xml']), { code: 1, said: [], failed: ['PhotoSense\'s version: --format must be msbuild, json or text, not "xml".'] });
    assert.deepEqual(call([], { read: () => '{"linked": 1}' }).failed, ['PhotoSense\'s version: version.json: "linked" must be true or false.']);
  });

  it('reads this repository\'s version.json and history, and writes to the console, unless told otherwise', t => {
    const said = t.mock.method(console, 'log', () => {});
    const failed = t.mock.method(console, 'error', () => {});
    assert.equal(main(['--format', 'json'], { root }), 0);
    assert.match(JSON.parse(said.mock.calls[0].arguments[0]).display, /^\d+\.\d+\.\d+\.\d+$/);
    assert.equal(main(['--format', 'xml'], { root }), 1);
    assert.equal(failed.mock.calls.length, 1);
  });

  it('runs as a program', () => {
    const out = execFileSync(process.execPath, [join(root, 'tools', 'version.mjs'), '--part', 'server', '--format', 'msbuild'], { encoding: 'utf8' });
    assert.match(out.trim(), /^\d+\.\d+\.\d+;\d+;\d+\.\d+\.\d+(\.\d+)?$/);
  });
});
