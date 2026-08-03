export function readDemoConfig(documentRef = globalThis.document) {
  const encoded = documentRef?.querySelector('meta[name="bug-tracker-demo"]')?.getAttribute('content');
  if (!encoded || encoded === '__DEMO_CONFIG__') return null;

  try {
    return JSON.parse(globalThis.atob(encoded));
  } catch {
    return null;
  }
}
