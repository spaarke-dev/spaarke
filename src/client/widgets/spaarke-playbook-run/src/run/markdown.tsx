/**
 * A small, safe Markdown renderer for a playbook run's result sections (task 037c, Q1; notes/037-decisions.md §6).
 *
 * The section text is model output saved in the analysis (037b returns it as stored). It is rendered as React text ONLY: every string ends
 * up as a text child, so `<script>`, `<img onerror>`, an `<a href="javascript:">` or any other HTML in it shows as the characters it is and
 * runs nothing. There is no `dangerouslySetInnerHTML`, no HTML parser and no Markdown library (the bundle stays small; the widget's
 * surface guard forbids the first two).
 *
 * The subset, chosen for what analysis output uses:
 *  - blocks: ATX headings (`#`..`######`), bullet lists (`-`, `*`, `+`), numbered lists (`1.` / `1)`), fenced code (```` ``` ```` / `~~~`),
 *    block quotes (`>`, shown as an indented paragraph), table rows (lines starting with `|`, shown as preformatted text), thematic breaks
 *    (`---`, `***`), and paragraphs (lines kept apart by line breaks);
 *  - inline: `**bold**` / `__bold__`, `*italic*` / `_italic_`, `` `code` ``, and links `[text](url)` shown as "text (url)" in plain text
 *    (a link in model text is never made clickable; the widget's links are the server-built ones);
 *  - a backslash before punctuation keeps it literal.
 * Anything else is text. Nested lists are flattened to one level.
 */
import * as React from 'react';
import { makeStyles, tokens } from '@fluentui/react-components';

export type InlineNode =
  | { type: 'text'; text: string }
  | { type: 'strong'; children: InlineNode[] }
  | { type: 'em'; children: InlineNode[] }
  | { type: 'code'; text: string };

export type Block =
  | { type: 'heading'; level: number; children: InlineNode[] }
  | { type: 'paragraph'; lines: InlineNode[][] }
  | { type: 'quote'; lines: InlineNode[][] }
  | { type: 'list'; ordered: boolean; start: number; items: InlineNode[][] }
  | { type: 'code'; text: string }
  | { type: 'rule' };

