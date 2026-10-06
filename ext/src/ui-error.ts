import { copy } from "./copy";

export type UiFailure = {
  code: string;
  message: string;
  httpStatus?: number;
};

export function formatUiError(err: UiFailure): string {
  if (err.code === "config_missing") return copy.tokenMissing;
  if (err.code === "connection_failed") return copy.serviceUnreachable;
  if (err.httpStatus === 401 || err.code === "unauthorized") return copy.tokenInvalid;
  if (err.code === "tag_not_found") return copy.tagNotFoundWithoutCreate;
  return err.message;
}
