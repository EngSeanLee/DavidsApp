/**
 * Low-level Sheets access: tab bootstrap, header-mapped row read/write, and a
 * LockService wrapper for read-modify-write action handlers.
 *
 * Schema source of truth: docs/api-contract.md + docs/Lead_Testing_Voice_Entry_Build_Spec.md §3.
 */

var SHEET_NAMES = {
  PROJECTS: 'Projects',
  FINDINGS: 'Findings',
  SETTINGS: 'Settings',
  API_LOG: 'API_Log',
};

var SHEET_HEADERS = {
  Projects: ['ProjectID', 'Testing Address', 'Testing Date', 'Job Number', 'Start Time', 'Stop Time', 'Created On'],
  Findings: ['ProjectID', '#', 'Room', 'Wall', 'POS', 'Color', 'Substrate', 'State', 'Component Details', 'Reading', 'Entered On'],
  Settings: ['Type', 'Value', 'Normalized'],
  API_Log: ['Timestamp', 'Action', 'ProjectID', 'Status', 'ErrorCode', 'RequestSummary', 'ResponseSummary'],
};

/**
 * One-time (or idempotent) bootstrap: creates any missing tabs with their header row.
 * Run manually once from the Apps Script editor (function picker → setupSheets → Run),
 * or re-run any time — it only creates what's missing, never touches existing data.
 */
function setupSheets() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  Object.keys(SHEET_HEADERS).forEach(function (name) {
    var sheet = ss.getSheetByName(name);
    if (!sheet) {
      sheet = ss.insertSheet(name);
    }
    var headers = SHEET_HEADERS[name];
    var existingHeaderRange = sheet.getRange(1, 1, 1, headers.length);
    var existingValues = existingHeaderRange.getValues()[0];
    var headersMatch = headers.every(function (h, i) { return existingValues[i] === h; });
    if (!headersMatch) {
      existingHeaderRange.setValues([headers]);
      sheet.setFrozenRows(1);
    }
  });

  // Remove the default "Sheet1" that clasp's `create --type sheets` leaves behind, if empty.
  var defaultSheet = ss.getSheetByName('Sheet1');
  if (defaultSheet && defaultSheet.getLastRow() === 0) {
    ss.deleteSheet(defaultSheet);
  }

  Logger.log('setupSheets complete: ' + Object.keys(SHEET_HEADERS).join(', '));
}

function getSheet_(name) {
  var sheet = SpreadsheetApp.getActiveSpreadsheet().getSheetByName(name);
  if (!sheet) {
    throw new Error('Sheet not found: ' + name + ' — run setupSheets() first.');
  }
  return sheet;
}

function getHeaders_(sheet) {
  var lastCol = sheet.getLastColumn();
  return sheet.getRange(1, 1, 1, lastCol).getValues()[0];
}

/** Reads all data rows of a sheet as an array of header-keyed objects. */
function readAllRows_(sheetName) {
  var sheet = getSheet_(sheetName);
  var lastRow = sheet.getLastRow();
  if (lastRow < 2) return [];
  var headers = getHeaders_(sheet);
  var values = sheet.getRange(2, 1, lastRow - 1, headers.length).getValues();
  return values.map(function (row) {
    var obj = {};
    headers.forEach(function (h, i) { obj[h] = row[i]; });
    return obj;
  });
}

/** Appends a single header-keyed object as a new row. Returns the row object as written. */
function appendRow_(sheetName, rowObject) {
  var sheet = getSheet_(sheetName);
  var headers = getHeaders_(sheet);
  var row = headers.map(function (h) { return Object.prototype.hasOwnProperty.call(rowObject, h) ? rowObject[h] : ''; });
  sheet.appendRow(row);
  return rowObject;
}

/** Deletes the sheet's last physical row and returns it as a header-keyed object, or null if no data rows exist. */
function deleteLastRow_(sheetName) {
  var sheet = getSheet_(sheetName);
  var lastRow = sheet.getLastRow();
  if (lastRow < 2) return null;
  var headers = getHeaders_(sheet);
  var values = sheet.getRange(lastRow, 1, 1, headers.length).getValues()[0];
  var obj = {};
  headers.forEach(function (h, i) { obj[h] = values[i]; });
  sheet.deleteRow(lastRow);
  return obj;
}

/**
 * Returns [{ rowIndex, row }] for every data row matching predicate(rowObject), in sheet order.
 * rowIndex is the 1-based physical sheet row (header is row 1), suitable for deleteRowAtIndex_.
 */
function findRowsWithIndex_(sheetName, predicate) {
  var sheet = getSheet_(sheetName);
  var lastRow = sheet.getLastRow();
  if (lastRow < 2) return [];
  var headers = getHeaders_(sheet);
  var values = sheet.getRange(2, 1, lastRow - 1, headers.length).getValues();
  var results = [];
  values.forEach(function (rowValues, i) {
    var obj = {};
    headers.forEach(function (h, colIdx) { obj[h] = rowValues[colIdx]; });
    if (predicate(obj)) {
      results.push({ rowIndex: i + 2, row: obj });
    }
  });
  return results;
}

function deleteRowAtIndex_(sheetName, rowIndex) {
  getSheet_(sheetName).deleteRow(rowIndex);
}

/**
 * Runs fn() while holding the script lock, waiting up to timeoutMs. Use around any
 * read-modify-write sequence (saveFinding, deleteLastFinding, getLastSavedRow's callers)
 * so concurrent callers (two devices, two inspectors) can't race on the same rows.
 */
function withLock_(fn, timeoutMs) {
  var lock = LockService.getScriptLock();
  lock.waitLock(timeoutMs || 10000);
  try {
    return fn();
  } finally {
    lock.releaseLock();
  }
}
