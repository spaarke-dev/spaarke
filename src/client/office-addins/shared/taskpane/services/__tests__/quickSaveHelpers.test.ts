import {
  buildEmailSaveRequest,
  buildDocumentSaveRequest,
  computeQuickSaveIdempotencyKey,
  arrayBufferToBase64,
  fileNameFromWebUrl,
  describeQuickSaveSuccess,
  describeQuickSaveFailure,
  describeUnsavableIdentity,
  buildQuickSaveRecordLink,
  isUrlOnConfiguredOrigin,
  type QuickSaveEmailContext,
} from '../quickSaveHelpers';
import type { EntitySearchResult } from '../../hooks/useEntitySearch';
import { LOGICAL_TO_ENTITY_TYPE } from '../communicationSuggestionsService';

const target: EntitySearchResult = {
  id: '11111111-1111-1111-1111-111111111111',
  entityType: 'Matter',
  logicalName: 'sprk_matter',
  name: 'Smith v Jones',
};

const context: QuickSaveEmailContext = {
  internetMessageId: '<abc@contoso.com>',
  subject: 'Re: Contract terms',
  senderEmail: 'sender@contoso.com',
  senderName: 'The Sender',
  recipients: [
    { email: 'a@x.com', displayName: 'A', type: 'to' },
    { email: 'b@x.com', type: 'cc' },
  ],
  sentDate: new Date('2026-08-01T10:00:00Z'),
};

describe('buildEmailSaveRequest', () => {
  it('files to the predicted record via the Email save contract', () => {
    const req = buildEmailSaveRequest(context, target, 'idem-key-1');

    expect(req.contentType).toBe('Email');
    expect(req.triggerAiProcessing).toBe(true);
    // Target entity uses the FRIENDLY type name the save accepts ("Matter"), never the logical name
    // ("sprk_matter") — the server refuses that with 400 OFFICE_002 (#1075, task 084).
    expect(req.targetEntity).toEqual({
      entityType: 'Matter',
      entityId: '11111111-1111-1111-1111-111111111111',
      displayName: 'Smith v Jones',
    });
    // Body/attachments are fetched server-side; client sends only the message id + metadata.
    expect(req.email.internetMessageId).toBe('<abc@contoso.com>');
    expect(req.email.subject).toBe('Re: Contract terms');
    expect(req.email.senderEmail).toBe('sender@contoso.com');
    expect(req.email.body).toBeUndefined();
    // Recipient types are mapped to the server's PascalCase enum.
    expect(req.email.recipients).toEqual([
      { type: 'To', email: 'a@x.com', name: 'A' },
      { type: 'Cc', email: 'b@x.com' },
    ]);
    expect(req.email.sentDate).toBe('2026-08-01T10:00:00.000Z');
    // Idempotency key travels in the body (the server accepts it there or via header).
    expect(req.idempotencyKey).toBe('idem-key-1');
  });

  it('an UNFILED save (target null, task 118) carries no targetEntity at all', () => {
    const req = buildEmailSaveRequest(context, null, 'idem-key-1');

    expect('targetEntity' in req).toBe(false);
    expect(req.email.internetMessageId).toBe(context.internetMessageId);
  });

  it('never sends the logical name as targetEntity.entityType (#1075: "sprk_matter" → 400 OFFICE_002)', () => {
    const req = buildEmailSaveRequest(context, target, 'idem-key-1');

    expect(req.targetEntity?.entityType).not.toBe(target.logicalName);
    expect(req.targetEntity?.entityType).toBe(target.entityType);
  });

  it("marks the name system-derived: the ribbon files under the email's own subject, never a typed name (task 046)", () => {
    const req = buildEmailSaveRequest(context, target, 'idem-key-1');

    expect(req.email.isNameSystemDerived).toBe(true);
  });

  it('falls back to a placeholder subject/sender when the email lacks them', () => {
    const bare: QuickSaveEmailContext = { internetMessageId: '<x@y>', subject: '' };
    const req = buildEmailSaveRequest(bare, target, 'k');
    expect(req.documentMetadata.name).toBe('Untitled Email');
    expect(req.email.subject).toBe('Untitled Email');
    expect(req.email.senderEmail).toBe('unknown@placeholder.com');
    expect(req.email.recipients).toEqual([]);
    expect(req.email.sentDate).toBeUndefined();
  });
});

