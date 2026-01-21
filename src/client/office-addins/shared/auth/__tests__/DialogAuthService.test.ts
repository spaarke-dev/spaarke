/**
 * Unit tests for DialogAuthService
 *
 * Tests the Dialog API fallback authentication for Office Add-ins
 * when NAA is not supported.
 */

import {
  DialogAuthServiceImpl,
  DialogAuthError,
  DialogAuthErrorCode,
  DialogMessageType,
  type DialogAuthCompleteMessage,
  type DialogAuthErrorMessage,
} from '../DialogAuthService';

// Mock Office.js context setup
function createMockDialog() {
  return {
    close: jest.fn(),
    addEventHandler: jest.fn(),
    messageChild: jest.fn(),
  };
}

describe('DialogAuthService', () => {
  let authService: DialogAuthServiceImpl;
  let mockDialog: ReturnType<typeof createMockDialog>;

  beforeEach(() => {
    // Reset singleton instance
    DialogAuthServiceImpl.resetInstance();

    // Create fresh mock dialog
    mockDialog = createMockDialog();

    // Reset Office.context.ui mock
    global.Office.context.ui.displayDialogAsync = jest.fn(
      (url: string, options: unknown, callback: (result: Office.AsyncResult<Office.Dialog>) => void) => {
        const result = {
          status: Office.AsyncResultStatus.Succeeded,
          value: mockDialog as unknown as Office.Dialog,
          error: null,
        } as Office.AsyncResult<Office.Dialog>;
        callback(result);
      }
    );

    // Get fresh instance
    authService = DialogAuthServiceImpl.getInstance();
  });

  afterEach(() => {
    jest.clearAllMocks();
    DialogAuthServiceImpl.resetInstance();
  });

  describe('getInstance', () => {
    it('should return the same instance on multiple calls', () => {
      const instance1 = DialogAuthServiceImpl.getInstance();
      const instance2 = DialogAuthServiceImpl.getInstance();

      expect(instance1).toBe(instance2);
    });

    it('should return new instance after reset', () => {
      const instance1 = DialogAuthServiceImpl.getInstance();
      DialogAuthServiceImpl.resetInstance();
      const instance2 = DialogAuthServiceImpl.getInstance();

      expect(instance1).not.toBe(instance2);
    });
  });

  describe('initialize', () => {
    it('should initialize successfully', async () => {
      await authService.initialize();

      expect(authService.isInitialized()).toBe(true);
    });

    it('should not reinitialize if already initialized', async () => {
      await authService.initialize();
      await authService.initialize();

      // Should not throw, just warn
      expect(authService.isInitialized()).toBe(true);
    });

    it('should accept custom config', async () => {
      await authService.initialize({
        clientId: 'custom-client-id',
        tenantId: 'custom-tenant-id',
      });

      expect(authService.isInitialized()).toBe(true);
      expect(authService.getDialogUrl()).toContain('/dialog.html');
    });
  });

  describe('getDialogUrl', () => {
    it('should return default dialog URL', async () => {
      await authService.initialize();

      const url = authService.getDialogUrl();

      expect(url).toBe('https://localhost:3000/dialog.html');
    });

    it('should return configured fallbackRedirectUri if set', async () => {
      await authService.initialize({
        fallbackRedirectUri: 'https://custom.com/auth-dialog.html',
      });

      const url = authService.getDialogUrl();

      expect(url).toBe('https://custom.com/auth-dialog.html');
    });
  });

  describe('authenticate', () => {
    beforeEach(async () => {
      await authService.initialize();
    });

    it('should throw if not initialized', async () => {
      DialogAuthServiceImpl.resetInstance();
      const uninitializedService = DialogAuthServiceImpl.getInstance();

      await expect(uninitializedService.authenticate()).rejects.toThrow(DialogAuthError);
    });

    it('should open dialog with correct parameters', async () => {
      // Start authentication (it will wait for dialog response)
      const authPromise = authService.authenticate({
        width: 50,
        height: 70,
      });

      // Verify dialog was opened
      expect(global.Office.context.ui.displayDialogAsync).toHaveBeenCalled();

      const callArgs = (global.Office.context.ui.displayDialogAsync as jest.Mock).mock.calls[0];
      const dialogOptions = callArgs[1];

      expect(dialogOptions.width).toBe(50);
      expect(dialogOptions.height).toBe(70);
      expect(dialogOptions.displayInIframe).toBe(false);

      // Simulate dialog closure to end the test
      const eventHandler = mockDialog.addEventHandler.mock.calls.find(
        (call: unknown[]) => call[0] === Office.EventType.DialogEventReceived
      );
      if (eventHandler) {
        eventHandler[1]({ error: 12002 }); // Dialog closed
      }

      await expect(authPromise).rejects.toThrow(DialogAuthError);
    });

    it('should register event handlers on dialog', async () => {
      const authPromise = authService.authenticate();

      // Verify event handlers were registered
      expect(mockDialog.addEventHandler).toHaveBeenCalledWith(
        Office.EventType.DialogMessageReceived,
        expect.any(Function)
      );
      expect(mockDialog.addEventHandler).toHaveBeenCalledWith(
        Office.EventType.DialogEventReceived,
        expect.any(Function)
      );

      // Cleanup: simulate dialog close
      const eventHandler = mockDialog.addEventHandler.mock.calls.find(
        (call: unknown[]) => call[0] === Office.EventType.DialogEventReceived
      );
      if (eventHandler) {
        eventHandler[1]({ error: 12002 });
      }

      await expect(authPromise).rejects.toThrow();
    });

    it('should handle AUTH_COMPLETE message correctly', async () => {
      const authPromise = authService.authenticate();

      // Get the message handler
      const messageHandler = mockDialog.addEventHandler.mock.calls.find(
        (call: unknown[]) => call[0] === Office.EventType.DialogMessageReceived
      );

      const authCompleteMessage: DialogAuthCompleteMessage = {
        type: DialogMessageType.AUTH_COMPLETE,
        accessToken: 'test-access-token',
        expiresOn: new Date(Date.now() + 3600 * 1000).toISOString(),
        scopes: ['api://test/.default'],
        account: {
          homeAccountId: 'home-id',
          environment: 'login.microsoftonline.com',
          tenantId: 'tenant-id',
          username: 'test@example.com',
          localAccountId: 'local-id',
          name: 'Test User',
        },
      };

      // Simulate message from dialog
      messageHandler[1]({ message: JSON.stringify(authCompleteMessage), origin: undefined });

      const result = await authPromise;

      expect(result.accessToken).toBe('test-access-token');
      expect(result.scopes).toEqual(['api://test/.default']);
      expect(result.account.username).toBe('test@example.com');
    });

    it('should handle AUTH_ERROR message correctly', async () => {
      const authPromise = authService.authenticate();

      // Get the message handler
      const messageHandler = mockDialog.addEventHandler.mock.calls.find(
        (call: unknown[]) => call[0] === Office.EventType.DialogMessageReceived
      );

      const authErrorMessage: DialogAuthErrorMessage = {
        type: DialogMessageType.AUTH_ERROR,
        errorCode: 'MSAL_ERROR',
        errorMessage: 'Authentication failed in MSAL',
      };

      // Simulate error message from dialog
      messageHandler[1]({ message: JSON.stringify(authErrorMessage), origin: undefined });

      await expect(authPromise).rejects.toThrow(DialogAuthError);
      await expect(authPromise).rejects.toMatchObject({
        code: DialogAuthErrorCode.MSAL_ERROR,
      });
    });

    it('should handle CANCELLED message correctly', async () => {
      const authPromise = authService.authenticate();

      // Get the message handler
      const messageHandler = mockDialog.addEventHandler.mock.calls.find(
        (call: unknown[]) => call[0] === Office.EventType.DialogMessageReceived
      );

      // Simulate cancelled message from dialog
      messageHandler[1]({
        message: JSON.stringify({ type: DialogMessageType.CANCELLED }),
        origin: undefined,
      });

      await expect(authPromise).rejects.toThrow(DialogAuthError);
      await expect(authPromise).rejects.toMatchObject({
        code: DialogAuthErrorCode.USER_CANCELLED,
      });
    });

    it('should handle dialog close event', async () => {
      const authPromise = authService.authenticate();

      // Get the event handler
      const eventHandler = mockDialog.addEventHandler.mock.calls.find(
        (call: unknown[]) => call[0] === Office.EventType.DialogEventReceived
      );

      // Simulate dialog close (event type 12002)
      eventHandler[1]({ error: 12002 });

      await expect(authPromise).rejects.toThrow(DialogAuthError);
      await expect(authPromise).rejects.toMatchObject({
        code: DialogAuthErrorCode.DIALOG_CLOSED,
      });
    });

    it('should handle dialog open failure', async () => {
      // Override mock to simulate failure
      global.Office.context.ui.displayDialogAsync = jest.fn(
        (url: string, options: unknown, callback: (result: Office.AsyncResult<Office.Dialog>) => void) => {
          callback({
            status: Office.AsyncResultStatus.Failed,
            error: { code: 12006, message: 'Dialog already opened' } as Office.Error,
            value: undefined,
          } as unknown as Office.AsyncResult<Office.Dialog>);
        }
      );

      await expect(authService.authenticate()).rejects.toThrow(DialogAuthError);
      await expect(authService.authenticate()).rejects.toMatchObject({
        code: DialogAuthErrorCode.DIALOG_OPEN_FAILED,
      });
    });

    it('should handle invalid JSON message', async () => {
      const authPromise = authService.authenticate();

      // Get the message handler
      const messageHandler = mockDialog.addEventHandler.mock.calls.find(
        (call: unknown[]) => call[0] === Office.EventType.DialogMessageReceived
      );

      // Simulate invalid JSON message
      messageHandler[1]({ message: 'not valid json', origin: undefined });

      await expect(authPromise).rejects.toThrow(DialogAuthError);
      await expect(authPromise).rejects.toMatchObject({
        code: DialogAuthErrorCode.INVALID_MESSAGE,
      });
    });

    it('should timeout if authentication takes too long', async () => {
      jest.useFakeTimers();

      const authPromise = authService.authenticate({ timeout: 5000 });

      // Fast-forward time past the timeout
      jest.advanceTimersByTime(6000);

      await expect(authPromise).rejects.toThrow(DialogAuthError);
      await expect(authPromise).rejects.toMatchObject({
        code: DialogAuthErrorCode.AUTH_TIMEOUT,
      });

      jest.useRealTimers();
    });
  });

  describe('cancelAuthentication', () => {
    beforeEach(async () => {
      await authService.initialize();
    });

    it('should cancel ongoing authentication', async () => {
      const authPromise = authService.authenticate();

      // Cancel the authentication
      authService.cancelAuthentication();

      await expect(authPromise).rejects.toThrow(DialogAuthError);
      await expect(authPromise).rejects.toMatchObject({
        code: DialogAuthErrorCode.USER_CANCELLED,
      });
    });

    it('should close the dialog when cancelling', async () => {
      authService.authenticate();

      authService.cancelAuthentication();

      expect(mockDialog.close).toHaveBeenCalled();
    });

    it('should handle cancel when no authentication is in progress', () => {
      // Should not throw
      expect(() => authService.cancelAuthentication()).not.toThrow();
    });
  });
});

describe('DialogAuthError', () => {
  it('should create error with code and message', () => {
    const error = new DialogAuthError(
      DialogAuthErrorCode.DIALOG_CLOSED,
      'Dialog was closed'
    );

    expect(error.code).toBe(DialogAuthErrorCode.DIALOG_CLOSED);
    expect(error.userMessage).toBe('Dialog was closed');
    expect(error.message).toBe('Dialog was closed');
    expect(error.name).toBe('DialogAuthError');
  });

  it('should include original error when provided', () => {
    const originalError = new Error('Original error');
    const error = new DialogAuthError(
      DialogAuthErrorCode.UNKNOWN,
      'Wrapped error',
      originalError
    );

    expect(error.originalError).toBe(originalError);
  });
});
