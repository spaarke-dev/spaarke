/**
 * Result envelope for `TodoDataverseService` (#2972).
 *
 * Structurally identical to the hosts' `IResult<T>` (`src/solutions/LegalWorkspace/src/types/result.ts`), so a host
 * service that extends `TodoDataverseService` returns values its existing callers already read
 * (`result.success`, `result.data`, `result.error.message`). Methods never throw.
 */

export interface ITodoServiceError {
  code: string;
  message: string;
  raw?: unknown;
}

export type ITodoServiceResult<T> = { success: true; data: T } | { success: false; error: ITodoServiceError };

export function todoOk<T>(data: T): { success: true; data: T } {
  return { success: true, data };
}

export function todoFail(code: string, message: string, raw?: unknown): { success: false; error: ITodoServiceError } {
  return { success: false, error: { code, message, raw } };
}

/** Runs `fn` and wraps its value, or the thrown error's message, in a result. */
export async function todoTryCatch<T>(fn: () => Promise<T>, errorCode: string): Promise<ITodoServiceResult<T>> {
  try {
    return todoOk(await fn());
  } catch (e: unknown) {
    let message = 'An unexpected error occurred.';
    if (typeof e === 'string') {
      message = e;
    } else if (e && typeof e === 'object' && typeof (e as { message?: unknown }).message === 'string') {
      message = (e as { message: string }).message;
    }
    return todoFail(errorCode, message, e);
  }
}
