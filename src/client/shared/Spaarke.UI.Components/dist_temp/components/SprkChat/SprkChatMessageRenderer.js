/**
 * SprkChatMessageRenderer - Structured response card renderer for SprkChat
 *
 * Selects the appropriate card renderer based on `responseType` and renders
 * structured response data. Supports five card types:
 * - markdown (default): rendered as formatted text
 * - citations: text with clickable citation references + source list
 * - diff: summary with "Open in Diff Viewer" action button
 * - entity_card: Dataverse record link with key fields and navigation
 * - action_confirmation: completed action summary with status badge
 *
 * Navigation for entity cards is handled via the onNavigate callback —
 * this component MUST NOT call Xrm.Navigation directly (ADR-012).
 *
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode required
 * @see ADR-012 - Shared Component Library; no Xrm/ComponentFramework imports
 */
import * as React from 'react';
import { makeStyles, shorthands, tokens, Text, Button, Badge, Card, CardHeader, CardPreview, Link, } from '@fluentui/react-components';
import { DocumentRegular, GlobeRegular, OpenRegular, CheckmarkCircleRegular, ErrorCircleRegular, RecordRegular, } from '@fluentui/react-icons';
import { renderMarkdown as renderMarkdownHtml, SPRK_MARKDOWN_CSS } from '../../services/renderMarkdown';
// ─────────────────────────────────────────────────────────────────────────────
// Markdown CSS injection (one-time, idempotent)
// ─────────────────────────────────────────────────────────────────────────────
const SPRK_MARKDOWN_STYLE_ID = 'sprk-markdown-styles';
/**
 * Injects the SPRK_MARKDOWN_CSS stylesheet into the document <head> once.
 * Uses an idempotent check via a data attribute to avoid duplicate injection
 * when multiple instances of the component mount.
 */
