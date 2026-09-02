using System.ComponentModel;
using ModelContextProtocol.Server;

namespace MyMcp.Server;

[McpServerToolType]
public static class WorkspaceTools
{
    [McpServerTool, Description("List files and folders under the workspace root.")]
    public static string ListFiles(
        WorkspaceService workspace,
        [Description("Relative path inside the workspace root. Leave empty to start at the root.")]
        string? relativePath = null,
        [Description("Maximum directory depth to traverse.")]
        int maxDepth = 2,
        [Description("Include hidden files and folders.")]
        bool includeHidden = false,
        [Description("Maximum number of entries to return.")]
        int maxEntries = 200)
    {
        var entries = workspace.ListFiles(relativePath ?? string.Empty, maxDepth, includeHidden, maxEntries);
        return string.Join(Environment.NewLine, entries);
    }

    [McpServerTool, Description("Read a text file from the workspace and return numbered lines.")]
    public static string ReadFile(
        WorkspaceService workspace,
        [Description("Path relative to the workspace root.")]
        string path,
        [Description("First line number to return, 1-based.")]
        int startLine = 1,
        [Description("Number of lines to return.")]
        int lineCount = 200)
        => workspace.ReadTextFile(path, startLine, lineCount);

    [McpServerTool, Description("Write a text file into the workspace. The file is created or replaced.")]
    public static string WriteFile(
        WorkspaceService workspace,
        [Description("Path relative to the workspace root.")]
        string path,
        [Description("File content to write.")]
        string content,
        [Description("Create missing parent directories.")]
        bool createDirectories = true,
        [Description("Approval token required when MYMCP_REQUIRE_WRITE_APPROVAL=true.")]
        string? approvalToken = null)
        => workspace.WriteTextFile(path, content, createDirectories, approvalToken);

    [McpServerTool, Description("Read a text file in pages to control context size. Use startLine and lineCount for the next page.")]
    public static string ReadFilePage(
        WorkspaceService workspace,
        [Description("Path relative to the workspace root.")] string path,
        [Description("First line of the page, 1-based.")] int startLine = 1,
        [Description("Maximum lines in this page.")] int lineCount = 100)
        => workspace.ReadTextFilePage(path, startLine, lineCount);

    [McpServerTool, Description("Write a text file in pages. Page 1 replaces the file; later pages append when append is true.")]
    public static string WriteFilePage(
        WorkspaceService workspace,
        [Description("Path relative to the workspace root.")] string path,
        [Description("Content for this page.")] string content,
        [Description("Page number, starting at 1.")] int page = 1,
        [Description("Append this page after the existing content. Required for pages after the first.")] bool append = false,
        [Description("Create missing parent directories.")] bool createDirectories = true,
        [Description("Approval token required when write approval is enabled.")] string? approvalToken = null)
        => workspace.WriteTextFilePage(path, content, page, append, createDirectories, approvalToken);

    [McpServerTool, Description("Show the active MyMCP write permissions and approval requirements.")]
    public static string GetWorkspacePermissions(WorkspaceService workspace)
        => workspace.GetWorkspacePermissions();

    [McpServerTool, Description("Restore the last backed-up file change made through MyMCP.")]
    public static string RollbackLastChange(WorkspaceService workspace, [Description("Approval token when write approval is enabled.")] string? approvalToken = null)
        => workspace.RollbackLastChange(approvalToken);

    [McpServerTool, Description("Report the configured MyMCP context budget and estimate the available project context size. The model's private token balance is not exposed to MCP.")]
    public static string GetContextBudget(WorkspaceService workspace)
        => workspace.GetContextBudget();

    [McpServerTool, Description("Read the main context markdown file used to hold durable project rules.")]
    public static string ReadMainContext(WorkspaceService workspace)
        => workspace.ReadMainContext();

    [McpServerTool, Description("Write the main context markdown file used to hold durable project rules.")]
    public static string WriteMainContext(
        WorkspaceService workspace,
        [Description("Markdown content for the main context file.")]
        string content,
        [Description("Create missing parent directories.")]
        bool createDirectories = true)
        => workspace.WriteMainContext(content, createDirectories);

    [McpServerTool, Description("Read a markdown document from the docs pack under .mymcp/docs.")]
    public static string ReadDocsMarkdown(
        WorkspaceService workspace,
        [Description("Relative path inside the docs pack, such as README.md or architecture.md.")]
        string path)
        => workspace.ReadDocsMarkdown(path);

