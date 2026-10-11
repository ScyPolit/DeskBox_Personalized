'use strict';

const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { DatabaseSync } = require('node:sqlite');

const SQLITE_HEADER = Buffer.from('SQLite format 3\0', 'ascii');
const DATABASE_NAMES = ['task.db', 'todos.db'];

function parseArguments(argv) {
  const result = {};
  for (let index = 0; index < argv.length; index += 1) {
    const key = argv[index];
    if (!key.startsWith('--')) {
      throw new Error('Arguments must start with --name.');
    }
    const next = argv[index + 1];
    if (next !== undefined && !next.startsWith('--')) {
      result[key.slice(2)] = next;
      index += 1;
    } else {
      result[key.slice(2)] = '';
    }
  }
  return result;
}

function sha256(value) {
  return crypto.createHash('sha256').update(value).digest('hex');
}

function readHeader(filePath) {
  const handle = fs.openSync(filePath, fs.constants.O_RDONLY);
  try {
    const header = Buffer.alloc(SQLITE_HEADER.length);
    const length = fs.readSync(handle, header, 0, header.length, 0);
    return header.subarray(0, length);
  } finally {
    fs.closeSync(handle);
  }
}

function quoteIdentifier(identifier) {
  return `"${String(identifier).replaceAll('"', '""')}"`;
}

function scalar(database, statement) {
  const row = database.prepare(statement).get();
  return row ? Object.values(row)[0] : null;
}

function inspectSqliteDatabase(filePath) {
  const header = readHeader(filePath);
  if (!header.equals(SQLITE_HEADER)) {
    return {
      name: path.basename(filePath),
      format: 'encrypted-or-unrecognized',
      structureAccessible: false,
      concurrentRead: false,
      fingerprint: null,
      errorCode: 'not-plain-sqlite'
    };
  }

  let database;
  try {
    database = new DatabaseSync(filePath, {
      open: true,
      readOnly: true,
      enableForeignKeyConstraints: false
    });
    database.exec('PRAGMA query_only=ON; PRAGMA busy_timeout=1500;');

    const objects = database.prepare(
      "SELECT type, name FROM sqlite_schema " +
      "WHERE type IN ('table','view','index','trigger') " +
      "AND name NOT LIKE 'sqlite_%' ORDER BY type, name"
    ).all();
    const tablesAndViews = objects.filter(
      item => item.type === 'table' || item.type === 'view'
    );
    const columns = [];
    const indexes = [];
    for (const item of tablesAndViews) {
      const tableName = String(item.name);
      for (const column of database.prepare(
        `PRAGMA table_xinfo(${quoteIdentifier(tableName)})`
      ).all()) {
        columns.push({
          object: tableName,
          name: String(column.name),
          declaredType: String(column.type || ''),
          notNull: Number(column.notnull || 0),
          primaryKey: Number(column.pk || 0),
          hidden: Number(column.hidden || 0)
        });
      }
      if (item.type === 'table') {
        for (const index of database.prepare(
          `PRAGMA index_list(${quoteIdentifier(tableName)})`
        ).all()) {
          indexes.push({
            object: tableName,
            name: String(index.name),
            unique: Number(index.unique || 0),
            origin: String(index.origin || ''),
            partial: Number(index.partial || 0)
          });
        }
      }
    }

    columns.sort((left, right) =>
      left.object.localeCompare(right.object) || left.name.localeCompare(right.name));
    indexes.sort((left, right) =>
      left.object.localeCompare(right.object) || left.name.localeCompare(right.name));
    const structure = {
      userVersion: Number(scalar(database, 'PRAGMA user_version') || 0),
      applicationId: Number(scalar(database, 'PRAGMA application_id') || 0),
      journalMode: String(scalar(database, 'PRAGMA journal_mode') || 'unknown'),
      objects: objects.map(item => ({ type: String(item.type), name: String(item.name) })),
      columns,
      indexes
    };
    const canonical = JSON.stringify(structure);
    return {
      name: path.basename(filePath),
      format: 'sqlite3',
      structureAccessible: true,
      concurrentRead: true,
      userVersion: structure.userVersion,
      applicationId: structure.applicationId,
      journalMode: structure.journalMode,
      objectCounts: {
        tables: objects.filter(item => item.type === 'table').length,
        views: objects.filter(item => item.type === 'view').length,
        indexes: objects.filter(item => item.type === 'index').length,
        triggers: objects.filter(item => item.type === 'trigger').length,
        columns: columns.length
      },
      schema: {
        objects: structure.objects,
        columns,
        indexes
      },
      fingerprint: `sha256:${sha256(canonical)}`,
      errorCode: null
    };
  } catch (error) {
    return {
      name: path.basename(filePath),
      format: 'sqlite3',
      structureAccessible: false,
      concurrentRead: false,
      fingerprint: null,
      errorCode: error && error.code === 'SQLITE_BUSY' ? 'busy' : 'structure-unavailable'
    };
  } finally {
    if (database) database.close();
  }
}

function walkAsarFiles(node, prefix, output) {
  if (!node || typeof node !== 'object') return;
  if (node.files && typeof node.files === 'object') {
    for (const name of Object.keys(node.files).sort()) {
      const childPath = prefix ? `${prefix}/${name}` : name;
      const child = node.files[name];
      if (child && child.files) walkAsarFiles(child, childPath, output);
      else output.push(childPath);
    }
  }
}

