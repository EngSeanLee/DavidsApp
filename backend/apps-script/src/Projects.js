/**
 * startProject, listProjects — see docs/api-contract.md.
 */

function projectRowToEnvelope_(row) {
  return {
    projectId: row['ProjectID'],
    testingAddress: row['Testing Address'],
    testingDate: row['Testing Date'],
    jobNumber: row['Job Number'],
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
