/**
 * ComposeEditor.opaqueAtomTheme.test.ts — spaarkeai-compose-r4 task 024 (Phase-2 cross-cutting
 * capture tests, Success Criterion 2 client side).
 *
 * The ONE genuinely new assertion task 024 calls for that the existing task-020/021/022 colocated
 * suites (`stepOperationInterceptor.test.ts`, `opaqueAtomNode.test.ts`) do not already cover: the
 * ADR-021 dark-mode legibility of the R4 opaque-atom placeholders AT THE LEVEL WHERE THE SEMANTIC
 * TOKENS ACTUALLY LIVE — `ComposeEditor.tsx`'s own `useStyles()` `.compose-atom` / `.compose-atom-
 * block` / `.compose-atom-inline` / `.ProseMirror-selectednode.compose-atom` Griffel rule blocks.
 *
 * `opaqueAtomNode.test.ts`'s own "ADR-021 — no inline hex/style leaks" describe block already
 * proves the atom NODE's `renderHTML()` never emits inline style/hex — but that block mounts a
 * bare headless `@tiptap/core` `Editor` with NO `FluentProvider`, and the atom node itself only
 * ever emits STATIC class names (`compose-atom`, `compose-atom-block`, `compose-atom-inline`) — so
 * that assertion is trivially theme-INDEPENDENT (see that file's own doc comment: "this module
 * emits classes only... Token-based rules live in ComposeEditor's `useStyles()`"). The COLOR rules
 * that make the placeholder legible in both themes live entirely in `ComposeEditor.tsx`.
 *
 * WHY A SOURCE-LEVEL CHECK, NOT A FULL `ComposeEditor` REACT MOUNT: the established repo
 * convention for an ADR-021 dark-mode assertion (`ComposeToolbarPolish.test.tsx` §4) mounts the
 * real `ComposeEditor` under `FluentProvider theme={webDarkTheme}` and asserts no hardcoded hex in
 * the rendered HTML. That was attempted here first and is NOT currently runnable in this worktree:
 * `ComposeEditor.tsx` transitively imports `@spaarke/auth` and (`ComposeAiToolbar.tsx` →)
 * `@spaarke/ui-components`, neither of which has a built `dist/` here, and `@spaarke/ui-components`
 * additionally has no `node_modules` installed at all — a pre-existing, documented environmental
 * gap (root `CLAUDE.md`: "Pre-existing `@spaarke/*` sibling-dist ... errors are environmental —
 * ignore"; this package's own `jest.config.js`: "`@spaarke/*` runtime deps resolve via
 * `node_modules` → their built `dist/` (produced by `scripts/Build-AllClientComponents.ps1
 * -Component SharedLibs`)" — that build has not been run in this worktree, and running it is out
 * of task 024's scope). `ComposeToolbarPolish.test.tsx` itself currently fails to even load in
 * this worktree for the identical reason (verified: `Cannot find module '@spaarke/auth'` /
 * `Cannot find module '@spaarke/ui-components'`) — this is a pre-existing condition, not a
 * regression introduced by this task.
 *
 * Instead, this file verifies the SAME production rule text at the "unit level" the task calls
 * for: it reads `ComposeEditor.tsx`'s own source and extracts each `.compose-atom*` Griffel rule
 * block, then asserts every color-bearing declaration in it references a `tokens.*` semantic value
 * — never a hardcoded hex/rgb literal or an inline `style=` construct. This exercises the ACTUAL
 * shipped rule text (not a reimplementation or a synthetic stand-in), so it fails the moment a
 * future edit swaps a token for a literal color — the exact ADR-021 regression this task guards
 * against — without depending on the broader `@spaarke/ui-components`/`@spaarke/auth` module graph.
 * Once the sibling-dist build is available, a full-mount `ComposeEditor` variant (mirroring
 * `ComposeToolbarPolish.test.tsx` §4, seeding atom content via the `initialHtml` prop with the same
 * fixture markup `opaqueAtomNode.test.ts` uses) is the natural follow-up — not required here.
 *
 * Do NOT duplicate: rebase-across-concurrent-edits (already covered end-to-end by
 * `stepOperationInterceptor.test.ts`'s "RebasedOperationLog — rebase-across-concurrent-edits"
 * describe block) and the atom-is-non-editable negative case (already covered end-to-end by
 * `opaqueAtomNode.test.ts`'s "non-editability — typing inside an atom is a no-op" describe block,
 * exercised through a real editor schema) are NOT re-asserted here.
 */
import * as fs from 'fs';
import * as path from 'path';

const COMPOSE_EDITOR_SOURCE = fs.readFileSync(path.join(__dirname, 'ComposeEditor.tsx'), 'utf8');

