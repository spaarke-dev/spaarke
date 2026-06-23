/**
 * composition.integration.test.ts — R6 task 084 / D-D-05.
 *
 * End-to-end integration test for Pillar 8 composition per spec FR-52:
 *
 *   1. `/summarize #engagement-letter.docx`
 *      (soft slash + single filename reference)
 *
 *   2. `/draft response to @opposing-counsel about #motion-to-dismiss`
 *      (soft slash + multi-reference + interleaved natural-language text)
 *
 * Plus NFR-11 binding regression: natural-language equivalent
 *   "summarize the engagement letter"
 * must STILL produce a passthrough body (intentHint absent, no resolved
 * references) so the existing CapabilityRouter / SprkChat natural-language
 * path continues to work UNCHANGED.
 *
 * The test orchestrates the full chain across the 4 Pillar 8 modules:
 *
 *   CommandRouter.parse(text)
 *     → ReferenceResolver.resolveAll(intent.references, ctx) with stubbed
 *       adapters (no network, no React, no SprkChat)
 *     → SoftSlashRouter.decorateBody(intent, body)
 *     → final composed BFF body { message, intentHint?, resolvedReferences? }
 *
 * Composition contract verified here:
 *   - decorateBody adds `intentHint` ONLY for soft slashes
 *   - resolveAll adds `resolvedReferences[]` independently — the two
 *     decorations target distinct fields and compose without conflict
 *   - interleaved natural-language text between references is preserved
 *     verbatim in the `message` field
 *   - NFR-11 passthrough: command:null inputs produce no decoration
 *
 * Per CLAUDE.md §3 + ADR-029: this file is test-only.
 * BFF publish-size delta = 0 MB.
 *
 * @see CommandRouter.ts (task 080)
 * @see HardSlashExecutor.ts (task 081 — not exercised here; hard slashes
 *      do not compose with references)
 * @see SoftSlashRouter.ts (task 082) — decorateBody, toCommandIntent
 * @see ReferenceResolver.ts (task 083) — resolveAll, ResolverContext
 * @see projects/spaarke-ai-platform-unification-r6/spec.md FR-52, NFR-11
 */

import { parse, type Intent } from '../CommandRouter';
import {
  decorateBody,
  type DecoratedChatBody,
} from '../SoftSlashRouter';
import {
  resolveAll,
  __resetCacheForTests,
  type ResolverContext,
  type ResolvedReference,
  type ScopeLookupResult,
  type SessionFileMetadata,
} from '../ReferenceResolver';

// ---------------------------------------------------------------------------
// Test fixtures
// ---------------------------------------------------------------------------

const TENANT_ID = 'tenant-r6-integration-0001';
const SESSION_ID = 'session-r6-integration-0001';

const ENGAGEMENT_LETTER_FILE: SessionFileMetadata = {
  documentId: 'doc-engagement-letter-001',
  filename: 'engagement-letter.docx',
};

const MOTION_FILE: SessionFileMetadata = {
  documentId: 'doc-motion-to-dismiss-001',
  filename: 'motion-to-dismiss',
};

/**
 * Build a fileLookup stub mimicking the session-files index. Matches the
 * `fileLookup(filename)` contract from `ReferenceResolver.ResolverContext`.
 * Case-insensitive; returns null on miss.
 */
function buildFileLookup(
  files: ReadonlyArray<SessionFileMetadata>,
): (filename: string) => Promise<SessionFileMetadata | null> {
  return async (filename: string) => {
    const wanted = filename.toLowerCase();
    for (const f of files) {
      if (f.filename.toLowerCase() === wanted) return f;
    }
    return null;
  };
}

/**
 * Build a scopeFetch stub. The integration test does not exercise scope
 * references (the two FR-52 examples use filename + entity refs only) but
 * supplying a stub demonstrates the host wiring path.
 */
function buildScopeFetch(): (search: string) => Promise<ScopeLookupResult | null> {
  return async () => null;
}

