/** Thrown when token acquisition fails across all strategies. */
export class AuthError extends Error {
    constructor(message, code = 'auth_failed') {
        super(message);
        this.name = 'AuthError';
        this.code = code;
        Object.setPrototypeOf(this, AuthError.prototype);
    }
}
/** Thrown when an API call fails with a structured error response. */
export class ApiError extends Error {
    constructor(message, status, problemDetails = null) {
        super(message);
        this.name = 'ApiError';
        this.status = status;
        this.problemDetails = problemDetails;
        Object.setPrototypeOf(this, ApiError.prototype);
    }
}
//# sourceMappingURL=errors.js.map