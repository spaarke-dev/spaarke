/**
 * Dialog API Authentication Service for Office Add-ins
 *
 * Provides fallback authentication for older Office clients that don't support
 * NAA (Nested App Authentication). Uses Office.context.ui.displayDialogAsync
 * to open an authentication popup that communicates back via messageParent.
 *
 * Key features:
 * - Opens dialog window for MSAL redirect flow
 * - Handles OAuth callback in dialog
 * - Communicates token back to task pane via Office.context.ui.messageParent
 * - Handles dialog close/cancel events gracefully
 * - Timeout handling for abandoned auth flows
 *
 * Per auth.md constraints:
 * - MUST use sessionStorage for tokens
 * - MUST try silent acquisition before interactive
 *
 * @see https://learn.microsoft.com/en-us/office/dev/add-ins/develop/dialog-api-in-office-add-ins
 */

import type { AccountInfo } from '@azure/msal-browser';
import { type NaaAuthConfig, DEFAULT_AUTH_CONFIG, getBffApiScopes } from './authConfig';

/**
 * Message types for dialog communication
 */
export enum DialogMessageType {
  /** Authentication completed successfully */
  AUTH_COMPLETE = 'auth-complete',
  /** Authentication failed with error */
  AUTH_ERROR = 'auth-error',
  /** Dialog is ready and waiting */
  READY = 'ready',
  /** User cancelled authentication */
  CANCELLED = 'cancelled',
}

/**
 * Message format for auth completion
 */
export interface DialogAuthCompleteMessage {
  type: DialogMessageType.AUTH_COMPLETE;
  accessToken: string;
  expiresOn: string;
  scopes: string[];
  account: {
    homeAccountId: string;
    environment: string;
    tenantId: string;
    username: string;
    localAccountId: string;
    name?: string;
  };
}

/**
 * Message format for auth error
 */
export interface DialogAuthErrorMessage {
  type: DialogMessageType.AUTH_ERROR;
  errorCode: string;
  errorMessage: string;
}

/**
 * Message format for ready signal
 */
export interface DialogReadyMessage {
  type: DialogMessageType.READY;
}

/**
 * Message format for cancellation
 */
export interface DialogCancelledMessage {
  type: DialogMessageType.CANCELLED;
}

/**
 * Union type for all dialog messages
 */
export type DialogMessage =
  | DialogAuthCompleteMessage
  | DialogAuthErrorMessage
  | DialogReadyMessage
  | DialogCancelledMessage;

/**
 * Dialog authentication error codes
 */
export enum DialogAuthErrorCode {
  /** Dialog was closed by user */
  DIALOG_CLOSED = 'DIALOG_001',
  /** Dialog failed to open */
  DIALOG_OPEN_FAILED = 'DIALOG_002',
  /** Authentication timed out */
  AUTH_TIMEOUT = 'DIALOG_003',
  /** Invalid message from dialog */
  INVALID_MESSAGE = 'DIALOG_004',
  /** Dialog communication error */
  COMMUNICATION_ERROR = 'DIALOG_005',
  /** User cancelled authentication */
  USER_CANCELLED = 'DIALOG_006',
  /** MSAL error in dialog */
  MSAL_ERROR = 'DIALOG_007',
  /** Unknown error */
  UNKNOWN = 'DIALOG_999',
}

/**
 * Dialog authentication error
 */
export class DialogAuthError extends Error {
  public readonly code: DialogAuthErrorCode;
  public readonly userMessage: string;
  public readonly originalError?: Error;

  constructor(code: DialogAuthErrorCode, userMessage: string, originalError?: Error) {
    super(userMessage);
    this.name = 'DialogAuthError';
    this.code = code;
    this.userMessage = userMessage;
    this.originalError = originalError;
  }
}

/**
 * Token result from dialog authentication
 */
export interface DialogTokenResult {
  /** The access token */
  accessToken: string;
  /** Token expiration timestamp */
  expiresOn: Date;
  /** Scopes granted by the token */
  scopes: string[];
  /** Account information */
  account: AccountInfo;
}

/**
 * Dialog authentication service options
 */
export interface DialogAuthOptions {
  /** Dialog width in percentage (default: 40) */
  width?: number;
  /** Dialog height in percentage (default: 60) */
  height?: number;
  /** Authentication timeout in milliseconds (default: 120000 = 2 minutes) */
  timeout?: number;
  /** Display mode (default: false = popup) */
  displayInIframe?: boolean;
  /** Prompt type for MSAL */
  promptHint?: string;
}

/**
 * Default dialog options
 */
const DEFAULT_DIALOG_OPTIONS: Required<DialogAuthOptions> = {
  width: 40,
  height: 60,
  timeout: 120000, // 2 minutes
  displayInIframe: false,
  promptHint: '',
};

/**
 * Dialog Authentication Service interface
 */
export interface IDialogAuthService {
  /**
   * Initialize the service
   */
  initialize(config?: Partial<NaaAuthConfig>): Promise<void>;

  /**
   * Check if the service is initialized
   */
  isInitialized(): boolean;

