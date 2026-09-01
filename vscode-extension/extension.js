const childProcess = require('child_process');
const fs = require('fs');
const path = require('path');
const vscode = require('vscode');

const MAIN_CONTEXT_RELATIVE = path.join('.mymcp', 'context', 'main.md');
const SPECS_ROOT_RELATIVE = path.join('.mymcp', 'specs');
const DOCS_ROOT_RELATIVE = path.join('.mymcp', 'docs');
const FEATURES_ROOT_RELATIVE = path.join('.mymcp', 'specs', 'features');
const TASKS_ROOT_RELATIVE = path.join('.mymcp', 'specs', 'tasks');
const ACTIVE_PROJECT_KEY = 'mymcp.activeProjectUri';

function activate(context) {
  const extensionRoot = context.extensionUri.fsPath;
  const serverProject = path.resolve(extensionRoot, '..', 'server', 'MyMcp.Server', 'MyMcp.Server.csproj');
  const serverProjectDir = path.dirname(serverProject);
  const output = vscode.window.createOutputChannel('MyMCP');
  const didChangeEmitter = new vscode.EventEmitter();
  const treeProvider = new MyMcpTreeProvider(context, output);
  let providerVersion = 1;

  context.subscriptions.push(output);
  context.subscriptions.push(didChangeEmitter);
  context.subscriptions.push(vscode.window.registerTreeDataProvider('mymcp.view', treeProvider));

  context.subscriptions.push(
    vscode.lm.registerMcpServerDefinitionProvider('mymcp.provider', {
      onDidChangeMcpServerDefinitions: didChangeEmitter.event,
      provideMcpServerDefinitions: async () => {
        const folders = getWorkspaceFolders();
        if (folders.length === 0) {
          return [];
        }

        return folders.map((folder, index) =>
          createServerDefinition(folder, extensionRoot, serverProject, serverProjectDir, providerVersion, index)
        );
      },
      resolveMcpServerDefinition: async (server) => server
    })
  );

  registerCommand(context, 'mymcp.openMcpServers', async () => {
    await openMcpServersView();
  });

  registerCommand(context, 'mymcp.startMcpServer', async () => {
    didChangeEmitter.fire();
    await openMcpServersView();
  });

  registerCommand(context, 'mymcp.restartMcpServer', async () => {
    providerVersion += 1;
    didChangeEmitter.fire();
    await openMcpServersView();
  });

  registerCommand(context, 'mymcp.testConnection', async () => {
    const folder = await getTargetProjectFolder(context, true);
    if (!folder) {
      return;
    }

    await vscode.window.withProgress(
      {
        location: vscode.ProgressLocation.Notification,
        title: 'Testing MyMCP connection',
        cancellable: false
      },
      async () => {
        const result = await runConnectionSmokeTest(folder, extensionRoot, serverProject, serverProjectDir);
        treeProvider.setConnectionState(result);

        if (result.ok) {
          output.appendLine(`[ok] ${result.message}`);
          vscode.window.showInformationMessage(result.message);
          return;
        }

        output.appendLine(`[error] ${result.message}`);
        vscode.window.showErrorMessage(result.message);
      }
    );
  });

  registerCommand(context, 'mymcp.selectActiveProject', async () => {
    const folders = getWorkspaceFolders();
    if (folders.length === 0) {
      vscode.window.showInformationMessage('Open a workspace folder first.');
      return;
    }

    const activeUri = getActiveProjectUri(context);
    const pick = await vscode.window.showQuickPick(
      folders.map((folder) => ({
        label: folder.name,
        description: folder.uri.fsPath,
        folder
      })),
      {
        title: 'Select active project',
        placeHolder: activeUri ? 'Active project is already set' : 'Choose the project to work on'
      }
    );

    if (!pick) {
      return;
    }

    await setActiveProjectUri(context, pick.folder.uri.toString());
    treeProvider.refresh();
    vscode.window.showInformationMessage(`Active project set to ${pick.folder.name}.`);
  });

  registerCommand(context, 'mymcp.bootstrapProjectDocs', async () => {
    const folder = await getTargetProjectFolder(context, true);
    if (!folder) {
      return;
    }

    await bootstrapProjectDocs(folder);
    treeProvider.refresh();
  });

  registerCommand(context, 'mymcp.bootstrapDocsPack', async () => {
    const folder = await getTargetProjectFolder(context, true);
    if (!folder) {
      return;
    }

    await bootstrapDocsPack(folder);
    treeProvider.refresh();
  });

  registerCommand(context, 'mymcp.openMainContext', async () => {
    const folder = await getTargetProjectFolder(context, false);
    if (!folder) {
      return;
    }

    await openOrCreateMarkdown(
      path.join(folder.uri.fsPath, MAIN_CONTEXT_RELATIVE),
      buildMainContextTemplate()
    );
  });

  registerCommand(context, 'mymcp.editMainContext', async () => {
    const folder = await getTargetProjectFolder(context, false);
    if (!folder) {
      return;
    }

    await openOrCreateMarkdown(
      path.join(folder.uri.fsPath, MAIN_CONTEXT_RELATIVE),
      buildMainContextTemplate(),
      true
    );
  });

  registerCommand(context, 'mymcp.openActiveProjectSpecs', async () => {
    const folder = await getTargetProjectFolder(context, false);
    if (!folder) {
      return;
    }

    await openOrCreateMarkdown(
      path.join(folder.uri.fsPath, SPECS_ROOT_RELATIVE, 'README.md'),
      buildSpecsReadmeTemplate()
    );
  });

  registerCommand(context, 'mymcp.openActiveDocsPack', async () => {
    const folder = await getTargetProjectFolder(context, false);
    if (!folder) {
      return;
    }

    await openOrCreateMarkdown(
      path.join(folder.uri.fsPath, DOCS_ROOT_RELATIVE, 'README.md'),
      buildDocsPackReadmeTemplate()
    );
  });

  registerCommand(context, 'mymcp.openSpecsRoot', async () => {
    const folder = await getTargetProjectFolder(context, false);
    if (!folder) {
      return;
    }

    await openOrCreateMarkdown(
      path.join(folder.uri.fsPath, SPECS_ROOT_RELATIVE, 'README.md'),
      buildSpecsReadmeTemplate()
    );
  });

  registerCommand(context, 'mymcp.listSpecArtifacts', async () => {
    const folder = await getTargetProjectFolder(context, false);
    if (!folder) {
      return;
    }

    const artifacts = listMarkdownArtifacts(path.join(folder.uri.fsPath, SPECS_ROOT_RELATIVE));
    if (artifacts.length === 0) {
      vscode.window.showInformationMessage('No spec artifacts found yet.');
      return;
    }

    const pick = await vscode.window.showQuickPick(
      artifacts.map((filePath) => ({
        label: path.basename(filePath),
        description: path.relative(folder.uri.fsPath, filePath),
        filePath
      })),
      { placeHolder: 'Select a spec artifact to open' }
    );

    if (!pick) {
      return;
    }

    await openFile(pick.filePath);
  });

  registerCommand(context, 'mymcp.listDocsArtifacts', async () => {
    const folder = await getTargetProjectFolder(context, false);
    if (!folder) {
      return;
    }

    const artifacts = listMarkdownArtifacts(path.join(folder.uri.fsPath, DOCS_ROOT_RELATIVE));
    if (artifacts.length === 0) {
      vscode.window.showInformationMessage('No docs artifacts found yet.');
      return;
    }

    const pick = await vscode.window.showQuickPick(
      artifacts.map((filePath) => ({
        label: path.basename(filePath),
        description: path.relative(folder.uri.fsPath, filePath),
        filePath
      })),
      { placeHolder: 'Select a docs artifact to open' }
    );

    if (!pick) {
      return;
    }

    await openFile(pick.filePath);
  });

  registerCommand(context, 'mymcp.createFeatureSpec', async () => {
    const folder = await getTargetProjectFolder(context, false);
    if (!folder) {
      return;
    }

    const slug = await askInput('Feature slug', 'new-feature');
    if (!slug) return;

    const title = await askInput('Feature title', 'New Feature');
    if (!title) return;

    const summary = await askInput('Feature summary', 'Describe the feature in one or two sentences.');
    if (!summary) return;

    const featureRoot = path.join(folder.uri.fsPath, FEATURES_ROOT_RELATIVE, normalizeSlug(slug));
    await fs.promises.mkdir(featureRoot, { recursive: true });

    await writeIfMissing(path.join(featureRoot, 'spec.md'), buildFeatureSpecTemplate(title, summary));
    await writeIfMissing(path.join(featureRoot, 'tasks.md'), buildFeatureTasksTemplate(title));
    await writeIfMissing(path.join(featureRoot, 'notes.md'), buildFeatureNotesTemplate(title));

    await openFile(path.join(featureRoot, 'spec.md'));
  });

  registerCommand(context, 'mymcp.createTaskDoc', async () => {
    const folder = await getTargetProjectFolder(context, false);
    if (!folder) {
      return;
    }

    const slug = await askInput('Task slug', 'task-name');
    if (!slug) return;

    const title = await askInput('Task title', 'Task title');
    if (!title) return;

    const summary = await askInput('Task summary', 'Describe the task briefly.');
    if (!summary) return;

    const taskPath = path.join(folder.uri.fsPath, TASKS_ROOT_RELATIVE, `${normalizeSlug(slug)}.md`);
    await fs.promises.mkdir(path.dirname(taskPath), { recursive: true });
    await fs.promises.writeFile(taskPath, buildTaskTemplate(title, summary), 'utf8');
    await openFile(taskPath);
  });

  registerCommand(context, 'mymcp.createDocsPack', async () => {
    const folder = await getTargetProjectFolder(context, false);
    if (!folder) {
      return;
    }

    const slug = await askInput('Docs slug', 'project-docs');
    if (!slug) return;

    const title = await askInput('Docs title', 'Project Documentation');
    if (!title) return;

    const summary = await askInput('Docs summary', 'Describe what this documentation pack covers.');
    if (!summary) return;

    const docsRoot = path.join(folder.uri.fsPath, DOCS_ROOT_RELATIVE, normalizeSlug(slug));
    await fs.promises.mkdir(docsRoot, { recursive: true });

    await writeIfMissing(path.join(docsRoot, 'README.md'), buildDocsPackReadmeTemplate(title, summary));
    await writeIfMissing(path.join(docsRoot, 'architecture.md'), buildArchitectureDocTemplate(title, summary));
    await writeIfMissing(path.join(docsRoot, 'runbook.md'), buildRunbookDocTemplate(title));
    await writeIfMissing(path.join(docsRoot, 'decisions.md'), buildDecisionsDocTemplate(title));

    await openFile(path.join(docsRoot, 'README.md'));
  });
}

