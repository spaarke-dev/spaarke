/**
 * TrackingFieldTrio Family tool - unit tests (field-mapping-server-write-path-r1 task 074, spec FR-33).
 *
 * The Family icon is a third governance-toolbar affordance, opt-in through `onOpenFamily` exactly as the
 * person and email icons are opt-in through `onOpenGrantModal` / `onOpenEmailMembers`:
 *  - with the callback the icon renders and a click calls it once;
 *  - without it there is no icon and the toolbar is byte-identical to before (existing consumers unchanged);
 *  - it is NOT gated by `canGrantAccess` - the Family view is a read of records the caller may already read
 *    (the server filters it), so a person who cannot manage access still needs it;
 *  - it is not disabled by the control's read-only state either: opening a read-only list writes nothing.
 *
 * `src/client/pcf/TrackingFieldTrio/index.ts` is a PCF `StandardControl` class (it imports `./generated/ManifestTypes` and
 * PCF-only globals), so - exactly as `TrackingFieldTrio.emailMembers.test.tsx` does for the email action - the wiring
 * suite at the bottom mounts the REAL toolbar and the REAL `RecordFamilyModal` behind a thin harness that reproduces
 * index.ts's three decisions: the icon is offered only on a bound record, a click opens the modal for the form's entity
 * logical name and id, and the modal's fetch is the host's injected `authenticatedFetch` (no token).
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { TrackingFieldTrio } from '../TrackingFieldTrio';
import type { ITrackingFieldTrioProps, IAccessPermissionOption } from '../types';
import { RecordFamilyModal } from '../../RecordFamilyModal';

const renderWithTheme = (ui: React.ReactElement, theme = webLightTheme) =>
  render(<FluentProvider theme={theme}>{ui}</FluentProvider>);

const ACCESS_PERMISSION_OPTIONS: IAccessPermissionOption[] = [
  { value: 100000000, label: 'Standard', color: '#00B050' },
  { value: 100000001, label: 'Limited', color: '#FFC000' },
  { value: 100000002, label: 'Restricted', color: '#FF0000' },
];

function makeProps(overrides?: Partial<ITrackingFieldTrioProps>): ITrackingFieldTrioProps {
  return {
    monitor: false,
    highPriority: false,
    accessPermission: 100000000,
    accessPermissionOptions: ACCESS_PERMISSION_OPTIONS,
    monitorLabel: 'Monitor',
    highPriorityLabel: 'High Priority',
    accessPermissionLabel: 'Access Permission',
    onMonitorChange: jest.fn(),
    onHighPriorityChange: jest.fn(),
    onAccessPermissionChange: jest.fn(),
    ...overrides,
  };
}

describe('TrackingFieldTrio - Family tool (task 074, FR-33)', () => {
  describe('onOpenFamily supplied', () => {
    it('renders the Family icon', () => {
      renderWithTheme(<TrackingFieldTrio {...makeProps({ onOpenFamily: jest.fn() })} />);

      expect(screen.getByRole('button', { name: 'Family' })).toBeInTheDocument();
    });

    it('calls onOpenFamily exactly once per click, with no arguments', () => {
      const onOpenFamily = jest.fn();
      renderWithTheme(<TrackingFieldTrio {...makeProps({ onOpenFamily })} />);

      fireEvent.click(screen.getByRole('button', { name: 'Family' }));

      expect(onOpenFamily).toHaveBeenCalledTimes(1);
      expect(onOpenFamily).toHaveBeenCalledWith();
    });

    it('renders the toolbar with Family alone (no grant or email callback needed)', () => {
      renderWithTheme(<TrackingFieldTrio {...makeProps({ onOpenFamily: jest.fn() })} />);

      expect(screen.getByRole('button', { name: 'Family' })).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Grant access' })).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Email members' })).not.toBeInTheDocument();
    });

    it('sits beside the person and email icons without changing them', () => {
      const onOpenGrantModal = jest.fn();
      const onOpenEmailMembers = jest.fn();
      const onOpenFamily = jest.fn();
      renderWithTheme(
        <TrackingFieldTrio {...makeProps({ onOpenGrantModal, onOpenEmailMembers, onOpenFamily, canGrantAccess: true })} />
      );

      fireEvent.click(screen.getByRole('button', { name: 'Family' }));
      expect(onOpenFamily).toHaveBeenCalledTimes(1);
      expect(onOpenGrantModal).not.toHaveBeenCalled();
      expect(onOpenEmailMembers).not.toHaveBeenCalled();

      fireEvent.click(screen.getByRole('button', { name: 'Grant access' }));
      fireEvent.click(screen.getByRole('button', { name: 'Email members' }));
      expect(onOpenGrantModal).toHaveBeenCalledTimes(1);
      expect(onOpenEmailMembers).toHaveBeenCalledTimes(1);
      expect(onOpenFamily).toHaveBeenCalledTimes(1);
    });

    it('is enabled when canGrantAccess is false or omitted (independent of the grant gate)', () => {
      const onOpenFamily = jest.fn();
      const { unmount } = renderWithTheme(
        <TrackingFieldTrio {...makeProps({ onOpenFamily, onOpenGrantModal: jest.fn(), canGrantAccess: false })} />
      );
      expect(screen.getByRole('button', { name: 'Family' })).not.toBeDisabled();
      fireEvent.click(screen.getByRole('button', { name: 'Family' }));
      expect(onOpenFamily).toHaveBeenCalledTimes(1);
      unmount();

      renderWithTheme(<TrackingFieldTrio {...makeProps({ onOpenFamily, onOpenGrantModal: jest.fn() })} />);
      expect(screen.getByRole('button', { name: 'Family' })).not.toBeDisabled();
    });

    it('stays enabled on a read-only control (opening the Family writes nothing)', () => {
      const onOpenFamily = jest.fn();
      renderWithTheme(<TrackingFieldTrio {...makeProps({ onOpenFamily, disabled: true })} />);

      const button = screen.getByRole('button', { name: 'Family' });
      expect(button).not.toBeDisabled();
      fireEvent.click(button);
      expect(onOpenFamily).toHaveBeenCalledTimes(1);
    });

    it('renders with the title header row too', () => {
      const onOpenFamily = jest.fn();
      renderWithTheme(<TrackingFieldTrio {...makeProps({ onOpenFamily, title: 'Tracking' })} />);

      expect(screen.getByText('Tracking')).toBeInTheDocument();
      fireEvent.click(screen.getByRole('button', { name: 'Family' }));
      expect(onOpenFamily).toHaveBeenCalledTimes(1);
    });
  });

  describe('onOpenFamily omitted (backward compatibility, FR-33)', () => {
    it('renders no Family icon', () => {
      renderWithTheme(<TrackingFieldTrio {...makeProps()} />);

      expect(screen.queryByRole('button', { name: 'Family' })).not.toBeInTheDocument();
    });

    it('renders exactly the same buttons as before when onOpenFamily is explicitly undefined', () => {
      const { container } = renderWithTheme(
        <TrackingFieldTrio
          {...makeProps({ onOpenGrantModal: jest.fn(), onOpenEmailMembers: jest.fn(), onOpenFamily: undefined })}
        />
      );

      expect(screen.queryByRole('button', { name: 'Family' })).not.toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Grant access' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Email members' })).toBeInTheDocument();
      // person + email + the access-permission pill's menu button; nothing added.
      expect(container.querySelectorAll('button')).toHaveLength(3);
    });

    it('draws no toolbar at all when no toolbar callback is supplied', () => {
      renderWithTheme(<TrackingFieldTrio {...makeProps()} />);

      expect(screen.queryByRole('button', { name: 'Grant access' })).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Email members' })).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Family' })).not.toBeInTheDocument();
      expect(screen.getAllByRole('switch')).toHaveLength(2);
    });
  });

  describe('adr-021-dark-mode and console cleanliness', () => {
    it('renders the Family icon under webDarkTheme with zero console errors or warnings', () => {
      const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});

      renderWithTheme(
        <TrackingFieldTrio
          {...makeProps({ onOpenGrantModal: jest.fn(), onOpenEmailMembers: jest.fn(), onOpenFamily: jest.fn() })}
        />,
        webDarkTheme
      );

      expect(screen.getByRole('button', { name: 'Family' })).toBeInTheDocument();
      expect(errorSpy).not.toHaveBeenCalled();
      expect(warnSpy).not.toHaveBeenCalled();

      errorSpy.mockRestore();
      warnSpy.mockRestore();
    });
  });

  describe('PCF wiring - the harness reproduces index.ts (task 074 criterion 3)', () => {
    const RECORD_ID = '{ABCDEF01-0000-0000-0000-000000000001}';
    const CANONICAL_ID = 'abcdef01-0000-0000-0000-000000000001';

    /** Mirrors index.ts: `onOpenFamily` only with a bound record; the modal gets the entity + id and the host fetch. */
    const Host: React.FC<{
      entity: string;
      recordId: string | null;
      authenticatedFetch: (url: string, init?: RequestInit) => Promise<Response>;
    }> = ({ entity, recordId, authenticatedFetch }) => {
      const [open, setOpen] = React.useState(false);
      return (
        <>
          <TrackingFieldTrio {...makeProps({ onOpenFamily: recordId ? () => setOpen(true) : undefined })} />
          {recordId ? (
            <RecordFamilyModal
              open={open}
              onClose={() => setOpen(false)}
              table={entity}
              recordId={recordId}
              authenticatedFetch={authenticatedFetch}
            />
          ) : null}
        </>
      );
    };

    const familyBody = {
      anchor: { id: CANONICAL_ID, table: 'sprk_matter' },
      root: null,
      truncated: false,
      continuation: null,
      members: [
        {
          id: CANONICAL_ID,
          table: 'sprk_matter',
          name: 'Acme Acquisition',
          recordNumber: 'MAT-0001',
          recordType: null,
          status: 'Active',
          modifiedOn: null,
          kind: 'Filing',
          edgeKind: 'FilingCore',
          parentId: null,
          depth: 0,
        },
      ],
    };
    const okResponse = (body: unknown) => ({ ok: true, status: 200, json: async () => body }) as unknown as Response;

    it.each(['sprk_matter', 'sprk_project', 'sprk_workassignment', 'sprk_event'])(
      'opens the Family for %s and reads that record only after the click',
      async entity => {
        const authenticatedFetch = jest.fn().mockResolvedValue(okResponse(familyBody));
        renderWithTheme(<Host entity={entity} recordId={RECORD_ID} authenticatedFetch={authenticatedFetch} />);

        // Nothing is read while the modal is closed.
        expect(authenticatedFetch).not.toHaveBeenCalled();

        fireEvent.click(screen.getByRole('button', { name: 'Family' }));

        await waitFor(() => expect(authenticatedFetch).toHaveBeenCalledTimes(1));
        const url = authenticatedFetch.mock.calls[0][0] as string;
        expect(url).toContain(`/api/v1/records/${entity}/${CANONICAL_ID}/family`);
        // No bearer token is ever handed over: the modal only receives the fetch.
        expect(authenticatedFetch.mock.calls[0][1]?.headers).toBeUndefined();
        expect(await screen.findByText('Acme Acquisition')).toBeInTheDocument();
      }
    );

    it('shows the modal error state, not a crash, while the family endpoint does not answer yet', async () => {
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});
      const authenticatedFetch = jest.fn().mockRejectedValue(new Error('404'));
      renderWithTheme(<Host entity="sprk_matter" recordId={RECORD_ID} authenticatedFetch={authenticatedFetch} />);

      fireEvent.click(screen.getByRole('button', { name: 'Family' }));

      expect(await screen.findByText(/family could not be loaded/i)).toBeInTheDocument();
      warnSpy.mockRestore();
    });

    it('offers no Family icon and mounts no modal when the form has no bound record', () => {
      const authenticatedFetch = jest.fn();
      renderWithTheme(<Host entity="sprk_matter" recordId={null} authenticatedFetch={authenticatedFetch} />);

      expect(screen.queryByRole('button', { name: 'Family' })).not.toBeInTheDocument();
      expect(authenticatedFetch).not.toHaveBeenCalled();
    });
  });
});
