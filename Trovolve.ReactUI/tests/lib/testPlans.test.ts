import { describe, expect, it } from 'vitest';
import { TEST_PLANS, testPlanSaid } from '../../lib/testPlans';

describe('plans for testing', () => {
  it('are offered as bought first, then the free plan, the trial and each way of paying for Pro', () => {
    expect(TEST_PLANS.map(p => p.id)).toEqual(['asBought', 'free', 'trial', 'monthly', 'yearly', 'lifetime']);
  });

  it('say what was chosen, and that only a development build can, or that the plan is back to what was bought', () => {
    expect(testPlanSaid('lifetime')).toBe('Testing as Pro, paid once. Only a development build can do this.');
    expect(testPlanSaid('asBought')).toBe('Back to the plan as bought.');
  });
});
