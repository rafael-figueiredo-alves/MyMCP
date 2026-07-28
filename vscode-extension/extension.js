const fs = require('fs');
const path = require('path');
const vscode = require('vscode');

const MAIN_CONTEXT_RELATIVE = path.join('.mymcp', 'context', 'main.md');
const SPECS_ROOT_RELATIVE = path.join('.mymcp', 'specs');
const FEATURES_ROOT_RELATIVE = path.join('.mymcp', 'specs', 'features');
const TASKS_ROOT_RELATIVE = path.join('.mymcp', 'specs', 'tasks');
const ACTIVE_PROJECT_KEY = 'mymcp.activeProjectUri';

function activate(context) {
  const extensionRoot = context.extensionUri.fsPath;
  const serverProject = path.resolve(extensionRoot, '..', 'server', 'MyMcp.Server', 'MyMcp.Server.csproj');
  const serverProjectDir = path.dirname(serverProject);
  const didChangeEmitter = new vscode.EventEmitter();
  let providerVersion = 1;

  context.subscriptions.push(didChangeEmitter);
  context.subscriptions.push(
    vscode.lm.registerMcpServerDefinitionProvider('mymcp.provider', {
      onDidChangeMcpServerDefinitions: didChangeEmitter.event,
      provideMcpServerDefinitions: async () => {
        const folders = getWorkspaceFolders();
        if (folders.length === 0) {
          return [];
        }

        return folders.map((folder, index) =>
          new vscode.McpStdioServerDefinition({
            label: `MyMCP (${folder.name})`,
            command: 'dotnet',
            args: [
              'run',
              '--project',
              serverProject,
              '--',
              '--root',
              folder.uri.fsPath
            ],
            cwd: vscode.Uri.file(serverProjectDir),
            version: `0.1.0.${providerVersion}.${index}`
          })
        );
      },
      resolveMcpServerDefinition: async (server) => server
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand('mymcp.openMcpServers', async () => {
      await openMcpServersView();
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand('mymcp.startMcpServer', async () => {
      didChangeEmitter.fire();
      await openMcpServersView();
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand('mymcp.restartMcpServer', async () => {
      providerVersion += 1;
      didChangeEmitter.fire();
      await openMcpServersView();
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand('mymcp.selectActiveProject', async () => {
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
      vscode.window.showInformationMessage(`Active project set to ${pick.folder.name}.`);
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand('mymcp.bootstrapProjectDocs', async () => {
      const folder = await getTargetProjectFolder(context, true);
      if (!folder) {
        return;
      }

      await bootstrapProjectDocs(folder);
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand('mymcp.openMainContext', async () => {
      const folder = await getTargetProjectFolder(context, false);
      if (!folder) {
        return;
      }

      await openOrCreateMarkdown(
        path.join(folder.uri.fsPath, MAIN_CONTEXT_RELATIVE),
        buildMainContextTemplate()
      );
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand('mymcp.editMainContext', async () => {
      const folder = await getTargetProjectFolder(context, false);
      if (!folder) {
        return;
      }

      await openOrCreateMarkdown(
        path.join(folder.uri.fsPath, MAIN_CONTEXT_RELATIVE),
        buildMainContextTemplate(),
        true
      );
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand('mymcp.openActiveProjectSpecs', async () => {
      const folder = await getTargetProjectFolder(context, false);
      if (!folder) {
        return;
      }

      await openOrCreateMarkdown(
        path.join(folder.uri.fsPath, SPECS_ROOT_RELATIVE, 'README.md'),
        buildSpecsReadmeTemplate()
      );
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand('mymcp.openSpecsRoot', async () => {
      const folder = await getTargetProjectFolder(context, false);
      if (!folder) {
        return;
      }

      await openOrCreateMarkdown(
        path.join(folder.uri.fsPath, SPECS_ROOT_RELATIVE, 'README.md'),
        buildSpecsReadmeTemplate()
      );
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand('mymcp.listSpecArtifacts', async () => {
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
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand('mymcp.createFeatureSpec', async () => {
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
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand('mymcp.createTaskDoc', async () => {
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
    })
  );
}

function deactivate() {}

function getWorkspaceFolders() {
  return vscode.workspace.workspaceFolders ?? [];
}

function getActiveProjectUri(context) {
  return context.workspaceState.get(ACTIVE_PROJECT_KEY) ?? null;
}

async function setActiveProjectUri(context, uri) {
  await context.workspaceState.update(ACTIVE_PROJECT_KEY, uri);
}

async function getTargetProjectFolder(context, allowPrompt = true) {
  const folders = getWorkspaceFolders();
  if (folders.length === 0) {
    vscode.window.showInformationMessage('Open a workspace folder first.');
    return null;
  }

  const activeUri = getActiveProjectUri(context);
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

  await setActiveProjectUri(context, pick.folder.uri.toString());
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

async function bootstrapProjectDocs(folder) {
  await openOrCreateMarkdown(path.join(folder.uri.fsPath, MAIN_CONTEXT_RELATIVE), buildMainContextTemplate());
  await openOrCreateMarkdown(path.join(folder.uri.fsPath, SPECS_ROOT_RELATIVE, 'README.md'), buildSpecsReadmeTemplate());
  await fs.promises.mkdir(path.join(folder.uri.fsPath, FEATURES_ROOT_RELATIVE), { recursive: true });
  await fs.promises.mkdir(path.join(folder.uri.fsPath, TASKS_ROOT_RELATIVE), { recursive: true });
  vscode.window.showInformationMessage(`SDD workspace initialized for ${folder.name}.`);
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
- Escrita livre continua disponivel, mas o fluxo preferencial e via ferramentas de spec.
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

module.exports = {
  activate,
  deactivate
};
