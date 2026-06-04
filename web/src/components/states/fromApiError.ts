import { ApiError } from "../../api/client";
import type { ErrorAlertProps } from "../ErrorAlert";

export function errorAlertProps(error: unknown): ErrorAlertProps {
  if (error instanceof ApiError) {
    return {
      message: error.message,
      technicalDetail: [error.code, error.technicalDetail].filter(Boolean).join(" ") || null,
      traceId: error.traceId,
    };
  }
  return { message: "Something went wrong. Please try again.", technicalDetail: null, traceId: null };
}
