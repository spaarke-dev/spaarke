/**
 * Task 141: `resolve-email-identity` also answers `savedAttachmentNames` (the saved email's children the caller may read).
 *
 * - A list is carried onto the resolved outcome (blank entries dropped), so the pane can mark each attachment saved.
 * - An absent, null or malformed field is "not determined": the outcome has NO `savedAttachmentNames`, never an empty
 *   list — an empty list would claim every attachment is unsaved.
 */

import { resolveEmailIdentity } from '../documentIdentityService';
import { apiClient } from '@shared/services';

jest.mock('@shared/services', () => {
  const actual = jest.requireActual('@shared/services');
  return {
    ...actual,
    apiClient: {
      get: jest.fn(),
      post: jest.fn(),
      put: jest.fn(),
      delete: jest.fn(),
      uploadFile: jest.fn(),
      configure: jest.fn(),
    },
  };
});

const mockPost = apiClient.post as jest.Mock;

const DOC_ID = '6fb8c3fa-13c3-f111-a05c-0022482913fc';
const MESSAGE_ID = '<CAF0-141@mail.example.com>';

const resolvedResponse = {
  resolved: true,
  documentId: DOC_ID,
  documentName: 'RE: Settlement',
  fileName: 'RE_ Settlement.eml',
  relatedRecord: null,
  reason: null,
};

describe('resolveEmailIdentity savedAttachmentNames (task 141)', () => {
  beforeEach(() => mockPost.mockReset());

  it('carries the saved attachment names onto the resolved outcome, dropping blank entries', async () => {
    mockPost.mockResolvedValue({
      ...resolvedResponse,
      savedAttachmentNames: ['Settlement terms.pdf', '', '  ', 'Exhibit A.pdf'],
    });

    const outcome = await resolveEmailIdentity({ internetMessageId: MESSAGE_ID, exchangeItemId: null });

    expect(outcome).toMatchObject({
      kind: 'resolved',
      documentId: DOC_ID,
      savedAttachmentNames: ['Settlement terms.pdf', 'Exhibit A.pdf'],
    });
  });

  it('an empty list is kept: the server says no attachment is saved', async () => {
    mockPost.mockResolvedValue({ ...resolvedResponse, savedAttachmentNames: [] });

    const outcome = await resolveEmailIdentity({ internetMessageId: MESSAGE_ID, exchangeItemId: null });

    expect(outcome).toMatchObject({ kind: 'resolved', savedAttachmentNames: [] });
  });

  it.each([
    ['absent (an older server, or the read failed)', {}],
    ['null', { savedAttachmentNames: null }],
    ['not a list', { savedAttachmentNames: 'Settlement terms.pdf' }],
  ])('a field that is %s leaves the names undetermined (no property, never an empty list)', async (_label, extra) => {
    mockPost.mockResolvedValue({ ...resolvedResponse, ...extra });

    const outcome = await resolveEmailIdentity({ internetMessageId: MESSAGE_ID, exchangeItemId: null });

    expect(outcome).not.toBeNull();
    expect(outcome).toMatchObject({ kind: 'resolved', documentId: DOC_ID });
    expect(outcome).not.toHaveProperty('savedAttachmentNames');
  });
});
