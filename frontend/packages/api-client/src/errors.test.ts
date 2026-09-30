import { describe, expect, it } from "vitest";

import {
  ApiError,
  NETWORK_ERROR_CODE,
  apiErrorFrom,
  isApiError,
  networkError,
  unwrap,
} from "./errors";

const response = (status: number) => new Response(null, { status });

describe("ApiError", () => {
  it("reads the Auxilia ProblemDetails extensions", () => {
    const error = apiErrorFrom(response(400), {
      title: "The request is not valid",
      status: 400,
      errorCode: "AUX-10020",
      traceId: "00-abc-01",
      errors: { userName: ["validation.notEmpty"] },
    });

    expect(isApiError(error)).toBe(true);
    expect(error).toMatchObject({
      status: 400,
      errorCode: "AUX-10020",
      traceId: "00-abc-01",
      message: "The request is not valid",
    });
    expect(error.fieldErrors).toEqual({ userName: ["validation.notEmpty"] });
    expect(error.isValidation).toBe(true);
    expect(error.isTransient).toBe(false);
    expect(error.messageKey).toBe("errors.AUX-10020");
  });

  it("falls back to a generic code for bodies that are not ProblemDetails", () => {
    const error = apiErrorFrom(response(502), "<html>Bad gateway</html>");
    expect(error.errorCode).toBe("AUX-WEB-UNKNOWN");
    expect(error.message).toBe("HTTP 502");
    expect(error.isTransient).toBe(true);
    expect(error.messageKey).toBe("errors.AUX-WEB-UNKNOWN");
  });

  it("classifies statuses", () => {
    expect(new ApiError(401, undefined).isUnauthenticated).toBe(true);
    expect(new ApiError(429, undefined).isTransient).toBe(true);
    expect(new ApiError(408, undefined).isTransient).toBe(true);
    expect(new ApiError(501, undefined).isTransient).toBe(false);
    expect(new ApiError(404, undefined).isTransient).toBe(false);
    expect(new ApiError(400, { title: "x" }).isValidation).toBe(false);
    expect(new ApiError(500, { errorCode: "custom" }).messageKey).toBe("errors.generic");
  });

  it("represents network failures with status 0", () => {
    const cause = new TypeError("fetch failed");
    const error = networkError(cause);
    expect([error.status, error.errorCode, error.isTransient]).toEqual([
      0,
      NETWORK_ERROR_CODE,
      true,
    ]);
    expect(error.cause).toBe(cause);
  });
});

describe("unwrap", () => {
  it("returns data of a success and throws the ApiError of a failure", () => {
    expect(unwrap({ data: { a: 1 }, response: response(200) })).toEqual({ a: 1 });
    expect(unwrap({ data: undefined, response: response(204) })).toBeUndefined();
    expect(() =>
      unwrap({
        error: { errorCode: "AUX-21001", title: "Translation key not found" },
        response: response(404),
      }),
    ).toThrowError(expect.objectContaining({ status: 404, errorCode: "AUX-21001" }));
  });
});
