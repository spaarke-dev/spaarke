/**
 * describeSendFailure.test.ts — the words a user sees when an email send fails (owner decision
 * 2026-10-09: a failed send shows a DESCRIPTIVE dialog; never "HTTP 500" or raw JSON).
 */
import { SendCommunicationError } from '../../../services/communicationApi';
import {
  describeDraftSaveFailure,
  describeSendFailure,
  meaningfulDetail,
  toSendCommunicationError,
  SEND_FAILURE_CLIENT_CODE,
  SEND_FAILURE_NETWORK_CODE,
  SEND_FAILURE_UNCONFIRMED_CODE,
} from '../describeSendFailure';
import { apiErrorFor, authExhausted } from '../../../__tests__/helpers/authenticatedFetchDouble';

const DRAFT_KEPT = 'Your draft is still here — nothing was lost.';

function sce(status: number, detail: string, code = `HTTP_${status}`, correlationId?: string) {
  return new SendCommunicationError(status, code, detail, correlationId);
}

describe('describeSendFailure — one row per status', () => {
  it('titles every failure "Email not sent"', () => {
    for (const status of [0, 400, 401, 403, 404, 409, 412, 413, 422, 429, 500, 503, 418]) {
      expect(describeSendFailure(sce(status, 'x')).title).toBe('Email not sent');
    }
  });

  it('400 → the server detail (it names the bad field) + "Correct it and send again." + draft kept', () => {
    expect(describeSendFailure(sce(400, 'The recipient "bob@" is not a valid email address')).message).toBe(
      `The recipient "bob@" is not a valid email address. Correct it and send again. ${DRAFT_KEPT}`
    );
  });

  it('422 → the server detail as-is when it already ends a sentence', () => {
    expect(describeSendFailure(sce(422, 'Subject is too long (max 255 characters).')).message).toBe(
      `Subject is too long (max 255 characters). Correct it and send again. ${DRAFT_KEPT}`
    );
  });

  it('400 with only a framework title → a checklist sentence, not the boilerplate', () => {
    const { message } = describeSendFailure(sce(400, 'One or more validation errors occurred.'));
    expect(message).toBe(
      "The email couldn't be accepted as written. Check the recipients, subject and attachments, then send again. " +
        DRAFT_KEPT
    );
  });

  it('401 → the existing sign-in sentence, and a warning that refreshing clears the draft', () => {
    const { message } = describeSendFailure(
      sce(401, 'Your sign-in has expired. Refresh the page and sign in again, then resend.')
    );
    expect(message).toBe(
      'Your sign-in has expired. Refresh the page and sign in again, then resend. ' +
        'Your draft is still here, but refreshing clears it — copy anything you want to keep first.'
    );
  });

  it('403 → no permission to send from this mailbox + detail + what to do', () => {
    expect(
      describeSendFailure(sce(403, 'Mailbox shared@contoso.com is not approved for sending', 'FROM_NOT_APPROVED'))
        .message
    ).toBe(
      "You don't have permission to send this email from the selected mailbox. " +
        'Mailbox shared@contoso.com is not approved for sending. ' +
        'Choose a different From mailbox or ask your administrator for access. ' +
        DRAFT_KEPT
    );
  });

  it('404 → record or mailbox not found + detail', () => {
    expect(describeSendFailure(sce(404, 'Matter 1234 was not found')).message).toBe(
      "The record or mailbox this email depends on couldn't be found. It may have been deleted or moved. " +
        `Matter 1234 was not found. ${DRAFT_KEPT}`
    );
  });

  it.each([409, 412])('%i → changed elsewhere; reopen and try again; copy the draft before closing', status => {
    expect(describeSendFailure(sce(status, 'Conflict')).message).toBe(
      'This email or its record was changed elsewhere while you were working. Close it, reopen it and try again. ' +
        'Your draft is still here — copy anything you want to keep before you close it.'
    );
  });

  it('413 → attachments too large; remove some and resend', () => {
    expect(describeSendFailure(sce(413, 'Payload Too Large')).message).toBe(
      `The attachments are too large to send. Remove some attachments, then send again. ${DRAFT_KEPT}`
    );
  });

  it('429 → too many sends; wait a moment', () => {
    expect(describeSendFailure(sce(429, 'Too Many Requests')).message).toBe(
      `Too many emails are being sent right now. Wait a moment, then send again. ${DRAFT_KEPT}`
    );
  });

  it.each([500, 502, 503, 504])('%i → the email service could not send it right now; try again shortly', status => {
    expect(describeSendFailure(sce(status, `HTTP ${status}`)).message).toBe(
      `The email service couldn't send this right now. Try again shortly. ${DRAFT_KEPT}`
    );
  });

  it('503 keeps a meaningful server detail', () => {
    expect(describeSendFailure(sce(503, 'Microsoft Graph is throttling this mailbox')).message).toBe(
      "The email service couldn't send this right now. Try again shortly. " +
        `Microsoft Graph is throttling this mailbox. ${DRAFT_KEPT}`
    );
  });

  it('status 0 / NETWORK → no response; check the connection; never claims it definitely did not go out', () => {
    expect(describeSendFailure(sce(0, 'Failed to fetch', SEND_FAILURE_NETWORK_CODE))).toEqual({
      title: 'Email may not have been sent',
      message:
        "Couldn't get a response from the Spaarke server. Check your network connection. " +
        "If you're not sure whether it went out, check your Sent Items before sending again. " +
        DRAFT_KEPT,
    });
  });

  it('a raw fetch TypeError (not yet normalized) → the network sentence, not the browser wording', () => {
    const { message } = describeSendFailure(new TypeError('Failed to fetch'));
    expect(message).toMatch(/^Couldn't get a response from the Spaarke server/);
    expect(message).not.toMatch(/Failed to fetch/);
  });

  it('a 2xx without a record id → "may have been sent" and a warning against a duplicate', () => {
    const d = describeSendFailure(new Error('sendCommunication: response did not include communicationId.'));
    expect(d.title).toBe('Email may have been sent');
    expect(d.message).toBe(
      "Spaarke accepted the email but didn't confirm it, so it may not appear on the record. " +
        'Check your Sent Items before sending it again. Your draft is still here.'
    );
    expect(toSendCommunicationError(new Error('sendCommunication: response body was not valid JSON.')).code).toBe(
      SEND_FAILURE_UNCONFIRMED_CODE
    );
  });

  it('any other status → generic sentence + detail', () => {
    expect(describeSendFailure(sce(418, 'The teapot refused')).message).toBe(
      `The email couldn't be sent. The teapot refused. Try sending again. ${DRAFT_KEPT}`
    );
  });

  it('a client-side Error → generic sentence, never the developer string', () => {
    const { message } = describeSendFailure(new Error('sendCommunication: authenticatedFetch is required.'));
    expect(message).not.toMatch(/sendCommunication|authenticatedFetch/);
    expect(message).toMatch(/^Something went wrong on this page and the email was not sent\./);
  });
});

describe('describeSendFailure — reference + suppression', () => {
  it('returns the correlationId as `reference`', () => {
    expect(describeSendFailure(sce(500, 'boom', 'HTTP_500', 'corr-42')).reference).toBe('corr-42');
  });

  it('omits `reference` when there is no correlationId', () => {
    expect(describeSendFailure(sce(500, 'boom')).reference).toBeUndefined();
    expect(describeSendFailure(sce(500, 'boom', 'HTTP_500', '  ')).reference).toBeUndefined();
  });

  it.each([
    ['HTTP 500', 500],
    ['HTTP_500', 500],
    ['{"title":"Internal Server Error","status":500}', 500],
    ['<!DOCTYPE html><html><body>Service Unavailable</body></html>', 503],
    ['Internal Server Error', 500],
    ['Bad Request', 400],
  ])('never shows %p', (detail, status) => {
    const { message } = describeSendFailure(sce(status as number, detail as string));
    expect(message).not.toContain(detail as string);
    expect(message).not.toMatch(/HTTP[ _]?\d{3}/);
    expect(message).not.toMatch(/[{}<>]/);
  });

  it('clips a very long detail', () => {
    const detail = meaningfulDetail('x'.repeat(1000));
    expect(detail!.length).toBeLessThanOrEqual(300);
    expect(detail!.endsWith('…')).toBe(true);
  });
});

describe('toSendCommunicationError', () => {
  it('passes a SendCommunicationError through unchanged', () => {
    const e = sce(400, 'bad');
    expect(toSendCommunicationError(e)).toBe(e);
  });

  it.each(['Failed to fetch', 'NetworkError when attempting to fetch resource.', 'Load failed'])(
    'maps a fetch TypeError (%p) to status 0 / NETWORK',
    msg => {
      const e = toSendCommunicationError(new TypeError(msg));
      expect(e).toBeInstanceOf(SendCommunicationError);
      expect(e.status).toBe(0);
      expect(e.code).toBe(SEND_FAILURE_NETWORK_CODE);
    }
  );

  it('maps a programming TypeError to CLIENT_ERROR, not NETWORK', () => {
    const e = toSendCommunicationError(new TypeError("Cannot read properties of undefined (reading 'x')"));
    expect(e.status).toBe(0);
    expect(e.code).toBe(SEND_FAILURE_CLIENT_CODE);
  });

  it('maps an argument-guard Error to CLIENT_ERROR, keeping its message as detail', () => {
    const e = toSendCommunicationError(new Error('sendCommunication: subject is required.'));
    expect(e.code).toBe(SEND_FAILURE_CLIENT_CODE);
    expect(e.detail).toBe('sendCommunication: subject is required.');
  });

  it('maps the thrown @spaarke/auth shapes (defensive)', () => {
    expect(toSendCommunicationError(authExhausted()).status).toBe(401);
    const e = toSendCommunicationError(
      apiErrorFor(403, { title: 'Forbidden', status: 403, detail: 'No access', correlationId: 'c-1' })
    );
    expect(e).toMatchObject({ status: 403, detail: 'No access', correlationId: 'c-1' });
  });
});

describe('describeDraftSaveFailure', () => {
  it('says drafts are not available when the host wired no draft persistence', () => {
    const d = describeDraftSaveFailure(new Error('no handler'), true);
    expect(d.title).toBe('Draft not saved');
    expect(d.message).toMatch(/^Saving a draft isn't available here yet\./);
  });

  it('describes a failed save with the server detail and the reference', () => {
    const d = describeDraftSaveFailure(sce(500, 'Dataverse rejected the update', 'HTTP_500', 'r-9'), false);
    expect(d).toEqual({
      title: 'Draft not saved',
      message:
        "The draft couldn't be saved. Dataverse rejected the update. Your message is still here — nothing was lost.",
      reference: 'r-9',
    });
  });
});
