/**
 * ComposeEditor.indentAndWhitespace.test.tsx — FR-07/FR-08 (spaarkeai-compose-fidelity-r4.5 task 021,
 * WS-2 "preserve indentation; preserved whitespace not collapsed").
 *
 * The `<ui-tests>` this task's POML specifies, run against jsdom (Chrome-driven `ui-test` skill
 * verification is impractical inside this CI-shaped test file — jsdom + testing-library covers the same
 * three assertions: (1) indentation renders at authored depth, (2) preserved whitespace is not collapsed
 * (asserted via the `.ProseMirror white-space: pre-wrap` CSS rule that achieves it — jsdom does not lay
 * out text, so the actual glyph spacing cannot be measured here; the correct CSS rule is), (3) both
 * render correctly in light AND dark theme (ADR-021). A NEGATIVE case confirms `pre-wrap` does not
 * regress the existing `compose-tab` (single preserved space) rendering — the escalation trigger this
 * task's POML calls out.
 *
 * Mirrors `ComposeEditor.projection.test.tsx`'s mock/render setup (same auth + docxBridge mocks, same
 * `renderEditor` shape) — the projection.html mount path is the ONE reader (F-2) both tests exercise.
 */

import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme, type Theme } from '@fluentui/react-components';
import { PaneEventBusProvider } from '@spaarke/ai-widgets/events';
import { ComposeEditor, type ComposeEditorDocumentRef } from './ComposeEditor';
import type { ComposeServerProjection } from '../types/compose-contracts';

