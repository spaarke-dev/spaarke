/**
 * PolymorphicPicker unit tests
 *
 * @see components/PolymorphicPicker/PolymorphicPicker.tsx
 *
 * Tests cover:
 *  - menu opens on toolbar-button click
 *  - MenuList renders one MenuItem per catalog entry (displayName label)
 *  - selecting an item invokes Xrm.Utility.lookupObjects with the right entity
 *  - onSelect is fired with (entityType, cleaned recordId, recordName)
 *  - disabled prop suppresses the trigger
 *  - readOnly prop hides the trigger entirely
 *  - Xrm.Utility.lookupObjects returning empty (cancel) does not fire onSelect
 *  - Xrm.Utility.lookupObjects being unavailable surfaces an inline error and
 *    calls onError
 */

import * as React from 'react';
import { render, screen, waitFor, fireEvent, act } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import {
  PolymorphicPicker,
  type PolymorphicPickerProps,
  type RecordTypeCatalogEntry,
  type IPolymorphicPickerWebApi,
} from '../PolymorphicPicker';

// ---------------------------------------------------------------------------
// Test fixtures
// ---------------------------------------------------------------------------

const TestWrapper: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <FluentProvider theme={webLightTheme}>{children}</FluentProvider>
);

const renderPicker = (props: Partial<PolymorphicPickerProps> = {}) => {
  const merged: PolymorphicPickerProps = {
    catalog: sampleCatalog,
    onSelect: jest.fn(),
    webApi: emptyWebApi,
    ...props,
  };
  const utils = render(<PolymorphicPicker {...merged} />, { wrapper: TestWrapper });
  return { ...utils, props: merged };
};

const sampleCatalog: RecordTypeCatalogEntry[] = [
  {
    recordTypeRefId: '11111111-1111-1111-1111-111111111111',
    displayName: 'Matter',
    logicalName: 'sprk_matter',
    regardingField: 'sprk_regardingmatter',
    regardingRecordNumberField: 'sprk_matternumber',
  },
  {
    recordTypeRefId: '22222222-2222-2222-2222-222222222222',
    displayName: 'Project',
    logicalName: 'sprk_project',
    regardingField: 'sprk_regardingproject',
    regardingRecordNumberField: 'sprk_projectnumber',
  },
  {
    recordTypeRefId: '33333333-3333-3333-3333-333333333333',
    displayName: 'Contact',
    logicalName: 'contact',
    regardingField: 'sprk_regardingcontact',
    // regardingRecordNumberField deliberately undefined — graceful-blank path
  },
];

const emptyWebApi: IPolymorphicPickerWebApi = {};

// ---------------------------------------------------------------------------
// Xrm bridge helpers
// ---------------------------------------------------------------------------

type LookupOpts = {
  entityTypes: string[];
  defaultEntityType?: string;
  allowMultiSelect: boolean;
};

type LookupResult = Array<{ id: string; name: string; entityType?: string }>;

interface WindowWithXrm extends Window {
  Xrm?: {
    Utility?: {
      lookupObjects?: (opts: LookupOpts) => Promise<LookupResult>;
    };
  };
}

function installXrm(lookupImpl?: (opts: LookupOpts) => Promise<LookupResult>) {
  (window as WindowWithXrm).Xrm = {
    Utility: {
      lookupObjects: lookupImpl,
    },
  };
}

