/**
 * ComposeEditor.numberAtom.test.tsx — FR-13/FR-14 (spaarkeai-compose-fidelity-r4.5 task 032, WS-3
 * "render the 031-computed label as an explicit non-editable number-atom").
 *
 * Mirrors `ComposeEditor.indentAndWhitespace.test.tsx`'s mock/render setup (same auth + docxBridge
 * mocks, same `renderEditor` shape) — the projection.html mount path is the ONE reader (F-2) both tests
 * exercise. Covers what the headless `composeNumberAtomExtension.test.ts` suite cannot: the FULL React +
 * Griffel CSS surface — double-numbering suppression (`<ol>`'s native marker), ADR-021 dark-mode token
 * usage (no hardcoded hex), and end-to-end mount through the real `ComposeEditor` component (not just the
 * bare TipTap schema).
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
  const documentRef: ComposeEditorDocumentRef = { speDriveItemId: 'matter-doc-032', fileName: 'numbered.docx' };
  return render(
    <FluentProvider theme={theme}>
      <PaneEventBusProvider>
        <ComposeEditor
          docxBytes={docxBuffer()}
          projection={projection(html)}
          documentRef={documentRef}
          sessionId="session-numberatom-032"
        />
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

/** Collect all inserted CSSOM rule text (Griffel inserts lazily on first render — mount first). */
function allCssRuleText(): string[] {
  return Array.from(document.styleSheets).flatMap(sheet => {
    try {
      return Array.from(sheet.cssRules ?? []).map(r => r.cssText);
    } catch {
      return [];
    }
  });
}