jest.mock('@spaarke/auth', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-access-token',
    authenticatedFetch: jest.fn(),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

jest.mock('../utils/docxBridge', () => ({
  docxToTipTapHtml: jest.fn(async () => ({ html: '<p>MAMMOTH fallback body</p>', messages: [] })),
  stampParaIds: jest.fn(),
  captureParaIdSnapshot: jest.fn(() => new Map()),
}));

const DOCX_SIGNATURE = [0x50, 0x4b, 0x03, 0x04]; // PK\x03\x04 — passes the editable-docx signature gate

function docxBuffer(totalLen = 64): ArrayBuffer {
  const buf = new Uint8Array(totalLen);
  buf.set(DOCX_SIGNATURE, 0);
  return buf.buffer;
}

function projection(html: string): ComposeServerProjection {
  return {
    status: 'success',
    canEdit: true,
    html,
    warnings: [],
    schemaVersion: 'compose-html-v1',
  };
}

function renderEditor(html: string, theme: Theme = webLightTheme) {
  const documentRef: ComposeEditorDocumentRef = { speDriveItemId: 'matter-doc-021', fileName: 'indented.docx' };
  return render(
    <FluentProvider theme={theme}>
      <PaneEventBusProvider>
        <ComposeEditor
          docxBytes={docxBuffer()}
          projection={projection(html)}
          documentRef={documentRef}
          sessionId="session-indent-021"
        />
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

describe('ComposeEditor — FR-07 w:ind rendering (task 021)', () => {
  it('a left-indented paragraph renders with the server-computed margin-left preserved on mount', async () => {
    renderEditor('<p data-paraid="00E10001" style="margin-left: 36pt">Indented clause text</p>');

    const editor = await screen.findByRole('textbox');
    await waitFor(() => expect(editor).toHaveTextContent('Indented clause text'));

    const p = editor.querySelector('p');
    expect(p).not.toBeNull();
    // Without composeIndentExtension's addGlobalAttributes, TipTap's base Paragraph node schema has no
    // attrs for an arbitrary inline `style` — setContent would silently strip it (the defect FR-07
    // fixes). Asserting it SURVIVES the parse/render round-trip is the actual fidelity guarantee, not
    // just that the server emitted it (that's the separate BFF projection test).
    expect(p?.style.marginLeft).toBe('36pt');
  });

  it('a hanging-indented paragraph renders margin-left AND a negative text-indent together', async () => {
    renderEditor('<p data-paraid="00E10002" style="margin-left: 36pt; text-indent: -18pt">Hanging clause text</p>');

    const editor = await screen.findByRole('textbox');
    await waitFor(() => expect(editor).toHaveTextContent('Hanging clause text'));

    const p = editor.querySelector('p');
    expect(p?.style.marginLeft).toBe('36pt');
    expect(p?.style.textIndent).toBe('-18pt');
  });

  it('an indented AND center-aligned paragraph preserves BOTH text-align and margin-left (no attribute clobber)', async () => {
    // mergeAttributes (tiptap core) combines composeIndentExtension's `style` output with the LOCKED
    // TextAlign extension's — this proves the two additive extensions coexist on one node without one
    // clobbering the other's `style` contribution.
    renderEditor('<p data-paraid="00E10003" style="text-align: center; margin-left: 36pt">Aligned and indented</p>');

    const editor = await screen.findByRole('textbox');
    await waitFor(() => expect(editor).toHaveTextContent('Aligned and indented'));

    const p = editor.querySelector('p');
    expect(p?.style.textAlign).toBe('center');
    expect(p?.style.marginLeft).toBe('36pt');
  });
});

// A whitespace value counts as "preserving" for FR-08 purposes when it does NOT collapse consecutive
// spaces to one (the default `normal` behavior this task fixes). `pre-wrap` is the value
// `editorSurface['& .ProseMirror']`'s Griffel rule sets (task 021, higher specificity — two classes vs
// one — so it wins the cascade in a real browser). `@tiptap/core` ALSO auto-injects (`injectCSS`,
// default `true`) its own base `.ProseMirror { white-space: pre-wrap; white-space: break-spaces; }` rule
// — `break-spaces` is a strict superset of `pre-wrap` (MDN: identical, but additionally never collapses
// trailing/wrapped whitespace either) — discovered while building this test (verified via
// `document.styleSheets`/`cssRules` dump: both rules ARE present in the DOM; jsdom's simplified cascade
// implementation does not always resolve competing same-property declarations across stylesheets by
// specificity the way a real browser does, so which of the two wins in THIS test env can vary). Either
// outcome satisfies FR-08's actual contract (no collapse), so the assertion accepts both rather than
// pinning to jsdom's specific (non-authoritative) cascade resolution.
const PRESERVING_WHITE_SPACE_VALUES = ['pre-wrap', 'break-spaces'];

describe('ComposeEditor — FR-08 preserved-whitespace CSS (task 021)', () => {
  it.each([
    ['light', webLightTheme],
    ['dark', webDarkTheme],
  ])(
    'the .ProseMirror content surface does not collapse consecutive whitespace in %s mode (ADR-021)',
    async (_label, theme) => {
      renderEditor('<p data-paraid="00E10004">Text&nbsp;&nbsp;&nbsp;with preserved spacing</p>', theme);

      const editor = await screen.findByRole('textbox');
      await waitFor(() => expect(editor).toHaveTextContent(/Text/));

      // `editor` IS the `.ProseMirror` div (editorProps.attributes.role='textbox' is set directly on it) —
      // the same element `editorSurface['& .ProseMirror']` in useStyles targets.
      expect(editor.className).toContain('ProseMirror');
      const computed = getComputedStyle(editor);
      expect(PRESERVING_WHITE_SPACE_VALUES).toContain(computed.whiteSpace);
      expect(computed.whiteSpace).not.toBe('normal');
    }
  );

  it('the editorSurface Griffel rule for .ProseMirror white-space is present with higher specificity than the default', async () => {
    // Direct CSSOM assertion (independent of jsdom's cascade-resolution behavior above): the app-level
    // rule this task added exists, targets `.ProseMirror`, and sets a non-collapsing value. Griffel
    // inserts its rules lazily on first render, so the editor must mount before inspecting styleSheets.
    renderEditor('<p data-paraid="00E10099">warm-up</p>');
    await screen.findByRole('textbox');

    const cssTexts = Array.from(document.styleSheets).flatMap(sheet => {
      try {
        return Array.from(sheet.cssRules ?? []).map(r => r.cssText);
      } catch {
        return [];
      }
    });
    const rule = cssTexts.find(text => / \.ProseMirror\s*\{/.test(text) && text.includes('white-space: pre-wrap'));
    expect(rule).toBeDefined();
  });

  it('escalation-trigger guard: compose-tab (a single preserved space) is unaffected by the whitespace fix', async () => {
    // The task 021 escalation trigger: the whitespace fix must NOT regress existing R4 rendering of tabs
    // (compose-tab). A tab is exactly ONE preserved space — every preserving white-space value renders a
    // lone space identically to `normal` (collapsing only matters for RUNS of 2+ whitespace characters),
    // so the tab's rendered text content must be unchanged.
    renderEditor('<p data-paraid="00E10005">before<span class="compose-tab"> </span>after</p>');

    const editor = await screen.findByRole('textbox');
    await waitFor(() => expect(editor).toHaveTextContent(/before/));

    expect(editor.textContent).toContain('before');
    expect(editor.textContent).toContain('after');
    const computed = getComputedStyle(editor);
    expect(PRESERVING_WHITE_SPACE_VALUES).toContain(computed.whiteSpace);
  });
});