function deactivate() {}

function registerCommand(context, commandId, handler) {
  context.subscriptions.push(vscode.commands.registerCommand(commandId, handler));
}

class MyMcpTreeProvider {
  constructor(extensionContext, output) {
    this.extensionContext = extensionContext;
    this.output = output;
    this.emitter = new vscode.EventEmitter();
    this.onDidChangeTreeData = this.emitter.event;
    this.connectionState = {
      status: 'unknown',
      message: 'Connection has not been tested yet.'
    };
  }

  refresh() {
    this.emitter.fire();
  }

  setConnectionState(result) {
    this.connectionState = result.ok
      ? {
          status: 'connected',
          message: result.message
        }
      : {
          status: 'error',
          message: result.message
        };
    this.refresh();
  }

  getTreeItem(element) {
    return element;
  }

  getChildren(element) {
    if (element) {
      return element.children ?? [];
    }

    return this.buildRootItems();
  }

  buildRootItems() {
    const folder = getCurrentProjectFolder(this.extensionContext);
    const activeLabel = folder ? folder.name : 'No project selected';

    return [
      this.createStatusItem(),
      this.createProjectItem(activeLabel),
      this.createGroupItem('Server', 'server', 'gear', [
        this.createActionItem('Start MCP Server', 'play', 'mymcp.startMcpServer'),
        this.createActionItem('Restart MCP Server', 'refresh', 'mymcp.restartMcpServer'),
        this.createActionItem('Test Connection', 'check', 'mymcp.testConnection'),
        this.createActionItem('Open MCP Servers', 'list-selection', 'mymcp.openMcpServers')
      ]),
      this.createGroupItem('Project Context', 'context', 'book', [
        this.createActionItem('Select Active Project', 'workspace', 'mymcp.selectActiveProject'),
        this.createActionItem('Bootstrap Project Docs', 'new-file', 'mymcp.bootstrapProjectDocs'),
        this.createActionItem('Open Main Context', 'file-text', 'mymcp.openMainContext'),
        this.createActionItem('Edit Main Context', 'edit', 'mymcp.editMainContext')
      ]),
      this.createGroupItem('SDD', 'sdd', 'beaker', [
        this.createActionItem('Open Specs Root', 'folder-opened', 'mymcp.openSpecsRoot'),
        this.createActionItem('Open Active Specs', 'library', 'mymcp.openActiveProjectSpecs'),
        this.createActionItem('Create Feature Spec', 'add', 'mymcp.createFeatureSpec'),
        this.createActionItem('Create Task Doc', 'checklist', 'mymcp.createTaskDoc'),
        this.createActionItem('List Spec Artifacts', 'list-tree', 'mymcp.listSpecArtifacts')
      ]),
      this.createGroupItem('Documentation', 'docs', 'book', [
        this.createActionItem('Bootstrap Docs Pack', 'new-file', 'mymcp.bootstrapDocsPack'),
        this.createActionItem('Open Active Docs Pack', 'library', 'mymcp.openActiveDocsPack'),
        this.createActionItem('Create Docs Pack', 'add', 'mymcp.createDocsPack'),
        this.createActionItem('List Docs Artifacts', 'list-tree', 'mymcp.listDocsArtifacts')
      ])
    ];
  }

