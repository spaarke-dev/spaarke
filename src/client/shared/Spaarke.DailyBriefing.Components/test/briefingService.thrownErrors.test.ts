/**
 * briefingService classifies what `@spaarke/auth`'s `authenticatedFetch` THROWS.
 *
 * That fetch throws `ApiError` (field `status`) for a non-2xx response and `AuthError` once its 401
 * retries are spent. The service read `err.statusCode`, which neither carries, so a 503/429 (the
 * yellow "temporarily unavailable" state) and a 401/403 (sign-in) all fell to the red generic error.
 * The mocks here throw the REAL classes (`test/__mocks__/spaarke-auth.ts` re-exports them).
 */
import { ApiError, AuthError, authenticatedFetch } from '@spaarke/auth';
import {
  emailBriefingToColleague,
  fetchAiBriefing,
  fetchBriefingLive,
  fetchBriefingNarration,
} from '../src/services/briefingService';
import type { ChannelFetchResult } from '../src/types/notifications';

const mockFetch = authenticatedFetch as unknown as jest.Mock;

const apiError = (status: number) => new ApiError(`HTTP ${status}`, status, null);
const authExhausted = () => new AuthError('Authentication failed after all retry attempts', 'auth_exhausted');

/** One successful channel with one item, so summarize/narrate do not short-circuit on "no data". */
const channels = [
  {
    status: 'success',
    group: {
      meta: { category: 'tasks-overdue', label: 'Overdue tasks' },
      items: [{ id: 'n1', title: 'Overdue task', body: 'Body', priority: 'high', createdOn: new Date().toISOString() }],
      unreadCount: 1,
    },
  },
] as unknown as ChannelFetchResult[];

beforeEach(() => {
  mockFetch.mockReset();
  jest.spyOn(console, 'error').mockImplementation(() => undefined);
});

afterEach(() => {
  (console.error as jest.Mock).mockRestore();
});

describe('fetchBriefingLive (the live /render path)', () => {
  it.each([503, 429])('a thrown %i is "temporarily unavailable", not an error', async status => {
    mockFetch.mockRejectedValueOnce(apiError(status));
    await expect(fetchBriefingLive()).resolves.toEqual({
      status: 'unavailable',
      reason: 'AI briefing service is temporarily unavailable.',
    });
  });

  it('a thrown 403 asks for sign-in', async () => {
    mockFetch.mockRejectedValueOnce(apiError(403));
    await expect(fetchBriefingLive()).resolves.toEqual({
      status: 'unavailable',
      reason: 'Sign-in required to view your daily briefing.',
    });
  });

  it('an exhausted 401 (AuthError) asks for sign-in', async () => {
    mockFetch.mockRejectedValueOnce(authExhausted());
    await expect(fetchBriefingLive()).resolves.toEqual({
      status: 'unavailable',
      reason: 'Sign-in required to view your daily briefing.',
    });
  });

  it('a thrown 500 stays an error carrying the message', async () => {
    mockFetch.mockRejectedValueOnce(new ApiError('Narrator failed', 500, { title: 'Narrator failed', status: 500 }));
    await expect(fetchBriefingLive()).resolves.toEqual({ status: 'error', message: 'Narrator failed' });
  });
});

// fetchBriefingNarration short-circuits to fetchBriefingLive while USE_LIVE_RENDER is on (its own /narrate
// catch is unreachable until that flag is turned off, and is fixed the same way); fetchAiBriefing has no
// live caller today. Both are kept right for re-enable.
describe('fetchBriefingNarration / fetchAiBriefing', () => {
  it('narration (live path): a thrown 503 is unavailable', async () => {
    mockFetch.mockRejectedValueOnce(apiError(503));
    await expect(fetchBriefingNarration(channels)).resolves.toEqual({
      status: 'unavailable',
      reason: 'AI briefing service is temporarily unavailable.',
    });
  });

  it('narration: an exhausted 401 asks for sign-in', async () => {
    mockFetch.mockRejectedValueOnce(authExhausted());
    await expect(fetchBriefingNarration(channels)).resolves.toEqual({
      status: 'unavailable',
      reason: 'Sign-in required to view your daily briefing.',
    });
  });

  it('summary: a thrown 429 is unavailable', async () => {
    mockFetch.mockRejectedValueOnce(apiError(429));
    await expect(fetchAiBriefing(channels)).resolves.toEqual({
      status: 'unavailable',
      reason: 'AI briefing service is temporarily unavailable.',
    });
  });

  it('summary: an exhausted 401 is the authentication state', async () => {
    mockFetch.mockRejectedValueOnce(authExhausted());
    await expect(fetchAiBriefing(channels)).resolves.toEqual({
      status: 'unavailable',
      reason: 'Authentication required for AI briefing.',
    });
  });
});

describe('emailBriefingToColleague', () => {
  it('a thrown 400 explains the recipient rule', async () => {
    mockFetch.mockRejectedValueOnce(apiError(400));
    await expect(emailBriefingToColleague('x@external.com')).resolves.toEqual({
      status: 'error',
      message: 'That recipient must be an active internal user, or the address is invalid.',
    });
  });

  it('a thrown 503 is temporarily unavailable', async () => {
    mockFetch.mockRejectedValueOnce(apiError(503));
    await expect(emailBriefingToColleague('a@b.com')).resolves.toEqual({
      status: 'error',
      message: 'Briefing email service is temporarily unavailable. Please try again.',
    });
  });

  it('an exhausted 401 asks for sign-in', async () => {
    mockFetch.mockRejectedValueOnce(authExhausted());
    await expect(emailBriefingToColleague('a@b.com')).resolves.toEqual({
      status: 'error',
      message: 'Sign-in required to send the briefing.',
    });
  });
});
