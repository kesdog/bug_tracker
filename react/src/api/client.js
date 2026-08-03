export const SESSION_UNAUTHORIZED_EVENT = 'bug-tracker:session-unauthorized';

export async function authenticatedFetch(input, init) {
  const response = await fetch(input, init);

  if (response.status === 401 && typeof window !== 'undefined') {
    window.dispatchEvent(new Event(SESSION_UNAUTHORIZED_EVENT));
  }

  return response;
}
