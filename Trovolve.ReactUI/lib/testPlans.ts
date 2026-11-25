import type { TestPlan } from '../types';

/** The plans a development build can test as, in the order offered, each as Settings names it. */
export const TEST_PLANS: readonly { readonly id: TestPlan; readonly label: string }[] = [
  { id: 'asBought', label: 'As bought' },
  { id: 'free', label: 'Free' },
  { id: 'trial', label: 'Trial' },
  { id: 'monthly', label: 'Pro monthly' },
  { id: 'yearly', label: 'Pro yearly' },
  { id: 'lifetime', label: 'Pro, paid once' },
];

/** The message after a plan is chosen for testing. */
export const testPlanSaid = (plan: TestPlan) => (plan === 'asBought'
  ? 'Back to the plan as bought.'
  : `Testing as ${TEST_PLANS.find(p => p.id === plan)!.label}. Only a development build can do this.`);
