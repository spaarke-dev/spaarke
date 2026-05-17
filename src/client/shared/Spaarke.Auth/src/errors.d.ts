import type { IProblemDetails } from './types';
/** Thrown when token acquisition fails across all strategies. */
export declare class AuthError extends Error {
  readonly code: string;
  constructor(message: string, code?: string);
}
/** Thrown when an API call fails with a structured error response. */
export declare class ApiError extends Error {
  readonly status: number;
  readonly problemDetails: IProblemDetails | null;
  constructor(message: string, status: number, problemDetails?: IProblemDetails | null);
}
//# sourceMappingURL=errors.d.ts.map
