/**
 * InternalOnlySwitch, tags variant (task 068; owner 2026-10-14: "(External) | (Internal Only)").
 * Covers the keyboard contract of the radio group and the locked (known-Private) state; the send payload is
 * covered through the Conversation compose bar (ConversationView.compose.test.tsx).
 */
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { InternalOnlySwitch } from '../subcomponents/InternalOnlySwitch';

const renderTags = (props: Partial<React.ComponentProps<typeof InternalOnlySwitch>> = {}) => {
  const onChange = jest.fn();
  render(
    <FluentProvider theme={webLightTheme}>
      <InternalOnlySwitch variant="tags" checked={false} onChange={onChange} {...props} />
    </FluentProvider>
  );
  return onChange;
};

describe('InternalOnlySwitch - tags variant', () => {
  it('arrow keys move the selection: from External, ArrowRight selects Internal Only', async () => {
    const onChange = renderTags();
    screen.getByRole('radio', { name: 'External' }).focus();
    await userEvent.keyboard('{ArrowRight}');
    expect(onChange).toHaveBeenCalledWith(true);
  });

  it('locked: Internal Only is selected and External cannot be chosen', async () => {
    const onChange = renderTags({ locked: true });
    expect(screen.getByRole('radio', { name: 'Internal Only' })).toBeChecked();
    expect(screen.getByRole('radio', { name: 'External' })).toHaveAttribute('aria-disabled', 'true');
    await userEvent.click(screen.getByRole('radio', { name: 'External' }));
    expect(onChange).not.toHaveBeenCalled();
  });
});

describe('InternalOnlySwitch - tags variant hint', () => {
  it('hovering Internal Only shows the owner helper text as its tooltip', async () => {
    renderTags();
    await userEvent.hover(screen.getByRole('radio', { name: 'Internal Only' }));
    expect(await screen.findByText('Only people in your organization can see this')).toBeInTheDocument();
  });
});