function ensureMarkdownCssInjected() {
    if (typeof document === 'undefined')
        return;
    if (document.getElementById(SPRK_MARKDOWN_STYLE_ID))
        return;
    const style = document.createElement('style');
    style.id = SPRK_MARKDOWN_STYLE_ID;
    style.textContent = SPRK_MARKDOWN_CSS;
    document.head.appendChild(style);
}
// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────
const useStyles = makeStyles({
    // ── Markdown renderer ────────────────────────────────────────────────────
    markdownRoot: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXS,
    },
    markdownText: {
        fontSize: tokens.fontSizeBase300,
        lineHeight: tokens.lineHeightBase300,
        color: tokens.colorNeutralForeground1,
        wordBreak: 'break-word',
    },
    // ── Citations renderer ────────────────────────────────────────────────────
    citationsRoot: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalS,
    },
    citationsText: {
        fontSize: tokens.fontSizeBase300,
        lineHeight: tokens.lineHeightBase300,
        color: tokens.colorNeutralForeground1,
        wordBreak: 'break-word',
    },
    citationsSuper: {
        fontSize: tokens.fontSizeBase100,
        verticalAlign: 'super',
        color: tokens.colorBrandForeground1,
        cursor: 'pointer',
        textDecoration: 'none',
        fontWeight: tokens.fontWeightSemibold,
        ':hover': {
            textDecoration: 'underline',
        },
    },
    sourcesList: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXXS,
        ...shorthands.padding(tokens.spacingVerticalXS, 0, 0, 0),
        ...shorthands.borderTop('1px', 'solid', tokens.colorNeutralStroke2),
    },
    sourcesHeading: {
        fontSize: tokens.fontSizeBase200,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground3,
        textTransform: 'uppercase',
        letterSpacing: '0.05em',
        marginBottom: tokens.spacingVerticalXXS,
    },
    sourceItem: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalXS,
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
    },
    sourceIcon: {
        color: tokens.colorNeutralForeground3,
        flexShrink: 0,
    },
    webSourceIcon: {
        color: tokens.colorPaletteBerryForeground1,
        flexShrink: 0,
    },
    // ── Diff renderer ─────────────────────────────────────────────────────────
    diffRoot: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalS,
        ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalS),
        backgroundColor: tokens.colorNeutralBackground3,
        ...shorthands.borderRadius(tokens.borderRadiusMedium),
        ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke2),
    },
    diffSummaryText: {
        fontSize: tokens.fontSizeBase300,
        lineHeight: tokens.lineHeightBase300,
        color: tokens.colorNeutralForeground1,
    },
    diffActions: {
        display: 'flex',
        justifyContent: 'flex-end',
    },
    // ── Entity card renderer ───────────────────────────────────────────────────
    entityCardRoot: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXS,
    },
    entityTypeBadge: {
        alignSelf: 'flex-start',
    },
    entityFieldsGrid: {
        display: 'grid',
        gridTemplateColumns: '1fr 1fr',
        gap: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalS}`,
        marginTop: tokens.spacingVerticalXS,
    },
    entityField: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXXS,
    },
    entityFieldLabel: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground3,
        textTransform: 'uppercase',
        letterSpacing: '0.04em',
        fontWeight: tokens.fontWeightSemibold,
    },
    entityFieldValue: {
        fontSize: tokens.fontSizeBase300,
        color: tokens.colorNeutralForeground1,
    },
    entityCardActions: {
        display: 'flex',
        justifyContent: 'flex-end',
        marginTop: tokens.spacingVerticalXS,
    },
    // ── Action confirmation renderer ──────────────────────────────────────────
    confirmationRoot: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXS,
        ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalS),
        ...shorthands.borderRadius(tokens.borderRadiusMedium),
        ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke2),
        backgroundColor: tokens.colorNeutralBackground2,
    },
    confirmationHeader: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
    },
    confirmationActionName: {
        fontSize: tokens.fontSizeBase300,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
    },
    confirmationSummary: {
        fontSize: tokens.fontSizeBase300,
        lineHeight: tokens.lineHeightBase300,
        color: tokens.colorNeutralForeground2,
    },
    successIcon: {
        color: tokens.colorStatusSuccessForeground1,
    },
    failureIcon: {
        color: tokens.colorStatusDangerForeground1,
    },
});
// ─────────────────────────────────────────────────────────────────────────────
// Citation marker regex (matches [1], [2], [12], etc.)
// ─────────────────────────────────────────────────────────────────────────────
const CITATION_MARKER_REGEX = /\[(\d+)\]/g;
// ─────────────────────────────────────────────────────────────────────────────
// Sub-renderers
// ─────────────────────────────────────────────────────────────────────────────
function renderMarkdownCard(data, styles) {
    if (!data.text) {
        return React.createElement('div', { className: styles.markdownRoot });
    }
    const html = renderMarkdownHtml(data.text);
    return (React.createElement("div", { className: styles.markdownRoot },
        React.createElement("div", { className: styles.markdownText, dangerouslySetInnerHTML: { __html: html } })));
}
function renderCitations(data, styles) {
    // Build index map for O(1) lookup
    const citationMap = new Map();
    for (const c of data.citations) {
        citationMap.set(c.index, c);
    }
    // Render the text body as markdown, then inject superscript citation links.
    // Citation markers [N] survive markdown parsing (they are not consumed by marked).
    // After rendering to HTML, we replace [N] with styled <sup> elements.
    const renderCitationsHtml = (text) => {
        if (!text)
            return '';
        let html = renderMarkdownHtml(text);
        // Replace [N] markers in the rendered HTML with superscript citation links
        html = html.replace(/\[(\d+)\]/g, (_match, num) => {
            const citationIndex = parseInt(num, 10);
            const citation = citationMap.get(citationIndex);
            if (citation) {
                return `<sup class="${styles.citationsSuper}">[${citationIndex}]</sup>`;
            }
            return _match;
        });
        return html;
    };
    return (React.createElement("div", { className: styles.citationsRoot },
        React.createElement("div", { className: styles.citationsText, dangerouslySetInnerHTML: { __html: renderCitationsHtml(data.text) } }),
        data.citations.length > 0 && (React.createElement("div", { className: styles.sourcesList },
            React.createElement("div", { className: styles.sourcesHeading }, "Sources"),
            data.citations.map((c) => {
                const isWeb = c.sourceType === 'web';
                const key = isWeb ? `web-${c.index}` : (c.documentId ?? `doc-${c.index}`);
                return (React.createElement("div", { key: key, className: styles.sourceItem },
                    isWeb ? (React.createElement(GlobeRegular, { className: styles.webSourceIcon })) : (React.createElement(DocumentRegular, { className: styles.sourceIcon })),
                    React.createElement("span", null,
                        "[",
                        c.index,
                        "]\u00A0",
                        isWeb && c.url ? (React.createElement(Link, { href: c.url, target: "_blank", rel: "noopener noreferrer", title: c.title }, c.title)) : (React.createElement(Link, { href: `#doc-${c.documentId}`, title: c.title }, c.title)))));
            })))));
}
function renderDiff(data, styles, onOpenDiff) {
    const diffHtml = data.summary ? renderMarkdownHtml(data.summary) : '';
    return (React.createElement("div", { className: styles.diffRoot },
        diffHtml ? (React.createElement("div", { className: styles.diffSummaryText, dangerouslySetInnerHTML: { __html: diffHtml } })) : (React.createElement(Text, { className: styles.diffSummaryText }, data.summary)),
        React.createElement("div", { className: styles.diffActions },
            React.createElement(Button, { appearance: "outline", size: "small", icon: React.createElement(OpenRegular), onClick: () => onOpenDiff?.(data.proposedText), disabled: !onOpenDiff }, "Open in Diff Viewer"))));
}
function renderEntityCard(data, styles, onNavigate) {
    return (React.createElement(Card, null,
        React.createElement(CardHeader, { image: React.createElement(RecordRegular, { style: { fontSize: 20, color: tokens.colorBrandForeground1 } }), header: React.createElement(Text, { weight: "semibold", size: 300 }, data.entityName), description: React.createElement(Badge, { className: styles.entityTypeBadge, appearance: "tint", color: "brand", size: "small" }, data.entityType) }),
        data.fields && data.fields.length > 0 && (React.createElement(CardPreview, null,
            React.createElement("div", { className: styles.entityFieldsGrid }, data.fields.map((field) => (React.createElement("div", { key: field.label, className: styles.entityField },
                React.createElement("span", { className: styles.entityFieldLabel }, field.label),
                React.createElement("span", { className: styles.entityFieldValue }, field.value))))))),
        React.createElement("div", { className: styles.entityCardActions },
            React.createElement(Button, { appearance: "primary", size: "small", onClick: () => onNavigate?.(data.entityType, data.entityId), disabled: !onNavigate }, "View Record"))));
}
function renderActionConfirmation(data, styles) {
    const isSuccess = data.status === 'success';
    return (React.createElement("div", { className: styles.confirmationRoot },
        React.createElement("div", { className: styles.confirmationHeader },
            isSuccess
                ? React.createElement(CheckmarkCircleRegular, {
                    className: styles.successIcon,
                    style: { fontSize: 20 },
                })
                : React.createElement(ErrorCircleRegular, {
                    className: styles.failureIcon,
                    style: { fontSize: 20 },
                }),
            React.createElement(Text, { className: styles.confirmationActionName }, data.actionName),
            React.createElement(Badge, { appearance: "tint", color: isSuccess ? 'success' : 'danger', size: "small" }, isSuccess ? 'Completed' : 'Failed')),
        data.summary ? (React.createElement("div", { className: styles.confirmationSummary, dangerouslySetInnerHTML: { __html: renderMarkdownHtml(data.summary) } })) : (React.createElement(Text, { className: styles.confirmationSummary }, data.summary))));
}
// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────
/**
 * SprkChatMessageRenderer - Renders structured AI response cards.
 *
 * Selects a card renderer based on `responseType`. Unknown types fall back to
 * the markdown renderer without errors.
 *
 * @example
 * ```tsx
 * <SprkChatMessageRenderer
 *   responseType="diff"
 *   data={{ summary: "Simplified 3 sentences", proposedText: "..." }}
 *   onOpenDiff={(text) => setDiffText(text)}
 * />
 *
 * <SprkChatMessageRenderer
 *   responseType="entity_card"
 *   data={{ entityName: "Smith v. Jones", entityType: "matter", entityId: "abc-123", fields: [] }}
 *   onNavigate={(type, id) => navigateToRecord(type, id)}
 * />
 * ```
 */
export const SprkChatMessageRenderer = ({ responseType, data, onNavigate, onOpenDiff, }) => {
    const styles = useStyles();
    // Inject the SPRK_MARKDOWN_CSS stylesheet once on first mount.
    // Uses an idempotent helper so multiple instances don't duplicate the <style> tag.
    React.useEffect(() => {
        ensureMarkdownCssInjected();
    }, []);
    switch (responseType) {
        case 'citations':
            return renderCitations(data, styles);
        case 'diff':
            return renderDiff(data, styles, onOpenDiff);
        case 'entity_card':
            return renderEntityCard(data, styles, onNavigate);
        case 'action_confirmation':
            return renderActionConfirmation(data, styles);
        case 'markdown':
        default:
            // Unknown responseType falls back to markdown — no error
            return renderMarkdownCard(data, styles);
    }
};
export default SprkChatMessageRenderer;
//# sourceMappingURL=SprkChatMessageRenderer.js.map