/**
 * Task 084 (#1075). MIRROR of the type list the save accepts — `OfficeEndpoints.ValidateSaveRequest`
 * (src/server/api/Sprk.Bff.Api/Api/Office/OfficeEndpoints.cs, `validEntityTypes`, compared
 * lowercased). If the server list changes, change this mirror in the same PR. It is a TEST-only copy:
 * production code never carries a second copy of the server's rule.
 */
const SAVE_ACCEPTED_ENTITY_TYPES = ['matter', 'project', 'invoice', 'workassignment', 'event', 'todo', 'contact'];

describe('the ribbon quick-save only ever sends a type the save accepts (#1075, task 084)', () => {
  const producible = Array.from(new Set(Object.values(LOGICAL_TO_ENTITY_TYPE)));

  it('the logical-name map produces at least the filing types the picker offers', () => {
    expect(producible).toEqual(expect.arrayContaining(['Matter', 'Project', 'Invoice']));
  });

  it.each(producible)('%s, lowercased, is in the list of types the save accepts', entityType => {
    expect(SAVE_ACCEPTED_ENTITY_TYPES).toContain(entityType.toLowerCase());
  });

  it.each(Object.entries(LOGICAL_TO_ENTITY_TYPE))(
    'a prediction of %s reaches the wire as an accepted type (%s)',
    (logicalName, entityType) => {
      const predicted: EntitySearchResult = { id: target.id, entityType, logicalName, name: 'Predicted' };
      const req = buildEmailSaveRequest(context, predicted, 'k');
      expect(SAVE_ACCEPTED_ENTITY_TYPES).toContain(req.targetEntity?.entityType.toLowerCase());
    }
  );
});

describe('computeQuickSaveIdempotencyKey', () => {
  it('is deterministic for the same message id + target (email kind)', async () => {
    const a = await computeQuickSaveIdempotencyKey({ kind: 'email', internetMessageId: '<abc@contoso.com>', target });
    const b = await computeQuickSaveIdempotencyKey({ kind: 'email', internetMessageId: '<abc@contoso.com>', target });
    expect(a).toBe(b);
    expect(a.length).toBeGreaterThan(0);
  });

  it('differs when the target differs (email kind)', async () => {
    const a = await computeQuickSaveIdempotencyKey({ kind: 'email', internetMessageId: '<abc@contoso.com>', target });
    const other: EntitySearchResult = { ...target, id: '22222222-2222-2222-2222-222222222222' };
    const b = await computeQuickSaveIdempotencyKey({
      kind: 'email',
      internetMessageId: '<abc@contoso.com>',
      target: other,
    });
    expect(a).not.toBe(b);
  });

  it('is deterministic for the same title + content (document kind)', async () => {
    const a = await computeQuickSaveIdempotencyKey({ kind: 'document', title: 'Contract.docx', contentBase64: 'AAAA' });
    const b = await computeQuickSaveIdempotencyKey({ kind: 'document', title: 'Contract.docx', contentBase64: 'AAAA' });
    expect(a).toBe(b);
    expect(a.length).toBeGreaterThan(0);
  });

  it('differs when the document content differs (task 037 double-click-does-not-duplicate guard)', async () => {
    const a = await computeQuickSaveIdempotencyKey({ kind: 'document', title: 'Contract.docx', contentBase64: 'AAAA' });
    const b = await computeQuickSaveIdempotencyKey({ kind: 'document', title: 'Contract.docx', contentBase64: 'BBBB' });
    expect(a).not.toBe(b);
  });

  it('the email and document kinds never collide even over similar-looking inputs', async () => {
    const emailKey = await computeQuickSaveIdempotencyKey({ kind: 'email', internetMessageId: 'x', target });
    const documentKey = await computeQuickSaveIdempotencyKey({ kind: 'document', title: 'x', contentBase64: '' });
    expect(emailKey).not.toBe(documentKey);
  });
});

