/**
 * Shared markdown rendering utility for all Spaarke UI surfaces.
 *
 * Converts markdown text to sanitized HTML using `marked` for parsing and
 * `DOMPurify` for XSS protection. Styles use Fluent UI v9 semantic CSS custom
 * property tokens so dark mode works automatically via FluentProvider theme
 * switching (ADR-021).
 *
 * **Surfaces**: SprkChat messages, PlanPreviewCard, Lexical editor insertion,
 * AnalysisWorkspace content.
 *
 * **Client-side only**: DOMPurify requires a browser DOM (window/document).
 * For server-side rendering (e.g., Word export via BFF), use a dedicated
 * markdown-to-OpenXML converter instead.
 *
 * @module renderMarkdown
 * @see ADR-012 — shared component library
 * @see ADR-021 — Fluent v9 design system, dark mode
 */
/**
 * Options for the renderMarkdown function.
 */
export interface RenderMarkdownOptions {
    /**
     * Allow specific HTML tags through the DOMPurify sanitizer.
     * By default, DOMPurify strips all dangerous tags (script, iframe, etc.)
     * but allows standard HTML elements.
     * @default undefined (use DOMPurify defaults)
     */
    allowedTags?: string[];
    /**
     * Treat single newlines as `<br>` elements.
     * @default true
     */
    breaks?: boolean;
    /**
     * CSS class prefix for the wrapper div.
     * The output HTML is wrapped in `<div class="{classPrefix}">...</div>`.
     * @default "sprk-markdown"
     */
    classPrefix?: string;
}
/**
 * CSS styles for markdown content using Fluent UI v9 semantic tokens.
 *
 * These tokens are injected as CSS custom properties by FluentProvider
 * (e.g., `--colorNeutralForeground1`). When the FluentProvider switches
 * between light/dark themes, token values change automatically.
 *
 * **ADR-021 compliance**: NO hard-coded colors — all colors reference
 * Fluent v9 CSS custom properties.
 *
 * Inject this CSS string once in your application (via a `<style>` tag or
 * equivalent) to style all `.sprk-markdown` content.
 */
export declare const SPRK_MARKDOWN_CSS: string;
/**
 * Renders a markdown string to sanitized HTML wrapped in a themed container.
 *
 * The output HTML is wrapped in `<div class="sprk-markdown">...</div>` (or a
 * custom class prefix). Pair with {@link SPRK_MARKDOWN_CSS} to apply Fluent v9
 * semantic token styles.
 *
 * **Security**: All output is sanitized via DOMPurify. Script tags, event
 * handlers, and other XSS vectors are stripped regardless of input.
 *
 * **Pure function**: No side effects — input string in, HTML string out.
 * The caller decides how to render (typically `dangerouslySetInnerHTML`).
 *
 * **Client-side only**: Requires browser DOM for DOMPurify. For server-side
 * Word export, use a dedicated markdown-to-OpenXML converter.
 *
 * @param markdown - Raw markdown string to render
 * @param options - Optional rendering configuration
 * @returns Sanitized HTML string wrapped in a themed container div
 *
 * @example
 * ```tsx
 * import { renderMarkdown } from "@spaarke/ui-components";
 *
 * const html = renderMarkdown("**Hello** from *SprkChat*");
 * // → '<div class="sprk-markdown"><p><strong>Hello</strong> from <em>SprkChat</em></p>\n</div>'
 *
 * return <div dangerouslySetInnerHTML={{ __html: html }} />;
 * ```
 */
export declare function renderMarkdown(markdown: string, options?: RenderMarkdownOptions): string;
//# sourceMappingURL=renderMarkdown.d.ts.map