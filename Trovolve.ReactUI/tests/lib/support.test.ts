import { describe, expect, it } from 'vitest';
import { supportDetails, supportMailto, systemOf } from '../../lib/support';

describe('the system, from the browser', () => {
  it.each([
    ['Mozilla/5.0 (iPhone; CPU iPhone OS 18_1 like Mac OS X) AppleWebKit/605.1.15', 'iOS 18.1'],
    ['Mozilla/5.0 (iPhone; CPU iPhone OS 26 like Mac OS X) AppleWebKit/605.1.15', 'iOS 26'],
    ['Mozilla/5.0 (iPad; CPU OS 18_1 like Mac OS X) AppleWebKit/605.1.15', 'iPadOS'],
    ['Mozilla/5.0 (Linux; Android 14; Pixel 9) AppleWebKit/537.36', 'Android 14'],
    ['Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36', 'Windows'],
    ['Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15', 'macOS'],
    ['Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36', 'Linux'],
    ['SomethingElse/1.0', undefined],
  ])('reads %s as %s', (agent, system) => {
    expect(systemOf(agent)).toBe(system);
  });
});

describe('the details for support', () => {
  const about = { name: 'Trovolve', version: '1.2.3', program: 'android' };

  it('name the program, its version and the system, and nothing of errors while there are none', () => {
    expect(supportDetails(about, 'Mozilla/5.0 (Linux; Android 14)', [])).toBe('Trovolve 1.2.3 for Android\nSystem: Android 14');
  });

  it('say Trovolve alone before the service has said, a program it does not know without words, and a system not known', () => {
    expect(supportDetails(undefined, 'SomethingElse', ['x'])).toBe('Trovolve\nSystem: not known\nLatest errors:\n- x');
    expect(supportDetails({ ...about, program: 'other' }, 'SomethingElse', [])).toBe('Trovolve 1.2.3\nSystem: not known');
  });

  it('make a mailto with the subject, the questions and the details', () => {
    expect(supportMailto('a@b.example', 'D')).toBe(`mailto:a@b.example?subject=Trovolve%20support&body=${encodeURIComponent('What I did:\n\nWhat I expected:\n\nWhat happened instead:\n\n---\nD\n')}`);
  });
});
