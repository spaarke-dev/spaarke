/**
 * RecordCommandBar tests — FR-04 / FR-05 (spaarke-SPA-external-access-platform-r4, task 001).
 *
 * Scope (task 001 acceptance criteria): visibility (hide, don't disable),
 * tooltip = accessible name for inline and overflow actions, overflow menu
 * presence, keyboard (Tab / Enter / Space / Esc + focus return), empty and
 * all-hidden lists, light / dark / high-contrast themes, the icon map, and
 * importability from the package barrel.
 */

import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import {
  FluentProvider,
  teamsHighContrastTheme,
  webDarkTheme,
  webLightTheme,
  type Theme,
} from '@fluentui/react-components';

import { RecordCommandBar } from '../RecordCommandBar';
import { RECORD_ACTION_ICONS } from '../recordActionIcons';
import type { RecordCommandBarAction } from '../types';

const IconStub: React.FC<{ id: string }> = ({ id }) => <span data-testid={`icon-${id}`} aria-hidden="true" />;

const makeAction = (
  overrides: Partial<RecordCommandBarAction> & Pick<RecordCommandBarAction, 'id' | 'tooltip'>
): RecordCommandBarAction => ({
  label: overrides.tooltip,
  icon: <IconStub id={overrides.id} />,
  onClick: jest.fn(),
  visible: true,
  ...overrides,
});

const renderBar = (actions: RecordCommandBarAction[], theme: Theme = webLightTheme) =>
  render(
    <FluentProvider theme={theme}>
      <RecordCommandBar actions={actions} ariaLabel="Record actions" />
    </FluentProvider>
  );

