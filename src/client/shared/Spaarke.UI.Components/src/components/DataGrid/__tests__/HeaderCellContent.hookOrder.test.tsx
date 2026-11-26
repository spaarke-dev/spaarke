/**
 * HeaderCellContent.hookOrder.test — regression (spaarke-SPA-external-access-platform-r4 task 032 fix round 2).
 *
 * The chevron button's `useRef` used to be called AFTER the plain-label early return, so a header that gained its menu
 * between renders (a sort or filter descriptor arriving once metadata loads) called one more hook than before and React
 * threw "Rendered more hooks than during the previous render". The ref is now declared before the early return.
 */
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { HeaderCellContent } from '../HeaderCellContent';
import type { ChipDescriptor } from '../filterChips/types';

const DESCRIPTOR: ChipDescriptor = { kind: 'text', attribute: 'sprk_name', label: 'Name' };

function Header({ withMenu }: { withMenu: boolean }) {
  return (
    <FluentProvider theme={webLightTheme}>
      <HeaderCellContent
        label="Name"
        descriptor={withMenu ? DESCRIPTOR : undefined}
        state={{}}
        onStateChange={() => undefined}
      />
    </FluentProvider>
  );
}

describe('HeaderCellContent hook order', () => {
  it('a header that gains its menu between renders renders the menu trigger instead of throwing', () => {
    const { rerender } = render(<Header withMenu={false} />);
    expect(screen.queryByRole('button', { name: 'Name column options' })).toBeNull();

    expect(() => rerender(<Header withMenu />)).not.toThrow();
    expect(screen.getByRole('button', { name: 'Name column options' })).toBeTruthy();
  });

  it('a header that loses its menu between renders goes back to the plain label without throwing', () => {
    const { rerender } = render(<Header withMenu />);
    expect(() => rerender(<Header withMenu={false} />)).not.toThrow();
    expect(screen.queryByRole('button', { name: 'Name column options' })).toBeNull();
    expect(screen.getByText('Name')).toBeTruthy();
  });
});
