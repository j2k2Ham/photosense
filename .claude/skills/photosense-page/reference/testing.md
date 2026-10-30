# Testing the page (Vitest)

## Rules

- **Layout:** tests live in `tests/`, mirroring `app`, `components` and `lib`. Fixtures are in `tests/fixtures.ts`, and `deferred` (a promise a test settles itself) is in `tests/deferred.ts`.
- **Names** are plain sentences of what the person sees: "asks first, then takes the files off the page at once and offers to put them back".
- **Mocking the API client:**

  ```ts
  const api = vi.hoisted(() => ({ useSendState: vi.fn(), startSending: vi.fn() }));
  vi.mock('../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../lib/apiClient')>()), ...api }));
  ```

  `mockReset()` in Vitest 4 goes back to the implementation given to `vi.fn(...)`.
- **Mocking a whole component:** a test of the page or of Organize can stand in for a heavy one, such as `vi.mock('../../components/SendWindow', () => ({ SendWindow: … }))`, to see only what it was given.
- **Testing `lib/apiClient.ts`:** mock `swr` and `fetch` (`tests/lib/apiClient.test.ts`), and load the module afresh with `vi.resetModules()` when an environment variable matters.
- **What Istanbul counts as a branch:** each arm of an `if`, a ternary, `&&`, `||` and `??`, and each default parameter value. It does not count `?.`. A branch no user action can reach is a sign to simplify the code, not to test around it.

## Traps already met

- The test setup's `localStorage` (`tests/setup.ts`, `MemoryStorage`) has no `removeItem`. Set an empty string instead.
- A key event a component listens for on `window` is sent with `userEvent.keyboard('{Escape}')` or `fireEvent.keyDown(window, …)`, not on a node.
- `Scrim` closes on a `mouseDown` on its own element, not anywhere in the body.
- A union of string literals passed to `Segmented` needs its type spelled out: `<Segmented<'copy' | 'move'> …>`.
- jsdom has `PointerEvent`; hover cards ignore touch.
