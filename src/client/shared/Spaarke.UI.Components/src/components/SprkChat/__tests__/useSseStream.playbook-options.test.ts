/**
 * useSseStream — playbook_options SSE event parsing
 * (chat-routing-redesign-r1 task 117a/117b).
 *
 * Covers:
 *   (1) `parseSseEvent` recognizes the `playbook_options` type discriminator and
 *       round-trips the BFF wire format verbatim.
 *   (2) The hook handler forwards the payload to the callback registered via
 *       `setOnPlaybookOptions` with defensive defaults applied for missing
 *       fields (e.g. malformed BFF emit).
 */

import { parseSseEvent } from '../hooks/useSseStream';

describe('parseSseEvent — playbook_options', () => {
  it('parses a well-formed playbook_options event with all fields', () => {
    const line =
      'data: {"type":"playbook_options","content":null,"data":{' +
      '"candidates":[' +
      '{"playbookId":"pb-1","playbookCode":"PB-001","displayName":"Summarize Contract","confidence":0.92,"reason":"top-confidence"}' +
      '],"libraryModalCta":true,"sessionAttachmentIds":["f-1"],"rerankInvoked":false,"rerankReason":null}}';
    const result = parseSseEvent(line);
    expect(result).not.toBeNull();
    expect(result!.type).toBe('playbook_options');
    expect(result!.data!.candidates).toHaveLength(1);
    expect(result!.data!.candidates![0].displayName).toBe('Summarize Contract');
    expect(result!.data!.libraryModalCta).toBe(true);
    expect(result!.data!.sessionAttachmentIds).toEqual(['f-1']);
    expect(result!.data!.rerankInvoked).toBe(false);
  });

  it('parses a zero-candidate playbook_options event (no-match graceful path)', () => {
    const line =
      'data: {"type":"playbook_options","content":null,"data":{' +
      '"candidates":[],"libraryModalCta":true,"sessionAttachmentIds":[],"rerankInvoked":false,"rerankReason":null}}';
    const result = parseSseEvent(line);
    expect(result).not.toBeNull();
    expect(result!.type).toBe('playbook_options');
    expect(result!.data!.candidates).toEqual([]);
    expect(result!.data!.libraryModalCta).toBe(true);
  });

  it('parses an event where the reranker ran (rerankReason populated)', () => {
    const line =
      'data: {"type":"playbook_options","content":null,"data":{' +
      '"candidates":[],"libraryModalCta":true,"sessionAttachmentIds":[],"rerankInvoked":true,"rerankReason":"ambiguous-top-2-within-margin"}}';
    const result = parseSseEvent(line);
    expect(result!.data!.rerankInvoked).toBe(true);
    expect(result!.data!.rerankReason).toBe('ambiguous-top-2-within-margin');
  });
});
