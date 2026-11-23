import { HttpErrorResponse } from '@angular/common/http';

/** An API error in a form the UI can show: one message plus per-field messages. */
export interface ApiProblem {
  status: number;
  message: string;
  fields: Record<string, string>;
}

/** Reads ASP.NET ProblemDetails / ValidationProblemDetails into {@link ApiProblem}. */
export function toProblem(error: unknown): ApiProblem {
  if (!(error instanceof HttpErrorResponse)) {
    return { status: 0, message: 'Something went wrong. Please try again.', fields: {} };
  }
  if (error.status === 0) {
    return { status: 0, message: 'Could not reach the server. Check your connection.', fields: {} };
  }

  const body = error.error ?? {};
  const fields: Record<string, string> = {};
  for (const [key, messages] of Object.entries((body.errors ?? {}) as Record<string, string[]>)) {
    // "FullName" / "$.status" → "fullName" / "status"
    const name = key.replace(/^\$\./, '');
    fields[name.charAt(0).toLowerCase() + name.slice(1)] = messages[0];
  }
  if (typeof body.field === 'string') fields[body.field] = body.title;

  const message =
    error.status === 403 && !body.title
      ? 'You do not have permission to do this.'
      : (body.title as string | undefined) ?? 'Something went wrong. Please try again.';
  return { status: error.status, message, fields };
}
