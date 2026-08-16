/**
 * API_Log writer. Never logs the raw apiKey value — only enough to debug an action, not
 * enough to replay one.
 */

function logApiCall_(action, projectId, responseEnvelope, requestSummary) {
  try {
    appendRow_(SHEET_NAMES.API_LOG, {
      'Timestamp': new Date(),
      'Action': action || '',
      'ProjectID': projectId || '',
      'Status': responseEnvelope ? responseEnvelope.status : '',
      'ErrorCode': (responseEnvelope && responseEnvelope.errorCode) || '',
      'RequestSummary': requestSummary ? JSON.stringify(requestSummary).slice(0, 500) : '',
      'ResponseSummary': responseEnvelope ? JSON.stringify(responseEnvelope.data).slice(0, 500) : '',
    });
  } catch (loggingError) {
    // Logging must never break the actual request.
    Logger.log('logApiCall_ failed: ' + loggingError);
  }
}

/** Strips the apiKey (and anything else sensitive) before a payload is used as a log summary. */
function redactForLog_(payload) {
  if (!payload || typeof payload !== 'object') return payload;
  var copy = {};
  Object.keys(payload).forEach(function (k) {
    if (k === 'apiKey') return;
    copy[k] = payload[k];
  });
  return copy;
}
