import type { IProblemDetails } from "./types";

/** Thrown when token acquisition fails across all strategies. */
export class AuthError extends Error {
  public readonly code: string;

  constructor(message: string, code = "auth_failed") {
    super(message);
    this.name = "AuthError";
    this.code = code;
    Object.setPrototypeOf(this, AuthError.prototype);
  }
}

/** Thrown when an API call fails with a structured error response. */
export class ApiError extends Error {
  public readonly status: number;
  public readonly problemDetails: IProblemDetails | null;

  constructor(
    message: string,
    status: number,
    problemDetails: IProblemDetails | null = null,
  ) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.problemDetails = problemDetails;
    Object.setPrototypeOf(this, ApiError.prototype);
  }
}