    [McpServerTool, Description("Write a markdown document under the docs pack at .mymcp/docs.")]
    public static string WriteDocsMarkdown(
        WorkspaceService workspace,
        [Description("Relative path inside the docs pack.")]
        string path,
        [Description("Markdown content to write.")]
        string content,
        [Description("Create missing parent directories.")]
        bool createDirectories = true)
        => workspace.WriteDocsMarkdown(path, content, createDirectories);

    [McpServerTool, Description("Create a documentation pack with README, architecture, runbook and decisions files.")]
    public static string CreateDocsPack(
        WorkspaceService workspace,
        [Description("Slug used for the docs folder name.")]
        string slug,
        [Description("Human-readable title for the docs pack.")]
        string title,
        [Description("Short summary of the docs pack.")]
        string summary,
        [Description("Overwrite existing markdown files if the pack already exists.")]
        bool overwriteExisting = false)
        => workspace.CreateDocsPack(new DocsPackRequest(slug, title, summary, overwriteExisting));

    [McpServerTool, Description("List markdown artifacts under the documentation pack.")]
    public static string ListDocsArtifacts(WorkspaceService workspace)
        => string.Join(Environment.NewLine, workspace.ListDocsArtifacts());

    [McpServerTool, Description("List markdown artifacts under the spec-driven design folder.")]
    public static string ListSpecArtifacts(WorkspaceService workspace)
        => string.Join(Environment.NewLine, workspace.ListSpecArtifacts());

    [McpServerTool, Description("Read a markdown document under the spec-driven design folder.")]
    public static string ReadSpecMarkdown(
        WorkspaceService workspace,
        [Description("Path relative to the workspace root.")]
        string path)
    {
        ValidateSpecPath(path);
        return workspace.ReadMarkdownFile(path);
    }

    [McpServerTool, Description("Write a markdown document under the spec-driven design folder.")]
    public static string WriteSpecMarkdown(
        WorkspaceService workspace,
        [Description("Path relative to the workspace root.")]
        string path,
        [Description("Markdown content to write.")]
        string content,
        [Description("Create missing parent directories.")]
        bool createDirectories = true)
    {
        ValidateSpecPath(path);
        return workspace.WriteTextFile(path, content, createDirectories);
    }

    [McpServerTool, Description("Create a feature spec package with spec.md, tasks.md, and notes.md under the spec folder.")]
    public static string CreateFeatureSpec(
        WorkspaceService workspace,
        [Description("Slug used for the feature folder name.")]
        string slug,
        [Description("Human-readable feature title.")]
        string title,
        [Description("Short summary of the feature.")]
        string summary,
        [Description("Overwrite existing markdown files if the package already exists.")]
        bool overwriteExisting = false)
        => workspace.CreateFeatureSpec(new FeatureSpecRequest(slug, title, summary, overwriteExisting));

    [McpServerTool, Description("Create a task markdown document under the spec tasks folder.")]
    public static string CreateTaskDoc(
        WorkspaceService workspace,
        [Description("Slug used for the task file name.")]
        string slug,
        [Description("Human-readable task title.")]
        string title,
        [Description("Short task summary.")]
        string summary,
        [Description("Overwrite the file if it already exists.")]
        bool overwriteExisting = false)
        => workspace.CreateTaskDoc(new TaskDocRequest(slug, title, summary, overwriteExisting));

    [McpServerTool, Description("Create the mandatory unit-test plan for a feature. The feature must not be considered complete until ValidateFeatureTests passes.")]
    public static string CreateFeatureTestPlan(
        WorkspaceService workspace,
        [Description("Slug of the feature under .mymcp/specs/features.")] string slug,
        [Description("Human-readable feature title.")] string title,
        [Description("Short summary of the behavior that must be covered by unit tests.")] string summary,
        [Description("Typical test file paths that the agent must create, relative to the workspace root.")] IReadOnlyList<string>? expectedTestPaths = null,
        [Description("Overwrite the existing test plan and merge the required test tasks.")] bool overwriteExisting = false)
        => workspace.CreateFeatureTestPlan(new FeatureTestPlanRequest(slug, title, summary, expectedTestPaths ?? [], overwriteExisting));

    [McpServerTool, Description("Validate that the unit-test files for a feature exist. This is the completion gate: do not mark the feature done when this tool fails.")]
    public static string ValidateFeatureTests(
        WorkspaceService workspace,
        [Description("Slug of the feature under .mymcp/specs/features.")] string slug,
        [Description("Test file paths relative to the workspace root. Pass every unit-test file created for the feature.")] IReadOnlyList<string> testPaths)
        => workspace.ValidateFeatureTests(slug, testPaths);

