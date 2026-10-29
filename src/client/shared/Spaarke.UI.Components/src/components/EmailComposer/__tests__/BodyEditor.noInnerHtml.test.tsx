/**
 * BodyEditor.noInnerHtml.test.tsx - spaarke-ontology-platform-r1 task 160 (fix round 1, F1).
 *
 * Switching the body from rich text to plain text converts the HTML to text. That conversion must parse the markup into an inert
 * document: assigning it to a live element's `innerHTML` runs `<img onerror>` even when the element is never attached, and the
 * body can carry a record name (the decision wizard's templated draft names the matter).
 *
 * jsdom never loads an <img>, so a live `<img onerror>` cannot fire here; the test pins the sink (no `innerHTML` write of the
 * draft) and that the converted text is right.
 */
import * as React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { BodyEditor } from '../subcomponents/BodyEditor';

describe('BodyEditor rich text to plain text (task 160 F1)', () => {
  const payload = 'Regarding Acme <img src=x onerror="window.__xss=1">: <b>over budget</b>';

  function withInnerHtmlSpy(run: (writes: string[]) => void) {
    const writes: string[] = [];
    const descriptor = Object.getOwnPropertyDescriptor(Element.prototype, 'innerHTML')!;
    Object.defineProperty(Element.prototype, 'innerHTML', {
      ...descriptor,
      set(this: Element, value: string) {
        writes.push(String(value));
        descriptor.set!.call(this, value);
      },
    });
    try {
      run(writes);
    } finally {
      Object.defineProperty(Element.prototype, 'innerHTML', descriptor);
    }
  }

  it('hands the body to no live element and keeps only the text', () => {
    withInnerHtmlSpy(writes => {
      const onChange = jest.fn();
      const onFormatChange = jest.fn();
      renderWithProviders(
        <BodyEditor value={payload} format="HTML" onChange={onChange} onFormatChange={onFormatChange} />
      );

      fireEvent.click(screen.getByRole('button', { name: 'Switch to plain text' }));

      expect((window as unknown as { __xss?: number }).__xss).toBeUndefined();
      expect(writes.filter(w => /onerror/i.test(w))).toEqual([]);
      expect(onFormatChange).toHaveBeenCalledWith('PlainText');
      expect(onChange).toHaveBeenCalledWith('Regarding Acme : over budget');
    });
  });

  it('must-not-fire: block ends become line breaks and entities are decoded', () => {
    const onChange = jest.fn();
    renderWithProviders(
      <BodyEditor
        value="<p>Hello &amp; welcome</p><p>Second<br/>line</p>"
        format="HTML"
        onChange={onChange}
        onFormatChange={jest.fn()}
      />
    );

    fireEvent.click(screen.getByRole('button', { name: 'Switch to plain text' }));

    expect(onChange).toHaveBeenCalledWith('Hello & welcome\nSecond\nline');
  });
});
