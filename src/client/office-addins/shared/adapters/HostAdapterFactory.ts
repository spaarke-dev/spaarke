/**
 * Factory for creating host-specific adapters.
 *
 * The factory detects the Office host at runtime and instantiates
 * the appropriate adapter implementation.
 *
 * @example
 * ```typescript
 * // Basic usage
 * const adapter = await HostAdapterFactory.createAndInitialize();
 * const hostType = adapter.getHostType();
 *
 * // Manual initialization
 * const adapter = HostAdapterFactory.create();
 * await adapter.initialize();
 * ```
 */

import type { IHostAdapter } from './IHostAdapter';
import type { HostType, HostAdapterError, HostAdapterErrorCode } from './types';

/**
 * Type definition for host adapter constructors.
 */
type HostAdapterConstructor = new () => IHostAdapter;

/**
 * Registry of available host adapters.
 * Implementations are registered via registerAdapter().
 */
const adapterRegistry: Map<HostType, HostAdapterConstructor> = new Map();

/**
 * Cached adapter instance for singleton pattern.
 */
let cachedAdapter: IHostAdapter | null = null;

/**
 * Creates a custom HostAdapterError.
 */
function createError(
  code: HostAdapterErrorCode,
  message: string,
  innerError?: Error
): HostAdapterError {
  return { code, message, innerError };
}

/**
 * Factory for creating host-specific adapters.
 *
 * This factory:
 * - Detects the Office host (Outlook or Word) at runtime
 * - Creates the appropriate adapter implementation
 * - Supports singleton pattern for the adapter instance
 * - Allows lazy registration of adapter implementations
 */
export const HostAdapterFactory = {
  /**
   * Register a host adapter implementation.
   *
   * Call this to register adapter implementations before creating adapters.
   * This allows for lazy loading of adapter code.
   *
   * @param hostType - The host type this adapter handles
   * @param adapterClass - The adapter class constructor
   *
   * @example
   * ```typescript
   * import { OutlookHostAdapter } from '../outlook/OutlookHostAdapter';
   * HostAdapterFactory.registerAdapter('outlook', OutlookHostAdapter);
   * ```
   */
  registerAdapter(hostType: HostType, adapterClass: HostAdapterConstructor): void {
    adapterRegistry.set(hostType, adapterClass);
  },

  /**
   * Detect the current Office host type.
   *
   * This method queries Office.js to determine which host application
   * is running. Must be called after Office.js is loaded.
   *
   * @returns The detected host type
   * @throws When not running in a supported Office host
   */
  detectHostType(): HostType {
    // Check if Office.js is available
    if (typeof Office === 'undefined' || !Office.context) {
      throw createError(
        'API_NOT_AVAILABLE',
        'Office.js is not available. Make sure the script is running within an Office add-in context.'
      );
    }

    const host = Office.context.host;

    if (host === Office.HostType.Outlook) {
      return 'outlook';
    }

    if (host === Office.HostType.Word) {
      return 'word';
    }

    throw createError(
      'INVALID_HOST',
      `Unsupported Office host: ${host}. Only Outlook and Word are supported.`
    );
  },

  /**
   * Create a host adapter instance without initializing it.
   *
   * Use this when you need manual control over initialization.
   * Remember to call adapter.initialize() before using the adapter.
   *
   * @param hostType - Optional host type. If not provided, auto-detects.
   * @returns A new adapter instance (not initialized)
   * @throws When no adapter is registered for the host type
   */
  create(hostType?: HostType): IHostAdapter {
    const targetHost = hostType ?? this.detectHostType();
    const AdapterClass = adapterRegistry.get(targetHost);

    if (!AdapterClass) {
      throw createError(
        'INVALID_HOST',
        `No adapter registered for host type: ${targetHost}. ` +
        `Register an adapter using HostAdapterFactory.registerAdapter().`
      );
    }

    return new AdapterClass();
  },

  /**
   * Create and initialize a host adapter instance.
   *
   * This is the recommended way to get an adapter instance.
   * It creates the adapter and calls initialize() automatically.
   *
   * @param hostType - Optional host type. If not provided, auto-detects.
   * @returns Promise resolving to an initialized adapter instance
   * @throws When initialization fails
   */
  async createAndInitialize(hostType?: HostType): Promise<IHostAdapter> {
    const adapter = this.create(hostType);
    await adapter.initialize();
    return adapter;
  },

  /**
   * Get or create a singleton adapter instance.
   *
   * Returns the cached adapter if available, otherwise creates
   * and initializes a new one. Use this for most scenarios where
   * you want a single shared adapter instance.
   *
   * @returns Promise resolving to the singleton adapter instance
   */
  async getOrCreate(): Promise<IHostAdapter> {
    if (cachedAdapter && cachedAdapter.isInitialized()) {
      return cachedAdapter;
    }

    cachedAdapter = await this.createAndInitialize();
    return cachedAdapter;
  },

  /**
   * Clear the cached adapter instance.
   *
   * Use this when you need to force creation of a new adapter,
   * such as during testing or after a context change.
   */
  clearCache(): void {
    cachedAdapter = null;
  },

  /**
   * Check if an adapter is registered for a host type.
   *
   * @param hostType - The host type to check
   * @returns True if an adapter is registered
   */
  hasAdapter(hostType: HostType): boolean {
    return adapterRegistry.has(hostType);
  },

  /**
   * Get the list of registered host types.
   *
   * @returns Array of registered host types
   */
  getRegisteredHosts(): HostType[] {
    return Array.from(adapterRegistry.keys());
  },

  /**
   * Wait for Office.js to be ready and detect the host.
   *
   * This is a convenience method that wraps Office.onReady
   * and returns the detected host type.
   *
   * @returns Promise resolving to the host type once Office.js is ready
   */
  async waitForOfficeReady(): Promise<HostType> {
    return new Promise((resolve, reject) => {
      if (typeof Office === 'undefined') {
        reject(
          createError(
            'API_NOT_AVAILABLE',
            'Office.js is not loaded. Make sure to include the Office.js script.'
          )
        );
        return;
      }

      Office.onReady((info) => {
        if (info.host === Office.HostType.Outlook) {
          resolve('outlook');
        } else if (info.host === Office.HostType.Word) {
          resolve('word');
        } else {
          reject(
            createError(
              'INVALID_HOST',
              `Unsupported Office host: ${info.host}`
            )
          );
        }
      });
    });
  },
};

/**
 * Type guard to check if an error is a HostAdapterError.
 *
 * @param error - The error to check
 * @returns True if the error is a HostAdapterError
 */
export function isHostAdapterError(error: unknown): error is HostAdapterError {
  return (
    typeof error === 'object' &&
    error !== null &&
    'code' in error &&
    'message' in error &&
    typeof (error as HostAdapterError).code === 'string' &&
    typeof (error as HostAdapterError).message === 'string'
  );
}

// Default export for convenience
export default HostAdapterFactory;
