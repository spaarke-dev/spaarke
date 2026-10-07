/**
 * useSseStream — HTTP 429 message (customer-provisioning-orchestration-r1 task 254).
 *
 * The BFF answers 429 for rate limiting AND for the stamp's monthly AI usage limit. The usage limit must show the
 * server's own message (AI resumes next month or when an administrator raises it), never "sending too quickly",
 * which would have the user retry for the rest of the month.
 */

import { describeTooManyRequests, AI_SPEND_LIMIT_ERROR_CODE } from '../../../hooks/useSseStream';

describe('describeTooManyRequests', () => {
  it('shows the server message for the monthly AI usage limit', () => {
    const body = JSON.stringify({
      status: 429,
      title: 'AI Usage Limit Reached',
      detail: 'This environment has reached its monthly AI usage limit.',
      extensions: { code: AI_SPEND_LIMIT_ERROR_CODE },
    });

    expect(describeTooManyRequests(body)).toBe('This environment has reached its monthly AI usage limit.');
  });

  it('keeps the rate-limit message for any other 429', () => {
    expect(describeTooManyRequests('')).toMatch(/too quickly/);
    expect(describeTooManyRequests('not json')).toMatch(/too quickly/);
    expect(describeTooManyRequests(JSON.stringify({ status: 429, extensions: { code: 'rate_limited' } }))).toMatch(
      /too quickly/
    );
  });
});
