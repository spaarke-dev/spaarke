/**
 * MessageRow.htmlBody.test.tsx — ISS-003 (#1636).
 *
 * `MessageRow` renders an inbound HTML email body through the shared `sanitizeEmailHtml` with no options, so it must
 * inherit the safe defaults: no overlay positioning, no remote image load, no CSS url().
 */
import * as React from 'react';
import { render } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { MessageRow } from '../subcomponents/MessageRow';
import type { TimelineMessage } from '../CommunicationTimeline.types';

const HTML_BODY =
  '<p>Hello <b>team</b></p>' +
  '<div title="overlay" style="position:fixed;inset:0;z-index:2147483647;width:100vw;height:100vh;background:#fff;color:#333">' +
  'Session expired. Sign in to continue</div>' +
  '<img src="https://t.example/p.gif" alt="pixel" width="1" height="1">' +
  '<div title="bg" style="background-image:url(https://t.example/bg.gif);color:#333">styled</div>';

function message(body: string): TimelineMessage {
  return {
    id: 'comm-1',
    channelType: 'email',
    channelTypeRaw: 100000000,
    bodyFormat: 'html',
    body,
    privilege: 100000000,
    attachments: [],
  };
}

describe('MessageRow — inbound HTML body is neutralised by the shared sanitizer (ISS-003)', () => {
  it('drops overlay positioning and remote loads but keeps the content and harmless formatting', () => {
    const { container } = render(
      <FluentProvider theme={webLightTheme}>
        <MessageRow message={message(HTML_BODY)} depth={0} />
      </FluentProvider>
    );

    expect(container.querySelector('b')?.textContent).toBe('team');
    const overlay = container.querySelector('[title="overlay"]');
    expect(overlay?.textContent).toContain('Sign in to continue');
    expect(overlay?.getAttribute('style')).toBe('background:#fff;color:#333');
    expect(container.querySelector('img')?.hasAttribute('src')).toBe(false);
    expect(container.querySelector('[title="bg"]')?.getAttribute('style')).toBe('color:#333');
    expect(container.innerHTML).not.toContain('t.example');
  });
});

/** Hostile body: reuses host Griffel classes and paints outside its box with an in-flow element. */
const PAINT_OVERFLOW_BODY =
  '<p>Hello</p>' +
  '<div class="r1u3t6p6" id="DialogSurface">Sign in again</div>' +
  '<div title="paint" style="position:relative;color:red;box-shadow:-3000px -3000px 0 6000px #fff;outline:5000px solid #fff;' +
  'text-shadow:-800px -400px 0 #000;filter:drop-shadow(0 0 0 red);-webkit-box-reflect:above 0;letter-spacing:-40px">x</div>';

/** The CSS text of every stylesheet rule whose selector uses one of the element's classes (Griffel emits atomic rules). */
function cssOf(el: Element): string {
  const classes = new Set(Array.from(el.classList));
  const rules: string[] = [];
  for (const sheet of Array.from(document.styleSheets)) {
    for (const rule of Array.from(sheet.cssRules)) {
      const selector = (rule as CSSStyleRule).selectorText ?? '';
      if (Array.from(selector.matchAll(/\.([\w-]+)/g)).some(m => classes.has(m[1]))) rules.push(rule.cssText);
    }
  }
  return rules.join('\n').replace(/\s+/g, ' ');
}

describe('MessageRow — class/id and paint-overflow CSS are dropped, and the body box is contained (ISS-003 review)', () => {
  const renderRow = (body: string) =>
    render(
      <FluentProvider theme={webLightTheme}>
        <MessageRow message={message(body)} depth={0} />
      </FluentProvider>
    );

  it('renders no host class or id from the body, and no paint-overflow declaration', () => {
    const { container } = renderRow(PAINT_OVERFLOW_BODY);
    const host = container.querySelector('p')?.parentElement as HTMLElement;
    expect(host).not.toBeNull();
    expect(host.innerHTML).not.toContain('r1u3t6p6');
    expect(host.innerHTML).not.toMatch(/\b(class|id)\s*=/i);
    expect(host.querySelector('[title="paint"]')?.getAttribute('style')).toBe('color:red');
    expect(host.textContent).toContain('Sign in again');
  });

  it('the element that renders the sanitized HTML carries paint containment, a positioned clipped box and horizontal scroll', () => {
    const { container } = renderRow(PAINT_OVERFLOW_BODY);
    const host = container.querySelector('p')?.parentElement as HTMLElement;
    const css = cssOf(host);
    expect(css).toMatch(/contain:\s*paint/);
    expect(css).toMatch(/position:\s*relative/);
    expect(css).toMatch(/overflow-y:\s*hidden/);
    expect(css).toMatch(/overflow-x:\s*auto/);
    // A height-constrained flex parent must not shrink the body and clip the end of a long email.
    expect(css).toMatch(/flex-shrink:\s*0/);
  });
});
