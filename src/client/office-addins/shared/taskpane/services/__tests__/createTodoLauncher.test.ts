/**
 * Unit tests for createTodoLauncher.
 *
 * Covers smart-todo-decoupling-r3 FR-27 / task 070 acceptance:
 *   - buildCreateTodoLaunchUrl emits ?action=createTodo, ?regardingType, ?regardingId, ?regardingName
 *   - GUID is lowercased + braces stripped
 *   - URLSearchParams encoding handles special chars in recordName
 *   - Existing query string on the base URL is preserved
 *   - Throws when codePageBaseUrl or communicationId is missing
 *   - openCreateTodoWizard calls windowOpen with the built URL + standard features
 *   - openCreateTodoWizard returns null when the popup is blocked
 */

import {
  CREATE_TODO_ACTION,
  CREATE_TODO_LAUNCH_PARAMS,
  CREATE_TODO_WINDOW_FEATURES,
  buildCreateTodoLaunchUrl,
  openCreateTodoWizard,
} from '../createTodoLauncher';

describe('createTodoLauncher', () => {
  describe('buildCreateTodoLaunchUrl', () => {
    const baseInput = {
      codePageBaseUrl: 'https://contoso.crm.dynamics.com/main.aspx?pagetype=webresource&webresourceName=sprk_smarttodo',
      communicationId: 'AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE',
      recordName: 'Q4 planning kickoff',
    };

    it('appends the four launch params with the canonical key names', () => {
      const url = new URL(buildCreateTodoLaunchUrl(baseInput));

      expect(url.searchParams.get(CREATE_TODO_LAUNCH_PARAMS.ACTION)).toBe(CREATE_TODO_ACTION);
      expect(url.searchParams.get(CREATE_TODO_LAUNCH_PARAMS.REGARDING_TYPE)).toBe('sprk_communication');
      expect(url.searchParams.get(CREATE_TODO_LAUNCH_PARAMS.REGARDING_ID)).toBe(
        'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee', // lowercased
      );
      expect(url.searchParams.get(CREATE_TODO_LAUNCH_PARAMS.REGARDING_NAME)).toBe('Q4 planning kickoff');
    });

    it('strips braces from a Dataverse-formatted GUID', () => {
      const url = new URL(
        buildCreateTodoLaunchUrl({ ...baseInput, communicationId: '{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}' }),
      );
      expect(url.searchParams.get(CREATE_TODO_LAUNCH_PARAMS.REGARDING_ID)).toBe(
        'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      );
    });

    it('encodes special chars in recordName via URLSearchParams', () => {
      const url = buildCreateTodoLaunchUrl({
        ...baseInput,
        recordName: 'Re: Q&A — pending #urgent',
      });

      // The encoded form must round-trip back through URLSearchParams.
      const parsed = new URL(url).searchParams.get(CREATE_TODO_LAUNCH_PARAMS.REGARDING_NAME);
      expect(parsed).toBe('Re: Q&A — pending #urgent');
    });

    it('preserves existing query-string params on the base URL', () => {
      const url = new URL(
        buildCreateTodoLaunchUrl({
          ...baseInput,
          codePageBaseUrl: 'https://contoso.crm.dynamics.com/main.aspx?pagetype=webresource&webresourceName=sprk_smarttodo',
        }),
      );

      // Existing params from the base URL survive.
      expect(url.searchParams.get('pagetype')).toBe('webresource');
      expect(url.searchParams.get('webresourceName')).toBe('sprk_smarttodo');
      // New launch params are also present.
      expect(url.searchParams.get(CREATE_TODO_LAUNCH_PARAMS.ACTION)).toBe(CREATE_TODO_ACTION);
    });

    it('supports overriding entityType (for the task 040 parent-form reuse path)', () => {
      const url = new URL(
        buildCreateTodoLaunchUrl({ ...baseInput, entityType: 'sprk_matter' }),
      );
      expect(url.searchParams.get(CREATE_TODO_LAUNCH_PARAMS.REGARDING_TYPE)).toBe('sprk_matter');
    });

    it('throws when codePageBaseUrl is empty', () => {
      expect(() =>
        buildCreateTodoLaunchUrl({ ...baseInput, codePageBaseUrl: '' }),
      ).toThrow(/codePageBaseUrl is required/);
    });

    it('throws when codePageBaseUrl is whitespace', () => {
      expect(() =>
        buildCreateTodoLaunchUrl({ ...baseInput, codePageBaseUrl: '   ' }),
      ).toThrow(/codePageBaseUrl is required/);
    });

    it('throws when communicationId is empty', () => {
      expect(() =>
        buildCreateTodoLaunchUrl({ ...baseInput, communicationId: '' }),
      ).toThrow(/communicationId is required/);
    });

    it('tolerates an empty recordName (renders a generic AssociateToStep card)', () => {
      const url = new URL(buildCreateTodoLaunchUrl({ ...baseInput, recordName: '' }));
      expect(url.searchParams.get(CREATE_TODO_LAUNCH_PARAMS.REGARDING_NAME)).toBe('');
    });
  });

  describe('openCreateTodoWizard', () => {
    const baseInput = {
      codePageBaseUrl: 'https://contoso.crm.dynamics.com/main.aspx',
      communicationId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      recordName: 'Email subject',
    };

    it('calls windowOpen with the built URL, _blank target, and standard features', () => {
      const fakeWindow = { closed: false } as unknown as Window;
      const windowOpen = jest.fn().mockReturnValue(fakeWindow);

      const result = openCreateTodoWizard(baseInput, windowOpen);

      expect(windowOpen).toHaveBeenCalledTimes(1);
      const [openedUrl, target, features] = windowOpen.mock.calls[0]!;
      expect(target).toBe('_blank');
      expect(features).toBe(CREATE_TODO_WINDOW_FEATURES);
      expect(openedUrl).toContain('action=createTodo');
      expect(openedUrl).toContain('regardingType=sprk_communication');
      expect(result).toBe(fakeWindow);
    });

    it('returns null when the browser blocks the popup', () => {
      const windowOpen = jest.fn().mockReturnValue(null);
      const result = openCreateTodoWizard(baseInput, windowOpen);
      expect(result).toBeNull();
    });
  });
});
