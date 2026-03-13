import { AuthError, ApiError } from "../src/errors";

describe("AuthError", () => {
  it("creates with message and default code", () => {
    const err = new AuthError("token failed");
    expect(err.message).toBe("token failed");
    expect(err.code).toBe("auth_failed");
    expect(err.name).toBe("AuthError");
    expect(err instanceof Error).toBe(true);
  });

  it("creates with custom code", () => {
    const err = new AuthError("xrm required", "xrm_required");
    expect(err.code).toBe("xrm_required");
  });
});

describe("ApiError", () => {
  it("creates with status and message", () => {
    const err = new ApiError("Not Found", 404);
    expect(err.message).toBe("Not Found");
    expect(err.status).toBe(404);
    expect(err.problemDetails).toBeNull();
    expect(err.name).toBe("ApiError");
  });

  it("creates with ProblemDetails", () => {
    const pd = { title: "Forbidden", status: 403, detail: "Access denied" };
    const err = new ApiError("Forbidden", 403, pd);
    expect(err.problemDetails).toEqual(pd);
  });
});
