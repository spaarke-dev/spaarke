/**
 * Minimal Jest mock for the `marked` ESM-only package.
 *
 * The real `marked` ships as pure ESM which ts-jest's CommonJS transform
 * cannot consume — every test file that transitively imports
 * `@spaarke/ui-components/services/renderMarkdown` fails with
 * "SyntaxError: Unexpected token 'export'" at marked.esm.js parse time.
 *
 * This stub provides just enough surface for renderMarkdown to call into
 * without exercising any actual Markdown parsing. Tests that need a real
 * Markdown render path are out-of-scope for wizard unit tests.
 *
 * Mirrors SpaarkeAi/src/__mocks__/marked.ts — DEF-001 wiring for
 * spaarke-dataset-grid-framework-r2.
 */

type SyncRenderer = (input: string, options?: unknown) => string;

const noopRender: SyncRenderer = (input: string) => input ?? '';

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
    /* no-op */
  },
});

export const marked = markedFn;
export default markedFn;

export type MarkedOptions = Record<string, unknown>;