const HEADING = /^ {0,3}(#{1,6})[ \t]+(.*?)(?:[ \t]+#+)?[ \t]*$/;
const BULLET = /^ {0,8}[-*+][ \t]+(.*)$/;
const ORDERED = /^ {0,8}(\d{1,9})[.)][ \t]+(.*)$/;
const FENCE = /^ {0,3}(`{3,}|~{3,})/;
const QUOTE = /^ {0,3}>[ \t]?(.*)$/;
const RULE = /^ {0,3}(?:(?:-[ \t]*){3,}|(?:\*[ \t]*){3,}|(?:_[ \t]*){3,})$/;
const TABLE = /^ {0,3}\|/;

/** Splits Markdown into blocks. Pure; never throws. */
export function parseBlocks(markdown: string): Block[] {
  const lines = markdown.replace(/\r\n?/g, '\n').split('\n');
  const blocks: Block[] = [];
  let i = 0;

  while (i < lines.length) {
    const line = lines[i];

    if (line.trim() === '') {
      i++;
      continue;
    }

    const fence = FENCE.exec(line);
    if (fence) {
      const marker = fence[1];
      const body: string[] = [];
      i++;
      while (i < lines.length && !lines[i].trimStart().startsWith(marker)) body.push(lines[i++]);
      i++; // the closing fence (or the end of the text)
      blocks.push({ type: 'code', text: body.join('\n') });
      continue;
    }

    const heading = HEADING.exec(line);
    if (heading) {
      blocks.push({ type: 'heading', level: heading[1].length, children: parseInline(heading[2]) });
      i++;
      continue;
    }

    if (RULE.test(line)) {
      blocks.push({ type: 'rule' });
      i++;
      continue;
    }

    if (TABLE.test(line)) {
      const rows: string[] = [];
      while (i < lines.length && TABLE.test(lines[i])) rows.push(lines[i++].trim());
      blocks.push({ type: 'code', text: rows.join('\n') });
      continue;
    }

    if (QUOTE.test(line)) {
      const quoted: InlineNode[][] = [];
      let match: RegExpExecArray | null;
      while (i < lines.length && (match = QUOTE.exec(lines[i]))) {
        if (match[1].trim() !== '') quoted.push(parseInline(match[1]));
        i++;
      }
      blocks.push({ type: 'quote', lines: quoted });
      continue;
    }

    const bullet = BULLET.exec(line);
    const ordered = bullet ? null : ORDERED.exec(line);
    if (bullet || ordered) {
      const isOrdered = ordered !== null;
      const items: InlineNode[][] = [];
      while (i < lines.length) {
        const item = isOrdered ? ORDERED.exec(lines[i]) : BULLET.exec(lines[i]);
        if (item) {
          items.push(parseInline(isOrdered ? item[2] : item[1]));
          i++;
          continue;
        }
        // A wrapped item: an indented, non-blank line that starts no other block continues the item before it.
        const next = lines[i];
        if (next.trim() !== '' && /^[ \t]+/.test(next) && !startsBlock(next) && items.length) {
          items[items.length - 1] = [
            ...items[items.length - 1],
            { type: 'text', text: ' ' },
            ...parseInline(next.trim()),
          ];
          i++;
          continue;
        }
        break;
      }
      blocks.push({ type: 'list', ordered: isOrdered, start: isOrdered ? Number(ordered![1]) : 1, items });
      continue;
    }

    const paragraph: InlineNode[][] = [];
    while (i < lines.length && lines[i].trim() !== '' && (paragraph.length === 0 || !startsBlock(lines[i]))) {
      paragraph.push(parseInline(lines[i].trim()));
      i++;
    }
    blocks.push({ type: 'paragraph', lines: paragraph });
  }

  return blocks;
}

function startsBlock(line: string): boolean {
  return (
    FENCE.test(line) ||
    HEADING.test(line) ||
    RULE.test(line) ||
    TABLE.test(line) ||
    QUOTE.test(line) ||
    BULLET.test(line) ||
    ORDERED.test(line)
  );
}

const ESCAPABLE = /[\\`*_{}[\]()#+\-.!|>~<"'&]/;

/** Parses inline emphasis, code and links into nodes. Pure; never throws; unbalanced markers stay literal. */
export function parseInline(text: string): InlineNode[] {
  const nodes: InlineNode[] = [];
  let buffer = '';
  const flush = () => {
    if (buffer) {
      nodes.push({ type: 'text', text: buffer });
      buffer = '';
    }
  };

  let i = 0;
  while (i < text.length) {
    const ch = text[i];

    if (ch === '\\' && i + 1 < text.length && ESCAPABLE.test(text[i + 1])) {
      buffer += text[i + 1];
      i += 2;
      continue;
    }

    if (ch === '`') {
      const end = text.indexOf('`', i + 1);
      if (end > i + 1) {
        flush();
        nodes.push({ type: 'code', text: text.slice(i + 1, end) });
        i = end + 1;
        continue;
      }
    }

    if ((ch === '*' || ch === '_') && text[i + 1] === ch) {
      const marker = ch + ch;
      // `__` inside a word (a__b, __init__-like names) is not emphasis.
      const intraword = ch === '_' && i > 0 && /\w/.test(text[i - 1]);
      let end = text.indexOf(marker, i + 2);
      while (ch === '_' && end > 0 && end + 2 < text.length && /\w/.test(text[end + 2]))
        end = text.indexOf(marker, end + 2);
      if (!intraword && end > i + 2) {
        flush();
        nodes.push({ type: 'strong', children: parseInline(text.slice(i + 2, end)) });
        i = end + 2;
        continue;
      }
    }

    if ((ch === '*' || ch === '_') && text[i + 1] !== ch && text[i + 1] !== undefined && text[i + 1] !== ' ') {
      // An underscore inside a word (snake_case) is not emphasis.
      const wordBefore = ch === '_' && i > 0 && /\w/.test(text[i - 1]);
      const end = findClosing(text, ch, i + 1);
      if (!wordBefore && end > i + 1) {
        flush();
        nodes.push({ type: 'em', children: parseInline(text.slice(i + 1, end)) });
        i = end + 1;
        continue;
      }
    }

    if (ch === '[') {
      const close = text.indexOf('](', i + 1);
      const end = close > i ? text.indexOf(')', close + 2) : -1;
      if (close > i && end > close) {
        flush();
        const label = text.slice(i + 1, close);
        const target = text.slice(close + 2, end).trim();
        nodes.push(...parseInline(label));
        if (target && target !== label) nodes.push({ type: 'text', text: ` (${target})` });
        i = end + 1;
        continue;
      }
    }

    buffer += ch;
    i++;
  }

  flush();
  return nodes;
}

/** The closing single marker: not doubled, not preceded by a space. */
function findClosing(text: string, marker: string, from: number): number {
  for (let j = from; j < text.length; j++) {
    if (text[j] === '\\') {
      j++;
      continue;
    }
    if (text[j] === marker && text[j + 1] !== marker && text[j - 1] !== ' ') {
      if (marker === '_' && j + 1 < text.length && /\w/.test(text[j + 1])) continue;
      return j;
    }
  }
  return -1;
}

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    color: tokens.colorNeutralForeground1,
    fontFamily: tokens.fontFamilyBase,
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    overflowWrap: 'anywhere',
  },
  block: {
    margin: 0,
  },
  heading: {
    margin: 0,
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
  },
  list: {
    margin: 0,
    paddingLeft: tokens.spacingHorizontalXL,
  },
  quote: {
    margin: 0,
    paddingLeft: tokens.spacingHorizontalM,
    borderLeftWidth: tokens.strokeWidthThick,
    borderLeftStyle: 'solid',
    borderLeftColor: tokens.colorNeutralStroke2,
    color: tokens.colorNeutralForeground2,
  },
  code: {
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusSmall,
    paddingLeft: tokens.spacingHorizontalXXS,
    paddingRight: tokens.spacingHorizontalXXS,
  },
  pre: {
    margin: 0,
    padding: tokens.spacingHorizontalS,
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusMedium,
    overflowX: 'auto',
    whiteSpace: 'pre',
  },
  rule: {
    width: '100%',
    borderTopWidth: tokens.strokeWidthThin,
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
    borderBottomStyle: 'none',
    borderLeftStyle: 'none',
    borderRightStyle: 'none',
    margin: 0,
  },
});

