const DEFAULT_TTL_MS = 60_000;

function defaultNow() {
  return Date.now();
}

export class BugsQueryCache {
  constructor({ ttlMs = DEFAULT_TTL_MS, now = defaultNow } = {}) {
    this.ttlMs = ttlMs;
    this.now = now;
    this.entries = new Map();
    this.inflight = new Map();
  }

  get(key) {
    const entry = this.entries.get(key);
    if (!entry) {
      return null;
    }

    if (entry.expiresAt <= this.now()) {
      this.entries.delete(key);
      return null;
    }

    return entry.value;
  }

  set(key, value) {
    this.entries.set(key, {
      value,
      expiresAt: this.now() + this.ttlMs
    });
  }

  invalidatePrefix(prefix) {
    Array.from(this.entries.keys())
      .filter((key) => key.startsWith(prefix))
      .forEach((key) => this.entries.delete(key));
  }

  clear() {
    this.entries.clear();
    this.inflight.clear();
  }

  async getOrFetch(key, fetcher, { force = false } = {}) {
    if (!force) {
      const cached = this.get(key);
      if (cached !== null) {
        return cached;
      }
    }

    const inflight = this.inflight.get(key);
    if (inflight) {
      return inflight;
    }

    const nextPromise = Promise.resolve()
      .then(fetcher)
      .then((value) => {
        this.set(key, value);
        return value;
      })
      .finally(() => {
        this.inflight.delete(key);
      });

    this.inflight.set(key, nextPromise);
    return nextPromise;
  }
}

function tokenScope(token) {
  const normalized = typeof token === 'string' ? token.trim() : '';
  if (!normalized) {
    return 'anon';
  }

  return normalized.slice(-16);
}

function filterScope(filters = {}) {
  const entries = Object.entries(filters || {})
    .filter(([, value]) => value !== undefined && value !== null && String(value).trim() !== '')
    .sort(([left], [right]) => left.localeCompare(right));

  if (entries.length === 0) {
    return 'none';
  }

  return encodeURIComponent(JSON.stringify(Object.fromEntries(entries)));
}

export function activeBugsKey(limit, search = '', filters = {}) {
  return `bugs:active:${limit}:${encodeURIComponent(search || '')}:${filterScope(filters)}`;
}

export function closedBugsKey(limit, search = '', filters = {}) {
  return `bugs:closed:${limit}:${encodeURIComponent(search || '')}:${filterScope(filters)}`;
}

export function allocatedBugsKey(token, limit, search = '', filters = {}) {
  return `bugs:allocated:${tokenScope(token)}:${limit}:${encodeURIComponent(search || '')}:${filterScope(filters)}`;
}

export const bugsQueryCache = new BugsQueryCache();

export function invalidateBugCacheScopes(scopes = ['active', 'closed', 'allocated']) {
  if (scopes.includes('active')) {
    bugsQueryCache.invalidatePrefix('bugs:active:');
  }
  if (scopes.includes('closed')) {
    bugsQueryCache.invalidatePrefix('bugs:closed:');
  }
  if (scopes.includes('allocated')) {
    bugsQueryCache.invalidatePrefix('bugs:allocated:');
  }
}

export function clearBugCache() {
  bugsQueryCache.clear();
}
