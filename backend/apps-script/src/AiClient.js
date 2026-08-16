/**
 * Natural-language extraction for parseFinding / resolveMissingField, via Google Gemini
 * (Google AI Studio API key). See docs/decisions/0003-gemini-instead-of-openai.md — the original
 * spec assumed OpenAI throughout; this file's name and the GEMINI_API_KEY script property reflect
 * the substitution.
 *
 * Design principle carried over from the rest of the backend ("Apps Script stays authoritative
 * for field requirements, normalization, vocabulary"): Gemini's only job is turning speech into
 * structured fields (including resolving shorthand like "same reading 1.2" against context this
 * function is given). It does NOT decide what's missing or what's unknown vocabulary — the
 * already-existing, already-tested validateFindingRow_ (Findings.js) does that, deterministically,
 * against the real Settings sheet. This keeps behavior consistent regardless of model quirks and
 * reuses code instead of duplicating validation logic inside a prompt.
 */

// Google GA'd a new "Interactions API" in June 2026 and is retiring generateContent for new
// accounts (confirmed via a live 404 from this exact key: "This model models/gemini-2.5-flash is
// no longer available to new users... use the Interactions API"). Endpoint/shape below per
// https://ai.google.dev/gemini-api/docs/migrate-to-interactions and
// https://ai.google.dev/gemini-api/docs/structured-output — verified against the live API in
// backend/apps-script (see docs/decisions/0003-gemini-instead-of-openai.md for the trail).
var GEMINI_MODEL_ = 'gemini-3.6-flash';
var GEMINI_INTERACTIONS_URL_ = 'https://generativelanguage.googleapis.com/v1beta/interactions';

// `required` matters a lot here, not just as documentation: without it, the model treated an
// empty/partial object as a genuinely "completed" response (confirmed via a live raw response —
// status: "completed", 21 output tokens, 0 thought tokens — it wasn't being cut off, it was
// satisficing). Marking every property required forces constrained decoding to populate all of
// them; the prompt below tells the model to use "" for whatever it truly can't determine, since
// omission is no longer a valid response under this schema.
var TERSE_FIELD_HINT_ = 'One or two words maximum. No explanation, no reasoning, no alternatives — just the final value, or "" if not determinable.';
var FINDING_FIELDS_ = ['room', 'wall', 'pos', 'color', 'substrate', 'state', 'component', 'reading'];
var FINDING_RESPONSE_SCHEMA_ = {
  type: 'object',
  properties: {
    room: { type: 'string', description: TERSE_FIELD_HINT_ },
    wall: { type: 'string', description: TERSE_FIELD_HINT_ },
    pos: { type: 'string', description: TERSE_FIELD_HINT_ },
    color: { type: 'string', description: TERSE_FIELD_HINT_ },
    substrate: { type: 'string', description: TERSE_FIELD_HINT_ },
    state: { type: 'string', description: TERSE_FIELD_HINT_ },
    component: { type: 'string', description: TERSE_FIELD_HINT_ },
    reading: { type: 'string', description: 'A plain decimal number as a string (e.g. "1.2"), or "" if not determinable. Nothing else.' },
  },
  required: FINDING_FIELDS_,
};

/**
 * Extracts as many Finding fields as it can from `transcript`, given prior context. Gemini's
 * response always has all 8 keys (schema-required — see FINDING_RESPONSE_SCHEMA_'s comment for
 * why that's necessary), using "" for whatever it isn't confident about; this function turns that
 * back into a sparse object (only the fields it actually determined, falling back to
 * pendingRowSoFar otherwise) for callers to merge in.
 *
 * @param {string} transcript what the inspector said (or, for resolveMissingField, the value they
 *   gave for one specific field)
 * @param {Object} pendingRowSoFar already-known fields for the finding in progress (empty {} for a fresh parseFinding)
 * @param {Object|null} lastRow the project's most recently saved finding, for shorthand ("same room")
 */
function interpretTranscript_(transcript, pendingRowSoFar, lastRow) {
  var prompt = buildFindingPrompt_(transcript, pendingRowSoFar, lastRow);
  var result = callGemini_(prompt, FINDING_RESPONSE_SCHEMA_);
  var merged = {};
  FINDING_FIELDS_.forEach(function (field) {
    var fromModel = result && result[field];
    if (fromModel !== undefined && fromModel !== null && fromModel !== '') {
      merged[field] = fromModel;
    } else if (pendingRowSoFar && pendingRowSoFar[field] !== undefined) {
      merged[field] = pendingRowSoFar[field];
    }
  });
  return merged;
}