/**
 * Build the resolver context the host (ConversationPane) would assemble at
 * send-message time. The full chain is host-supplied; the test mirrors the
 * shape per `ResolverContext` jsdoc.
 */
function buildResolverContext(
  files: ReadonlyArray<SessionFileMetadata> = [],
): ResolverContext {
  return {
    tenantId: TENANT_ID,
    sessionId: SESSION_ID,
    fileLookup: buildFileLookup(files),
    scopeFetch: buildScopeFetch(),
  };
}

/**
 * Compose the final BFF body the way ConversationPane would after running
 * the full chain. This mirrors the composition contract documented in the
 * file header: `intentHint` from SoftSlashRouter + `resolvedReferences`
 * appended independently.
 *
 * Returns the body PLUS the resolver output for assertion convenience.
 */
async function runComposition(
  userText: string,
  ctx: ResolverContext,
): Promise<{
  intent: Intent;
  resolvedReferences: ResolvedReference[];
  body: DecoratedChatBody & { resolvedReferences?: ResolvedReference[] };
}> {
  // 1) Parse
  const intent = parse(userText);

  // 2) Resolve references (NFR-01 — never throws)
  const resolvedReferences = await resolveAll(intent.references, ctx);

  // 3) Build the base body that SprkChat would emit
  const base: DecoratedChatBody = { message: userText };

  // 4) Decorate via SoftSlashRouter (adds intentHint IFF soft slash)
  const decorated = decorateBody(intent, base);

  // 5) Compose the resolved-references field if any references are present.
  //    Per task 083 design, the host attaches the resolved set to the body
  //    so the agent prompt receives "known entities". We use a separate
  //    field name (`resolvedReferences`) that does not collide with the
  //    existing SoftSlashRouter decoration surface.
  const body: DecoratedChatBody & { resolvedReferences?: ResolvedReference[] } =
    resolvedReferences.length > 0
      ? { ...decorated, resolvedReferences }
      : { ...decorated };

  return { intent, resolvedReferences, body };
}

beforeEach(() => {
  __resetCacheForTests();
});

// ---------------------------------------------------------------------------
// Scenario 1 — `/summarize #engagement-letter.docx`
// (FR-52 example: soft slash + single filename reference)
// ---------------------------------------------------------------------------

