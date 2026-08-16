/**
 * doPost(e) entry point — see docs/api-contract.md for the full envelope contract.
 */

var ACTIONS_ = {
  startProject: startProject,
  listProjects: listProjects,
  getLastSavedRow: getLastSavedRow,
  parseFinding: parseFinding,
  saveFinding: saveFinding,
  deleteLastFinding: deleteLastFinding,
  resolveMissingField: resolveMissingField,
  resolveVocabulary: resolveVocabulary,
  generateReport: generateReport,
};

function doPost(e) {
  var request;
  var response;
  var action = '';
  var projectId = '';

  try {
    request = JSON.parse(e.postData.contents);
    action = request.action;

    var auth = checkAuth_(request);
    if (!auth.ok) {
      response = errorEnvelope_(auth.errorCode, 'Authentication failed.');
      return respond_(response);
    }

    var handler = ACTIONS_[action];
    if (!handler) {
      response = errorEnvelope_('unknown_action', 'Unknown action: ' + action);
      return respond_(response);
    }

    var payload = request.payload || {};
    projectId = payload.projectId || '';
    response = handler(payload);
  } catch (err) {
    response = errorEnvelope_('internal_error', String(err && err.message ? err.message : err));
  }

  // Never log the raw apiKey; redact before writing the request summary.
  logApiCall_(action, projectId, response, request ? redactForLog_(request.payload) : null);
  return respond_(response);
}

function respond_(envelope) {
  return ContentService.createTextOutput(JSON.stringify(envelope)).setMimeType(ContentService.MimeType.JSON);
}
