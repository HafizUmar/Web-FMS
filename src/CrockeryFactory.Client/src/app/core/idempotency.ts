/**
 * One key per submission attempt, reused across retries of that submission.
 *
 * The point is a tablet on marginal factory WiFi that sends a request, loses the reply
 * and sends it again: the server replays the original response instead of creating a
 * second document. Generating a new key on retry would create exactly the duplicate the
 * header exists to prevent, so callers must hold the key across the retry.
 */
export function newIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) {
    return crypto.randomUUID();
  }

  // Older browsers on a factory tablet. Uniqueness only has to hold within 7 days.
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 12)}`;
}
