/**
 * resolveVocabulary + shared vocabulary lookups used by both the CRUD-only actions here and by
 * parseFinding/resolveMissingField (Gemini-backed — see AiClient.js).
 *
 * See docs/api-contract.md and spec §3 for the ROOM/POS/COLOR/SUBSTRATE/COMPONENT (dynamic,
 * user-extensible via Settings) vs STATE (fixed, closed) split.
 */

var DYNAMIC_CATEGORIES_ = ['ROOM', 'POS', 'COLOR', 'SUBSTRATE', 'COMPONENT'];

var CATEGORY_TO_FIELD_ = {
  ROOM: 'room',
  POS: 'pos',
  COLOR: 'color',
  SUBSTRATE: 'substrate',
  COMPONENT: 'component',
};

/**
 * PLACEHOLDER default — the spec (§3) names STATE as a fixed/closed vocabulary but doesn't
 * enumerate its values. Seeded here with the common HUD/EPA lead-paint condition ratings as a
 * starting point; confirm/edit with the user before relying on this list, then update here
 * (not just in the Sheet, since STATE is validated in code, not looked up from Settings).
 */
var STATE_VALUES_ = ['Intact', 'Fair', 'Poor'];

/** True if `value` (case-insensitive) is already known for `category`. */
function isKnownVocabularyValue_(category, value) {
  if (!value) return false;
  var normalized = String(value).trim().toLowerCase();
  if (category === 'STATE') {
    return STATE_VALUES_.some(function (v) { return v.toLowerCase() === normalized; });
  }
  var settingsRows = readAllRows_(SHEET_NAMES.SETTINGS).filter(function (r) { return r['Type'] === category; });
  return settingsRows.some(function (r) {
    return String(r['Value']).trim().toLowerCase() === normalized || String(r['Normalized']).trim().toLowerCase() === normalized;
  });
}

/** Unique known values for a dynamic category (Normalized form), for feeding to AiClient.js as context. */
function knownVocabularyValues_(category) {
  var seen = {};
  var values = [];
  readAllRows_(SHEET_NAMES.SETTINGS)
    .filter(function (r) { return r['Type'] === category; })
    .forEach(function (r) {
      var normalized = String(r['Normalized'] || r['Value']).trim();
      if (normalized && !seen[normalized.toLowerCase()]) {
        seen[normalized.toLowerCase()] = true;
        values.push(normalized);
      }
    });
  return values;
}

function resolveVocabulary(payload) {
  var category = payload.category;
  var pendingRow = payload.pendingRowSoFar || {};

  if (DYNAMIC_CATEGORIES_.indexOf(category) === -1) {
    return errorEnvelope_('validation_failed', 'resolveVocabulary does not accept category: ' + category);
  }

  var field = CATEGORY_TO_FIELD_[category];

  if (!payload.accepted) {
    return missingFieldEnvelope_(field, 'What is the correct ' + field + '?', pendingRow);
  }

  var normalized = payload.normalizedValue || payload.rawValue;
  appendRow_(SHEET_NAMES.SETTINGS, { 'Type': category, 'Value': payload.rawValue, 'Normalized': normalized });

  pendingRow[field] = normalized;
  return confirmEnvelope_({ pendingRow: pendingRow }, category + ' "' + normalized + '" added to vocabulary.');
}
