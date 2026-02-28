/**
 * Simple markdown-to-HTML converter for analysis content.
 *
 * The BFF streams analysis results as markdown text (headings, bold, lists,
 * paragraphs). The RichTextEditor's setHtml() expects HTML — DOMParser
 * treats raw markdown as plain text. This converter handles the subset
 * of markdown used by the AI tool outputs.
 *
 * Supported syntax:
 *   - ### Heading → <h3>
 *   - ## Heading  → <h2>
 *   - **bold**    → <strong>
 *   - *italic*    → <em>
 *   - - item      → <ul><li>
 *   - [status]    → <span> (dimmed progress messages)
 *   - Paragraphs  → <p> (double newline separated)
 */

/**
 * Convert markdown text to HTML for the RichTextEditor.
 */
export function markdownToHtml(markdown: string): string {
    if (!markdown) return "";

    const lines = markdown.split("\n");
    const htmlParts: string[] = [];
    let inList = false;

    for (let i = 0; i < lines.length; i++) {
        let line = lines[i];

        // Skip empty lines (handle paragraph breaks)
        if (line.trim() === "") {
            if (inList) {
                htmlParts.push("</ul>");
                inList = false;
            }
            continue;
        }

        // Headings
        if (line.startsWith("### ")) {
            if (inList) { htmlParts.push("</ul>"); inList = false; }
            htmlParts.push(`<h3>${applyInline(line.substring(4).trim())}</h3>`);
            continue;
        }
        if (line.startsWith("## ")) {
            if (inList) { htmlParts.push("</ul>"); inList = false; }
            htmlParts.push(`<h2>${applyInline(line.substring(3).trim())}</h2>`);
            continue;
        }
        if (line.startsWith("# ")) {
            if (inList) { htmlParts.push("</ul>"); inList = false; }
            htmlParts.push(`<h1>${applyInline(line.substring(2).trim())}</h1>`);
            continue;
        }

        // Unordered list items
        const listMatch = line.match(/^(\s*)[-*•]\s+(.*)/);
        if (listMatch) {
            if (!inList) {
                htmlParts.push("<ul>");
                inList = true;
            }
            htmlParts.push(`<li>${applyInline(listMatch[2])}</li>`);
            continue;
        }

        // Progress messages like [Resolving playbook scopes...]
        if (line.trim().startsWith("[") && line.trim().endsWith("]")) {
            if (inList) { htmlParts.push("</ul>"); inList = false; }
            htmlParts.push(
                `<p style="color:#888;font-style:italic;">${escapeHtml(line.trim())}</p>`
            );
            continue;
        }

        // Regular paragraph
        if (inList) { htmlParts.push("</ul>"); inList = false; }
        htmlParts.push(`<p>${applyInline(line.trim())}</p>`);
    }

    if (inList) {
        htmlParts.push("</ul>");
    }

    return htmlParts.join("\n");
}

/**
 * Apply inline formatting: **bold**, *italic*, `code`.
 */
function applyInline(text: string): string {
    let result = escapeHtml(text);

    // Bold: **text**
    result = result.replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>");

    // Italic: *text* (but not inside **)
    result = result.replace(/(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)/g, "<em>$1</em>");

    // Inline code: `text`
    result = result.replace(/`(.+?)`/g, "<code>$1</code>");

    return result;
}

/**
 * Escape HTML special characters.
 */
function escapeHtml(text: string): string {
    return text
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;");
}
