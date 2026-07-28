/**
 * ThreePaneLayout.statePreserved.test.tsx — nda-r1 UAT #2/#3 regression.
 *
 * A collapsed pane must be HIDDEN, not UNMOUNTED. Before the fix, the collapsed branch dropped the
 * pane's children from the JSX, so React unmounted the pane and destroyed all its pane-local state —
 * the Assistant's chat session/history were lost on a collapse/expand cycle, and the Workspace's open
 * compose tab was lost on close. These tests pin the contract: children stay MOUNTED across a
 * collapse/expand cycle (no remount), and the collapsed pane is hidden + aria-hidden.
 */
import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { ThreePaneLayout } from '../ThreePaneLayout';

/** Counts how many times it MOUNTS (a remount = destroyed state). */
function MountSpy({ id, onMount }: { id: string; onMount: () => void }): React.JSX.Element {
  React.useEffect(() => {
    onMount();
  }, [onMount]);
  return <div data-testid={id}>{id} content</div>;
}

function renderLayout(props: { leftCollapsed?: boolean; onLeftMount: () => void }) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <ThreePaneLayout
        leftPane={<MountSpy id="left-content" onMount={props.onLeftMount} />}
        centerPane={<div data-testid="center-content">center</div>}
        rightPane={<div data-testid="right-content">right</div>}
        leftCollapsed={props.leftCollapsed}
      />
    </FluentProvider>,
  );
}

describe('ThreePaneLayout — collapse hides (does not unmount) pane children', () => {
  it('keeps the left pane MOUNTED across a collapse → expand cycle (no remount = state survives)', () => {
    const onLeftMount = jest.fn();
    const { rerender } = renderLayout({ leftCollapsed: false, onLeftMount });

    // Mounted once, visible.
    expect(screen.getByTestId('left-content')).toBeInTheDocument();
    expect(onLeftMount).toHaveBeenCalledTimes(1);

    const rerenderWith = (leftCollapsed: boolean) =>
      rerender(
        <FluentProvider theme={webLightTheme}>
          <ThreePaneLayout
            leftPane={<MountSpy id="left-content" onMount={onLeftMount} />}
            centerPane={<div data-testid="center-content">center</div>}
            rightPane={<div data-testid="right-content">right</div>}
            leftCollapsed={leftCollapsed}
          />
        </FluentProvider>,
      );

    // Collapse: content STILL in the DOM (hidden, not removed) — and NOT remounted.
    rerenderWith(true);
    expect(screen.getByTestId('left-content')).toBeInTheDocument();
    expect(onLeftMount).toHaveBeenCalledTimes(1);

    // Expand again: still the SAME mount — state was preserved the whole time.
    rerenderWith(false);
    expect(screen.getByTestId('left-content')).toBeInTheDocument();
    expect(onLeftMount).toHaveBeenCalledTimes(1);
  });

  it('marks the collapsed pane aria-hidden while keeping its children present', () => {
    const { container } = renderLayout({ leftCollapsed: true, onLeftMount: jest.fn() });
    // Children present…
    const content = screen.getByTestId('left-content');
    expect(content).toBeInTheDocument();
    // …inside an aria-hidden wrapper (hidden from AT + display:none removes it from layout/tab order).
    const hiddenWrapper = container.querySelector('[aria-hidden="true"]');
    expect(hiddenWrapper).not.toBeNull();
    expect(hiddenWrapper).toContainElement(content);
  });
});
