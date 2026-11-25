/**
 * GitHub #1571 review F1 (spaarkeai-word-add-in-r1 task 136, fix round 1): the ribbon Quick Save's sign-in helpers.
 * - `ensureQuickSaveSignedIn` signs in ONCE from the click when there is no silent token, and throws the sign-in error
 *   when there is still none;
 * - a Quick Save that failed for want of a sign-in (no token, the API client's "Not authenticated", a 401 after the
 *   retry) says "Open the Spaarke pane and sign in, then try again." — never "Check your connection".
 */
import {
  QUICK_SAVE_SIGN_IN_MESSAGE,
  QuickSaveSignInRequiredError,
  describeQuickSaveFailure,
  ensureQuickSaveSignedIn,
  isQuickSaveSignInFailure,
} from '../quickSaveHelpers';

function auth(tokens: Array<string | null>, signIn: () => Promise<void> = async () => undefined) {
  const queue = [...tokens];
  return {
    getAccessToken: jest.fn(async () => (queue.length > 1 ? queue.shift()! : (queue[0] ?? null))),
    signIn: jest.fn(signIn),
  };
}

describe('ensureQuickSaveSignedIn (#1571 review F1)', () => {
  it('a silent token: no sign-in', async () => {
    const a = auth(['token']);

    await ensureQuickSaveSignedIn(a);

    expect(a.signIn).not.toHaveBeenCalled();
  });

  it('no silent token: signs in once, and a token after it is enough', async () => {
    const a = auth([null, 'token']);

    await ensureQuickSaveSignedIn(a);

    expect(a.signIn).toHaveBeenCalledTimes(1);
  });

  it('a sign-in that does not complete throws the sign-in error', async () => {
    const a = auth([null], async () => {
      throw new Error('Sign-in did not complete.');
    });
    jest.spyOn(console, 'warn').mockImplementation(() => undefined);

    await expect(ensureQuickSaveSignedIn(a)).rejects.toBeInstanceOf(QuickSaveSignInRequiredError);
    expect(a.signIn).toHaveBeenCalledTimes(1);
  });

  it('a sign-in that returns but leaves no token throws the sign-in error', async () => {
    const a = auth([null]);

    await expect(ensureQuickSaveSignedIn(a)).rejects.toBeInstanceOf(QuickSaveSignInRequiredError);
  });
});

describe('describeQuickSaveFailure for a sign-in failure (#1571 review F1)', () => {
  it.each([
    ['the sign-in error', new QuickSaveSignInRequiredError()],
    ["the API client's 'Not authenticated'", new Error('Not authenticated')],
    [
      'a 401 after the retry',
      Object.assign(new Error('Unauthorized'), { error: { title: 'Unauthorized', status: 401 } }),
    ],
  ])('%s says to sign in from the pane', (_name, error) => {
    expect(isQuickSaveSignInFailure(error)).toBe(true);
    expect(describeQuickSaveFailure(error, 'connect')).toBe(QUICK_SAVE_SIGN_IN_MESSAGE);
    expect(describeQuickSaveFailure(error, 'save')).toBe(QUICK_SAVE_SIGN_IN_MESSAGE);
  });

  it.each([
    ['a network failure', new TypeError('Failed to fetch')],
    ['a 403 refusal', Object.assign(new Error('Forbidden'), { error: { title: 'Forbidden', status: 403 } })],
  ])('NEGATIVE: %s is not a sign-in failure', (_name, error) => {
    expect(isQuickSaveSignInFailure(error)).toBe(false);
    expect(describeQuickSaveFailure(error, 'save')).not.toBe(QUICK_SAVE_SIGN_IN_MESSAGE);
  });
});
