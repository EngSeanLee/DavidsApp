/**
 * Shared-secret auth. See docs/decisions/0002-auth-and-secrets.md — the secret travels as a
 * JSON body field (`apiKey`), never a header, because doPost(e) can't read custom headers.
 */

function checkAuth_(request) {
  var expected = PropertiesService.getScriptProperties().getProperty('SHARED_SECRET');
  if (!expected) {
    // Not configured yet — fail closed, but with a distinct error code so it's obviously a
    // deployment issue and not a wrong-key issue on the client side.
    return { ok: false, errorCode: 'server_not_configured' };
  }
  if (!request || request.apiKey !== expected) {
    return { ok: false, errorCode: 'unauthorized' };
  }
  return { ok: true };
}
