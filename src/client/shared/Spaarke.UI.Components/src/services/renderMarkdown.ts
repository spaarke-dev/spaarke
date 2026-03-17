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

import { marked, type MarkedOptions, type Renderer } from 'marked';
import DOMPurify from 'dompurify';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

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

// ---------------------------------------------------------------------------
// Fluent v9 Semantic Token CSS
// ---------------------------------------------------------------------------

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
export const SPRK_MARKDOWN_CSS = `
.sprk-markdown {
  color: var(--colorNeutralForeground1);
  font-family: var(--fontFamilyBase);
  font-size: var(--fontSizeBase300);
  line-height: var(--lineHeightBase300);
  word-wrap: break-word;
  overflow-wrap: break-word;
}

.sprk-markdown h1,
.sprk-markdown h2,
.sprk-markdown h3,
.sprk-markdown h4,
.sprk-markdown h5,
.sprk-markdown h6 {
  color: var(--colorNeutralForeground1);
  font-family: var(--fontFamilyBase);
  margin-top: var(--spacingVerticalL);
  margin-bottom: var(--spacingVerticalS);
  font-weight: var(--fontWeightSemibold);
  line-height: 1.3;
}

.sprk-markdown h1 { font-size: var(--fontSizeBase500); }
.sprk-markdown h2 { font-size: var(--fontSizeBase400); }
.sprk-markdown h3 { font-size: var(--fontSizeBase300); font-weight: var(--fontWeightBold); }
.sprk-markdown h4 { font-size: var(--fontSizeBase300); }
.sprk-markdown h5 { font-size: var(--fontSizeBase200); }
.sprk-markdown h6 { font-size: var(--fontSizeBase200); color: var(--colorNeutralForeground3); }

.sprk-markdown p {
  margin-top: 0;
  margin-bottom: var(--spacingVerticalS);
}

.sprk-markdown strong {
  font-weight: var(--fontWeightSemibold);
  color: var(--colorNeutralForeground1);
}

.sprk-markdown em {
  font-style: italic;
}

.sprk-markdown a {
  color: var(--colorBrandForeground1);
  text-decoration: none;
}
.sprk-markdown a:hover {
  text-decoration: underline;
  color: var(--colorBrandForeground2);
}

.sprk-markdown code {
  font-family: var(--fontFamilyMonospace);
  font-size: var(--fontSizeBase200);
  background-color: var(--colorNeutralBackground3);
  color: var(--colorNeutralForeground1);
  padding: 2px 6px;
  border-radius: var(--borderRadiusSmall);
}

.sprk-markdown pre {
  background-color: var(--colorNeutralBackground3);
  border: 1px solid var(--colorNeutralStroke2);
  border-radius: var(--borderRadiusMedium);
  padding: var(--spacingHorizontalM);
  overflow-x: auto;
  margin-top: var(--spacingVerticalS);
  margin-bottom: var(--spacingVerticalS);
}

.sprk-markdown pre code {
  background-color: transparent;
  padding: 0;
  border-radius: 0;
  font-size: var(--fontSizeBase200);
  line-height: var(--lineHeightBase200);
}

.sprk-markdown ul,
.sprk-markdown ol {
  margin-top: 0;
  margin-bottom: var(--spacingVerticalS);
  padding-left: var(--spacingHorizontalXXL);
}

.sprk-markdown li {
  margin-bottom: var(--spacingVerticalXS);
}

.sprk-markdown li > ul,
.sprk-markdown li > ol {
  margin-top: var(--spacingVerticalXS);
  margin-bottom: 0;
}

.sprk-markdown blockquote {
  margin: var(--spacingVerticalS) 0;
  padding: var(--spacingVerticalXS) var(--spacingHorizontalM);
  border-left: 3px solid var(--colorBrandStroke1);
  color: var(--colorNeutralForeground2);
  background-color: var(--colorNeutralBackground2);
  border-radius: 0 var(--borderRadiusSmall) var(--borderRadiusSmall) 0;
}

.sprk-markdown blockquote p:last-child {
  margin-bottom: 0;
}

.sprk-markdown table {
  width: 100%;
  border-collapse: collapse;
  margin-top: var(--spacingVerticalS);
  margin-bottom: var(--spacingVerticalS);
}

.sprk-markdown th,
.sprk-markdown td {
  border: 1px solid var(--colorNeutralStroke1);
  padding: var(--spacingVerticalXS) var(--spacingHorizontalS);
  text-align: left;
}

.sprk-markdown th {
  background-color: var(--colorNeutralBackground3);
  font-weight: var(--fontWeightSemibold);
  color: var(--colorNeutralForeground1);
}

.sprk-markdown td {
  color: var(--colorNeutralForeground1);
}

.sprk-markdown tr:nth-child(even) td {
  background-color: var(--colorNeutralBackground2);
}

.sprk-markdown hr {
  border: none;
  border-top: 1px solid var(--colorNeutralStroke2);
  margin: var(--spacingVerticalM) 0;
}

.sprk-markdown img {
  max-width: 100%;
  height: auto;
}
`.trim();

// ---------------------------------------------------------------------------
// Custom renderer
// ---------------------------------------------------------------------------

/**
 * Build a custom marked renderer that:
 * 1. Opens links in a new tab with security attributes
 * 2. Applies no hard-coded colors (ADR-021)
 */
function createRenderer(): Partial<Renderer> {
  return {
    link({ href, title, text }): string {
      const titleAttr = title ? ` title="${title}"` : '';
      return `<a href="${href}"${titleAttr} target="_blank" rel="noopener noreferrer">${text}</a>`;
    },
  };
}

// ---------------------------------------------------------------------------
// DOMPurify configuration
// ---------------------------------------------------------------------------

const DEFAULT_ALLOWED_TAGS = [
  'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
  'p', 'br', 'hr',
  'strong', 'b', 'em', 'i', 'u', 's', 'del',
  'a',
  'ul', 'ol', 'li',
  'code', 'pre',
  'blockquote',
  'table', 'thead', 'tbody', 'tr', 'th', 'td',
  'img',
  'div', 'span',
];

const DEFAULT_ALLOWED_ATTRS = [
  'href', 'title', 'target', 'rel',
  'src', 'alt', 'width', 'height',
  'class',
];

// ---------------------------------------------------------------------------
// Main function
// ---------------------------------------------------------------------------

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
export function renderMarkdown(
  markdown: string,
  options?: RenderMarkdownOptions,
): string {
  if (!markdown) {
    return '';
  }

  const classPrefix = options?.classPrefix ?? 'sprk-markdown';
  const breaks = options?.breaks ?? true;

  // Configure marked
  const markedOptions: MarkedOptions = {
    breaks,
    gfm: true, // GitHub Flavored Markdown (tables, strikethrough, etc.)
    renderer: createRenderer() as Renderer,
  };

  // Parse markdown to HTML
  const rawHtml = marked.parse(markdown, markedOptions) as string;

  // Configure DOMPurify
  const allowedTags = options?.allowedTags ?? DEFAULT_ALLOWED_TAGS;
  const purifyConfig: DOMPurify.Config = {
    ALLOWED_TAGS: allowedTags,
    ALLOWED_ATTR: DEFAULT_ALLOWED_ATTRS,
  };

  // Sanitize
  const cleanHtml = DOMPurify.sanitize(rawHtml, purifyConfig);

  // Wrap in themed container
  return `<div class="${classPrefix}">${cleanHtml}</div>`;
}