  createStatusItem() {
    const status = this.connectionState.status;
    const iconName = status === 'connected' ? 'check' : status === 'error' ? 'error' : 'question';

    return this.makeItem(
      `Connection: ${status}`,
      this.connectionState.message,
      new vscode.ThemeIcon(iconName),
      vscode.TreeItemCollapsibleState.None,
      'mymcp.connection',
      { command: 'mymcp.testConnection', title: 'Test Connection' }
    );
  }

  createProjectItem(label) {
    return this.makeItem(
      `Project: ${label}`,
      'Select the active workspace folder used by the tools.',
      new vscode.ThemeIcon('workspace-trusted'),
      vscode.TreeItemCollapsibleState.None,
      'mymcp.project',
      { command: 'mymcp.selectActiveProject', title: 'Select Active Project' }
    );
  }

  createGroupItem(label, description, iconName, children) {
    return this.makeItem(
      label,
      description,
      new vscode.ThemeIcon(iconName),
      vscode.TreeItemCollapsibleState.Collapsed,
      'mymcp.group',
      undefined,
      children
    );
  }

  createActionItem(label, iconName, commandId) {
    return this.makeItem(
      label,
      undefined,
      new vscode.ThemeIcon(iconName),
      vscode.TreeItemCollapsibleState.None,
      'mymcp.action',
      { command: commandId, title: label }
    );
  }