  /**
   * Get the dialog URL for authentication
   */
  getDialogUrl(): string;

  /**
   * Authenticate using the dialog API
   * @param options - Optional dialog options
   * @returns Token result on success
   * @throws DialogAuthError on failure
   */
  authenticate(options?: DialogAuthOptions): Promise<DialogTokenResult>;

  /**
   * Cancel any ongoing authentication
   */
  cancelAuthentication(): void;
}

/**
 * Dialog Authentication Service implementation
 */
class DialogAuthServiceImpl implements IDialogAuthService {
  private static instance: DialogAuthServiceImpl | null = null;

  private config: NaaAuthConfig = DEFAULT_AUTH_CONFIG;
  private initialized: boolean = false;
  private activeDialog: Office.Dialog | null = null;
  private pendingResolve: ((result: DialogTokenResult) => void) | null = null;
  private pendingReject: ((error: DialogAuthError) => void) | null = null;
  private authTimeout: ReturnType<typeof setTimeout> | null = null;

  private constructor() {
    // Private constructor for singleton pattern
  }

  /**
   * Get the singleton instance
   */
  public static getInstance(): DialogAuthServiceImpl {
    if (!DialogAuthServiceImpl.instance) {
      DialogAuthServiceImpl.instance = new DialogAuthServiceImpl();
    }
    return DialogAuthServiceImpl.instance;
  }

  /**
   * Reset the singleton instance (for testing)
   */
  public static resetInstance(): void {
    if (DialogAuthServiceImpl.instance) {
      DialogAuthServiceImpl.instance.cancelAuthentication();
    }
    DialogAuthServiceImpl.instance = null;
  }

  public async initialize(config?: Partial<NaaAuthConfig>): Promise<void> {
    if (this.initialized) {
      console.warn('[DialogAuthService] Already initialized');
      return;
    }

    this.config = { ...DEFAULT_AUTH_CONFIG, ...config };
    this.initialized = true;
    console.info('[DialogAuthService] Initialized');
  }

  public isInitialized(): boolean {
    return this.initialized;
  }

  public getDialogUrl(): string {
    // The dialog page should be in the same domain as the add-in
    // Use fallbackRedirectUri from config or construct from window.location
    if (this.config.fallbackRedirectUri) {
      return this.config.fallbackRedirectUri;
    }
    return `${window.location.origin}/dialog.html`;
  }

  public async authenticate(options?: DialogAuthOptions): Promise<DialogTokenResult> {
    if (!this.initialized) {
      throw new DialogAuthError(
        DialogAuthErrorCode.DIALOG_OPEN_FAILED,
        'Dialog auth service is not initialized. Call initialize() first.'
      );
    }

    // Cancel any existing authentication
    this.cancelAuthentication();

    const mergedOptions = { ...DEFAULT_DIALOG_OPTIONS, ...options };

    return new Promise<DialogTokenResult>((resolve, reject) => {
      this.pendingResolve = resolve;
      this.pendingReject = reject;

      // Set up timeout
      this.authTimeout = setTimeout(() => {
        this.handleAuthTimeout();
      }, mergedOptions.timeout);

      // Build dialog URL with parameters
      const dialogUrl = this.buildDialogUrl(mergedOptions.promptHint);

      // Open the dialog
      Office.context.ui.displayDialogAsync(
        dialogUrl,
        {
          width: mergedOptions.width,
          height: mergedOptions.height,
          displayInIframe: mergedOptions.displayInIframe,
          promptBeforeOpen: false,
        },
        (asyncResult) => {
          if (asyncResult.status === Office.AsyncResultStatus.Failed) {
            this.handleDialogOpenError(asyncResult.error);
            return;
          }

          this.activeDialog = asyncResult.value;
          this.setupDialogEventHandlers();
        }
      );
    });
  }

  public cancelAuthentication(): void {
    this.cleanup();
    if (this.pendingReject) {
      this.pendingReject(
        new DialogAuthError(
          DialogAuthErrorCode.USER_CANCELLED,
          'Authentication was cancelled.'
        )
      );
    }
    this.clearPendingPromise();
  }

  // ============================================
  // Private methods
  // ============================================

  private buildDialogUrl(promptHint?: string): string {
    const baseUrl = this.getDialogUrl();
    const url = new URL(baseUrl);

    // Add parameters for the dialog to use
    url.searchParams.set('clientId', this.config.clientId);
    url.searchParams.set('tenantId', this.config.tenantId);
    url.searchParams.set('bffApiClientId', this.config.bffApiClientId);

    if (promptHint) {
      url.searchParams.set('prompt', promptHint);
    }

    return url.toString();
  }

  private setupDialogEventHandlers(): void {
    if (!this.activeDialog) return;

    // Handle messages from dialog
    this.activeDialog.addEventHandler(
      Office.EventType.DialogMessageReceived,
      (arg: { message: string; origin: string | undefined } | { error: number }) => {
        if ('error' in arg) {
          // This is an error event
          this.handleDialogError(arg.error);
        } else {
          // This is a message event
          this.handleDialogMessage(arg.message);
        }
      }
    );

    // Handle dialog close event
    this.activeDialog.addEventHandler(
      Office.EventType.DialogEventReceived,
      (arg: { error: number }) => {
        this.handleDialogEvent(arg.error);
      }
    );
  }

