/** How distances are said: in miles, the default, or in kilometres, as the person chose in Settings. */
export type DistanceUnit = 'miles' | 'km';

/** A mile, in kilometres: distances are worked out in kilometres and said in the person's unit. */
export const KM_PER_MILE = 1.609344;

/** One of the unit, in kilometres. */
export const kmPer = (unit: DistanceUnit) => (unit === 'km' ? 1 : KM_PER_MILE);

/** A distance in a few characters, as on a scale or a chip: "5 mi", "5 km". */
export const shortDistance = (amount: number, unit: DistanceUnit) => `${amount} ${unit === 'km' ? 'km' : 'mi'}`;

/** A distance in a sentence: "1 mile", "5 miles", "5 km". */
export const longDistance = (amount: number, unit: DistanceUnit) => (unit === 'km' ? `${amount} km` : amount === 1 ? '1 mile' : `${amount} miles`);

/** The choices of unit, in the order Settings shows them. */
export const DISTANCE_UNITS: readonly { readonly value: DistanceUnit; readonly name: string }[] = [{ value: 'miles', name: 'Miles' }, { value: 'km', name: 'Kilometers' }];