function clearXrm() {
  delete (window as WindowWithXrm).Xrm;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('PolymorphicPicker', () => {
  afterEach(() => {
    clearXrm();
    jest.clearAllMocks();
  });

  describe('rendering', () => {
    it('renders the default title "Related Record"', () => {
      renderPicker();
      expect(screen.getByTestId('polymorphic-picker-title')).toHaveTextContent('Related Record');
    });

    it('renders a custom title when provided', () => {
      renderPicker({ title: 'Regarding' });
      expect(screen.getByTestId('polymorphic-picker-title')).toHaveTextContent('Regarding');
    });

    it('renders the toolbar trigger by default', () => {
      renderPicker();
      expect(screen.getByTestId('polymorphic-picker-trigger')).toBeInTheDocument();
    });

    it('applies caller-supplied className after component root class', () => {
      const { container } = renderPicker({ className: 'my-custom-class' });
      const root = container.querySelector('[data-testid="polymorphic-picker"]');
      expect(root).toHaveClass('my-custom-class');
    });
  });

  describe('menu behavior', () => {
    it('opens the menu when the trigger is clicked and lists one MenuItem per catalog entry', async () => {
      renderPicker();

      fireEvent.click(screen.getByTestId('polymorphic-picker-trigger'));

      await waitFor(() => {
        expect(screen.getByTestId('polymorphic-picker-menu')).toBeInTheDocument();
      });
      expect(screen.getByTestId('polymorphic-picker-item-sprk_matter')).toHaveTextContent('Matter');
      expect(screen.getByTestId('polymorphic-picker-item-sprk_project')).toHaveTextContent('Project');
      expect(screen.getByTestId('polymorphic-picker-item-contact')).toHaveTextContent('Contact');
    });

    it('renders zero menu items for an empty catalog and disables the trigger', () => {
      renderPicker({ catalog: [] });
      const trigger = screen.getByTestId('polymorphic-picker-trigger');
      expect(trigger).toBeDisabled();
    });
  });

  describe('lookup flow', () => {
    it('calls Xrm.Utility.lookupObjects with the selected entity type', async () => {
      const lookup = jest.fn().mockResolvedValue([]);
      installXrm(lookup);
      renderPicker();

      fireEvent.click(screen.getByTestId('polymorphic-picker-trigger'));
      await waitFor(() => {
        expect(screen.getByTestId('polymorphic-picker-item-sprk_matter')).toBeInTheDocument();
      });
      await act(async () => {
        fireEvent.click(screen.getByTestId('polymorphic-picker-item-sprk_matter'));
      });

      await waitFor(() => {
        expect(lookup).toHaveBeenCalledWith({
          entityTypes: ['sprk_matter'],
          defaultEntityType: 'sprk_matter',
          allowMultiSelect: false,
        });
      });
    });

    it('invokes onSelect with (entityType, cleaned recordId, recordName) when a record is picked', async () => {
      const onSelect = jest.fn();
      installXrm(async () => [{ id: '{ABC-DEF-GHI}', name: 'Smith v. Jones', entityType: 'sprk_matter' }]);
      renderPicker({ onSelect });

      fireEvent.click(screen.getByTestId('polymorphic-picker-trigger'));
      await waitFor(() => {
        expect(screen.getByTestId('polymorphic-picker-item-sprk_matter')).toBeInTheDocument();
      });
      await act(async () => {
        fireEvent.click(screen.getByTestId('polymorphic-picker-item-sprk_matter'));
      });

      await waitFor(() => {
        expect(onSelect).toHaveBeenCalledWith('sprk_matter', 'abc-def-ghi', 'Smith v. Jones');
      });
    });

    it('does not invoke onSelect when the user cancels (lookupObjects returns empty)', async () => {
      const onSelect = jest.fn();
      installXrm(async () => []);
      renderPicker({ onSelect });

      fireEvent.click(screen.getByTestId('polymorphic-picker-trigger'));
      await waitFor(() => {
        expect(screen.getByTestId('polymorphic-picker-item-sprk_project')).toBeInTheDocument();
      });
      await act(async () => {
        fireEvent.click(screen.getByTestId('polymorphic-picker-item-sprk_project'));
      });

      expect(onSelect).not.toHaveBeenCalled();
    });

    it('surfaces an inline error message and calls onError when Xrm is not available', async () => {
      const onSelect = jest.fn();
      const onError = jest.fn();
      // Deliberately do NOT install Xrm.
      renderPicker({ onSelect, onError });

      fireEvent.click(screen.getByTestId('polymorphic-picker-trigger'));
      await waitFor(() => {
        expect(screen.getByTestId('polymorphic-picker-item-sprk_matter')).toBeInTheDocument();
      });
      await act(async () => {
        fireEvent.click(screen.getByTestId('polymorphic-picker-item-sprk_matter'));
      });

      await waitFor(() => {
        expect(screen.getByTestId('polymorphic-picker-error')).toHaveTextContent(
          'Xrm.Utility.lookupObjects is not available.'
        );
      });
      expect(onError).toHaveBeenCalledWith('Xrm.Utility.lookupObjects is not available.');
      expect(onSelect).not.toHaveBeenCalled();
    });

    it('surfaces the error and calls onError when lookupObjects rejects', async () => {
      const onSelect = jest.fn();
      const onError = jest.fn();
      installXrm(async () => {
        throw new Error('Network failure');
      });
      renderPicker({ onSelect, onError });

      fireEvent.click(screen.getByTestId('polymorphic-picker-trigger'));
      await waitFor(() => {
        expect(screen.getByTestId('polymorphic-picker-item-sprk_matter')).toBeInTheDocument();
      });
      await act(async () => {
        fireEvent.click(screen.getByTestId('polymorphic-picker-item-sprk_matter'));
      });

      await waitFor(() => {
        expect(screen.getByTestId('polymorphic-picker-error')).toHaveTextContent('Network failure');
      });
      expect(onError).toHaveBeenCalledWith('Network failure');
      expect(onSelect).not.toHaveBeenCalled();
    });
  });

  describe('disabled / readOnly', () => {
    it('disables the trigger when disabled prop is true', () => {
      renderPicker({ disabled: true });
      expect(screen.getByTestId('polymorphic-picker-trigger')).toBeDisabled();
    });

    it('hides the trigger entirely when readOnly prop is true', () => {
      renderPicker({ readOnly: true });
      expect(screen.queryByTestId('polymorphic-picker-trigger')).not.toBeInTheDocument();
      // Title still renders in read-only mode.
      expect(screen.getByTestId('polymorphic-picker-title')).toBeInTheDocument();
    });

    it('readOnly takes precedence over disabled', () => {
      renderPicker({ readOnly: true, disabled: true });
      expect(screen.queryByTestId('polymorphic-picker-trigger')).not.toBeInTheDocument();
    });
  });
});
