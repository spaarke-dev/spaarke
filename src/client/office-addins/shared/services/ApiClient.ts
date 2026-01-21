import { authService } from './AuthService';

/**
 * API client for communicating with the Spaarke BFF API.
 *
 * Uses the access token from AuthService to make authenticated requests.
 * Per auth.md: Uses `.default` scope for BFF API calls.
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

  async uploadFile(
    endpoint: string,
    file: File | Blob,
    fileName: string
  ): Promise<UploadResponse> {
    const accessToken = await this.getAccessToken();
    if (!accessToken) {
      throw new Error('Not authenticated');
    }

    const formData = new FormData();
    formData.append('file', file, fileName);

    const response = await fetch(`${this.baseUrl}${endpoint}`, {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${accessToken}`,
      },
      body: formData,
    });

    if (!response.ok) {
      await this.handleErrorResponse(response);
    }

    return response.json();
  }

  private async request<T>(
    method: string,
    endpoint: string,
    body?: unknown
  ): Promise<T> {
    const accessToken = await this.getAccessToken();
    if (!accessToken) {
      throw new Error('Not authenticated');
    }

    const headers: HeadersInit = {
      Authorization: `Bearer ${accessToken}`,
      'Content-Type': 'application/json',
    };

    const config: RequestInit = {
      method,
      headers,
    };

    if (body && (method === 'POST' || method === 'PUT')) {
      config.body = JSON.stringify(body);
    }

    const response = await fetch(`${this.baseUrl}${endpoint}`, config);

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

  private async getAccessToken(): Promise<string | null> {
    // Use `.default` scope per auth.md constraint
    const scopes = [`api://${this.bffApiClientId}/.default`];
    return authService.getAccessToken(scopes);
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
export const apiClient: IApiClient & { configure: (config: ApiClientConfig) => void } =
  new ApiClient();
