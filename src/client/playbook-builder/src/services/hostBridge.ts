/**
 * HostBridge Service
 *
 * Handles postMessage communication between the Playbook Builder (iframe)
 * and the PCF Host (Dataverse form). Uses a singleton pattern to ensure
 * consistent message handling across the application.
 *
 * Usage:
 *   const bridge = HostBridge.getInstance();
 *   bridge.initialize();
 *   bridge.sendDirtyChange(true);
 */

import type {
  HostToBuilderMessage,
  BuilderToHostMessage,
  InitMessage,
  AuthTokenMessage,
  SaveSuccessMessage,
  SaveErrorMessage,
  ThemeChangeMessage,
} from '../types/messages';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface HostBridgeConfig {
  /** Allowed origins for postMessage security */
  allowedOrigins: string[];
  /** Callback when INIT is received from host */
  onInit?: (payload: InitMessage['payload']) => void;
  /** Callback when AUTH_TOKEN is received */
  onAuthToken?: (payload: AuthTokenMessage['payload']) => void;
  /** Callback when SAVE_SUCCESS is received */
  onSaveSuccess?: (payload: SaveSuccessMessage['payload']) => void;
  /** Callback when SAVE_ERROR is received */
  onSaveError?: (payload: SaveErrorMessage['payload']) => void;
  /** Callback when THEME_CHANGE is received */
  onThemeChange?: (payload: ThemeChangeMessage['payload']) => void;
}

export type MessageHandler<T extends HostToBuilderMessage = HostToBuilderMessage> = (
  message: T
) => void;

// ─────────────────────────────────────────────────────────────────────────────
// HostBridge Class
// ─────────────────────────────────────────────────────────────────────────────

export class HostBridge {
  private static instance: HostBridge | null = null;

  private config: HostBridgeConfig;
  private isInitialized = false;
  private messageHandler: ((event: MessageEvent) => void) | null = null;

  // Stored data from host
  private playbookId: string = '';
  private playbookName: string = '';
  private canvasJson: string = '';
  private authToken: string = '';
  private tokenExpiresAt: number = 0;

  private constructor(config: Partial<HostBridgeConfig> = {}) {
    this.config = {
      allowedOrigins: config.allowedOrigins ?? ['*'], // In production, restrict this
      onInit: config.onInit,
      onAuthToken: config.onAuthToken,
      onSaveSuccess: config.onSaveSuccess,
      onSaveError: config.onSaveError,
      onThemeChange: config.onThemeChange,
    };
  }

  /**
   * Get the singleton instance of HostBridge.
   */
  public static getInstance(config?: Partial<HostBridgeConfig>): HostBridge {
    if (!HostBridge.instance) {
      HostBridge.instance = new HostBridge(config);
    } else if (config) {
      // Update config if provided
      Object.assign(HostBridge.instance.config, config);
    }
    return HostBridge.instance;
  }

  /**
   * Reset the singleton (useful for testing).
   */
  public static resetInstance(): void {
    if (HostBridge.instance) {
      HostBridge.instance.destroy();
      HostBridge.instance = null;
    }
  }

  /**
   * Initialize the bridge and start listening for messages.
   * Automatically sends READY to host.
   */
  public initialize(): void {
    if (this.isInitialized) {
      console.warn('[HostBridge] Already initialized');
      return;
    }

    // Check if we're running in an iframe
    if (!this.isEmbedded()) {
      console.info('[HostBridge] Not running in iframe, skipping initialization');
      return;
    }

    console.info('[HostBridge] Initializing...');

    // Set up message listener
    this.messageHandler = this.handleMessage.bind(this);
    window.addEventListener('message', this.messageHandler);

    this.isInitialized = true;

    // Send READY to host
    this.sendReady();

    console.info('[HostBridge] Initialized and READY sent');
  }

  /**
   * Clean up the bridge and stop listening for messages.
   */
  public destroy(): void {
    if (this.messageHandler) {
      window.removeEventListener('message', this.messageHandler);
      this.messageHandler = null;
    }
    this.isInitialized = false;
    console.info('[HostBridge] Destroyed');
  }

