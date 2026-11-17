import { describe, expect, it } from 'vitest';
import { automaticNote, backupButton, backupLine, backupPercent, backupTitle, checkedAt, gigabytes, ofStorage, storageBar, whenSaid } from '../../lib/backup';
import { BACKUP_SETTINGS, backupState, safeReview } from '../fixtures';

const now = new Date(2026, 10, 13, 15, 0);
const at = (day: number, hour: number, minute = 0) => new Date(2026, 10, day, hour, minute).toISOString();

describe('when a backup happened', () => {
  it('is today, yesterday, or the day', () => {
    expect(whenSaid(at(13, 9, 41), now)).toBe('Today, 9:41 AM');
    expect(whenSaid(at(12, 18, 2), now)).toBe('Yesterday, 6:02 PM');
    expect(whenSaid(at(10, 9, 41), now)).toBe('Nov 10, 9:41 AM');
  });

  it('reads inside a sentence', () => {
    expect(checkedAt(at(13, 9, 41), now)).toBe('today at 9:41 AM');
    expect(checkedAt(at(12, 18, 2), now)).toBe('yesterday at 6:02 PM');
    expect(checkedAt(at(10, 9, 41), now)).toBe('on Nov 10 at 9:41 AM');
  });
});

describe('the backup card', () => {
  it('says what waits, and when it goes', () => {
    const waiting = backupState();
    expect(backupTitle(waiting)).toBe('380 new items waiting');
    expect(backupTitle(backupState({ waiting: 1 }))).toBe('1 new item waiting');
    expect(backupLine(waiting, now, 'ios')).toBe('1.96 GB · automatic backup is off');
    const charging = backupState({ settings: { ...BACKUP_SETTINGS, automatic: true } });
    const any = backupState({ settings: { ...BACKUP_SETTINGS, automatic: true, onlyWhileCharging: false } });
    expect([backupLine(charging, now, 'ios'), backupLine(any, now, 'ios')]).toEqual(['1.96 GB · next backup while charging', '1.96 GB · next backup while PhotoSense is open']);
    expect([backupLine(charging, now, 'android'), backupLine(any, now, 'android')]).toEqual(['1.96 GB · next backup while charging on Wi-Fi', '1.96 GB · next backup on Wi-Fi']);
    expect(backupButton(waiting)).toBe('Back up now');
  });

  it('says how far a backup has got, and what is left, while it runs or is paused', () => {
    const running = backupState({ stage: 'running', done: 184, total: 380, bytes: 1e9, totalBytes: 2.1e9 });
    expect([backupTitle(running), backupButton(running), Math.round(backupPercent(running))]).toEqual(['Backing up 184 of 380', 'Pause', 48]);
    expect(backupLine(running, now, 'ios')).toBe('1.02 GB left · each file is checked after it arrives');
    const paused = { ...running, stage: 'paused' as const };
    expect([backupTitle(paused), backupButton(paused)]).toEqual(['Paused at 184 of 380', 'Resume']);
    // Nothing to send counts as nothing done.
    expect(backupPercent(backupState({ stage: 'running' }))).toBe(0);
    expect(backupPercent(backupState({ stage: 'running', bytes: 9, totalBytes: 3 }))).toBe(100);
    expect(backupLine(backupState({ stage: 'running', bytes: 9, totalBytes: 3 }), now, 'ios')).toBe('1 KB left · each file is checked after it arrives');
  });

  it('says that a backup stopped, and why', () => {
    const failed = backupState({ stage: 'failed', problem: 'Studio iMac was not found on this Wi-Fi.' });
    expect([backupTitle(failed), backupLine(failed, now, 'ios'), backupButton(failed)]).toEqual(['The backup stopped', 'Studio iMac was not found on this Wi-Fi.', 'Try again']);
    expect(backupLine(backupState({ stage: 'failed' }), now, 'ios')).toBe('');
  });

  it('says when backups run by themselves: on Android with the app closed too, on an iPhone while it is open', () => {
    expect(automaticNote('android', 'Studio iMac', true)).toBe('Backups run on Wi-Fi about once an hour while this phone is charging, with PhotoSense closed too, and whenever it is open.');
    expect(automaticNote('android', 'Studio iMac', false)).toBe('Backups run on Wi-Fi about once an hour, with PhotoSense closed too, and whenever it is open.');
    expect(automaticNote('ios', 'Studio iMac', true)).toBe('A backup runs whenever PhotoSense is open and Studio iMac is on the same Wi-Fi.');
  });

  it('says that everything is there, and since when', () => {
    const done = backupState({ waiting: 0, lastBackup: at(13, 9, 41) });
    expect([backupTitle(done), backupLine(done, now, 'ios'), backupButton(done)]).toEqual(['Up to date', '6,561 items on Studio iMac · backed up today at 9:41 AM', undefined]);
    expect(backupLine(backupState({ waiting: 0 }), now, 'ios')).toBe('6,561 items on Studio iMac');
  });
});

describe('the storage of the phone', () => {
  it('is split into what would be freed, everything else, and what is free', () => {
    expect(storageBar(safeReview())).toEqual({ remove: 28.1e9 / 128e9 * 100, other: 100 - 28.1e9 / 128e9 * 100 - 12.5, free: 12.5 });
    // No more can be freed than is taken; a phone that says it holds nothing is drawn all free.
    expect(storageBar(safeReview({ storageFree: 120e9 })).remove).toBe(6.25);
    expect(storageBar(safeReview({ storageTotal: 0, storageFree: 0, removable: { count: 0, bytes: 0 } }))).toEqual({ remove: 0, other: 100, free: 0 });
  });

  it('writes an amount in gigabytes to a tenth, at least a tenth when there is anything', () => {
    expect([gigabytes(28.1e9), gigabytes(1), gigabytes(0)]).toEqual(['28.1 GB', '0.1 GB', '0.0 GB']);
    expect([ofStorage(112e9, 128e9), ofStorage(8.4e9, 128e9)]).toEqual(['112 of 128 GB', '8.4 of 128 GB']);
  });
});
