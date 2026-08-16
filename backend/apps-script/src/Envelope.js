/**
 * Response envelope helpers — see docs/api-contract.md "Response envelope".
 */

function confirmEnvelope_(data, message) {
  return { status: 'confirm', data: data || {}, message: message || '' };
}

function missingFieldEnvelope_(field, message, pendingRowSoFar) {
  return { status: 'missing_field', data: { field: field, pendingRowSoFar: pendingRowSoFar || {} }, message: message };
}

function unknownValueEnvelope_(category, rawValue, pendingRowSoFar, message) {
  return {
    status: 'unknown_value',
    data: { category: category, rawValue: rawValue, pendingRowSoFar: pendingRowSoFar || {} },
    message: message,
  };
}

function errorEnvelope_(errorCode, message) {
  return { status: 'error', data: {}, message: message || '', errorCode: errorCode };
}
