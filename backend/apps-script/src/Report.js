/**
 * generateReport — Phase 4. See docs/api-contract.md and the locked defaults in the build spec
 * §7: on-demand trigger, DocumentApp builds a Google Doc (header + findings table grouped by
 * Room), exported to PDF via DriveApp, returned as a shareable Drive link. Independent of the
 * Gemini key.
 */

var FINDINGS_TABLE_COLUMNS_ = ['Wall', 'POS', 'Color', 'Substrate', 'State', 'Component Details', 'Reading'];

function generateReport(payload) {
  var projectId = payload.projectId;
  var project = findProjectRowById_(projectId);
  if (!project) {
    return errorEnvelope_('not_found', 'Unknown projectId.');
  }

  var findings = findingsForProject_(projectId).map(function (f) { return f.row; });
  var groupedByRoom = groupFindingsByRoom_(findings);

  var title = 'Lead Testing Report — ' + project['Testing Address'] + ' — ' + formatDate_(project['Testing Date']);
  var doc = DocumentApp.create(title);
  var body = doc.getBody();

  writeReportHeader_(body, project, findings.length);
  writeFindingsTables_(body, groupedByRoom);

  doc.saveAndClose();

  var pdfBlob = DriveApp.getFileById(doc.getId()).getAs(MimeType.PDF);
  var pdfFile = DriveApp.createFile(pdfBlob).setName(title + '.pdf');
  // "Anyone with the link" so the MAUI client (no Google auth of its own) can actually open it.
  pdfFile.setSharing(DriveApp.Access.ANYONE_WITH_LINK, DriveApp.Permission.VIEW);

  return confirmEnvelope_({ reportUrl: pdfFile.getUrl() }, 'Report generated for ' + project['Testing Address'] + '.');
}

/** Groups raw Findings rows by Room, preserving first-seen room order and original row order within each room. */
function groupFindingsByRoom_(findings) {
  var order = [];
  var byRoom = {};
  findings.forEach(function (row) {
    var room = row['Room'] || '(no room)';
    if (!byRoom[room]) {
      byRoom[room] = [];
      order.push(room);
    }
    byRoom[room].push(row);
  });
  return order.map(function (room) { return { room: room, rows: byRoom[room] }; });
}

function writeReportHeader_(body, project, findingCount) {
  body.appendParagraph(project['Testing Address']).setHeading(DocumentApp.ParagraphHeading.TITLE);

  var summary = body.appendTable([
    ['Job Number', String(project['Job Number'] || '—')],
    ['Testing Date', formatDate_(project['Testing Date'])],
    ['Start Time', formatDateTime_(project['Start Time'])],
    ['Stop Time', project['Stop Time'] ? formatDateTime_(project['Stop Time']) : '—'],
    ['Findings', String(findingCount)],
  ]);
  for (var i = 0; i < summary.getNumRows(); i++) {
    summary.getRow(i).getCell(0).setBold(true).setWidth(150);
  }
  body.appendParagraph('');
}

function writeFindingsTables_(body, groupedByRoom) {
  if (groupedByRoom.length === 0) {
    body.appendParagraph('No findings recorded for this project.');
    return;
  }

  groupedByRoom.forEach(function (group) {
    body.appendParagraph(group.room).setHeading(DocumentApp.ParagraphHeading.HEADING2);

    var tableRows = [FINDINGS_TABLE_COLUMNS_.slice()];
    group.rows.forEach(function (row) {
      tableRows.push(FINDINGS_TABLE_COLUMNS_.map(function (col) { return String(row[col] != null ? row[col] : ''); }));
    });

    var table = body.appendTable(tableRows);
    var headerRow = table.getRow(0);
    for (var c = 0; c < headerRow.getNumCells(); c++) {
      headerRow.getCell(c).setBold(true);
    }
    body.appendParagraph('');
  });
}

function formatDate_(value) {
  if (!value) return '—';
  var date = value instanceof Date ? value : new Date(value);
  return Utilities.formatDate(date, Session.getScriptTimeZone(), 'MMM d, yyyy');
}

function formatDateTime_(value) {
  if (!value) return '—';
  var date = value instanceof Date ? value : new Date(value);
  return Utilities.formatDate(date, Session.getScriptTimeZone(), 'MMM d, yyyy h:mm a');
}
