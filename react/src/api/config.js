export function resolveApiBaseUrl(
  configuredBaseUrl = '',
  pageLocation = globalThis.window?.location,
  isProduction = false
) {
  // Production is served behind the same origin as the API. Ignore inherited
  // or stale VITE_API_BASE_URL values so every request remains relative.
  if (isProduction) {
    return '';
  }

  const configured = typeof configuredBaseUrl === 'string'
    ? configuredBaseUrl.trim().replace(/\/$/, '')
    : '';

  // An empty base keeps browser requests on the current origin. During local
  // development Vite proxies these /api requests to the backend.
  if (!configured || !pageLocation) {
    return configured;
  }

  const pageHost = pageLocation.hostname;
  const isPageRemote = pageHost !== 'localhost' && pageHost !== '127.0.0.1';
  if (!isPageRemote) {
    return configured;
  }

  try {
    const parsed = new URL(configured);
    const isApiLocal = parsed.hostname === 'localhost' || parsed.hostname === '127.0.0.1';
    if (!isApiLocal) {
      return configured;
    }

    parsed.hostname = pageHost;
    return parsed.toString().replace(/\/$/, '');
  } catch {
    return configured;
  }
}

export const API_BASE_URL = resolveApiBaseUrl(
  import.meta.env.PROD ? '' : import.meta.env.VITE_API_BASE_URL,
  globalThis.window?.location,
  import.meta.env.PROD
);