describe('RecordCommandBar', () => {
  describe('visibility (hide, do not disable)', () => {
    it('render_HiddenAction_IsNotInDomAndGetsNoTabStop', async () => {
      const user = userEvent.setup();
      renderBar([
        makeAction({ id: 'refresh', tooltip: 'Refresh' }),
        makeAction({ id: 'upload', tooltip: 'Upload', visible: false }),
        makeAction({ id: 'copyLink', tooltip: 'Copy link' }),
      ]);

      expect(screen.queryByRole('button', { name: 'Upload' })).not.toBeInTheDocument();
      expect(screen.queryByTestId('icon-upload')).not.toBeInTheDocument();
      expect(screen.getAllByRole('button')).toHaveLength(2);

      await user.tab();
      expect(screen.getByRole('button', { name: 'Refresh' })).toHaveFocus();
      await user.tab();
      expect(screen.getByRole('button', { name: 'Copy link' })).toHaveFocus();
      await user.tab();
      expect(document.body).toHaveFocus();
    });

    it('render_EmptyActionList_RendersBarWithNoButtons', () => {
      renderBar([]);

      expect(screen.getByRole('toolbar', { name: 'Record actions' })).toBeInTheDocument();
      expect(screen.queryAllByRole('button')).toHaveLength(0);
    });

    it('render_AllActionsHidden_RendersBarWithNoButtons', () => {
      renderBar([
        makeAction({ id: 'upload', tooltip: 'Upload', visible: false }),
        makeAction({ id: 'delete', tooltip: 'Delete', visible: false, overflow: true }),
      ]);

      expect(screen.getByRole('toolbar', { name: 'Record actions' })).toBeInTheDocument();
      expect(screen.queryAllByRole('button')).toHaveLength(0);
    });
  });

  describe('tooltip is the accessible name', () => {
    it('render_InlineAction_TooltipIsAriaLabelAndShownOnHover', async () => {
      const user = userEvent.setup();
      renderBar([makeAction({ id: 'refresh', label: 'Refresh', tooltip: 'Refresh record' })]);

      const button = screen.getByRole('button', { name: 'Refresh record' });
      expect(button).toHaveAttribute('aria-label', 'Refresh record');

      await user.hover(button);
      expect(await screen.findByRole('tooltip')).toHaveTextContent('Refresh record');
    });

    it('openMenu_OverflowAction_TooltipIsAriaLabelAndShownOnHover', async () => {
      const user = userEvent.setup();
      renderBar([makeAction({ id: 'history', label: 'History', tooltip: 'View history', overflow: true })]);

      await user.click(screen.getByRole('button', { name: 'More actions' }));
      const item = await screen.findByRole('menuitem', { name: 'View history' });
      expect(item).toHaveAttribute('aria-label', 'View history');
      expect(item).toHaveTextContent('History');

      await user.hover(item);
      expect(await screen.findByRole('tooltip')).toHaveTextContent('View history');
    });
  });

  describe('overflow menu', () => {
    it('render_NoVisibleOverflowAction_HasNoMoreActionsControl', () => {
      renderBar([
        makeAction({ id: 'refresh', tooltip: 'Refresh' }),
        makeAction({ id: 'delete', tooltip: 'Delete', overflow: true, visible: false }),
      ]);

      expect(screen.queryByRole('button', { name: 'More actions' })).not.toBeInTheDocument();
      expect(screen.getAllByRole('button')).toHaveLength(1);
    });

    it('render_OverflowActions_RenderOnlyInsideMoreActionsMenu', async () => {
      const user = userEvent.setup();
      renderBar([
        makeAction({ id: 'refresh', tooltip: 'Refresh' }),
        makeAction({ id: 'download', tooltip: 'Download', overflow: true }),
        makeAction({ id: 'delete', tooltip: 'Delete', overflow: true }),
        makeAction({ id: 'history', tooltip: 'History', overflow: true, visible: false }),
      ]);

      // Inline strip: Refresh + the overflow trigger, no overflow action.
      expect(screen.getAllByRole('button')).toHaveLength(2);
      expect(screen.queryByRole('button', { name: 'Download' })).not.toBeInTheDocument();
      expect(screen.queryByRole('menuitem')).not.toBeInTheDocument();

      const more = screen.getByRole('button', { name: 'More actions' });
      expect(more).toHaveAttribute('aria-haspopup', 'menu');
      expect(more).toHaveAttribute('aria-expanded', 'false');

      await user.click(more);
      const items = await screen.findAllByRole('menuitem');
      expect(items.map(item => item.getAttribute('aria-label'))).toEqual(['Download', 'Delete']);
      expect(more).toHaveAttribute('aria-expanded', 'true');
    });

    it('clickTrigger_WhenMenuOpen_ClosesMenu', async () => {
      const user = userEvent.setup();
      renderBar([makeAction({ id: 'download', tooltip: 'Download', overflow: true })]);

      const more = screen.getByRole('button', { name: 'More actions' });
      await user.click(more);
      expect(await screen.findByRole('menu')).toBeInTheDocument();

      await user.click(more);
      await waitFor(() => expect(screen.queryByRole('menu')).not.toBeInTheDocument());
    });
  });

  describe('keyboard', () => {
    it('keyboard_EnterAndSpaceOnInlineAction_ActivateIt', async () => {
      const user = userEvent.setup();
      const onRefresh = jest.fn();
      const onCopy = jest.fn();
      renderBar([
        makeAction({ id: 'refresh', tooltip: 'Refresh', onClick: onRefresh }),
        makeAction({ id: 'copyLink', tooltip: 'Copy link', onClick: onCopy }),
      ]);

      await user.tab();
      await user.keyboard('{Enter}');
      expect(onRefresh).toHaveBeenCalledTimes(1);

      await user.tab();
      await user.keyboard(' ');
      expect(onCopy).toHaveBeenCalledTimes(1);
    });

    it('keyboard_OverflowMenu_EnterOpensArrowSelectsEscClosesAndFocusReturns', async () => {
      const user = userEvent.setup();
      const onDownload = jest.fn();
      const onDelete = jest.fn();
      renderBar([
        makeAction({ id: 'refresh', tooltip: 'Refresh' }),
        makeAction({ id: 'download', tooltip: 'Download', overflow: true, onClick: onDownload }),
        makeAction({ id: 'delete', tooltip: 'Delete', overflow: true, onClick: onDelete }),
      ]);
      const more = screen.getByRole('button', { name: 'More actions' });

      // Tab reaches the inline action, then the overflow trigger.
      await user.tab();
      expect(screen.getByRole('button', { name: 'Refresh' })).toHaveFocus();
      await user.tab();
      expect(more).toHaveFocus();

      // Enter opens and focus moves to the first item; Enter on the second item
      // runs it. Moving between items with the arrow keys is Fluent MenuList's
      // own (tabster) behaviour, which does not run under jsdom (it treats
      // layout-less elements as hidden), so focus is placed directly here.
      await user.keyboard('{Enter}');
      await waitFor(() => expect(screen.getByRole('menuitem', { name: 'Download' })).toHaveFocus());
      screen.getByRole('menuitem', { name: 'Delete' }).focus();
      await user.keyboard('{Enter}');

      expect(onDelete).toHaveBeenCalledTimes(1);
      expect(onDownload).not.toHaveBeenCalled();
      await waitFor(() => expect(screen.queryByRole('menu')).not.toBeInTheDocument());
      await waitFor(() => expect(more).toHaveFocus());

      // Reopen with Space; Esc closes and returns focus to the trigger.
      await user.keyboard(' ');
      await waitFor(() => expect(screen.getByRole('menuitem', { name: 'Download' })).toHaveFocus());
      await user.keyboard('{Escape}');

      await waitFor(() => expect(screen.queryByRole('menu')).not.toBeInTheDocument());
      await waitFor(() => expect(more).toHaveFocus());
      expect(onDownload).not.toHaveBeenCalled();
      expect(onDelete).toHaveBeenCalledTimes(1);
    });
  });

  describe('themes (ADR-021)', () => {
    it.each([
      ['light', webLightTheme],
      ['dark', webDarkTheme],
      ['high contrast', teamsHighContrastTheme],
    ])('render_%sTheme_RendersActionsWithoutConsoleErrors', async (_name, theme) => {
      const user = userEvent.setup();
      const consoleError = jest.spyOn(console, 'error').mockImplementation(() => undefined);
      try {
        renderBar(
          [
            makeAction({ id: 'refresh', tooltip: 'Refresh' }),
            makeAction({ id: 'delete', tooltip: 'Delete', overflow: true }),
          ],
          theme
        );

        expect(screen.getByRole('button', { name: 'Refresh' })).toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: 'More actions' }));
        expect(await screen.findByRole('menuitem', { name: 'Delete' })).toBeInTheDocument();
        expect(consoleError).not.toHaveBeenCalled();
      } finally {
        consoleError.mockRestore();
      }
    });
  });

  describe('RECORD_ACTION_ICONS (FR-05)', () => {
    it('iconMap_HoldsExactlyTheTwelveFr05Entries', () => {
      const Icons = require('@fluentui/react-icons');
      expect(RECORD_ACTION_ICONS).toEqual({
        refresh: Icons.ArrowClockwise20Regular,
        upload: Icons.ArrowUpload20Regular,
        newMessage: Icons.ChatAdd20Regular,
        addTodo: Icons.TaskListAdd20Regular,
        addEvent: Icons.CalendarAdd20Regular,
        submitInvoice: Icons.ReceiptAdd20Regular,
        copyLink: Icons.Link20Regular,
        expandSection: Icons.Open20Regular,
        download: Icons.ArrowDownload20Regular,
        delete: Icons.Delete20Regular,
        history: Icons.History20Regular,
        send: Icons.Send20Regular,
      });
      Object.values(RECORD_ACTION_ICONS).forEach(icon => expect(icon).toBeDefined());
      expect(Object.isFrozen(RECORD_ACTION_ICONS)).toBe(true);
    });
  });
});