function inspectAsar(asarPath) {
  if (!fs.existsSync(asarPath)) {
    return { state: 'not-found', candidateEntryCount: 0, candidateEntriesHash: null };
  }
  try {
    const handle = fs.openSync(asarPath, fs.constants.O_RDONLY);
    let headerSize;
    let header;
    try {
      const prefix = Buffer.alloc(16);
      if (fs.readSync(handle, prefix, 0, prefix.length, 0) !== prefix.length) {
        throw new Error('short-header');
      }
      headerSize = prefix.readUInt32LE(12);
      if (headerSize <= 0 || headerSize > 64 * 1024 * 1024) {
        throw new Error('invalid-header-size');
      }
      header = Buffer.alloc(headerSize);
      if (fs.readSync(handle, header, 0, header.length, 16) !== header.length) {
        throw new Error('short-json-header');
      }
    } finally {
      fs.closeSync(handle);
    }
    const tree = JSON.parse(header.toString('utf8').replace(/\0+$/u, ''));
    const files = [];
    walkAsarFiles(tree, '', files);
    const candidates = files.filter(file =>
      /(^|\/)(main|preload|ipc|server|service)[^/]*\.(c?js|mjs)$/iu.test(file) ||
      /(^|\/)(ipc|services?|servers?)\//iu.test(file));
    return {
      state: 'metadata-only',
      fileCount: files.length,
      candidateEntryCount: candidates.length,
      candidateEntriesHash: `sha256:${sha256(JSON.stringify(candidates))}`,
      callableBoundaryVerified: false
    };
  } catch {
    return {
      state: 'unrecognized-archive',
      candidateEntryCount: 0,
      candidateEntriesHash: null,
      callableBoundaryVerified: false
    };
  }
}

function inspectDatabase(dataRoot, name) {
  const filePath = path.join(dataRoot, name);
  if (!fs.existsSync(filePath)) {
    return {
      name,
      format: 'missing',
      structureAccessible: false,
      concurrentRead: false,
      fingerprint: null,
      errorCode: 'not-found'
    };
  }
  return inspectSqliteDatabase(filePath);
}

function main() {
  const args = parseArguments(process.argv.slice(2));
  const executable = args.executable ? path.resolve(args.executable) : null;
  const dataRoot = args['data-root'] ? path.resolve(args['data-root']) : null;
  const installationRoot = executable ? path.dirname(executable) : null;
  const version = args.version || null;
  const databases = dataRoot
    ? DATABASE_NAMES.map(name => inspectDatabase(dataRoot, name))
    : DATABASE_NAMES.map(name => ({
        name,
        format: 'missing',
        structureAccessible: false,
        concurrentRead: false,
        fingerprint: null,
        errorCode: 'data-root-not-found'
      }));
  const structuralFingerprint = databases.some(item => item.fingerprint)
    ? `sha256:${sha256(JSON.stringify(databases.map(item => ({
        name: item.name,
        fingerprint: item.fingerprint
      }))))}`
    : null;
  const readable = databases.some(item => item.structureAccessible);
  const todos = databases.find(item => item.name === 'todos.db');
  const todoColumns = new Set((todos?.schema?.columns || [])
    .filter(column => column.object === 'todos')
    .map(column => column.name));
  const requiredColumns = [
    'complete', 'create_time', 'deleted', 'sync_time', 'task_content',
    'task_describe', 'task_id', 'task_sort', 'todo_time', 'update_time', 'version'
  ];
  const knownReadAdapter = /^3\.14\.2(?:\.|$)/u.test(version || '') &&
    requiredColumns.every(column => todoColumns.has(column));
  const asar = installationRoot
    ? inspectAsar(path.join(installationRoot, 'resources', 'app.asar'))
    : { state: 'not-found', candidateEntryCount: 0, candidateEntriesHash: null };

  const report = {
    reportVersion: 1,
    privacy: {
      rowQueriesPerformed: false,
      taskContentRead: false,
      sqlDefinitionsEmitted: false,
      databasePathsEmitted: false
    },
    discovery: {
      installationFound: Boolean(executable && fs.existsSync(executable)),
      dataRootFound: Boolean(dataRoot && fs.existsSync(dataRoot)),
      productVersion: version,
      applicationRunning: args['application-running'] === 'true'
    },
    compatibility: {
      adapter: knownReadAdapter ? 'todo-qingdan-3.14.2-readonly' : 'unsupported',
      schemaFingerprint: structuralFingerprint,
      liveReadEvidence: readable ? 'structure-only' : 'unavailable',
      writePolicy: 'blocked',
      writeStopReasons: [
        'no-verified-todo-qingdan-owned-mutation-channel',
        'no-isolated-sync-semantics-evidence',
        'no-reviewed-known-fingerprint-manifest'
      ]
    },
    databases,
    asar,
    capabilities: {
      discoverInstallation: Boolean(executable && fs.existsSync(executable)),
      detectVersionSchema: Boolean(version || structuralFingerprint),
      readLists: false,
      readTasks: knownReadAdapter,
      createTask: false,
      updateTitle: false,
      setCompleted: false,
      deleteTask: false,
      setDueDate: false,
      setImportant: false,
      moveToList: false,
      reorder: false,
      readSteps: false,
      writeSteps: false,
      readNotes: false,
      writeNotes: false,
      readAttachments: false,
      openNativeTask: false
    }
  };
  process.stdout.write(`${JSON.stringify(report, null, 2)}\n`);
}

try {
  main();
} catch (error) {
  process.stderr.write(`TodoQingdan compatibility probe failed: ${error.message}\n`);
  process.exitCode = 1;
}
