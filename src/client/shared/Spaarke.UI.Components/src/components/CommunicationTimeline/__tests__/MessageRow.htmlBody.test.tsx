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
  '<div id="overlay" style="position:fixed;inset:0;z-index:2147483647;width:100vw;height:100vh;background:#fff;color:#333">' +
  'Session expired. Sign in to continue</div>' +
  '<img src="https://t.example/p.gif" alt="pixel" width="1" height="1">' +
  '<div id="bg" style="background-image:url(https://t.example/bg.gif);color:#333">styled</div>';

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
    const overlay = container.querySelector('#overlay');
    expect(overlay?.textContent).toContain('Sign in to continue');
    expect(overlay?.getAttribute('style')).toBe('background:#fff;color:#333');
    expect(container.querySelector('img')?.hasAttribute('src')).toBe(false);
    expect(container.querySelector('#bg')?.getAttribute('style')).toBe('color:#333');
    expect(container.innerHTML).not.toContain('t.example');
  });
});
