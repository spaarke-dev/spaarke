import { authService } from './AuthService';
import { authenticatedJsonFetch } from './authenticatedJsonFetch';

/**
 * API client for communicating with the Spaarke BFF API.
 *
 * Uses the access token from AuthService to make authenticated requests.
 *
 * Auth v2 (email-communication-solution-r4 task 072 / FR-25): `AuthService` now
 * delegates to `@spaarke/auth`'s `OfficeNaaStrategy`, which acquires a token for
 * the single `bffApiScope` configured at `initialize()` time
 * (`api://{BFF_API_CLIENT_ID}/user_impersonation`, per ADR-028).
 * `AuthService.getAccessToken()` ignores any scope argument and always returns
 * the token for the configured scope (task 040 / FR-B0: the dead `.default`
 * scope array previously built here — and passed but silently ignored — has
 * been removed).
 *
 * Single-retry-on-401 (task 040 / FR-B0): `request()` and `uploadFile()` route
 * through `authenticatedJsonFetch`, which mirrors `@spaarke/auth`'s
 * `authenticatedFetch` 401-retry shape (clear cache, re-acquire, retry exactly
 * once) — see that helper's header comment for why the office-addins package
 * can't call `@spaarke/auth`'s own `authenticatedFetch` directly.
 */

export interface IApiClient {
  get<T>(endpoint: string): Promise<T>;
  post<T>(endpoint: string, body?: unknown): Promise<T>;
  put<T>(endpoint: string, body?: unknown): Promise<T>;
  delete<T>(endpoint: string): Promise<T>;
  uploadFile(endpoint: string, file: File | Blob, fileName: string): Promise<UploadResponse>;
}

export interface ApiClientConfig {
  baseUrl: string;
  bffApiClientId: string;
}

export interface UploadResponse {
  documentId: string;
  jobId: string;
  status: 'pending' | 'processing' | 'completed' | 'failed';
}

export interface ApiError {
  type: string;
  title: string;
  status: number;
  detail?: string;
  instance?: string;
  correlationId?: string;
}

class ApiClient implements IApiClient {
  private baseUrl: string = '';
  private bffApiClientId: string = '';

  configure(config: ApiClientConfig): void {
    this.baseUrl = config.baseUrl.replace(/\/$/, ''); // Remove trailing slash
    this.bffApiClientId = config.bffApiClientId;
  }

  async get<T>(endpoint: string): Promise<T> {
    return this.request<T>('GET', endpoint);
  }

  async post<T>(endpoint: string, body?: unknown): Promise<T> {
    return this.request<T>('POST', endpoint, body);
  }

  async put<T>(endpoint: string, body?: unknown): Promise<T> {
    return this.request<T>('PUT', endpoint, body);
  }

  async delete<T>(endpoint: string): Promise<T> {
    return this.request<T>('DELETE', endpoint);
  }

  async uploadFile(endpoint: string, file: File | Blob, fileName: string): Promise<UploadResponse> {
    const accessToken = await this.getAccessToken();
    if (!accessToken) {
      throw new Error('Not authenticated');
    }

    const formData = new FormData();
    formData.append('file', file, fileName);

    const response = await authenticatedJsonFetch(
      `${this.baseUrl}${endpoint}`,
      {
        method: 'POST',
        body: formData,
      },
      accessToken,
      this.retryConfig()
    );

    if (!response.ok) {
      await this.handleErrorResponse(response);
    }

    return response.json();
  }

  private async request<T>(method: string, endpoint: string, body?: unknown): Promise<T> {
    const accessToken = await this.getAccessToken();
    if (!accessToken) {
      throw new Error('Not authenticated');
    }

    const config: RequestInit = {
      method,
      headers: {
        'Content-Type': 'application/json',
      },
    };

    if (body && (method === 'POST' || method === 'PUT')) {
      config.body = JSON.stringify(body);
    }

    const response = await authenticatedJsonFetch(
      `${this.baseUrl}${endpoint}`,
      config,
      accessToken,
      this.retryConfig()
    );

    if (!response.ok) {
      await this.handleErrorResponse(response);
    }

    // Handle empty responses
    const text = await response.text();
    if (!text) {
      return {} as T;
    }

    return JSON.parse(text) as T;
  }

  /** Single-retry-on-401 config shared by `request()` and `uploadFile()` (task 040 / FR-B0). */
  private retryConfig() {
    return {
      getRetryToken: async () => (await this.getAccessToken()) ?? '',
      onBeforeRetry: () => authService.clearCache(),
    };
  }

  private async getAccessToken(): Promise<string | null> {
    return authService.getAccessToken();
  }

  private async handleErrorResponse(response: Response): Promise<never> {
    let error: ApiError;

    try {
      error = await response.json();
    } catch {
      error = {
        type: 'about:blank',
        title: 'Request failed',
        status: response.status,
        detail: response.statusText,
      };
    }

    throw new ApiClientError(error);
  }
}

export class ApiClientError extends Error {
  public readonly error: ApiError;

  constructor(error: ApiError) {
    super(error.detail || error.title);
    this.name = 'ApiClientError';
    this.error = error;
  }
}

// Export singleton instance
export const apiClient: IApiClient & {
  configure: (config: ApiClientConfig) => void;
} = new ApiClient();