  makeItem(label, description, iconPath, collapsibleState, contextValue, command, children) {
    const item = new vscode.TreeItem(label, collapsibleState);
    item.description = description;
    item.tooltip = description ? `${label}\n${description}` : label;
    item.iconPath = iconPath;
    item.contextValue = contextValue;
    item.command = command;
    item.children = children ?? [];
    return item;
  }
}

function getWorkspaceFolders() {
  return vscode.workspace.workspaceFolders ?? [];
}

function getCurrentProjectFolder(extensionContext) {
  const folders = getWorkspaceFolders();
  const activeUri = getActiveProjectUri(extensionContext);
  if (activeUri) {
    const activeFolder = folders.find((folder) => folder.uri.toString() === activeUri);
    if (activeFolder) {
      return activeFolder;
    }
  }

  return folders[0] ?? null;
}

function getActiveProjectUri(extensionContext) {
  return extensionContext.workspaceState.get(ACTIVE_PROJECT_KEY) ?? null;
}

async function setActiveProjectUri(extensionContext, uri) {
  await extensionContext.workspaceState.update(ACTIVE_PROJECT_KEY, uri);
}

async function getTargetProjectFolder(extensionContext, allowPrompt = true) {
  const folders = getWorkspaceFolders();
  if (folders.length === 0) {
    vscode.window.showInformationMessage('Open a workspace folder first.');
    return null;
  }

  const activeUri = getActiveProjectUri(extensionContext);
  const activeFolder = activeUri
    ? folders.find((folder) => folder.uri.toString() === activeUri)
    : null;

  if (activeFolder) {
    return activeFolder;
  }

  if (!allowPrompt && folders.length === 1) {
    return folders[0];
  }

  const pick = await vscode.window.showQuickPick(
    folders.map((folder) => ({
      label: folder.name,
      description: folder.uri.fsPath,
      folder
    })),
    { placeHolder: 'Choose the project to work on' }
  );

  if (!pick) {
    return null;
  }

  await setActiveProjectUri(extensionContext, pick.folder.uri.toString());
  return pick.folder;
}

