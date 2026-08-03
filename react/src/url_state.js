export const SHAREABLE_FILTER_KEYS = ['priority', 'severity', 'tag', 'projectId', 'assigneeUserId', 'reporterUserId'];

export function readAppUrlState() {
  const params = new URLSearchParams(typeof window === 'undefined' ? '' : window.location.search);
  return {
    view: params.get('view') || 'dashboard',
    search: params.get('q') || params.get('search') || '',
    quick: params.get('quick') || 'all',
    ticket: params.get('ticket') || '',
    filters: Object.fromEntries(SHAREABLE_FILTER_KEYS.map((key) => [key, params.get(key) || '']))
  };
}

export function writeAppUrlState(next, { replace = false } = {}) {
  if (typeof window === 'undefined') return;
  const current = readAppUrlState();
  const value = {
    ...current,
    ...next,
    filters: next.filters === undefined
      ? current.filters
      : { ...Object.fromEntries(SHAREABLE_FILTER_KEYS.map((key) => [key, ''])), ...next.filters }
  };
  const params = new URLSearchParams();
  if (value.view && value.view !== 'dashboard') params.set('view', value.view);
  if (value.search) params.set('q', value.search);
  if (value.quick && value.quick !== 'all') params.set('quick', value.quick);
  SHAREABLE_FILTER_KEYS.forEach((key) => { if (value.filters[key]) params.set(key, value.filters[key]); });
  if (value.ticket) params.set('ticket', value.ticket);
  const url = `${window.location.pathname}${params.size ? `?${params}` : ''}`;
  window.history[replace ? 'replaceState' : 'pushState']({}, '', url);
}
