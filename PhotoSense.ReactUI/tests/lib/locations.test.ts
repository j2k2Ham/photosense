import { describe, expect, it } from 'vitest';
import { detailRows, hostOf, isCloud, isServer, masked, progressLine, SERVER_KINDS, startingSettings, statusLine } from '../../lib/locations';
import { location } from '../fixtures';

describe('what a location is', () => {
  it('is the computer, a cloud service, or one Connect to server added', () => {
    expect([isCloud('box'), isCloud('sftp'), isCloud('computer')]).toEqual([true, false, false]);
    expect([isServer('sftp'), isServer('box'), isServer('computer')]).toEqual([true, false, false]);
  });

  it('says under its name how it stands: why it stopped, paired or not, signed in or not, or the kind and host', () => {
    expect(statusLine(location({ problem: 'Down.' }))).toBe('Down.');
    expect(statusLine(location({ kind: 'computer', connected: true, settings: { folder: 'Pictures/PhotoSense' } }))).toBe('Paired · Pictures/PhotoSense');
    expect(statusLine(location({ kind: 'computer' }))).toBe('Not paired yet');
    expect(statusLine(location({ available: false }))).toBe('Not available in this version yet');
    expect(statusLine(location({ connected: true, account: 'alex.m@gmail.com' }))).toBe('alex.m@gmail.com');
    expect(statusLine(location({ connected: true }))).toBe('Signed in');
    expect(statusLine(location())).toBe('Not connected');
    expect(statusLine(location({ kind: 'amazonS3', settings: { bucket: 'photos-archive' } }))).toBe('Amazon S3 · photos-archive');
  });

  it('names the host it reaches: an address\'s, as typed when it is no address, or the bucket, host or account', () => {
    expect(hostOf({ settings: { url: 'https://cloud.example.com/dav' } })).toBe('cloud.example.com');
    expect(hostOf({ settings: { url: 'nas.local' } })).toBe('nas.local');
    expect(hostOf({ settings: { host: 'nas' } })).toBe('nas');
    expect(hostOf({ settings: { account: 'mystorage' } })).toBe('mystorage');
    expect(hostOf({ settings: {} })).toBeUndefined();
  });

  it('shows a key masked, and how far a backup to it has got or why it stopped', () => {
    expect([masked('AKIA1234567Q2F'), masked('short')]).toEqual(['AKIA…7Q2F', '••••']);
    expect(progressLine({ id: 'x', done: 84, total: 380, bytes: 1024 ** 3, totalBytes: 4 * 1024 ** 3, problem: null })).toBe('84 of 380 · 1.00 GB of 4.00 GB');
    expect(progressLine({ id: 'x', done: 0, total: 0, bytes: 0, totalBytes: 0, problem: null })).toBe('Nothing to send');
    expect(progressLine({ id: 'x', done: 0, total: 1, bytes: 0, totalBytes: 1, problem: 'Full.' })).toBe('Full.');
  });
});

describe('Connect to server', () => {
  it('offers five kinds, SMB left out, each starting on its first choices', () => {
    expect(SERVER_KINDS.map(k => k.label)).toEqual(['Amazon S3', 'S3-compatible', 'Azure Blob', 'WebDAV', 'SFTP']);
    expect(startingSettings(SERVER_KINDS[0])).toEqual({ region: 'us-east-1 (N. Virginia)', cls: 'Standard' });
    expect(startingSettings(SERVER_KINDS[2])).toEqual({ auth: 'SAS token', tier: 'Hot' });
    expect(startingSettings(SERVER_KINDS[3])).toEqual({});
  });

  it('shows how a location added is reached, its access key masked and its secrets never', () => {
    const s3 = location({ kind: 'amazonS3', settings: { bucket: 'photos', akid: 'AKIA1234567Q2F', region: 'us-west-2 (Oregon)', secret: 'never' } });
    expect(detailRows(s3)).toEqual([['Region', 'us-west-2 (Oregon)'], ['Bucket', 'photos'], ['Access key ID', 'AKIA…7Q2F']]);
    expect(detailRows(location())).toEqual([]);
  });
});