describe('buildDocumentSaveRequest (task 037 / FR-17; task 089 — version vs keep-both create)', () => {
  it('a CREATE is unfiled and asks the server to keep both on a name collision — never existingDocumentId/isNewVersion', () => {
    const req = buildDocumentSaveRequest({ title: 'Contract' }, 'BASE64BYTES', 'idem-key-1');

    expect(req.contentType).toBe('Document');
    expect(req.triggerAiProcessing).toBe(true);
    expect(req.document).toEqual({
      fileName: 'Contract.docx',
      title: 'Contract',
      contentType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      contentBase64: 'BASE64BYTES',
      allowRename: true,
    });
    expect(req.idempotencyKey).toBe('idem-key-1');
    // Association is optional for a Document save (mirrors useSaveFlow's "Association is optional"
    // rule) — a quick save never guesses a target entity.
    expect(req).not.toHaveProperty('targetEntity');
    expect(req.document).not.toHaveProperty('existingDocumentId');
    expect(req.document).not.toHaveProperty('isNewVersion');
  });

  it('a VERSION names the resolved document with isNewVersion, and never asks to rename (a version cannot collide by name)', () => {
    const req = buildDocumentSaveRequest({ title: 'Contract' }, 'B64', 'k', {
      mode: 'version',
      existingDocumentId: '2bcfc5d2-0000-4000-8000-000000000001',
    });

    expect(req.document.existingDocumentId).toBe('2bcfc5d2-0000-4000-8000-000000000001');
    expect(req.document.isNewVersion).toBe(true);
    expect(req.document).not.toHaveProperty('allowRename');
    expect(req).not.toHaveProperty('targetEntity');
  });

  it("names the file by the PANE's rule — the Document Name default minus its extension, then .docx (never doubled)", () => {
    expect(buildDocumentSaveRequest({ title: 'Contract.docx' }, 'x', 'k').document).toMatchObject({
      fileName: 'Contract.docx',
      title: 'Contract',
    });
    // The pane strips .doc too (SaveFlow's default name), then uploads as .docx (useSaveFlow).
    expect(buildDocumentSaveRequest({ title: 'Memo.doc' }, 'x', 'k').document.fileName).toBe('Memo.docx');
    expect(buildDocumentSaveRequest({ title: 'Untitled Document' }, 'x', 'k').document.fileName).toBe(
      'Untitled Document.docx'
    );
  });

  it("falls back to the pane's placeholder name when the title is blank", () => {
    const req = buildDocumentSaveRequest({ title: '   ' }, 'x', 'k');
    expect(req.document.title).toBe('document');
    expect(req.document.fileName).toBe('document.docx');
  });
});

describe('computeQuickSaveIdempotencyKey — version vs create (task 089)', () => {
  it('a version save of the same bytes is a different operation from a create of them', async () => {
    const create = await computeQuickSaveIdempotencyKey({ kind: 'document', title: 'C', contentBase64: 'AAAA' });
    const version = await computeQuickSaveIdempotencyKey({
      kind: 'document',
      title: 'C',
      contentBase64: 'AAAA',
      existingDocumentId: '11111111-1111-1111-1111-111111111111',
    });
    expect(version).not.toBe(create);
  });
});

describe('fileNameFromWebUrl (task 089 — the name the server actually stored)', () => {
  it('reads the file= parameter of the documented SPE Office web URL', () => {
    expect(
      fileNameFromWebUrl(
        'https://contoso.sharepoint.com/:w:r/contentstorage/CSP_x/_layouts/15/doc2.aspx?sourcedoc=%7Babc%7D&file=Brief%201.docx&action=default&mobileredirect=true'
      )
    ).toBe('Brief 1.docx');
  });

  it('reads the last segment of a direct file URL', () => {
    expect(
      fileNameFromWebUrl(
        'https://spaarke.sharepoint.com/contentstorage/CSP_1/Document%20Library/Examiner%20report%20draft.docx'
      )
    ).toBe('Examiner report draft.docx');
  });

  it('names nothing for a viewer page with no file parameter, an empty value, or garbage', () => {
    expect(fileNameFromWebUrl('https://contoso.sharepoint.com/_layouts/15/Doc.aspx?sourcedoc=%7Babc%7D')).toBeNull();
    expect(fileNameFromWebUrl(undefined)).toBeNull();
    expect(fileNameFromWebUrl('not a url')).toBeNull();
  });
});

