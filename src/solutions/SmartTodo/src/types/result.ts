/**
 * Typed Result pattern for safe, exception-free error handling across the data service layer.
 *
 * Every Xrm.WebApi call returns IResult<T> instead of throwing.
 * Consumer checks result.success before accessing result.data or result.error.
 */

/** Error detail attached to a failed IResult */
export interface IResultError {
  /** Short machine-readable error code (e.g. "NOT_FOUND", "NETWORK_ERROR") */
  code: string;
  /** Human-readable description suitable for display or logging */
  message: string;
  /** Optional raw error payload from the underlying Xrm.WebApi rejection */
  raw?: unknown;
}

/** Successful result — discriminated by success: true */
export interface ISuccessResult<T> {
  success: true;
  data: T;
}

/** Failed result — discriminated by success: false */
export interface IFailureResult {
  success: false;
  error: IResultError;
}

/**
 * IResult<T> is the union type returned by all DataverseService methods.
 *
 * Usage pattern:
 *   const result = await service.getMattersByUser(userId);
 *   if (result.success) {
 *     // result.data is IMatter[]
 *   } else {
 *     // result.error.code / result.error.message
 *   }
 */
export type IResult<T> = ISuccessResult<T> | IFailureResult;

/** Factory helper: create a successful result */
export function ok<T>(data: T): ISuccessResult<T> {
  return { success: true, data };
}

/** Factory helper: create a failed result */
export function fail(code: string, message: string, raw?: unknown): IFailureResult {
  return { success: false, error: { code, message, raw } };
}

/**
 * Wraps an async Xrm.WebApi call and converts any rejection into an IFailureResult.
 * Use this in every DataverseService method to avoid try/catch boilerplate.
 */
export async function tryCatch<T>(
  fn: () => Promise<T>,
  errorCode: string = 'UNKNOWN_ERROR'
): Promise<IResult<T>> {
  try {
    const data = await fn();
    return ok(data);
  } catch (e: unknown) {
    const raw = e;
    let message = 'An unexpected error occurred.';

    if (e && typeof e === 'object') {
      const err = e as Record<string, unknown>;
      if (typeof err['message'] === 'string') {
        message = err['message'];
      } else if (typeof err['errorCode'] === 'number' && typeof err['message'] === 'string') {
        // Xrm.WebApi rejection shape: { errorCode, message }
        message = err['message'];
      }
    } else if (typeof e === 'string') {
      message = e;
    }

    return fail(errorCode, message, raw);
  }
}