function renderInline(nodes: InlineNode[], codeClass: string): React.ReactNode[] {
  return nodes.map((node, index) => {
    switch (node.type) {
      case 'strong':
        return <strong key={index}>{renderInline(node.children, codeClass)}</strong>;
      case 'em':
        return <em key={index}>{renderInline(node.children, codeClass)}</em>;
      case 'code':
        return (
          <code key={index} className={codeClass}>
            {node.text}
          </code>
        );
      default:
        return <React.Fragment key={index}>{node.text}</React.Fragment>;
    }
  });
}

function renderLines(lines: InlineNode[][], codeClass: string): React.ReactNode[] {
  return lines.flatMap((line, index) =>
    index === 0 ? renderInline(line, codeClass) : [<br key={`br-${index}`} />, ...renderInline(line, codeClass)]
  );
}

/** A section's Markdown as React elements: text only, never HTML. */
export function MarkdownText({ text }: { text: string }) {
  const styles = useStyles();
  const blocks = React.useMemo(() => parseBlocks(text), [text]);

  return (
    <div className={styles.root}>
      {blocks.map((block, index) => {
        switch (block.type) {
          case 'heading': {
            // Section headings are the panel titles (h3); a heading inside a section sits one level below them.
            const Tag = (block.level <= 3 ? 'h4' : block.level === 4 ? 'h5' : 'h6') as 'h4' | 'h5' | 'h6';
            return (
              <Tag key={index} className={styles.heading}>
                {renderInline(block.children, styles.code)}
              </Tag>
            );
          }
          case 'list': {
            const items = block.items.map((item, itemIndex) => (
              <li key={itemIndex}>{renderInline(item, styles.code)}</li>
            ));
            return block.ordered ? (
              <ol key={index} className={styles.list} start={block.start}>
                {items}
              </ol>
            ) : (
              <ul key={index} className={styles.list}>
                {items}
              </ul>
            );
          }
          case 'code':
            return (
              <pre key={index} className={styles.pre}>
                {block.text}
              </pre>
            );
          case 'quote':
            return (
              <blockquote key={index} className={styles.quote}>
                {renderLines(block.lines, styles.code)}
              </blockquote>
            );
          case 'rule':
            return <hr key={index} className={styles.rule} />;
          default:
            return (
              <p key={index} className={styles.block}>
                {renderLines(block.lines, styles.code)}
              </p>
            );
        }
      })}
    </div>
  );
}
