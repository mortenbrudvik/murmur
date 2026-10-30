/** The message of a failed bridge call, or a generic one. */
export function errorMessage(err: unknown): string {
  return err instanceof Error && err.message ? err.message : "Something went wrong.";
}

/** "Could not save the settings (read-only)." — what was attempted, then why it failed. */
export function failure(action: string, err: unknown): string {
  return `${action} (${errorMessage(err).replace(/\.$/, "")}).`;
}