async function openMcpServersView() {
  const command = await findCommand([
    /mcp.*show.*installed.*server/i,
    /mcp.*list.*server/i
  ]);

  if (!command) {
    vscode.window.showInformationMessage('Open MCP Servers from the Command Palette.');
    return;
  }

  await vscode.commands.executeCommand(command);
}

async function askInput(title, placeholder) {
  return vscode.window.showInputBox({ title, placeHolder: placeholder, ignoreFocusOut: true });
}

async function openOrCreateMarkdown(filePath, content, openForEdit = false) {
  await fs.promises.mkdir(path.dirname(filePath), { recursive: true });
  if (!fs.existsSync(filePath)) {
    await fs.promises.writeFile(filePath, content, 'utf8');
  }

  await openFile(filePath, openForEdit);
}

async function openFile(filePath, preview = false) {
  const doc = await vscode.workspace.openTextDocument(vscode.Uri.file(filePath));
  await vscode.window.showTextDocument(doc, { preview });
}

function listMarkdownArtifacts(rootPath) {
  if (!fs.existsSync(rootPath)) {
    return [];
  }

  const files = [];
  walk(rootPath, files);
  return files.filter((filePath) => filePath.toLowerCase().endsWith('.md'));
}

function walk(dir, files) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === 'node_modules' || entry.name === 'bin' || entry.name === 'obj') {
      continue;
    }

    const fullPath = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      walk(fullPath, files);
      continue;
    }

    files.push(fullPath);
  }
}

async function findCommand(patterns) {
  const commands = await vscode.commands.getCommands(true);
  const match = commands.find((command) => patterns.some((pattern) => pattern.test(command)));
  return match ?? null;
}

async function writeIfMissing(filePath, content) {
  if (!fs.existsSync(filePath)) {
    await fs.promises.writeFile(filePath, content, 'utf8');
  }
}

function createServerDefinition(folder, extensionRoot, serverProject, serverProjectDir, providerVersion, index) {
  const launch = resolveServerLaunch(folder, extensionRoot, serverProject, serverProjectDir);

  return new vscode.McpStdioServerDefinition({
    label: `MyMCP (${folder.name})`,
    command: launch.command,
    args: launch.args,
    cwd: vscode.Uri.file(serverProjectDir),
    version: `0.1.0.${providerVersion}.${index}`
  });
}

