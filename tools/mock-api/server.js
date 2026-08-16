#!/usr/bin/env node
'use strict';

/**
 * Mock of the Apps Script doPost JSON envelope contract (docs/api-contract.md), for developing
 * the MAUI client before the real backend deployment (or an OpenAI key) exist. Zero npm
 * dependencies on purpose — `node server.js` and it's running.
 *
 * In-memory only: state resets every time you restart the process.
 *
 * parseFinding/resolveMissingField are canned rather than real NLP — the transcript text itself
 * selects which response shape comes back, so the client's full confirm/missing_field/
 * unknown_value/error state-machine paths can all be exercised on demand:
 *   transcript contains "missing"  -> missing_field (field: "reading")
 *   transcript contains "unknown"  -> unknown_value (category: "ROOM", rawValue: "Attic")
 *   transcript contains "error"    -> error (errorCode: "mock_error")
 *   anything else                  -> confirm, with a canned complete pendingRow
 */

const http = require('http');

const PORT = process.env.MOCK_API_PORT || 4127;
const API_KEY = process.env.MOCK_API_KEY || 'dev-mock-key';

/** @type {Map<string, object>} */
const projects = new Map();
/** @type {Map<string, object[]>} projectId -> ordered findings */
const findings = new Map();

function confirm(data, message) {
  return { status: 'confirm', data: data || {}, message: message || '' };
}
function missingField(field, message, pendingRowSoFar) {
  return { status: 'missing_field', data: { field, pendingRowSoFar: pendingRowSoFar || {} }, message };
}
function unknownValue(category, rawValue, pendingRowSoFar, message) {
  return { status: 'unknown_value', data: { category, rawValue, pendingRowSoFar: pendingRowSoFar || {} }, message };
}
function error(errorCode, message) {
  return { status: 'error', data: {}, errorCode, message: message || '' };
}

const CANNED_FINDING = { room: 'Kitchen', wall: 'North', pos: 'Trim', color: 'White', substrate: 'Wood', state: 'Intact', component: 'Window Sill', reading: '1.2' };

const actions = {
  startProject(payload) {
    if (!payload.testingAddress) return error('validation_failed', 'testingAddress is required.');
    const now = new Date().toISOString();
    const project = {
      projectId: `mock-${Date.now()}-${Math.floor(Math.random() * 1000)}`,
      testingAddress: payload.testingAddress,
      testingDate: payload.testingDate || now,
      jobNumber: payload.jobNumber || '',
      startTime: now,
      stopTime: null,
      createdOn: now,
    };
    projects.set(project.projectId, project);
    findings.set(project.projectId, []);
    return confirm(project, 'Project started.');
  },

  listProjects() {
    return confirm({ projects: Array.from(projects.values()) }, `${projects.size} project(s).`);
  },

  getLastSavedRow(payload) {
    const rows = findings.get(payload.projectId) || [];
    return confirm({ lastRow: rows.length ? rows[rows.length - 1] : null }, 'Last saved row retrieved.');
  },

  parseFinding(payload) {
    const t = (payload.transcript || '').toLowerCase();
    if (t.includes('missing')) return missingField('reading', 'What is the reading?', { room: 'Kitchen' });
    if (t.includes('unknown')) return unknownValue('ROOM', 'Attic', {}, 'Unrecognized room: "Attic". Add it?');
    if (t.includes('error')) return error('mock_error', 'Simulated failure for testing.');
    return confirm({ pendingRow: { ...CANNED_FINDING } }, 'Parsed finding.');
  },

  resolveMissingField(payload) {
    const row = { ...(payload.pendingRowSoFar || {}), [payload.field]: payload.value };
    return confirm({ pendingRow: { ...CANNED_FINDING, ...row } }, 'Field resolved.');
  },

  resolveVocabulary(payload) {
    const fieldByCategory = { ROOM: 'room', POS: 'pos', COLOR: 'color', SUBSTRATE: 'substrate', COMPONENT: 'component' };
    const field = fieldByCategory[payload.category];
    if (!field) return error('validation_failed', `Unknown category: ${payload.category}`);
    if (!payload.accepted) return missingField(field, `What is the correct ${field}?`, payload.pendingRowSoFar || {});
    const row = { ...(payload.pendingRowSoFar || {}), [field]: payload.normalizedValue || payload.rawValue };
    return confirm({ pendingRow: row }, `${payload.category} "${payload.rawValue}" added to vocabulary.`);
  },

  saveFinding(payload) {
    const rows = findings.get(payload.projectId);
    if (!rows) return error('not_found', 'Unknown projectId.');
    const number = rows.length + 1;
    const savedRow = { ...payload.row, projectId: payload.projectId, number, enteredOn: new Date().toISOString() };
    rows.push(savedRow);
    return confirm({ savedRow }, `Finding #${number} saved.`);
  },

  deleteLastFinding(payload) {
    const rows = findings.get(payload.projectId);
    if (!rows || rows.length === 0) return error('not_found', 'No findings to delete for this project.');
    const deletedRow = rows.pop();
    const previousLastRow = rows.length ? rows[rows.length - 1] : null;
    return confirm({ deletedRow, previousLastRow }, `Finding #${deletedRow.number} deleted.`);
  },

  generateReport(payload) {
    if (!projects.has(payload.projectId)) return error('not_found', 'Unknown projectId.');
    return confirm({ reportUrl: 'https://example.com/mock-report.pdf' }, 'Mock report generated (not a real PDF).');
  },
};

const server = http.createServer((req, res) => {
  if (req.method !== 'POST') {
    res.writeHead(405, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify(error('method_not_allowed', 'Only POST is supported.')));
    return;
  }

  let body = '';
  req.on('data', (chunk) => { body += chunk; });
  req.on('end', () => {
    res.setHeader('Content-Type', 'application/json');
    res.setHeader('Access-Control-Allow-Origin', '*');

    let request;
    try {
      request = JSON.parse(body || '{}');
    } catch {
      res.writeHead(400);
      res.end(JSON.stringify(error('invalid_json', 'Request body was not valid JSON.')));
      return;
    }

    if (request.apiKey !== API_KEY) {
      res.writeHead(200);
      res.end(JSON.stringify(error('unauthorized', 'Authentication failed.')));
      return;
    }

    const handler = actions[request.action];
    if (!handler) {
      res.writeHead(200);
      res.end(JSON.stringify(error('unknown_action', `Unknown action: ${request.action}`)));
      return;
    }

    let response;
    try {
      response = handler(request.payload || {});
    } catch (err) {
      response = error('internal_error', String((err && err.message) || err));
    }
    res.writeHead(200);
    res.end(JSON.stringify(response));
  });
});

server.listen(PORT, () => {
  console.log(`mock-api listening on http://localhost:${PORT}/ (apiKey: "${API_KEY}")`);
});