function buildFindingPrompt_(transcript, pendingRowSoFar, lastRow) {
  return [
    'You are extracting structured data from a spoken lead-paint inspection finding for a fixed',
    'schema: room, wall, pos (position on the wall, e.g. Trim/Sill/Casing), color, substrate',
    '(e.g. Wood, Plaster, Metal), state (paint condition), component (e.g. Window Sill, Door',
    'Frame, Baseboard), reading (a decimal XRF lead reading).',
    '',
    'The inspector may use shorthand referring to the previous finding, e.g. "same room, reading',
    '1.2" or "same wall and component, but color is white" — when they do, carry over the relevant',
    'field(s) from PREVIOUS FINDING below and only change what they explicitly said differs.',
    '',
    'PREVIOUS FINDING (may be empty if none yet): ' + JSON.stringify(lastRow || {}),
    'CURRENT PARTIAL FINDING (already-known fields for the one in progress): ' + JSON.stringify(pendingRowSoFar || {}),
    '',
    'Known room values so far: ' + JSON.stringify(knownVocabularyValues_('ROOM')),
    'Known pos values so far: ' + JSON.stringify(knownVocabularyValues_('POS')),
    'Known color values so far: ' + JSON.stringify(knownVocabularyValues_('COLOR')),
    'Known substrate values so far: ' + JSON.stringify(knownVocabularyValues_('SUBSTRATE')),
    'Known component values so far: ' + JSON.stringify(knownVocabularyValues_('COMPONENT')),
    'state must be exactly one of: ' + JSON.stringify(STATE_VALUES_) + '.',
    '',
    'Inspector said: "' + transcript + '"',
    '',
    'Extract the finding\'s fields. Every field is required in your response, but set a field to',
    '"" if you are not confident of its value from the inspector\'s statement, the previous',
    'finding (for shorthand), or the current partial finding above. Match room/pos/',
    'color/substrate/component to one of the "known" values above when the inspector\'s wording is',
    'clearly the same thing (minor phrasing/pluralization differences are fine), but if it\'s',
    'clearly a new value not in the list, return exactly what the inspector said so the system can',
    'ask whether to add it as a new vocabulary entry. reading must be a plain decimal number as a',
    'string (e.g. "1.2"), converting spoken-word numbers ("one point two") to digits.',
    '',
    'IMPORTANT: every field value must be ONE OR TWO WORDS ONLY (or a bare number for reading),',
    'or "" if not determinable. Do not explain your reasoning, do not list alternatives, do not',
    'describe your thought process — those do not belong in a field value.',
  ].join('\n');
}

function callGemini_(prompt, responseSchema) {
  var apiKey = PropertiesService.getScriptProperties().getProperty('GEMINI_API_KEY');
  if (!apiKey) {
    throw new AiNotConfiguredError_('GEMINI_API_KEY is not set.');
  }

  var requestBody = {
    model: GEMINI_MODEL_,
    input: prompt,
    // Thinking defaults to high on this model and eats the vast majority of the token budget
    // even on trivial prompts (observed: 146/164 tokens on a "say hello" test) — for the full
    // finding-extraction prompt this was starving the actual JSON output, producing
    // truncated/garbled results. "low" is a documented value for gemini-3-flash-preview; unclear
    // if gemini-3.6-flash accepts the same set, so callGemini_ still works if this is ignored.
    generation_config: { thinking_level: 'low' },
    response_format: {
      type: 'text',
      mime_type: 'application/json',
      schema: responseSchema,
    },
  };

  var response = UrlFetchApp.fetch(GEMINI_INTERACTIONS_URL_, {
    method: 'post',
    contentType: 'application/json',
    headers: { 'x-goog-api-key': apiKey },
    payload: JSON.stringify(requestBody),
    muteHttpExceptions: true,
  });

  var status = response.getResponseCode();
  var body = response.getContentText();

  if (status !== 200) {
    // Surface enough of the real error to debug (bad key, quota, model name, etc.) without
    // logging the API key itself.
    throw new Error('Gemini API returned HTTP ' + status + ': ' + body.slice(0, 500));
  }

  var text = extractInteractionsText_(JSON.parse(body));
  if (!text) {
    throw new Error('Gemini response had no extractable text: ' + body.slice(0, 800));
  }

  return JSON.parse(text);
}

/**
 * Confirmed against a live raw response (2026-08-16, model gemini-3.6-flash): `steps` is a
 * chronological array — a `thought` step (an opaque signed blob, not readable text — that's by
 * design, not a bug) followed by a `model_output` step whose `content` is an array of
 * `{ type: "text", text: "..." }` parts. Legacy generateContent's `candidates[]` shape kept as a
 * fallback only, in case a future account/key ever routes there instead.
 */
function extractInteractionsText_(parsed) {
  if (Array.isArray(parsed.steps)) {
    for (var i = 0; i < parsed.steps.length; i++) {
      var step = parsed.steps[i];
      if (step && step.type === 'model_output' && Array.isArray(step.content)) {
        for (var j = 0; j < step.content.length; j++) {
          if (step.content[j] && step.content[j].type === 'text' && step.content[j].text) {
            return step.content[j].text;
          }
        }
      }
    }
  }

  var legacyText = parsed.candidates && parsed.candidates[0] && parsed.candidates[0].content &&
    parsed.candidates[0].content.parts && parsed.candidates[0].content.parts[0] &&
    parsed.candidates[0].content.parts[0].text;
  if (legacyText) return legacyText;

  return null;
}

function AiNotConfiguredError_(message) {
  this.name = 'AiNotConfiguredError_';
  this.message = message;
}
AiNotConfiguredError_.prototype = Object.create(Error.prototype);
