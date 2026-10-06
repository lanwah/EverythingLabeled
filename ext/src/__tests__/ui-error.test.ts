import { describe, expect, it } from "vitest";
import { copy } from "../copy";
import { formatUiError } from "../ui-error";

describe("formatUiError", () => {
  it("maps config_missing to tokenMissing", () => {
    expect(formatUiError({ code: "config_missing", message: "x" })).toBe(copy.tokenMissing);
  });

  it("maps connection_failed to serviceUnreachable", () => {
    expect(formatUiError({ code: "connection_failed", message: "down" })).toBe(
      copy.serviceUnreachable,
    );
  });

  it("maps HTTP 401 to tokenInvalid", () => {
    expect(formatUiError({ code: "bad_request", message: "nope", httpStatus: 401 })).toBe(
      copy.tokenInvalid,
    );
  });

  it("maps unauthorized envelope to tokenInvalid", () => {
    expect(formatUiError({ code: "unauthorized", message: "nope" })).toBe(copy.tokenInvalid);
  });

  it("maps tag_not_found to the create-missing copy", () => {
    expect(formatUiError({ code: "tag_not_found", message: "missing" })).toBe(
      copy.tagNotFoundWithoutCreate,
    );
  });

  it("shows bad_request message as-is", () => {
    expect(formatUiError({ code: "bad_request", message: copy.invalidBaseUrl })).toBe(
      copy.invalidBaseUrl,
    );
  });

  it("falls back to error.message", () => {
    expect(formatUiError({ code: "internal_error", message: "boom" })).toBe("boom");
  });
});
