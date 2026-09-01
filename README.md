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
- `ApplyTextEdits`
- `ReadMainContext`
- `WriteMainContext`
- `ReadDocsMarkdown`
- `WriteDocsMarkdown`
- `CreateFeatureSpec`
- `CreateTaskDoc`
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

## Fluxo recomendado

1. Abra o workspace no VS Code.
2. Selecione o projeto com `MyMCP: Select Active Project`.
3. Rode `MyMCP: Bootstrap Project Docs` para criar `.mymcp/context/main.md` e `.mymcp/specs`.
4. Rode `MyMCP: Bootstrap Docs Pack` para criar `.mymcp/docs`.
5. Rode `MyMCP: Test Connection` para validar o MCP ativo.
6. Use o painel lateral do MyMCP para criar specs, tasks, docs e reiniciar o servidor.
7. Use o MCP no chat/agente para ler, escrever e aplicar ajustes no projeto.

## Observacao

O servidor e limitado ao workspace informado pela extensao e nao permite escrita fora dele.