const HEX_COLOR = /#[0-9a-fA-F]{3,8}\b/;
const RGB_FUNC = /\brgba?\(/;

/**
 * Extract a Griffel style-rule block's body (`'& .SELECTOR': { ... },`) by brace-depth counting
 * from the object literal's opening `{`. Not string/template-literal-aware, but every brace pair
 * that appears inside a template-literal expression in these blocks (e.g. `` `1px dashed
 * ${tokens.x}` ``) is itself balanced, so naive depth counting still finds the correct close.
 */
function extractStyleBlock(source: string, selector: string): string {
  const marker = `'& .${selector}':`;
  const markerStart = source.indexOf(marker);
  if (markerStart === -1) {
    throw new Error(`style block for "& .${selector}" not found in ComposeEditor.tsx`);
  }
  const braceStart = source.indexOf('{', markerStart);
  let depth = 0;
  let i = braceStart;
  for (; i < source.length; i++) {
    if (source[i] === '{') depth++;
    else if (source[i] === '}') {
      depth--;
      if (depth === 0) break;
    }
  }
  if (depth !== 0) {
    throw new Error(`unbalanced braces extracting "& .${selector}" from ComposeEditor.tsx`);
  }
  return source.slice(braceStart, i + 1);
}

describe('ADR-021 — ComposeEditor opaque-atom (.compose-atom*) styles use semantic tokens, never hardcoded colors (task 024)', () => {
  it('the extractor locates the real .compose-atom* rule blocks (sanity check on the fixture)', () => {
    expect(() => extractStyleBlock(COMPOSE_EDITOR_SOURCE, 'compose-atom')).not.toThrow();
    expect(() => extractStyleBlock(COMPOSE_EDITOR_SOURCE, 'compose-atom-block')).not.toThrow();
    expect(() => extractStyleBlock(COMPOSE_EDITOR_SOURCE, 'compose-atom-inline')).not.toThrow();
    expect(() =>
      extractStyleBlock(COMPOSE_EDITOR_SOURCE, 'ProseMirror-selectednode.compose-atom')
    ).not.toThrow();
  });

  it('the base .compose-atom rule carries color/background/border via tokens.* — no hex/rgb literal, no inline style', () => {
    const block = extractStyleBlock(COMPOSE_EDITOR_SOURCE, 'compose-atom');
    expect(block).not.toMatch(HEX_COLOR);
    expect(block).not.toMatch(RGB_FUNC);
    expect(block).not.toContain('style=');
    // Positive assertions: every color-bearing property IS a semantic token (theme-adaptive in
    // both light and dark — the ADR-021 requirement, not merely "absence of a bad pattern").
    expect(block).toMatch(/color:\s*tokens\./);
    expect(block).toMatch(/backgroundColor:\s*tokens\./);
    expect(block).toContain('border: `1px dashed ${tokens.');
  });

  it('the selected-atom outline rule uses a semantic brand-stroke token, not a hardcoded color', () => {
    const block = extractStyleBlock(COMPOSE_EDITOR_SOURCE, 'ProseMirror-selectednode.compose-atom');
    expect(block).not.toMatch(HEX_COLOR);
    expect(block).not.toMatch(RGB_FUNC);
    expect(block).toMatch(/outlineColor:\s*tokens\./);
  });

  it('the block/inline layout variants declare no color of their own (layout-only — they inherit .compose-atom\'s token colors, so there is nothing to leak)', () => {
    const blockVariant = extractStyleBlock(COMPOSE_EDITOR_SOURCE, 'compose-atom-block');
    const inlineVariant = extractStyleBlock(COMPOSE_EDITOR_SOURCE, 'compose-atom-inline');
    for (const block of [blockVariant, inlineVariant]) {
      expect(block).not.toMatch(HEX_COLOR);
      expect(block).not.toMatch(RGB_FUNC);
      expect(block).not.toMatch(/\bcolor:/);
      expect(block).not.toMatch(/backgroundColor:/);
    }
  });

  it('no hex/rgb color literal appears anywhere across the four .compose-atom* rule blocks combined', () => {
    const combined = [
      extractStyleBlock(COMPOSE_EDITOR_SOURCE, 'compose-atom'),
      extractStyleBlock(COMPOSE_EDITOR_SOURCE, 'compose-atom-block'),
      extractStyleBlock(COMPOSE_EDITOR_SOURCE, 'compose-atom-inline'),
      extractStyleBlock(COMPOSE_EDITOR_SOURCE, 'ProseMirror-selectednode.compose-atom'),
    ].join('\n');
    expect(combined).not.toMatch(HEX_COLOR);
    expect(combined).not.toMatch(RGB_FUNC);
  });
});
