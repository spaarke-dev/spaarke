/**
 * The safe Markdown subset for a run's result sections (task 037c, Q1). Model text is rendered as React text only: an HTML or script payload
 * shows as characters and creates no element, no handler and no link.
 */
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { MarkdownText, parseBlocks, parseInline } from '../run/markdown';

function renderMarkdown(text: string) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <MarkdownText text={text} />
    </FluentProvider>
  );
}

describe('parseInline', () => {
  it('reads bold, italic, code and links (as plain text)', () => {
    expect(parseInline('a **b** *c* `d` [e](https://x.example)')).toEqual([
      { type: 'text', text: 'a ' },
      { type: 'strong', children: [{ type: 'text', text: 'b' }] },
      { type: 'text', text: ' ' },
      { type: 'em', children: [{ type: 'text', text: 'c' }] },
      { type: 'text', text: ' ' },
      { type: 'code', text: 'd' },
      { type: 'text', text: ' ' },
      { type: 'text', text: 'e' },
      { type: 'text', text: ' (https://x.example)' },
    ]);
  });

  it('leaves unbalanced markers, snake_case and escapes literal', () => {
    expect(parseInline('2 * 3 and **open')).toEqual([{ type: 'text', text: '2 * 3 and **open' }]);
    expect(parseInline('a_field_name')).toEqual([{ type: 'text', text: 'a_field_name' }]);
    expect(parseInline('window.__x=1 and y.__x')).toEqual([{ type: 'text', text: 'window.__x=1 and y.__x' }]);
    expect(parseInline('a __strong__ word')).toEqual([
      { type: 'text', text: 'a ' },
      { type: 'strong', children: [{ type: 'text', text: 'strong' }] },
      { type: 'text', text: ' word' },
    ]);
    expect(parseInline('\\*not italic\\*')).toEqual([{ type: 'text', text: '*not italic*' }]);
  });
});

describe('parseBlocks', () => {
  it('splits headings, lists, code, quotes, tables, rules and paragraphs', () => {
    const blocks = parseBlocks(
      [
        '## Key terms',
        '- one',
        '- two',
        '',
        '1. first',
        '2) second',
        '',
        '```',
        '<b>x</b>',
        '```',
        '> quoted',
        '| a | b |',
        '|---|---|',
        '---',
        'line one',
        'line two',
      ].join('\n')
    );
    expect(blocks.map(b => b.type)).toEqual(['heading', 'list', 'list', 'code', 'quote', 'code', 'rule', 'paragraph']);
    expect(blocks[3]).toEqual({ type: 'code', text: '<b>x</b>' });
    expect(blocks[5]).toEqual({ type: 'code', text: '| a | b |\n|---|---|' });
    expect(blocks[7]).toMatchObject({ type: 'paragraph', lines: [[{ text: 'line one' }], [{ text: 'line two' }]] });
  });

  it('an unclosed fence runs to the end; CRLF is read as LF', () => {
    expect(parseBlocks('```\r\ncode\r\nmore')).toEqual([{ type: 'code', text: 'code\nmore' }]);
  });

  it('a wrapped list item continues the item', () => {
    const [list] = parseBlocks('- first part\n  continues here\n- second');
    expect(list).toMatchObject({ type: 'list', items: [expect.any(Array), [{ type: 'text', text: 'second' }]] });
    expect(JSON.stringify(list)).toContain('continues here');
  });
});

describe('MarkdownText rendering', () => {
  it('renders the subset as elements', () => {
    const { container } = renderMarkdown('### Inside\nSome **bold** and *italic* text.\n\n- a\n- b\n\n1. x\n\n`code`');
    expect(container.querySelector('h4')).toHaveTextContent('Inside');
    expect(container.querySelector('strong')).toHaveTextContent('bold');
    expect(container.querySelector('em')).toHaveTextContent('italic');
    expect(container.querySelectorAll('ul li')).toHaveLength(2);
    expect(container.querySelectorAll('ol li')).toHaveLength(1);
    expect(container.querySelector('code')).toHaveTextContent('code');
  });

  it.each([
    ['a script tag', '<script>window.__pwned = 1</script>'],
    ['an image with onerror', '<img src=x onerror="window.__pwned = 1">'],
    ['an iframe', '<iframe src="https://evil.example"></iframe>'],
    ['a javascript link (HTML)', '<a href="javascript:window.__pwned=1">click</a>'],
    ['a javascript link (Markdown)', '[click](javascript:window.__pwned=1)'],
    ['a style tag', '<style>body{display:none}</style>'],
  ])('renders %s inert: as text, with no element, handler or link', (_name, payload) => {
    const { container } = renderMarkdown(`Before\n\n${payload}\n\n**after** ${payload}`);
    expect(container.querySelector('script, img, iframe, a, style, [onerror], [href], [src]')).toBeNull();
    expect((window as unknown as { __pwned?: number }).__pwned).toBeUndefined();
    // The characters are shown as they were written.
    const visible = payload.startsWith('[click]') ? 'click (javascript:window.__pwned=1)' : payload;
    expect(
      screen.getAllByText((_c, el) => el?.tagName === 'P' && (el.textContent ?? '').includes(visible)).length
    ).toBeGreaterThan(0);
  });

  it('HTML inside a code block or inline code stays text', () => {
    const { container } = renderMarkdown('```\n<script>x()</script>\n```\n\nand `<img src=x onerror=y>`');
    expect(container.querySelector('script, img')).toBeNull();
    expect(container.querySelector('pre')).toHaveTextContent('<script>x()</script>');
    expect(container.querySelector('code')).toHaveTextContent('<img src=x onerror=y>');
  });
});