function resolveServerLaunch(folder, extensionRoot, serverProject, serverProjectDir) {
  const packagedExe = path.join(extensionRoot, 'server', 'MyMcp.Server', 'bin', 'Debug', 'net10.0', 'MyMcp.Server.exe');
  if (fs.existsSync(packagedExe)) {
    return {
      command: packagedExe,
      args: ['--root', folder.uri.fsPath]
    };
  }

  const localExe = path.join(serverProjectDir, 'bin', 'Debug', 'net10.0', 'MyMcp.Server.exe');
  if (fs.existsSync(localExe)) {
    return {
      command: localExe,
      args: ['--root', folder.uri.fsPath]
    };
  }

  const dotnet = resolveDotnetExecutable();
  if (!dotnet) {
    throw new Error(
      'Could not find dotnet.exe. Install the .NET 10 SDK or build the server before starting MyMCP.'
    );
  }

  return {
    command: dotnet,
    args: [
      'run',
      '--project',
      serverProject,
      '--',
      '--root',
      folder.uri.fsPath
    ]
  };
}

function resolveDotnetExecutable() {
  if (process.platform === 'win32') {
    const programFiles = process.env.ProgramFiles || 'C:\\Program Files';
    const programFilesX86 = process.env['ProgramFiles(x86)'] || 'C:\\Program Files (x86)';
    const candidates = [
      path.join(programFiles, 'dotnet', 'dotnet.exe'),
      path.join(programFilesX86, 'dotnet', 'dotnet.exe')
    ];

    for (const candidate of candidates) {
      if (fs.existsSync(candidate)) {
        return candidate;
      }
    }

    try {
      const output = childProcess.execFileSync('where', ['dotnet'], { encoding: 'utf8' }).trim();
      const match = output.split(/\r?\n/).find((line) => line && fs.existsSync(line));
      if (match) {
        return match;
      }
    } catch {
      return null;
    }
  }

  return 'dotnet';
}

async function bootstrapProjectDocs(folder) {
  await openOrCreateMarkdown(path.join(folder.uri.fsPath, MAIN_CONTEXT_RELATIVE), buildMainContextTemplate());
  await openOrCreateMarkdown(path.join(folder.uri.fsPath, SPECS_ROOT_RELATIVE, 'README.md'), buildSpecsReadmeTemplate());
  await fs.promises.mkdir(path.join(folder.uri.fsPath, FEATURES_ROOT_RELATIVE), { recursive: true });
  await fs.promises.mkdir(path.join(folder.uri.fsPath, TASKS_ROOT_RELATIVE), { recursive: true });
  vscode.window.showInformationMessage(`SDD workspace initialized for ${folder.name}.`);
}

async function bootstrapDocsPack(folder) {
  await openOrCreateMarkdown(path.join(folder.uri.fsPath, DOCS_ROOT_RELATIVE, 'README.md'), buildDocsPackReadmeTemplate());
  await openOrCreateMarkdown(path.join(folder.uri.fsPath, DOCS_ROOT_RELATIVE, 'architecture.md'), buildArchitectureDocTemplate('Project', 'Describe the architecture here.'));
  await openOrCreateMarkdown(path.join(folder.uri.fsPath, DOCS_ROOT_RELATIVE, 'runbook.md'), buildRunbookDocTemplate('Project'));
  await openOrCreateMarkdown(path.join(folder.uri.fsPath, DOCS_ROOT_RELATIVE, 'decisions.md'), buildDecisionsDocTemplate('Project'));
  vscode.window.showInformationMessage(`Documentation pack initialized for ${folder.name}.`);
}

function normalizeSlug(value) {
  return value
    .toLowerCase()
    .trim()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
}

function buildMainContextTemplate() {
  return `# Main Context

## Goal

Desenvolver software com regras duraveis, contexto centralizado e entregas guiadas por spec.

## Rules

- Mantenha este arquivo curto e estavel.
- Atualize este contexto quando uma regra de arquitetura, dominio ou processo precisar permanecer valida por varios ciclos.
- Prefira mudancas guiadas por spec em vez de alteracoes ad hoc.

## Constraints

- O MCP deve operar apenas dentro do workspace.
- Artefatos de feature e tarefa devem viver em .mymcp/specs.
- A documentacao duravel deve ficar em .mymcp/docs.
- Escrita livre continua disponivel, mas o fluxo preferencial e via ferramentas de spec e docs.
`;
}