describe('describeQuickSaveSuccess (task 089 — Quick Save always says what it did)', () => {
  const storedAs = (name: string) => ({
    documentId: 'd',
    webUrl: `https://c.sharepoint.com/contentstorage/CSP_1/Document%20Library/${encodeURIComponent(name)}`,
  });

  it('a version: "Saved a new version of \'{name}\'"', () => {
    expect(
      describeQuickSaveSuccess({
        target: { mode: 'version', existingDocumentId: 'd' },
        requestedFileName: 'Brief.docx',
        documentLabel: 'Brief',
        saved: storedAs('Brief.docx'),
        duplicate: false,
      })
    ).toBe("Saved a new version of 'Brief'.");
  });

  it('a create stored under the requested name names it', () => {
    expect(
      describeQuickSaveSuccess({
        target: { mode: 'create' },
        requestedFileName: 'Brief.docx',
        saved: storedAs('Brief.docx'),
        duplicate: false,
      })
    ).toBe("Saved to Spaarke as 'Brief.docx'.");
  });

  it('a create the server KEPT BOTH under another name names the file actually created, and says why', () => {
    const message = describeQuickSaveSuccess({
      target: { mode: 'create' },
      requestedFileName: 'Untitled Document.docx',
      saved: storedAs('Untitled Document 1.docx'),
      duplicate: false,
    });
    expect(message).toContain("'Untitled Document 1.docx'");
    expect(message).toMatch(/both were kept/);
  });

  it('never names a guessed file when the stored name is unknown', () => {
    expect(
      describeQuickSaveSuccess({
        target: { mode: 'create' },
        requestedFileName: 'Brief.docx',
        saved: null,
        duplicate: false,
      })
    ).toBe('Saved to Spaarke.');
  });
});

