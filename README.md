# MyMCP

Servidor MCP em C#/.NET 10 com extensao do VS Code, suporte a SDD e doc pack por Markdown.

## O que este repositorio entrega

- servidor MCP em `net10.0`
- ferramentas para listar, ler, buscar, escrever e aplicar edicoes em arquivos
- ferramentas para contexto principal, SDD e documentacao
- extensao do VS Code com Activity Bar, comandos e teste de conexao

## Estrutura

- `server/MyMcp.Server` - servidor MCP stdio
- `vscode-extension` - extensao VS Code que publica o servidor para o editor
- `.mymcp/context/main.md` - contexto principal duravel do projeto
- `.mymcp/specs` - pasta para specs, tasks e notas
- `.mymcp/docs` - pasta para documentacao duravel

## Ferramentas expostas no MCP

- `ListFiles`
- `ReadFile`
- `SearchText`
- `WriteFile`
- `ReadFilePage` e `WriteFilePage` para arquivos grandes sem consumir todo o contexto
- `GetContextBudget` para consultar o orcamento configurado e estimativa de contexto
- `RunProjectTests` e `GetTestRunHistory` para executar e auditar testes
- `GetGitStatus` e `GetGitDiff` para revisar alteracoes antes do commit
- `ValidateFeatureSdd` para validar os artefatos obrigatorios da feature
- `ApplyTextEdits`
- `ReadMainContext`
- `WriteMainContext`
- `ReadDocsMarkdown`
- `WriteDocsMarkdown`
- `CreateFeatureSpec`
- `CreateTaskDoc`
- `CreateFeatureTestPlan`
- `ValidateFeatureTests` (gate obrigatorio antes de concluir uma feature)
- `GetWorkspacePermissions` e `RollbackLastChange` para controle e recuperacao
- `DetectProjectLanguages`, `GetIncrementalContext` e perfis de agente
- `ReadDecisionMemory` e `WriteDecisionMemory` para memoria arquitetural
- `CreateDocsPack`
- `ListSpecArtifacts`
- `ListDocsArtifacts`

## Comandos da extensao

- `MyMCP: Select Active Project`
- `MyMCP: Test Connection`
- `MyMCP: Start MCP Server`
- `MyMCP: Restart MCP Server`
- `MyMCP: Bootstrap Project Docs`
- `MyMCP: Bootstrap Docs Pack`
- `MyMCP: Create Feature Spec`
- `MyMCP: Create Task Doc`
- `MyMCP: Create Docs Pack`

## Preparar a extensao no VS Code

Use o script na raiz do repositorio:

```powershell
powershell -ExecutionPolicy Bypass -File .\Prepare-MyMCP-VSCode.ps1 -Install
```

O script:

- reaproveita o build local do servidor quando ele ja existe
- empacota a extensao a partir de `vscode-extension`
- inclui o servidor compilado dentro do VSIX
- instala o VSIX no VS Code quando `-Install` e usado

O arquivo gerado fica em `artifacts\mymcp-vscode-extension.vsix`.

## Configuracao pelo VS Code

No painel MyMCP, use `Configurar projeto`. O formulario salva comandos e limites no `.vscode/settings.json` e guarda o token de aprovacao no armazenamento seguro da extensao. Depois de alterar permissoes ou token, use `Reiniciar servidor MCP`.

As configuracoes principais sao `mymcp.requireWriteApproval`, `mymcp.unitTestCommand`, `mymcp.automatedTestCommand`, `mymcp.allowedTestCommands` e `mymcp.contextTokenBudget`.

## Seguranca e validacao

Antes da entrega, execute `node .\tests\mymcp-smoke.js`. Para exigir aprovacao explicita em escritas, configure `MYMCP_REQUIRE_WRITE_APPROVAL=true` e `MYMCP_WRITE_APPROVAL_TOKEN` no processo do servidor; passe o mesmo token no parametro `approvalToken`. Backups ficam em `.mymcp/backups` e a auditoria em `.mymcp/audit/operations.log`.

## Fluxo recomendado

1. Abra o workspace no VS Code.
2. Selecione o projeto com `MyMCP: Select Active Project`.
3. Rode `MyMCP: Bootstrap Project Docs` para criar `.mymcp/context/main.md` e `.mymcp/specs`.
4. Rode `MyMCP: Bootstrap Docs Pack` para criar `.mymcp/docs`.
5. Rode `MyMCP: Test Connection` para validar o MCP ativo.
6. Use o painel lateral do MyMCP para criar specs, tasks, docs e reiniciar o servidor.
7. Use o MCP no chat/agente para ler, escrever e aplicar ajustes no projeto.

Para cada feature, `CreateFeatureSpec` cria automaticamente `.mymcp/specs/features/<slug>/tests.md` e tarefas obrigatorias de testes. Depois de implementar, informe os caminhos dos arquivos de teste a `ValidateFeatureTests`; a ferramenta falha se algum arquivo ainda nao existir.

## Observacao

O servidor e limitado ao workspace informado pela extensao e nao permite escrita fora dele.