  private handleDialogMessage(message: string): void {
    try {
      const parsed = JSON.parse(message) as DialogMessage;

      switch (parsed.type) {
        case DialogMessageType.AUTH_COMPLETE:
          this.handleAuthComplete(parsed);
          break;

        case DialogMessageType.AUTH_ERROR:
          this.handleAuthError(parsed);
          break;

        case DialogMessageType.CANCELLED:
          this.handleCancelled();
          break;

        case DialogMessageType.READY:
          console.info('[DialogAuthService] Dialog is ready');
          break;

        default:
          console.warn('[DialogAuthService] Unknown message type:', parsed);
      }
    } catch (error) {
      console.error('[DialogAuthService] Failed to parse dialog message:', error);
      this.rejectWithError(
        new DialogAuthError(
          DialogAuthErrorCode.INVALID_MESSAGE,
          'Invalid message received from authentication dialog.',
          error instanceof Error ? error : undefined
        )
      );
    }
  }

  private handleAuthComplete(message: DialogAuthCompleteMessage): void {
    this.cleanup();

    const result: DialogTokenResult = {
      accessToken: message.accessToken,
      expiresOn: new Date(message.expiresOn),
      scopes: message.scopes,
      account: {
        homeAccountId: message.account.homeAccountId,
        environment: message.account.environment,
        tenantId: message.account.tenantId,
        username: message.account.username,
        localAccountId: message.account.localAccountId,
        name: message.account.name,
        // Required by AccountInfo but may be undefined
        idTokenClaims: undefined,
        nativeAccountId: undefined,
      },
    };

    if (this.pendingResolve) {
      this.pendingResolve(result);
    }
    this.clearPendingPromise();
  }

  private handleAuthError(message: DialogAuthErrorMessage): void {
    this.cleanup();

    this.rejectWithError(
      new DialogAuthError(
        DialogAuthErrorCode.MSAL_ERROR,
        message.errorMessage || 'Authentication failed in dialog.'
      )
    );
  }

  private handleCancelled(): void {
    this.cleanup();

    this.rejectWithError(
      new DialogAuthError(
        DialogAuthErrorCode.USER_CANCELLED,
        'Authentication was cancelled by user.'
      )
    );
  }

  private handleDialogError(errorCode: number): void {
    console.error('[DialogAuthService] Dialog error:', errorCode);
    this.cleanup();

    this.rejectWithError(
      new DialogAuthError(
        DialogAuthErrorCode.COMMUNICATION_ERROR,
        `Dialog communication error (code: ${errorCode}).`
      )
    );
  }

  private handleDialogEvent(eventType: number): void {
    // Dialog event types:
    // 12002 - Dialog closed
    // 12003 - Dialog navigation error
    // 12004 - Domain blocked
    // 12005 - HTTPS required
    // 12006 - Dialog already opened
    // 12007 - Dialog navigation error

    this.cleanup();

    if (eventType === 12002) {
      // Dialog was closed by user
      this.rejectWithError(
        new DialogAuthError(
          DialogAuthErrorCode.DIALOG_CLOSED,
          'The authentication dialog was closed.'
        )
      );
    } else {
      this.rejectWithError(
        new DialogAuthError(
          DialogAuthErrorCode.COMMUNICATION_ERROR,
          `Dialog event error (code: ${eventType}).`
        )
      );
    }
  }

  private handleDialogOpenError(error: Office.Error): void {
    console.error('[DialogAuthService] Failed to open dialog:', error);
    this.cleanup();

    this.rejectWithError(
      new DialogAuthError(
        DialogAuthErrorCode.DIALOG_OPEN_FAILED,
        `Failed to open authentication dialog: ${error.message}`
      )
    );
  }

  private handleAuthTimeout(): void {
    console.warn('[DialogAuthService] Authentication timed out');
    this.cleanup();

    this.rejectWithError(
      new DialogAuthError(
        DialogAuthErrorCode.AUTH_TIMEOUT,
        'Authentication timed out. Please try again.'
      )
    );
  }

  private rejectWithError(error: DialogAuthError): void {
    if (this.pendingReject) {
      this.pendingReject(error);
    }
    this.clearPendingPromise();
  }

  private clearPendingPromise(): void {
    this.pendingResolve = null;
    this.pendingReject = null;
  }

  private cleanup(): void {
    // Clear timeout
    if (this.authTimeout) {
      clearTimeout(this.authTimeout);
      this.authTimeout = null;
    }

    // Close dialog
    if (this.activeDialog) {
      try {
        this.activeDialog.close();
      } catch {
        // Dialog may already be closed
      }
      this.activeDialog = null;
    }
  }
}

/**
 * Get the singleton Dialog Authentication Service instance
 */
export const dialogAuthService: IDialogAuthService = DialogAuthServiceImpl.getInstance();

/**
 * Export the class for testing purposes
 */
export { DialogAuthServiceImpl };
