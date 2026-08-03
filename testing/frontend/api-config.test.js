import { describe, expect, it } from 'vitest';
import { resolveApiBaseUrl } from '../../react/src/api/config';

describe('API base configuration', () => {
  it('defaults to same-origin requests', () => {
    expect(resolveApiBaseUrl(undefined, { hostname: 'localhost' })).toBe('');
    expect(resolveApiBaseUrl('   ', { hostname: 'localhost' })).toBe('');
  });

  it('always uses same-origin requests in production despite a configured override', () => {
    expect(resolveApiBaseUrl('https://stale-api.example.com', { hostname: 'app.example.com' }, true))
      .toBe('');
    expect(resolveApiBaseUrl('http://127.0.0.1:5040/', { hostname: 'localhost' }, true))
      .toBe('');
  });

  it('normalizes an explicitly configured API origin', () => {
    expect(resolveApiBaseUrl(' https://api.example.com/ ', { hostname: 'localhost' }))
      .toBe('https://api.example.com');
  });

  it('rewrites a loopback API host when the app is opened remotely', () => {
    expect(resolveApiBaseUrl('http://127.0.0.1:5040', { hostname: 'devbox.local' }))
      .toBe('http://devbox.local:5040');
  });

  it('does not rewrite a non-loopback API host', () => {
    expect(resolveApiBaseUrl('https://api.example.com', { hostname: 'devbox.local' }))
      .toBe('https://api.example.com');
  });
});