describe('describeQuickSaveFailure (task 089 — never the fixed "Failed to save" when the server gave a reason)', () => {
  const refusal = (problem: Record<string, unknown>) => Object.assign(new Error('x'), { error: problem });

  it("shows the server's own message for a refusal (403)", () => {
    const message = describeQuickSaveFailure(
      refusal({ status: 403, title: 'Forbidden', detail: 'You do not have write access to this document.' }),
      'save'
    );
    expect(message).toBe('Spaarke did not save this document: You do not have write access to this document. (403)');
    expect(message).not.toMatch(/Failed to save/);
  });

  it('falls back to the catalog message for a known errorCode with no detail, then to the title', () => {
    expect(
      describeQuickSaveFailure(refusal({ status: 413, errorCode: 'OFFICE_004', title: 'x', detail: '' }), 'save')
    ).toMatch(/25MB/);
    expect(describeQuickSaveFailure(refusal({ status: 429, title: 'Too Many Requests' }), 'save')).toBe(
      'Spaarke did not save this document: Too Many Requests (429)'
    );
  });

  it('says what failed in words when there is no server reason', () => {
    expect(describeQuickSaveFailure({ code: 'CONTENT_RETRIEVAL_FAILED', message: 'getFileAsync failed' }, 'read')).toBe(
      "Couldn't read this document from Word, so nothing was saved (getFileAsync failed)."
    );
    expect(describeQuickSaveFailure(new TypeError('Failed to fetch'), 'save')).toMatch(
      /^Couldn't reach Spaarke.*Failed to fetch/
    );
    expect(describeQuickSaveFailure(new Error('auth failed'), 'connect')).toMatch(/^Couldn't connect to Spaarke/);
  });
});

describe('describeUnsavableIdentity (task 089 — Quick Save never creates silently where the pane would not)', () => {
  it.each(['conflict', 'indeterminate', 'denied'] as const)('%s → a reason, so nothing is saved', kind => {
    const outcome =
      kind === 'indeterminate'
        ? ({ kind, reason: 'unavailable' } as const)
        : ({ kind } as { kind: 'conflict' | 'denied' });
    expect(describeUnsavableIdentity(outcome)).toMatch(/did not save/);
  });

  it('error → a reason naming the cause', () => {
    expect(describeUnsavableIdentity({ kind: 'error', message: 'boom' })).toMatch(/boom/);
  });

  it('new and resolved → null (Quick Save handles them)', () => {
    expect(describeUnsavableIdentity({ kind: 'new', reason: 'not_cloud_document' })).toBeNull();
    expect(
      describeUnsavableIdentity({
        kind: 'resolved',
        documentId: 'd',
        documentName: '',
        fileName: '',
        relatedRecord: null,
      })
    ).toBeNull();
  });
});

describe('arrayBufferToBase64', () => {
  it('round-trips known bytes the same way SaveView.captureDocumentContent does (Uint8Array -> btoa)', () => {
    const bytes = new Uint8Array([0x50, 0x4b, 0x03, 0x04, 0x00, 0xff]);
    const base64 = arrayBufferToBase64(bytes.buffer);
    // Decode back and compare — proves this is a real, correct base64 encoding, not just "some string".
    const decoded = atob(base64);
    const roundTripped = Uint8Array.from(decoded, c => c.charCodeAt(0));
    expect(Array.from(roundTripped)).toEqual(Array.from(bytes));
  });

  it('handles an empty buffer', () => {
    expect(arrayBufferToBase64(new ArrayBuffer(0))).toBe('');
  });
});

describe('buildQuickSaveRecordLink / isUrlOnConfiguredOrigin (task 110 — the "Open in Spaarke" link)', () => {
  const ID = '2bcfc5d2-0000-4000-8000-000000000001';

  it('builds the sprk_document record URL with the shared builder, tolerating a trailing slash on ORG_URL', () => {
    expect(buildQuickSaveRecordLink('https://org.crm.dynamics.com/', 'sprk_MatterManagement', ID)).toBe(
      `https://org.crm.dynamics.com/main.aspx?appname=sprk_MatterManagement&etn=sprk_document&id=${ID}&pagetype=entityrecord&navbar=off`
    );
  });

  it('canonicalizes a braced / upper-case id', () => {
    expect(buildQuickSaveRecordLink('https://org.crm.dynamics.com', '', `{${ID.toUpperCase()}}`)).toContain(
      `id=${ID}&`
    );
  });

  it.each([undefined, '', '   '])('NEGATIVE: ORG_URL %p → no link', orgUrl => {
    expect(buildQuickSaveRecordLink(orgUrl, 'app', ID)).toBeNull();
  });

  it.each([null, undefined, '', 'not-a-guid'])('NEGATIVE: id %p → no link', id => {
    expect(buildQuickSaveRecordLink('https://org.crm.dynamics.com', 'app', id)).toBeNull();
  });

  it('accepts only a URL on the configured origin', () => {
    const org = 'https://org.crm.dynamics.com';
    expect(isUrlOnConfiguredOrigin(`${org}/main.aspx?etn=sprk_document`, org)).toBe(true);
    expect(isUrlOnConfiguredOrigin(`${org}/main.aspx`, `${org}/`)).toBe(true);
  });

  it.each([
    ['another host', 'https://evil.example.com/main.aspx'],
    ['a look-alike prefix host', 'https://org.crm.dynamics.com.evil.example/x'],
    ['another scheme on the same host', 'http://org.crm.dynamics.com/x'],
    ['a javascript: URL', 'javascript:alert(1)'],
    ['a non-URL', 'main.aspx'],
    ['a non-string', 42],
  ])('NEGATIVE: refuses %s', (_label, url) => {
    expect(isUrlOnConfiguredOrigin(url, 'https://org.crm.dynamics.com')).toBe(false);
  });

  it('NEGATIVE: refuses everything when ORG_URL is unset', () => {
    expect(isUrlOnConfiguredOrigin('https://org.crm.dynamics.com/x', undefined)).toBe(false);
    expect(isUrlOnConfiguredOrigin('https://org.crm.dynamics.com/x', '')).toBe(false);
  });
});
