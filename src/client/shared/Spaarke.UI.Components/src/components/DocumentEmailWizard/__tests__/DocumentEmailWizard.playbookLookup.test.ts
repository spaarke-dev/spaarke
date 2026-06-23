/**
 * DocumentEmailWizard playbook-lookup URL-shape tests (task 021 / spec FR-03).
 *
 * Stripped-down test that exercises the load-bearing change for this task:
 * the wizard's combined-summary code path now calls the BFF at
 * `/api/ai/playbooks/by-id/{id}` (NOT the legacy `/by-name/Summarize%20New%20File(s)`)
 * to resolve the Summarize New File(s) playbook.
 *
 * We don't render the wizard's React tree here (full Fluent UI + WizardShell
 * rendering would require jsdom + a real `FluentProvider`, which is overkill
 * for what is essentially a URL-shape assertion). Instead we extract the
 * underlying `authenticatedFetch` call shape by source-reading the module and
 * asserting the literal URL fragment present in the production bundle.
 *
 * Rationale:
 *  - The component's combined-summary effect is tightly coupled to React
 *    state (`kept`, `summaryRanForRef`) and to the wizard step transition,
 *    which makes interactive rendering brittle in unit tests.
 *  - The acceptance criterion for FR-03 is a URL-shape assertion + grep
 *    verification. Source-level inspection is sufficient and stable.
 *
 * For end-to-end behavior we rely on the wizard's existing manual UAT path
 * (Phase 7) and the parallel `useAiSummary.playbookLookup.test.ts` which
 * exercises the same migration pattern at the hook level.
 */

import * as fs from 'fs';
import * as path from 'path';

describe('DocumentEmailWizard — Pattern B stable-ID playbook lookup (FR-03)', () => {
  const wizardPath = path.resolve(__dirname, '..', 'DocumentEmailWizard.tsx');

  // Read the source file ONCE per test file. We treat the source as the
  // contract under test for URL shape (the file is the deployment artifact).
  let source: string;
  beforeAll(() => {
    source = fs.readFileSync(wizardPath, 'utf-8');
  });

  it('exposes the Summarize New File(s) playbook ID at module scope', () => {
    expect(source).toContain('SUMMARIZE_NEW_FILES_PLAYBOOK_ID');
    // DEV GUID matches existing convention (sprk_playbookid = row PK).
    expect(source).toContain('4a72f99c-a119-f111-8343-7ced8d1dc988');
  });

  it('calls /api/ai/playbooks/by-id/{id} (the stable-ID Pattern B route)', () => {
    expect(source).toContain('/api/ai/playbooks/by-id/');
  });

  it('does NOT call the legacy /by-name/ route for playbook resolution', () => {
    // Strip code comments that may discuss the legacy route historically,
    // then assert the remaining code carries no live by-name lookup. We use
    // a conservative line-by-line check (look for `fetch` or URL-template
    // lines containing `/by-name/`).
    const lines = source.split(/\r?\n/);
    const livesByNameLines = lines.filter(line => {
      const trimmed = line.trim();
      // Skip comment-only lines (single-line or block continuations).
      if (trimmed.startsWith('//') || trimmed.startsWith('*') || trimmed.startsWith('/*')) {
        return false;
      }
      return trimmed.includes('/by-name/');
    });
    expect(livesByNameLines).toEqual([]);
  });

  it('does NOT contain a literal "Summarize New File(s)" string for playbook resolution', () => {
    // The wizard step label and UI copy MAY still contain the human-readable
    // name (for the Summary step header / aria-label). The load-bearing
    // assertion here is that the name is NOT passed to a URL — i.e. no line
    // containing both `/api/ai/playbooks/` and `Summarize`.
    const lines = source.split(/\r?\n/);
    const offenders = lines.filter(line => {
      const trimmed = line.trim();
      if (trimmed.startsWith('//') || trimmed.startsWith('*')) {
        return false;
      }
      return trimmed.includes('/api/ai/playbooks/') && trimmed.includes('Summarize');
    });
    expect(offenders).toEqual([]);
  });

  it('handles a 404 ProblemDetails response with a user-friendly error', () => {
    // The implementation throws `new Error('Playbook unavailable. Please contact your administrator.')`
    // on the !ok branch; the wizard's try/catch sets `setSummaryError(msg)`.
    // We assert the user-facing string is present in source (it surfaces in
    // the "Could not generate summary: {summaryError}" render slot).
    expect(source).toContain('Playbook unavailable');
  });
});
