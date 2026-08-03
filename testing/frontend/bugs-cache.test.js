import { describe, expect, it } from 'vitest';
import {
  BugsQueryCache,
  activeBugsKey,
  allocatedBugsKey,
  bugsQueryCache,
  clearBugCache,
  closedBugsKey,
  invalidateBugCacheScopes
} from '../../react/src/api/bugs_cache';

describe('bugs query cache', () => {
  it('CASE 1: returns cache hit while entry is fresh (page navigation within TTL)', async () => {
    let now = 1000;
    const cache = new BugsQueryCache({ ttlMs: 5000, now: () => now });
    const key = activeBugsKey(10);

    const first = await cache.getOrFetch(key, async () => [{ id: 'bug-1' }]);
    now += 100;
    const second = await cache.getOrFetch(key, async () => [{ id: 'bug-2' }]);

    expect(first).toEqual([{ id: 'bug-1' }]);
    expect(second).toEqual([{ id: 'bug-1' }]);
  });

  it('CASE 2: expires stale entries and refetches (user returns after TTL)', async () => {
    let now = 2000;
    const cache = new BugsQueryCache({ ttlMs: 200, now: () => now });
    const key = closedBugsKey(50);
    let calls = 0;

    const fetcher = async () => {
      calls += 1;
      return [{ id: `closed-${calls}` }];
    };

    const first = await cache.getOrFetch(key, fetcher);
    now += 500;
    const second = await cache.getOrFetch(key, fetcher);

    expect(first).toEqual([{ id: 'closed-1' }]);
    expect(second).toEqual([{ id: 'closed-2' }]);
    expect(calls).toBe(2);
  });

  it('CASE 3: de-duplicates in-flight requests (double mount / parallel readers)', async () => {
    const cache = new BugsQueryCache({ ttlMs: 1000 });
    const key = activeBugsKey(100);
    let calls = 0;

    const fetcher = async () => {
      calls += 1;
      await new Promise((resolve) => setTimeout(resolve, 5));
      return [{ id: 'same-result' }];
    };

    const [a, b] = await Promise.all([cache.getOrFetch(key, fetcher), cache.getOrFetch(key, fetcher)]);

    expect(calls).toBe(1);
    expect(a).toEqual([{ id: 'same-result' }]);
    expect(b).toEqual([{ id: 'same-result' }]);
  });

  it('CASE 4: separates allocated caches per user token scope', () => {
    const one = allocatedBugsKey('token-user-111111111111', 100);
    const two = allocatedBugsKey('token-user-222222222222', 100);

    expect(one).not.toBe(two);
  });

  it('CASE 5: invalidates only selected scopes after writes', async () => {
    clearBugCache();
    await bugsQueryCache.getOrFetch(activeBugsKey(10), async () => ['a']);
    await bugsQueryCache.getOrFetch(closedBugsKey(10), async () => ['c']);
    await bugsQueryCache.getOrFetch(allocatedBugsKey('token-a', 10), async () => ['al']);

    invalidateBugCacheScopes(['active']);

    expect(bugsQueryCache.get(activeBugsKey(10))).toBeNull();
    expect(bugsQueryCache.get(closedBugsKey(10))).toEqual(['c']);
    expect(bugsQueryCache.get(allocatedBugsKey('token-a', 10))).toEqual(['al']);
  });

  it('CASE 6: clears every scope on logout for safety', async () => {
    await bugsQueryCache.getOrFetch(activeBugsKey(10), async () => ['a']);
    await bugsQueryCache.getOrFetch(closedBugsKey(10), async () => ['c']);
    await bugsQueryCache.getOrFetch(allocatedBugsKey('token-z', 10), async () => ['z']);

    clearBugCache();

    expect(bugsQueryCache.get(activeBugsKey(10))).toBeNull();
    expect(bugsQueryCache.get(closedBugsKey(10))).toBeNull();
    expect(bugsQueryCache.get(allocatedBugsKey('token-z', 10))).toBeNull();
  });
});