    [McpServerTool, Description("Validate the required SDD artifacts for a feature. Fails when spec, tasks, notes or tests are missing.")]
    public static string ValidateFeatureSdd(
        WorkspaceService workspace,
        [Description("Feature slug under .mymcp/specs/features.")] string slug)
        => workspace.ValidateFeatureSdd(slug);

    [McpServerTool, Description("Run a configured unit, integration or automated test command and persist its result in the MyMCP audit history.")]
    public static string RunProjectTests(
        WorkspaceService workspace,
        [Description("Test command, such as dotnet test, npm test, cargo test or pytest.")] string command,
        [Description("Category recorded in the audit, such as unit, integration or automated.")] string kind = "unit")
        => workspace.RunProjectTests(command, kind);

    [McpServerTool, Description("Read recent MyMCP test execution history.")]
    public static string GetTestRunHistory(WorkspaceService workspace, [Description("Maximum records to return.")] int maxEntries = 20)
        => workspace.GetTestRunHistory(maxEntries);

    [McpServerTool, Description("Return git status for the workspace without changing files.")]
    public static string GetGitStatus(WorkspaceService workspace)
        => workspace.GetGitStatus();

    [McpServerTool, Description("Return the current git diff for review before committing changes.")]
    public static string GetGitDiff(WorkspaceService workspace)
        => workspace.GetGitDiff();

    [McpServerTool, Description("Detect the project languages and common test commands from workspace files.")]
    public static string DetectProjectLanguages(WorkspaceService workspace)
        => workspace.DetectProjectLanguages();

    [McpServerTool, Description("Return a compact incremental context based on core rules and files changed in git.")]
    public static string GetIncrementalContext(WorkspaceService workspace)
        => workspace.GetIncrementalContext();

    [McpServerTool, Description("List the built-in agent workflow profiles.")]
    public static string ListAgentProfiles(WorkspaceService workspace)
        => workspace.ListAgentProfiles();

    [McpServerTool, Description("Read a built-in agent workflow profile.")]
    public static string ReadAgentProfile(WorkspaceService workspace, [Description("Profile name: analysis, implementation, tests, review or documentation.")] string profile)
        => workspace.ReadAgentProfile(profile);

    [McpServerTool, Description("Read durable architectural decisions and project memory.")]
    public static string ReadDecisionMemory(WorkspaceService workspace)
        => workspace.ReadDecisionMemory();

    [McpServerTool, Description("Append a durable decision to the project memory.")]
    public static string WriteDecisionMemory(WorkspaceService workspace, [Description("Decision text, including context and rationale.")] string decision, [Description("Optional area or feature label.")] string area = "general", string? approvalToken = null)
        => workspace.WriteDecisionMemory(decision, area, approvalToken);

    [McpServerTool, Description("Apply one or more exact text edits to an existing file.")]
    public static string ApplyTextEdits(
        WorkspaceService workspace,
        [Description("Path relative to the workspace root.")]
        string path,
        [Description("Ordered list of edits to apply.")]
        IReadOnlyList<TextEditRequest> edits,
        [Description("Approval token required when write approval is enabled.")] string? approvalToken = null)
        => workspace.ApplyTextEdits(path, edits, approvalToken);

    [McpServerTool, Description("Search for text across workspace files and return matching snippets.")]
    public static string SearchText(
        WorkspaceService workspace,
        [Description("Text to search for.")]
        string query,
        [Description("Optional relative path to limit the search.")]
        string? relativePath = null,
        [Description("Maximum number of matches to return.")]
        int maxResults = 50)
    {
        var hits = workspace.SearchText(query, relativePath, maxResults);

        if (hits.Count == 0)
        {
            return "No matches found.";
        }

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            hits.Select(hit => $"# {hit.Path}:{hit.Line}{Environment.NewLine}{hit.Snippet}"));
    }

    private static void ValidateSpecPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must not be empty.", nameof(path));
        }

        var normalized = path.Replace('\\', '/');
        if (!normalized.Contains("/.mymcp/specs/", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith(".mymcp/specs/", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Equals(".mymcp/specs", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Spec markdown tools only allow paths under .mymcp/specs.");
        }
    }
}

public sealed record TextEditRequest(
    [property: Description("Exact text to replace.")] string OldText,
    [property: Description("Replacement text.")] string? NewText,
    [property: Description("Replace every occurrence instead of just one.")] bool AllOccurrences = false);