  /**
   * Check if the app is running in an iframe.
   */
  public isEmbedded(): boolean {
    try {
      return window.self !== window.top;
    } catch {
      // Cross-origin check failed, we're definitely embedded
      return true;
    }
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Getters for stored data
  // ─────────────────────────────────────────────────────────────────────────

  public getPlaybookId(): string {
    return this.playbookId;
  }

  public getPlaybookName(): string {
    return this.playbookName;
  }

  public getCanvasJson(): string {
    return this.canvasJson;
  }

  public getAuthToken(): string {
    return this.authToken;
  }

  public isTokenExpired(): boolean {
    if (!this.tokenExpiresAt) return true;
    return Date.now() >= this.tokenExpiresAt;
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Send messages to host
  // ─────────────────────────────────────────────────────────────────────────

  private sendToHost(message: BuilderToHostMessage): void {
    if (!this.isEmbedded()) {
      console.debug('[HostBridge] Not embedded, message not sent:', message.type);
      return;
    }

    try {
      window.parent.postMessage(message, '*');
      console.debug('[HostBridge] Sent to host:', message.type);
    } catch (error) {
      console.error('[HostBridge] Failed to send message:', error);
    }
  }

  /**
   * Send READY message to host.
   */
  public sendReady(): void {
    this.sendToHost({ type: 'READY' });
  }

  /**
   * Send DIRTY_CHANGE message to host.
   */
  public sendDirtyChange(isDirty: boolean): void {
    this.sendToHost({
      type: 'DIRTY_CHANGE',
      payload: { isDirty },
    });
  }

  /**
   * Send SAVE_REQUEST message to host with canvas data.
   */
  public sendSaveRequest(canvasJson: string): void {
    this.sendToHost({
      type: 'SAVE_REQUEST',
      payload: { canvasJson },
    });
  }

  /**
   * Send CANVAS_UPDATE message to host (for real-time sync).
   */
  public sendCanvasUpdate(canvasJson: string): void {
    this.sendToHost({
      type: 'CANVAS_UPDATE',
      payload: { canvasJson },
    });
  }

  /**
   * Send REQUEST_TOKEN message to host when token is expired.
   */
  public sendRequestToken(): void {
    this.sendToHost({ type: 'REQUEST_TOKEN' });
  }

  // ─────────────────────────────────────────────────────────────────────────
  // Handle messages from host
  // ─────────────────────────────────────────────────────────────────────────

  private handleMessage(event: MessageEvent): void {
    // Origin validation (in production, check against specific origins)
    if (this.config.allowedOrigins[0] !== '*') {
      if (!this.config.allowedOrigins.includes(event.origin)) {
        console.warn('[HostBridge] Ignoring message from unknown origin:', event.origin);
        return;
      }
    }

    const message = event.data;
    if (!message || typeof message !== 'object' || !message.type) {
      return; // Not a valid message
    }

    console.debug('[HostBridge] Received from host:', message.type);

    switch (message.type) {
      case 'INIT':
        this.handleInit(message as InitMessage);
        break;

      case 'AUTH_TOKEN':
        this.handleAuthToken(message as AuthTokenMessage);
        break;

      case 'SAVE_SUCCESS':
        this.handleSaveSuccess(message as SaveSuccessMessage);
        break;

      case 'SAVE_ERROR':
        this.handleSaveError(message as SaveErrorMessage);
        break;

      case 'THEME_CHANGE':
        this.handleThemeChange(message as ThemeChangeMessage);
        break;

      default:
        console.debug('[HostBridge] Unknown message type:', message.type);
    }
  }

  private handleInit(message: InitMessage): void {
    const { playbookId, playbookName, canvasJson, authToken } = message.payload;

    this.playbookId = playbookId;
    this.playbookName = playbookName;
    this.canvasJson = canvasJson;
    if (authToken) {
      this.authToken = authToken;
    }

    console.info('[HostBridge] Received INIT:', {
      playbookId,
      playbookName,
      hasCanvasJson: !!canvasJson,
      hasToken: !!authToken,
    });

    this.config.onInit?.(message.payload);
  }

  private handleAuthToken(message: AuthTokenMessage): void {
    const { token, expiresAt } = message.payload;

    this.authToken = token;
    this.tokenExpiresAt = expiresAt;

    console.info('[HostBridge] Received AUTH_TOKEN, expires at:', new Date(expiresAt));

    this.config.onAuthToken?.(message.payload);
  }

  private handleSaveSuccess(message: SaveSuccessMessage): void {
    console.info('[HostBridge] Save successful at:', new Date(message.payload.timestamp));
    this.config.onSaveSuccess?.(message.payload);
  }

  private handleSaveError(message: SaveErrorMessage): void {
    console.error('[HostBridge] Save failed:', message.payload.error);
    this.config.onSaveError?.(message.payload);
  }

  private handleThemeChange(message: ThemeChangeMessage): void {
    console.info('[HostBridge] Theme changed to:', message.payload.theme);
    this.config.onThemeChange?.(message.payload);
  }
}

// Export singleton getter for convenience
export const getHostBridge = HostBridge.getInstance;
