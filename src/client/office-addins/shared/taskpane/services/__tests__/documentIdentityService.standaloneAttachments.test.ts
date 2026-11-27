/**
 * Task 143: the email lookup's `not_saved` answer also carries `savedAttachmentNames` (attachments saved on their own, no
 * `.eml`). `resolveEmailIdentity` still answers `null` for it (the ordinary save form), and hands those names to its
 * caller. A missing field is no claim at all — the callback is not called (task 141's rule).
 */
import { resolveEmailIdentity, resolveEmailIdentityAnswer } from '../documentIdentityService';
import { apiClient, ApiClientError } from '@shared/services';

jest.mock('@shared/services', () => {
  const actual = jest.requireActual('@shared/services');
  return { ...actual, apiClient: { get: jest.fn(), post: jest.fn(), put: jest.fn(), delete: jest.fn() } };
});

const post = apiClient.post as jest.Mock;
const KEYS = { internetMessageId: '<CAF0-143@mail.example.com>', exchangeItemId: 'AAMk-143' };

beforeEach(() => post.mockReset());

describe('resolveEmailIdentity — names of attachments saved on their own (task 143)', () => {
  it('a not_saved answer with names: null identity, and the names reach the caller', async () => {
    post.mockResolvedValue({ resolved: false, reason: 'not_saved', savedAttachmentNames: ['Lease.docx', ' ', 7] });
    const sink = jest.fn();

    await expect(resolveEmailIdentity(KEYS, sink)).resolves.toBeNull();
    expect(sink).toHaveBeenCalledWith(['Lease.docx']);
    await expect(resolveEmailIdentityAnswer(KEYS)).resolves.toEqual({
      identity: null,
      savedAttachmentNames: ['Lease.docx'],
    });
  });

  it('negative: a not_saved answer without the field makes no claim', async () => {
    post.mockResolvedValue({ resolved: false, reason: 'not_saved' });
    const sink = jest.fn();

    await expect(resolveEmailIdentity(KEYS, sink)).resolves.toBeNull();
    expect(sink).not.toHaveBeenCalled();
    await expect(resolveEmailIdentityAnswer(KEYS)).resolves.toEqual({ identity: null });
  });

  it('negative: a saved email never reports names through the not-saved channel', async () => {
    post.mockResolvedValue({
      resolved: true,
      documentId: '6fb8c3fa-13c3-f111-a05c-0022482913fc',
      documentName: 'RE: Lease',
      fileName: 'RE_ Lease.eml',
      savedAttachmentNames: ['Lease.docx'],
    });
    const sink = jest.fn();

    const identity = await resolveEmailIdentity(KEYS, sink);
    expect(identity).toMatchObject({ kind: 'resolved', savedAttachmentNames: ['Lease.docx'] });
    expect(sink).not.toHaveBeenCalled();
  });

  it('negative: a failed lookup makes no claim', async () => {
    post.mockRejectedValue(new ApiClientError({ status: 503, title: 'Unavailable' } as never));
    const sink = jest.fn();

    await expect(resolveEmailIdentity(KEYS, sink)).resolves.toBeNull();
    expect(sink).not.toHaveBeenCalled();
  });
});
