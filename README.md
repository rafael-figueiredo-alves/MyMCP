# MyMCP

Servidor MCP em C#/.NET 10 com extensao do VS Code para registrar o servidor local automaticamente.

## O que este repositorio entrega

- servidor MCP em `net10.0`
- ferramentas para listar, ler, buscar, escrever e aplicar edicoes em arquivos
- ferramentas dedicadas para contexto principal e fluxo spec-driven em Markdown
- extensao do VS Code que registra o servidor via `vscode.lm.registerMcpServerDefinitionProvider`

## Estrutura

- `server/MyMcp.Server` - servidor MCP stdio
- `vscode-extension` - extensao VS Code que publica o servidor para o editor
- `.mymcp/context/main.md` - contexto principal duravel do projeto
- `.mymcp/specs` - pasta para specs, tasks e notas

## Ferramentas expostas

- `ListFiles`
- `ReadFile`
- `SearchText`
- `WriteFile`
- `ApplyTextEdits`
- `ReadMainContext`
- `WriteMainContext`
- `ListSpecArtifacts`
- `ReadSpecMarkdown`
- `WriteSpecMarkdown`
- `CreateFeatureSpec`
- `CreateTaskDoc`

## Fluxo sugerido

1. Leia `.mymcp/context/main.md` para pegar as regras duraveis.
2. Crie uma feature com `CreateFeatureSpec`.
3. Detalhe ou refine docs com `WriteSpecMarkdown`.
4. Crie tasks com `CreateTaskDoc`.
5. Use as tools de leitura e escrita para implementar a feature seguindo a spec.

## Observacao

O servidor e limitado ao workspace informado pela extensao e nao permite escrita fora dele.
