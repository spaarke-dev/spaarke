/**
 * Task 139: `describeStartupFailure` keeps the text of whatever the pane's start-up threw.
 * Host adapters throw `HostAdapterError` — a plain `{ code, message }` object, not an `Error`.
 */
import { describeStartupFailure, NO_ITEM_SELECTED_MESSAGE, STARTUP_FAILED_FALLBACK_MESSAGE } from '../errorMessages';

describe('describeStartupFailure', () => {
  it('reads the message of a plain adapter error object', () => {
    expect(describeStartupFailure({ code: 'CONTENT_RETRIEVAL_FAILED', message: 'Failed to get subject' })).toEqual({
      message: 'Failed to get subject',
      noItemSelected: false,
    });
  });

  it('reads the message of an Error', () => {
    expect(describeStartupFailure(new Error('boom'))).toEqual({ message: 'boom', noItemSelected: false });
  });

  it('maps NO_ITEM_SELECTED to plain text and flags it', () => {
    expect(
      describeStartupFailure({ code: 'NO_ITEM_SELECTED', message: 'No email item is currently selected.' })
    ).toEqual({
      message: NO_ITEM_SELECTED_MESSAGE,
      noItemSelected: true,
    });
    expect(NO_ITEM_SELECTED_MESSAGE).toBe('Select an email to use Spaarke.');
  });

  it.each([undefined, null, 'a thrown string', 7, {}, { message: '' }, { message: '  ' }, { message: 3 }])(
    'falls back to the generic sentence for %p',
    thrown => {
      expect(describeStartupFailure(thrown)).toEqual({
        message: STARTUP_FAILED_FALLBACK_MESSAGE,
        noItemSelected: false,
      });
    }
  );
});