describe('Pillar 8 composition integration (FR-52)', () => {
  describe('/summarize #engagement-letter.docx', () => {
    const INPUT = '/summarize #engagement-letter.docx';

    it('parses to soft slash + single filename reference', () => {
      const intent = parse(INPUT);

      // Soft-slash classification
      expect(intent.isHardSlash).toBe(false);
      expect(intent.isSoftSlash).toBe(true);
      expect(intent.command).toBe('/summarize');

      // Single filename reference
      expect(intent.references).toHaveLength(1);
      expect(intent.references[0]).toEqual({
        kind: 'filename',
        value: 'engagement-letter.docx',
        raw: '#engagement-letter.docx',
      });

      // rawText round-trip
      expect(intent.rawText).toBe(INPUT);
    });

    it('resolves the file reference via the file adapter', async () => {
      const ctx = buildResolverContext([ENGAGEMENT_LETTER_FILE]);
      const intent = parse(INPUT);
      const resolved = await resolveAll(intent.references, ctx);

      expect(resolved).toHaveLength(1);
      expect(resolved[0]).toEqual({
        type: 'file',
        rawToken: '#engagement-letter.docx',
        canonicalId: 'doc-engagement-letter-001',
        displayName: 'engagement-letter.docx',
        metadata: { source: 'session-files-index' },
        resolved: true,
      });
    });

    it('decorates the outbound body with intentHint + resolvedReferences', async () => {
      const ctx = buildResolverContext([ENGAGEMENT_LETTER_FILE]);
      const { body } = await runComposition(INPUT, ctx);

      // SoftSlashRouter contribution
      expect(body.intentHint).toBe('summarize');
      // ReferenceResolver contribution
      expect(body.resolvedReferences).toHaveLength(1);
      expect(body.resolvedReferences?.[0].resolved).toBe(true);
      expect(body.resolvedReferences?.[0].canonicalId).toBe(
        'doc-engagement-letter-001',
      );
      // Message preserved verbatim
      expect(body.message).toBe(INPUT);
    });

    it('produces a coherent BFF payload', async () => {
      const ctx = buildResolverContext([ENGAGEMENT_LETTER_FILE]);
      const { body } = await runComposition(INPUT, ctx);

      // The composed payload carries BOTH the command intent AND the
      // resolved entities the agent needs. The BFF deserializes both and
      // routes via Layer 0.5 (intentHint) + agent prompt (resolved refs).
      expect(body).toMatchObject({
        message: INPUT,
        intentHint: 'summarize',
        resolvedReferences: [
          {
            type: 'file',
            rawToken: '#engagement-letter.docx',
            canonicalId: 'doc-engagement-letter-001',
            displayName: 'engagement-letter.docx',
            resolved: true,
          },
        ],
      });
    });

    it('gracefully degrades when the file adapter cannot resolve', async () => {
      // NFR-01 binding: unresolved references DO NOT block the conversation.
      // The body still carries intentHint + an unresolved-flag entry the
      // agent prompt can surface for clarification.
      const ctx = buildResolverContext([]); // empty file index
      const { body } = await runComposition(INPUT, ctx);

      expect(body.intentHint).toBe('summarize');
      expect(body.resolvedReferences).toHaveLength(1);
      expect(body.resolvedReferences?.[0].resolved).toBe(false);
      expect(body.resolvedReferences?.[0].canonicalId).toBeNull();
      // displayName falls back to the raw token per NFR-01 invariant
      expect(body.resolvedReferences?.[0].displayName).toBe(
        '#engagement-letter.docx',
      );
    });
  });

  // ------------------------------------------------------------------------
  // Scenario 2 — `/draft response to @opposing-counsel about #motion-to-dismiss`
  // (FR-52 example: soft slash + multi-reference + interleaved NL text)
  // ------------------------------------------------------------------------

  describe('/draft response to @opposing-counsel about #motion-to-dismiss', () => {
    const INPUT =
      '/draft response to @opposing-counsel about #motion-to-dismiss';

    it('parses to soft slash + entity ref + filename ref', () => {
      const intent = parse(INPUT);

      // Soft-slash classification
      expect(intent.isHardSlash).toBe(false);
      expect(intent.isSoftSlash).toBe(true);
      expect(intent.command).toBe('/draft');

      // Two references — entity then filename (parser preserves source order)
      expect(intent.references).toHaveLength(2);

      const entityRef = intent.references.find((r) => r.kind === 'entity');
      const fileRef = intent.references.find((r) => r.kind === 'filename');

      expect(entityRef).toEqual({
        kind: 'entity',
        value: 'opposing-counsel',
        raw: '@opposing-counsel',
      });
      expect(fileRef).toEqual({
        kind: 'filename',
        value: 'motion-to-dismiss',
        raw: '#motion-to-dismiss',
      });
    });

    it('resolves both references in a single resolveAll call', async () => {
      const ctx = buildResolverContext([MOTION_FILE]);
      const intent = parse(INPUT);
      const resolved = await resolveAll(intent.references, ctx);

      expect(resolved).toHaveLength(2);

      // The entity ref `@opposing-counsel` is out-of-host-context per task 083
      // design (NFR-03 — no new entity-lookup endpoint in R6) → unresolved.
      const entity = resolved.find((r) => r.type === 'entity');
      expect(entity).toBeDefined();
      expect(entity?.resolved).toBe(false);
      expect(entity?.rawToken).toBe('@opposing-counsel');
      expect(entity?.canonicalId).toBeNull();

      // The file ref `#motion-to-dismiss` resolves via the fileLookup adapter.
      const file = resolved.find((r) => r.type === 'file');
      expect(file).toBeDefined();
      expect(file?.resolved).toBe(true);
      expect(file?.canonicalId).toBe('doc-motion-to-dismiss-001');
      expect(file?.displayName).toBe('motion-to-dismiss');
    });

    it('preserves interleaved text in the message field', async () => {
      const ctx = buildResolverContext([MOTION_FILE]);
      const { body } = await runComposition(INPUT, ctx);

      // The natural-language glue ("response to", "about") between the
      // references must reach the agent verbatim — the parser tokenizes
      // references but does NOT strip them from rawText.
      expect(body.message).toBe(INPUT);
      expect(body.message).toContain('response to');
      expect(body.message).toContain('about');
    });

    it('produces a coherent BFF payload with both refs surfaced', async () => {
      const ctx = buildResolverContext([MOTION_FILE]);
      const { body } = await runComposition(INPUT, ctx);

      expect(body.intentHint).toBe('draft');
      expect(body.resolvedReferences).toHaveLength(2);

      // Both references survive the chain — one resolved, one degraded.
      // Both shapes flow to the agent prompt per task 083 design.
      const types = body.resolvedReferences?.map((r) => r.type).sort();
      expect(types).toEqual(['entity', 'file']);
    });
  });

  // ------------------------------------------------------------------------
  // Scenario 3 — NFR-11 backward compat (natural-language equivalent)
  // ------------------------------------------------------------------------

  describe('NFR-11 backward compat', () => {
    it('"summarize the engagement letter" → command:null (passthrough)', () => {
      const intent = parse('summarize the engagement letter');

      // No slash → no command → no soft-slash flag
      expect(intent.command).toBeNull();
      expect(intent.isHardSlash).toBe(false);
      expect(intent.isSoftSlash).toBe(false);

      // No `#` / `@` tokens → empty references
      expect(intent.references).toEqual([]);

      // Full Intent shape matches the NFR-11 passthrough contract locked in
      // task 080's CommandRouter.test.ts.
      expect(intent).toEqual<Intent>({
        command: null,
        references: [],
        rawText: 'summarize the engagement letter',
        isHardSlash: false,
        isSoftSlash: false,
      });
    });

    it('natural-language input produces undecorated body', async () => {
      const ctx = buildResolverContext([ENGAGEMENT_LETTER_FILE]);
      const { body, resolvedReferences } = await runComposition(
        'summarize the engagement letter',
        ctx,
      );

      // No intentHint decoration — BFF falls through to existing Layer 1
      // keyword path UNCHANGED per NFR-11.
      expect('intentHint' in body).toBe(false);

      // No references → no resolvedReferences field at all (composition
      // helper omits the field when the resolver returns an empty array).
      expect(resolvedReferences).toEqual([]);
      expect('resolvedReferences' in body).toBe(false);

      // Message text reaches the BFF verbatim — the agent does the work.
      expect(body.message).toBe('summarize the engagement letter');
    });

    it('"draft response to opposing counsel about the motion" → no decoration', async () => {
      // Same intent as Scenario 2, expressed in natural language without
      // sigils. NFR-11 binding: this must continue to work via the existing
      // CapabilityRouter natural-language path — no intentHint, no
      // resolved references, message passes through verbatim.
      const text = 'draft response to opposing counsel about the motion';
      const ctx = buildResolverContext([MOTION_FILE]);
      const { intent, body, resolvedReferences } = await runComposition(
        text,
        ctx,
      );

      expect(intent.command).toBeNull();
      expect(intent.isSoftSlash).toBe(false);
      expect(intent.references).toEqual([]);
      expect('intentHint' in body).toBe(false);
      expect('resolvedReferences' in body).toBe(false);
      expect(resolvedReferences).toEqual([]);
      expect(body.message).toBe(text);
    });
  });
});
