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
        bool createDirectories = true)
        => workspace.WriteTextFile(path, content, createDirectories);

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

    [McpServerTool, Description("Apply one or more exact text edits to an existing file.")]
    public static string ApplyTextEdits(
        WorkspaceService workspace,
        [Description("Path relative to the workspace root.")]
        string path,
        [Description("Ordered list of edits to apply.")]
        IReadOnlyList<TextEditRequest> edits)
        => workspace.ApplyTextEdits(path, edits);

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