function buildFeatureSpecTemplate(title, summary) {
  return `# ${title}

## Summary

${summary}

## Problem

Descreva o problema que esta feature resolve.

## Goals

- Goal 1
- Goal 2

## Non-Goals

- Non-goal 1

## Scope

- In scope

## Acceptance Criteria

- [ ] Criterion 1
- [ ] Criterion 2
`;
}

function buildFeatureTasksTemplate(title) {
  return `# Tasks for ${title}

- [ ] Review main context
- [ ] Refine spec
- [ ] Implement feature
- [ ] Validate behavior
`;
}

function buildFeatureNotesTemplate(title) {
  return `# Notes for ${title}

- Track implementation notes here.
- Record tradeoffs, open questions, and follow-ups.
`;
}

function buildTaskTemplate(title, summary) {
  return `# ${title}

## Summary

${summary}

## Context

- Related feature or issue:

## Steps

- [ ] Step 1
- [ ] Step 2

## Done When

- [ ] Task is validated
`;
}

function buildSpecsReadmeTemplate() {
  return `# Spec Driven Workspace

Use esta pasta para organizar features, tarefas e notas de implementacao.

## Convencao

- features/<slug>/spec.md
- features/<slug>/tasks.md
- features/<slug>/notes.md
- tasks/<slug>.md

## Fluxo

1. Leia o contexto principal em .mymcp/context/main.md.
2. Crie ou atualize a spec da feature.
3. Quebre o trabalho em tasks.
4. Registre decisoes e ajustes em notes.md.
`;
}

function buildDocsPackReadmeTemplate(title = 'Project Documentation', summary = 'Documentation pack') {
  return `# ${title}

## Summary

${summary}

## Included Documents

- architecture.md
- runbook.md
- decisions.md

## Purpose

Keep durable project documentation close to the code and aligned with the main context.
`;
}

function buildArchitectureDocTemplate(title, summary) {
  return `# Architecture - ${title}

## Overview

${summary}

## Components

- Application layers
- MCP integration
- Editor integration

## Constraints

- Keep the design modular.
- Avoid coupling business rules to transport concerns.
`;
}

function buildRunbookDocTemplate(title) {
  return `# Runbook - ${title}

## Startup

- Start the server.
- Open the VS Code extension view.
- Test the connection.

## Common Actions

- Refresh the server definition.
- Bootstrap project docs.
- Create feature specs and task docs.

## Troubleshooting

- Ensure the .NET 10 SDK is installed.
- Rebuild the server if the executable is missing.
- Reinstall the VS Code extension if the view does not appear.
`;
}

function buildDecisionsDocTemplate(title) {
  return `# Decisions - ${title}

- Record durable architecture and workflow decisions here.
- Prefer short entries that explain why a choice was made.
- Capture the date, context, and follow-up if needed.
`;
}

