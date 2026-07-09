/**
 * Minimal Jest mock for the `marked` ESM-only package.
 *
 * The real `marked` ships as pure ESM which ts-jest's CommonJS transform
 * cannot consume — every test file that transitively imports
 * `@spaarke/ui-components/services/renderMarkdown` (via the widget
 * registries' `@spaarke/ui-components` barrel import) fails with
 * "SyntaxError: Unexpected token 'export'" at marked.esm.js parse time.
 *
 * This stub provides just enough surface for renderMarkdown to call into
 * without exercising any actual Markdown parsing. Tests that need a real
 * Markdown render path are out-of-scope for these registry unit tests; they
 * belong in the Spaarke.UI.Components Storybook visual suite.
 *
 * Mirrors src/solutions/SpaarkeAi/src/__mocks__/marked.ts verbatim (R6 Hotfix
 * Wave B-G9c3 origin). Added by ai-architecture-redesign-r2 task 021
 * (test-repair).
 */

type SyncRenderer = (input: string, options?: unknown) => string;

const noopRender: SyncRenderer = (input: string) => input ?? '';

// `marked.parse(...)` is the primary API used by renderMarkdown.ts; provide it
// as a plain pass-through. The `marked` callable form (`marked(input)`) is also
// supported by attaching .parse to the function itself.
const markedFn: SyncRenderer & {
  parse: SyncRenderer;
  setOptions: (opts: unknown) => void;
  use: (...extensions: unknown[]) => void;
} = Object.assign(noopRender, {
  parse: noopRender,
  setOptions: () => {
    /* no-op */
  },
  use: () => {
    /* no-op — production code calls marked.use({ renderer: {...} }) for link customisation;
         not exercised in unit tests so accept-and-discard is sufficient. */
  },
});

export const marked = markedFn;
export default markedFn;

// Type-only export proxies — empty objects suffice for ts-jest type erasure.
export type MarkedOptions = Record<string, unknown>;
