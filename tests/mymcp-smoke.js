const assert = require('assert');
const fs = require('fs');
const os = require('os');
const path = require('path');
const childProcess = require('child_process');

const root = path.resolve(__dirname, '..');
const server = path.join(root, 'server', 'MyMcp.Server', 'bin', 'Debug', 'net10.0', 'MyMcp.Server.exe');
const workspace = fs.mkdtempSync(path.join(os.tmpdir(), 'mymcp-test-'));
const processHandle = childProcess.spawn(server, ['--root', workspace], { stdio: ['pipe', 'pipe', 'ignore'] });
let buffer = '';
let nextId = 1;
const pending = new Map();

processHandle.stdout.on('data', (chunk) => {
  buffer += chunk.toString('utf8');
  const lines = buffer.split('\n');
  buffer = lines.pop();
  for (const line of lines) {
    if (!line.trim()) continue;
    const message = JSON.parse(line);
    const waiter = pending.get(message.id);
    if (waiter) {
      pending.delete(message.id);
      waiter(message);
    }
  }
});

function request(method, params = {}) {
  const id = nextId++;
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`Timeout calling ${method}`)), 5000);
    pending.set(id, (message) => {
      clearTimeout(timer);
      resolve(message);
    });
    processHandle.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', id, method, params })}\n`);
  });
}

async function callTool(name, args) {
  return request('tools/call', { name, arguments: args });
}

(async () => {
  const initialized = await request('initialize', {
    protocolVersion: '2024-11-05',
    capabilities: {},
    clientInfo: { name: 'MyMCP smoke test', version: '1.0' }
  });
  assert.ok(initialized.result, 'initialize must return a result');

  const listed = await request('tools/list');
  const names = listed.result.tools.map((tool) => tool.name);
  for (const name of ['read_file_page', 'write_file_page', 'get_context_budget', 'get_git_status', 'list_agent_profiles', 'read_decision_memory', 'get_workspace_permissions', 'rollback_last_change', 'validate_feature_sdd']) {
    assert.ok(names.includes(name), `missing tool: ${name}`);
  }

  const write = await callTool('write_file_page', { path: 'sample.txt', content: 'first page', page: 1, append: false });
  assert.ok(write.result, 'first page must be written');
  await callTool('write_file_page', { path: 'sample.txt', content: 'second page', page: 2, append: true });
  const read = await callTool('read_file_page', { path: 'sample.txt', startLine: 1, lineCount: 10 });
  assert.match(JSON.stringify(read), /first page/);
  assert.match(JSON.stringify(read), /second page/);
  await callTool('rollback_last_change');
  const rolledBack = await callTool('read_file_page', { path: 'sample.txt', startLine: 1, lineCount: 10 });
  assert.match(JSON.stringify(rolledBack), /first page/);

  const feature = await callTool('create_feature_spec', {
    slug: 'smoke-feature',
    title: 'Smoke Feature',
    summary: 'Feature used to validate SDD artifacts.'
  });
  assert.ok(feature.result, 'feature spec must be created');
  const sdd = await callTool('validate_feature_sdd', { slug: 'smoke-feature' });
  assert.ok(sdd.result, 'SDD validation must pass for generated artifacts');

  const blocked = await callTool('read_file_page', { path: '../outside.txt', startLine: 1, lineCount: 1 });
  assert.ok(blocked.error || blocked.result?.isError, 'path traversal must be rejected');
  const budget = await callTool('get_context_budget');
  assert.match(JSON.stringify(budget), /configuredContextTokenBudget/);
  const profiles = await callTool('list_agent_profiles');
  assert.match(JSON.stringify(profiles), /implementation/);
  const permissions = await callTool('get_workspace_permissions');
  assert.match(JSON.stringify(permissions), /delete: disabled/);

  processHandle.kill();
  fs.rmSync(workspace, { recursive: true, force: true });
  console.log('MyMCP smoke tests passed.');
})().catch((error) => {
  processHandle.kill();
  fs.rmSync(workspace, { recursive: true, force: true });
  console.error(error.stack || error.message);
  process.exitCode = 1;
});
