/**
 * startProject, listProjects — see docs/api-contract.md.
 */

function projectRowToEnvelope_(row) {
  return {
    projectId: row['ProjectID'],
    testingAddress: row['Testing Address'],
    testingDate: row['Testing Date'],
    // Sheets auto-detects a purely-numeric cell as a Number on read regardless of how it was
    // written (e.g. Job Number "1234"), which JSON.stringify then emits as an unquoted number.
    // The client's JobNumber is a string? — System.Text.Json won't coerce a JSON number into a
    // string, so an un-normalized value here crashed the app on listProjects with
    // JsonException: DeserializeUnableToConvertValue at $.data.projects[0].jobNumber. Force it
    // back to a string (or '' for null/undefined) so the wire type is always consistent.
    jobNumber: row['Job Number'] === null || row['Job Number'] === undefined ? '' : String(row['Job Number']),
    startTime: row['Start Time'],
    stopTime: row['Stop Time'] || null,
    createdOn: row['Created On'],
  };
}

function startProject(payload) {
  if (!payload.testingAddress) {
    return errorEnvelope_('validation_failed', 'testingAddress is required.');
  }

  var now = new Date();
  var row = {
    'ProjectID': Utilities.getUuid(),
    'Testing Address': payload.testingAddress,
    'Testing Date': payload.testingDate || now,
    'Job Number': payload.jobNumber || '',
    'Start Time': now,
    'Stop Time': '',
    'Created On': now,
  };

  appendRow_(SHEET_NAMES.PROJECTS, row);
  return confirmEnvelope_(projectRowToEnvelope_(row), 'Project started.');
}

function listProjects(_payload) {
  var rows = readAllRows_(SHEET_NAMES.PROJECTS);
  var projects = rows.map(projectRowToEnvelope_);
  return confirmEnvelope_({ projects: projects }, projects.length + ' project(s).');
}

/** Raw Projects sheet row (header-keyed, not the camelCase envelope shape) for a given ProjectID, or null. Used by Report.js. */
function findProjectRowById_(projectId) {
  var rows = readAllRows_(SHEET_NAMES.PROJECTS);
  for (var i = 0; i < rows.length; i++) {
    if (rows[i]['ProjectID'] === projectId) return rows[i];
  }
  return null;
}