async function runConnectionSmokeTest(folder, extensionRoot, serverProject, serverProjectDir) {
  const launch = resolveServerLaunch(folder, extensionRoot, serverProject, serverProjectDir);
  const child = childProcess.spawn(launch.command, launch.args, {
    cwd: serverProjectDir,
    env: {
      ...process.env,
      MYMCP_ROOT: folder.uri.fsPath
    },
    stdio: ['pipe', 'pipe', 'pipe']
  });

  const stderrChunks = [];
  child.stderr.on('data', (chunk) => stderrChunks.push(Buffer.from(chunk)));

  const session = new JsonRpcSession(child.stdin, child.stdout);

  try {
    await session.sendRequest(1, 'initialize', {
      protocolVersion: '2024-11-05',
      clientInfo: { name: 'MyMCP VS Code Extension', version: '0.1.0' },
      capabilities: {}
    });
    await session.sendNotification('notifications/initialized', {});

    const toolsResponse = await session.sendRequest(2, 'tools/list', {});
    const toolNames = Array.isArray(toolsResponse?.result?.tools)
      ? toolsResponse.result.tools.map((tool) => tool.name)
      : [];

    const requiredTools = ['ReadFile', 'WriteFile', 'ReadMainContext', 'CreateDocsPack'];
    const missingTools = requiredTools.filter((name) => !toolNames.includes(name));

    if (missingTools.length > 0) {
      return {
        ok: false,
        message: `Connection opened, but missing tools: ${missingTools.join(', ')}`,
        toolCount: toolNames.length,
        stderr: Buffer.concat(stderrChunks).toString('utf8')
      };
    }

    const readMainContextResponse = await session.sendRequest(3, 'tools/call', {
      name: 'ReadMainContext',
      arguments: {}
    });

    if (readMainContextResponse?.error) {
      return {
        ok: false,
        message: `Connection opened, but ReadMainContext failed: ${JSON.stringify(readMainContextResponse.error)}`,
        toolCount: toolNames.length,
        stderr: Buffer.concat(stderrChunks).toString('utf8')
      };
    }

    return {
      ok: true,
      message: `MyMCP connection is active for ${folder.name}. Tools loaded: ${toolNames.length}.`,
      toolCount: toolNames.length,
      stderr: Buffer.concat(stderrChunks).toString('utf8')
    };
  } catch (error) {
    return {
      ok: false,
      message: `MyMCP connection test failed: ${error.message}`,
      toolCount: 0,
      stderr: Buffer.concat(stderrChunks).toString('utf8')
    };
  } finally {
    try {
      child.kill();
    } catch {
      // ignore
    }
  }
}

class JsonRpcSession {
  constructor(stdin, stdout) {
    this.stdin = stdin;
    this.buffer = Buffer.alloc(0);
    this.pendingMessages = [];
    this.waiters = [];
    this.closed = false;

    stdout.on('data', (chunk) => this.onData(chunk));
    stdout.on('end', () => this.onClose());
  }

  onData(chunk) {
    this.buffer = Buffer.concat([this.buffer, chunk]);

    while (true) {
      const lineEnd = this.buffer.indexOf('\n');
      if (lineEnd < 0) {
        break;
      }

      const body = this.buffer.slice(0, lineEnd).toString('utf8').replace(/\r$/, '');
      this.buffer = this.buffer.slice(lineEnd + 1);

      if (!body.trim()) {
        continue;
      }

      try {
        this.pendingMessages.push(JSON.parse(body));
      } catch (error) {
        this.pendingMessages.push({ error: { message: error.message, body } });
      }
    }

    this.flushWaiters();
  }

  onClose() {
    this.closed = true;
    this.flushWaiters(new Error('Server closed before a response was received.'));
  }

  flushWaiters(error) {
    while (this.waiters.length > 0 && this.pendingMessages.length > 0) {
      const waiter = this.waiters.shift();
      waiter.resolve(this.pendingMessages.shift());
    }

    if (error) {
      while (this.waiters.length > 0) {
        const waiter = this.waiters.shift();
        waiter.reject(error);
      }
    }
  }

  sendFrame(message) {
    const body = Buffer.from(JSON.stringify(message), 'utf8');
    this.stdin.write(body);
    this.stdin.write(Buffer.from('\n', 'ascii'));
  }

  waitForMessage(timeoutMs = 15000) {
    if (this.pendingMessages.length > 0) {
      return Promise.resolve(this.pendingMessages.shift());
    }

    if (this.closed) {
      return Promise.reject(new Error('Server closed before a response was received.'));
    }

    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        reject(new Error('Timed out waiting for MCP response.'));
      }, timeoutMs);

      this.waiters.push({
        resolve: (value) => {
          clearTimeout(timer);
          resolve(value);
        },
        reject: (error) => {
          clearTimeout(timer);
          reject(error);
        }
      });
    });
  }

  async sendRequest(id, method, params) {
    this.sendFrame({
      jsonrpc: '2.0',
      id,
      method,
      params
    });

    return this.waitForMessage();
  }

  async sendNotification(method, params) {
    this.sendFrame({
      jsonrpc: '2.0',
      method,
      params
    });
  }
}

module.exports = {
  activate,
  deactivate
};
