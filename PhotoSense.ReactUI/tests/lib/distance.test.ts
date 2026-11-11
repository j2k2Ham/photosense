import { describe, expect, it } from 'vitest';
import { DISTANCE_UNITS, KM_PER_MILE, kmPer, longDistance, shortDistance } from '../../lib/distance';

describe('distances', () => {
  it('works in kilometres and says each distance in the unit chosen', () => {
    expect([kmPer('miles'), kmPer('km')]).toEqual([KM_PER_MILE, 1]);
    expect([shortDistance(5, 'miles'), shortDistance(5, 'km')]).toEqual(['5 mi', '5 km']);
    expect([longDistance(1, 'miles'), longDistance(5, 'miles'), longDistance(1, 'km')]).toEqual(['1 mile', '5 miles', '1 km']);
    expect(DISTANCE_UNITS.map(u => u.name)).toEqual(['Miles', 'Kilometers']);
  });
});