describe('ComposeEditor — FR-13 number-atom rendering (task 032)', () => {
  it('a numbered list item renders the server-computed label as an explicit atom prefix, not the browser <ol> auto-count', async () => {
    renderEditor(
      '<ol><li><p data-paraid="00320001" data-computed-number="4.2." data-numbering-level="0">Interrupted clause continuation</p></li></ol>'
    );

    const editor = await screen.findByRole('textbox');
    await waitFor(() => expect(editor).toHaveTextContent('Interrupted clause continuation'));

    const atom = editor.querySelector('.compose-number-atom');
    expect(atom).not.toBeNull();
    expect(atom?.textContent).toBe('4.2.');
    expect(atom?.getAttribute('contenteditable')).toBe('false');
  });

  it('a style-linked numbered heading (not a list item) also renders the atom prefix', async () => {
    renderEditor(
      '<h2 data-paraid="00320002" data-computed-number="4.1" data-numbering-level="0">Definitions (style-linked heading)</h2>'
    );

    const editor = await screen.findByRole('textbox');
    await waitFor(() => expect(editor).toHaveTextContent('Definitions'));

    const atom = editor.querySelector('.compose-number-atom');
    expect(atom?.textContent).toBe('4.1');
  });

  it('suppresses the native <ol> marker for a PROJECTED list only — scoped, not global (UAT round 2)', async () => {
    renderEditor(
      '<ol data-projected-list="1"><li><p data-paraid="00320003" data-computed-number="4.2.">Clause text</p></li></ol>'
    );
    await screen.findByRole('textbox');

    const rules = allCssRuleText();

    // The atom stays the SOLE source of a document-sourced number, avoiding "1. 4.2" double-numbering.
    const projectedRule = rules.find(text => /\.ProseMirror ol\[data-projected-list\]\s*\{/.test(text));
    expect(projectedRule).toBeDefined();
    expect(projectedRule).toMatch(/list-style-type:\s*none/);

    // UAT round 2: the suppression must NOT be global any more. It was, and that is why a list the USER
    // created rendered with no number at all and "Numbered list" looked like a dead button. An
    // unqualified `.ProseMirror ol { list-style-type: none }` is the regression this catches.
    const unscopedRule = rules.find(text => /\.ProseMirror ol\s*\{/.test(text) && /list-style-type:\s*none/.test(text));
    expect(unscopedRule).toBeUndefined();

    // Negative — bullet (<ul>) lists are untouched by this suppression (no legal number involved).
    const ulRule = rules.find(text => / \.ProseMirror ul\s*\{/.test(text) && /list-style-type:\s*none/.test(text));
    expect(ulRule).toBeUndefined();
  });

  it('an unnumbered plain paragraph and a bulleted list render unchanged (no regression)', async () => {
    renderEditor(
      '<p data-paraid="00320004">Ordinary paragraph, no legal number</p>' +
        '<ul><li><p data-paraid="00320005">Bullet point</p></li></ul>'
    );

    const editor = await screen.findByRole('textbox');
    await waitFor(() => expect(editor).toHaveTextContent('Ordinary paragraph'));
    expect(editor).toHaveTextContent('Bullet point');
    expect(editor.querySelector('.compose-number-atom')).toBeNull();
  });

  it.each([
    ['light', webLightTheme],
    ['dark', webDarkTheme],
  ])('the atom renders in %s mode using semantic tokens, not hardcoded hex (ADR-021)', async (_label, theme) => {
    renderEditor('<ol><li><p data-paraid="00320006" data-computed-number="1.">First</p></li></ol>', theme);

    const editor = await screen.findByRole('textbox');
    await waitFor(() => expect(editor.querySelector('.compose-number-atom')).not.toBeNull());
    expect(editor.querySelector('.compose-number-atom')?.textContent).toBe('1.');

    const rules = allCssRuleText();
    const atomRule = rules.find(text => /\.compose-number-atom\s*\{/.test(text));
    expect(atomRule).toBeDefined();
    // Fluent v9 semantic tokens resolve to CSS custom properties (`var(--...)`) — a hardcoded hex
    // literal (`#`) in this rule would violate ADR-021 (theme-adaptive color).
    expect(atomRule).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
  });

  it('the atom is not user-editable and not interactive (pointer-events/user-select suppressed as defense-in-depth)', async () => {
    renderEditor('<ol><li><p data-paraid="00320007" data-computed-number="1.">First</p></li></ol>');
    const editor = await screen.findByRole('textbox');
    await waitFor(() => expect(editor.querySelector('.compose-number-atom')).not.toBeNull());

    const atom = editor.querySelector('.compose-number-atom') as HTMLElement;
    const computed = getComputedStyle(atom);
    expect(computed.userSelect).toBe('none');
    expect(computed.pointerEvents).toBe('none');
    expect(atom.getAttribute('contenteditable')).toBe('false');
  });
});

// ---------------------------------------------------------------------------
// Editor typography (UAT round 2) — readability, not document spacing
// ---------------------------------------------------------------------------

describe('ComposeEditor — editor typography defaults', () => {
  it('gives headings a UNITLESS line-height so a large glyph is not trapped in a fixed 20px line box', async () => {
    renderEditor('<h1 data-paraid="TYPO0001">A heading long enough to wrap onto more than one line</h1>');
    await screen.findByRole('textbox');

    const rules = allCssRuleText();
    const headingRule = rules.find(t => /\.ProseMirror h1/.test(t) && /line-height/.test(t));
    expect(headingRule).toBeDefined();
    // The bug was inheriting FluentProvider's root line-height, a FIXED PIXEL value (20px) smaller than a
    // ~28px heading glyph. A unitless ratio scales with font-size; a px value would reintroduce it at a
    // different size, so the absence of a unit is the actual contract here.
    expect(headingRule).toMatch(/line-height:\s*1(\.\d+)?\s*[;}]/);
    expect(headingRule).not.toMatch(/line-height:\s*\d+px/);
  });

  it('gives body paragraphs their own line-height and bottom margin', async () => {
    renderEditor('<p data-paraid="TYPO0002">Body prose.</p>');
    await screen.findByRole('textbox');

    const rules = allCssRuleText();
    const paraRule = rules.find(t => /\.ProseMirror p\b/.test(t) && /line-height/.test(t));
    expect(paraRule).toBeDefined();
    expect(paraRule).not.toMatch(/line-height:\s*\d+px/);
  });

  it('keeps list and table paragraphs tight — the prose margins must not space list rows apart', async () => {
    renderEditor('<ul><li><p data-paraid="TYPO0003">Item</p></li></ul>');
    await screen.findByRole('textbox');

    const rules = allCssRuleText();
    const tightRule = rules.find(t => /\.ProseMirror li p/.test(t) && /margin-bottom/.test(t));
    expect(tightRule).toBeDefined();
  });
});
