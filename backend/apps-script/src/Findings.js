/**
 * getLastSavedRow, saveFinding, deleteLastFinding (CRUD), plus parseFinding /
 * resolveMissingField (AI-backed via AiClient.js / Gemini — see
 * docs/decisions/0003-gemini-instead-of-openai.md). See docs/api-contract.md.
 */

var REQUIRED_FINDING_FIELDS_ = ['room', 'wall', 'pos', 'color', 'substrate', 'state', 'component', 'reading'];

function findingRowToEnvelope_(row) {
  return {
    projectId: row['ProjectID'],
    number: row['#'],
    room: row['Room'],
    wall: row['Wall'],
    pos: row['POS'],
    color: row['Color'],
    substrate: row['Substrate'],
    state: row['State'],
    component: row['Component Details'],
    reading: row['Reading'],
    enteredOn: row['Entered On'],
  };
}

function findingsForProject_(projectId) {
  return findRowsWithIndex_(SHEET_NAMES.FINDINGS, function (r) { return r['ProjectID'] === projectId; });
}

function getLastSavedRow(payload) {
  var matches = findingsForProject_(payload.projectId);
  if (matches.length === 0) {
    return confirmEnvelope_({ lastRow: null }, 'No findings saved yet for this project.');
  }
  var last = matches[matches.length - 1].row;
  return confirmEnvelope_({ lastRow: findingRowToEnvelope_(last) }, 'Last saved row retrieved.');
}

var CATEGORY_BY_FIELD_ = { room: 'ROOM', pos: 'POS', color: 'COLOR', substrate: 'SUBSTRATE', component: 'COMPONENT' };

function validateFindingRow_(row) {
  var missing = REQUIRED_FINDING_FIELDS_.filter(function (f) { return row[f] === undefined || row[f] === null || row[f] === ''; });
  if (missing.length > 0) {
    return { ok: false, field: missing[0] };
  }
  if (!isKnownVocabularyValue_('STATE', row.state)) {
    // STATE is closed vocabulary — not user-extensible, so this is a validation failure, not an
    // unknown-value resolution opportunity.
    return { ok: false, field: 'state', invalidClosedVocabulary: true };
  }
  for (var field in CATEGORY_BY_FIELD_) {
    if (!isKnownVocabularyValue_(CATEGORY_BY_FIELD_[field], row[field])) {
      return { ok: false, field: field, unknownVocabulary: true, category: CATEGORY_BY_FIELD_[field] };
    }
  }
  if (isNaN(parseFloat(row.reading))) {
    return { ok: false, field: 'reading' };
  }
  return { ok: true };
}

function saveFinding(payload) {
  var projectId = payload.projectId;
  var row = payload.row || {};

  var validation = validateFindingRow_(row);
  if (!validation.ok) {
    if (validation.unknownVocabulary) {
      return unknownValueEnvelope_(validation.category, row[validation.field], row, 'Unrecognized ' + validation.field + ': "' + row[validation.field] + '". Add it?');
    }
    if (validation.invalidClosedVocabulary) {
      return missingFieldEnvelope_(validation.field, 'Invalid value for ' + validation.field + ' — must be one of the recognized states.', row);
    }
    return missingFieldEnvelope_(validation.field, 'Missing required field: ' + validation.field + '.', row);
  }

  return withLock_(function () {
    var existing = findingsForProject_(projectId);
    var nextNumber = existing.length + 1;
    var now = new Date();

    var sheetRow = {
      'ProjectID': projectId,
      '#': nextNumber,
      'Room': row.room,
      'Wall': row.wall,
      'POS': row.pos,
      'Color': row.color,
      'Substrate': row.substrate,
      'State': row.state,
      'Component Details': row.component,
      'Reading': parseFloat(row.reading).toFixed(1),
      'Entered On': now,
    };

    appendRow_(SHEET_NAMES.FINDINGS, sheetRow);
    return confirmEnvelope_({ savedRow: findingRowToEnvelope_(sheetRow) }, 'Finding #' + nextNumber + ' saved.');
  });
}

function deleteLastFinding(payload) {
  var projectId = payload.projectId;

  return withLock_(function () {
    var matches = findingsForProject_(projectId);
    if (matches.length === 0) {
      return errorEnvelope_('not_found', 'No findings to delete for this project.');
    }
    var last = matches[matches.length - 1];
    deleteRowAtIndex_(SHEET_NAMES.FINDINGS, last.rowIndex);

    var remaining = matches.slice(0, -1);
    var previousLastRow = remaining.length > 0 ? findingRowToEnvelope_(remaining[remaining.length - 1].row) : null;

    return confirmEnvelope_(
      { deletedRow: findingRowToEnvelope_(last.row), previousLastRow: previousLastRow },
      'Finding #' + last.row['#'] + ' deleted.'
    );
  });
}

/**
 * Builds the appropriate response envelope for a candidate (possibly incomplete) finding row —
 * shared by parseFinding and resolveMissingField, since both end up needing the same
 * confirm/missing_field/unknown_value decision once Gemini has done its extraction.
 */
function respondForCandidateRow_(row) {
  var validation = validateFindingRow_(row);
  if (validation.ok) {
    return confirmEnvelope_({ pendingRow: row }, 'Parsed finding.');
  }
  if (validation.unknownVocabulary) {
    return unknownValueEnvelope_(validation.category, row[validation.field], row, 'Unrecognized ' + validation.field + ': "' + row[validation.field] + '". Add it?');
  }
  if (validation.invalidClosedVocabulary) {
    return missingFieldEnvelope_(validation.field, 'What is the ' + validation.field + '? Must be one of the recognized states.', row);
  }
  return missingFieldEnvelope_(validation.field, 'What is the ' + validation.field + '?', row);
}

function parseFinding(payload) {
  try {
    var lastRow = payload.lastRow || null;
    var interpreted = interpretTranscript_(payload.transcript, {}, lastRow);
    return respondForCandidateRow_(interpreted);
  } catch (err) {
    if (err && err.name === 'AiNotConfiguredError_') {
      return errorEnvelope_('not_configured', 'parseFinding needs a GEMINI_API_KEY script property — not yet configured. See backend/apps-script/README.md.');
    }
    return errorEnvelope_('upstream_error', 'parseFinding failed: ' + String(err && err.message ? err.message : err));
  }
}

function resolveMissingField(payload) {
  try {
    var pendingRowSoFar = payload.pendingRowSoFar || {};
    var interpreted = interpretTranscript_(payload.value, pendingRowSoFar, null);
    // Defensive: make sure the specific field being resolved actually ends up set even if
    // Gemini's extraction of a short value (e.g. just "1.2") didn't map it to the right key —
    // an explicit fallback assignment beats a silently-still-missing field.
    if (interpreted[payload.field] === undefined || interpreted[payload.field] === '') {
      interpreted[payload.field] = payload.value;
    }
    return respondForCandidateRow_(interpreted);
  } catch (err) {
    if (err && err.name === 'AiNotConfiguredError_') {
      return errorEnvelope_('not_configured', 'resolveMissingField needs a GEMINI_API_KEY script property — not yet configured.');
    }
    return errorEnvelope_('upstream_error', 'resolveMissingField failed: ' + String(err && err.message ? err.message : err));
  }
}
