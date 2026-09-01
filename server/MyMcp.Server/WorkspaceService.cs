using System.Text;

namespace MyMcp.Server;

public sealed class WorkspaceService
{
    public const string MainContextRelativePath = ".mymcp/context/main.md";
    public const string SpecsRootRelativePath = ".mymcp/specs";
    public const string DocsRootRelativePath = ".mymcp/docs";
    public const string FeatureSpecsRootRelativePath = ".mymcp/specs/features";
    public const string TaskDocsRootRelativePath = ".mymcp/specs/tasks";

    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        ".idea",
        "bin",
        "obj",
        "node_modules",
        "dist",
        "out",
        "coverage"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".sln", ".slnx", ".json", ".jsonc", ".md", ".txt", ".yml", ".yaml",
        ".xml", ".config", ".props", ".targets", ".js", ".jsx", ".ts", ".tsx", ".css",
        ".html", ".razor", ".sql", ".editorconfig", ".gitignore", ".gitattributes",
        ".pas", ".dpr", ".dpk", ".inc", ".dfm", ".fmx", ".lfm", ".lpr", ".dproj", ".groupproj"
    };

    public WorkspaceService(WorkspaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        RootPath = Path.GetFullPath(options.RootPath);

        if (!Directory.Exists(RootPath))
        {
            throw new DirectoryNotFoundException($"Workspace root does not exist: {RootPath}");
        }
    }

    public string RootPath { get; }

    public string ResolvePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Path must not be empty.", nameof(relativePath));
        }

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        var combined = Path.GetFullPath(Path.Combine(RootPath, normalized));
        if (!IsInsideRoot(combined))
        {
            throw new InvalidOperationException($"Path escapes the workspace root: {relativePath}");
        }

        return combined;
    }

    public string GetRelativePath(string absolutePath)
    {
        var normalizedRoot = EnsureTrailingSeparator(Path.GetFullPath(RootPath));
        var normalizedPath = Path.GetFullPath(absolutePath);

        if (!normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Path is outside the workspace root: {absolutePath}");
        }

        var relative = normalizedPath[normalizedRoot.Length..];
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    public IReadOnlyList<string> ListFiles(string relativePath, int maxDepth, bool includeHidden, int maxEntries)
    {
        var startDirectory = string.IsNullOrWhiteSpace(relativePath)
            ? RootPath
            : ResolvePath(relativePath);

        if (!Directory.Exists(startDirectory))
        {
            throw new DirectoryNotFoundException($"Directory not found: {relativePath}");
        }

        var results = new List<string>();
        EnumerateDirectory(startDirectory, 0, maxDepth, includeHidden, maxEntries, results);
        return results;
    }

    public string ReadTextFile(string relativePath, int startLine, int lineCount)
    {
        var filePath = ResolvePath(relativePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {relativePath}", filePath);
        }

        var lines = File.ReadAllLines(filePath, Encoding.UTF8);
        if (lines.Length == 0)
        {
            return $"# {GetRelativePath(filePath)}\n(empty file)";
        }

        var firstLine = Math.Max(1, startLine);
        var count = Math.Max(1, lineCount);
        var lastLine = Math.Min(lines.Length, firstLine + count - 1);

        var builder = new StringBuilder();
        builder.AppendLine($"# {GetRelativePath(filePath)}");
        builder.AppendLine($"lines {firstLine}-{lastLine} of {lines.Length}");

        for (var lineIndex = firstLine; lineIndex <= lastLine; lineIndex++)
        {
            builder.Append(lineIndex.ToString().PadLeft(5));
            builder.Append(" | ");
            builder.AppendLine(lines[lineIndex - 1]);
        }

        return builder.ToString().TrimEnd();
    }

    public string WriteTextFile(string relativePath, string content, bool createDirectories)
    {
        var filePath = ResolvePath(relativePath);
        var directory = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrEmpty(directory) && createDirectories)
        {
            Directory.CreateDirectory(directory);
        }

        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Target directory does not exist: {directory}");
        }

        File.WriteAllText(filePath, content, Encoding.UTF8);
        return $"Wrote {GetRelativePath(filePath)} ({content.Length} chars)";
    }

    public string ReadMarkdownFile(string relativePath)
        => ReadTextFile(relativePath, 1, int.MaxValue);

    public string ReadMainContext()
    {
        var path = ResolvePath(MainContextRelativePath);
        if (!File.Exists(path))
        {
            return BuildMainContextTemplate();
        }

        return ReadMarkdownFile(MainContextRelativePath);
    }

    public string WriteMainContext(string content, bool createDirectories)
        => WriteTextFile(MainContextRelativePath, content, createDirectories);

    public string ReadDocsMarkdown(string relativePath)
        => ReadMarkdownFile(Path.Combine(DocsRootRelativePath, relativePath));

    public string WriteDocsMarkdown(string relativePath, string content, bool createDirectories)
        => WriteTextFile(Path.Combine(DocsRootRelativePath, relativePath), content, createDirectories);

    public string CreateDocsPack(DocsPackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var slug = NormalizeSlug(request.Slug);
        var docsRoot = ResolvePath(Path.Combine(DocsRootRelativePath, slug));
        Directory.CreateDirectory(docsRoot);

        var readmePath = Path.Combine(docsRoot, "README.md");
        var architecturePath = Path.Combine(docsRoot, "architecture.md");
        var runbookPath = Path.Combine(docsRoot, "runbook.md");
        var decisionsPath = Path.Combine(docsRoot, "decisions.md");

        if (!request.OverwriteExisting &&
            (File.Exists(readmePath) || File.Exists(architecturePath) || File.Exists(runbookPath) || File.Exists(decisionsPath)))
        {
            throw new InvalidOperationException($"Documentation pack already exists: {slug}");
        }

        File.WriteAllText(readmePath, BuildDocsReadmeTemplate(request), Encoding.UTF8);
        File.WriteAllText(architecturePath, BuildArchitectureDocTemplate(request), Encoding.UTF8);
        File.WriteAllText(runbookPath, BuildRunbookDocTemplate(request), Encoding.UTF8);
        File.WriteAllText(decisionsPath, BuildDecisionsDocTemplate(request), Encoding.UTF8);

        return string.Join(
            Environment.NewLine,
            new[]
            {
                $"Created docs readme: {GetRelativePath(readmePath)}",
                $"Created architecture doc: {GetRelativePath(architecturePath)}",
                $"Created runbook: {GetRelativePath(runbookPath)}",
                $"Created decisions log: {GetRelativePath(decisionsPath)}"
            });
    }

    public string CreateFeatureSpec(FeatureSpecRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var slug = NormalizeSlug(request.Slug);
        var featureRoot = ResolvePath(Path.Combine(FeatureSpecsRootRelativePath, slug));
        Directory.CreateDirectory(featureRoot);

        var specPath = Path.Combine(featureRoot, "spec.md");
        var tasksPath = Path.Combine(featureRoot, "tasks.md");
        var notesPath = Path.Combine(featureRoot, "notes.md");

        if (!request.OverwriteExisting && (File.Exists(specPath) || File.Exists(tasksPath) || File.Exists(notesPath)))
        {
            throw new InvalidOperationException($"Feature spec already exists: {slug}");
        }

        File.WriteAllText(specPath, BuildFeatureSpecTemplate(request), Encoding.UTF8);
        File.WriteAllText(tasksPath, BuildFeatureTasksTemplate(request), Encoding.UTF8);
        File.WriteAllText(notesPath, BuildFeatureNotesTemplate(request), Encoding.UTF8);

        return string.Join(
            Environment.NewLine,
            new[]
            {
                $"Created feature spec: {GetRelativePath(specPath)}",
                $"Created task plan: {GetRelativePath(tasksPath)}",
                $"Created notes: {GetRelativePath(notesPath)}"
            });
    }

    public string CreateTaskDoc(TaskDocRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var slug = NormalizeSlug(request.Slug);
        var taskPath = ResolvePath(Path.Combine(TaskDocsRootRelativePath, $"{slug}.md"));

        if (File.Exists(taskPath) && !request.OverwriteExisting)
        {
            throw new InvalidOperationException($"Task doc already exists: {slug}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(taskPath)!);
        File.WriteAllText(taskPath, BuildTaskDocTemplate(request), Encoding.UTF8);
        return $"Created task doc: {GetRelativePath(taskPath)}";
    }

    public IReadOnlyList<string> ListSpecArtifacts()
    {
        var root = ResolvePath(SpecsRootRelativePath);
        if (!Directory.Exists(root))
        {
            return [];
        }

        return EnumerateTextFiles(root)
            .Select(GetRelativePath)
            .Where(path => path.StartsWith($"{SpecsRootRelativePath}/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> ListDocsArtifacts()
    {
        var root = ResolvePath(DocsRootRelativePath);
        if (!Directory.Exists(root))
        {
            return [];
        }

        return EnumerateTextFiles(root)
            .Select(GetRelativePath)
            .Where(path => path.StartsWith($"{DocsRootRelativePath}/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string ApplyTextEdits(string relativePath, IReadOnlyList<TextEditRequest> edits)
    {
        if (edits.Count == 0)
        {
            throw new ArgumentException("At least one edit is required.", nameof(edits));
        }

        var filePath = ResolvePath(relativePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {relativePath}", filePath);
        }

        var content = File.ReadAllText(filePath, Encoding.UTF8);
        var original = content;

        foreach (var edit in edits)
        {
            if (string.IsNullOrEmpty(edit.OldText))
            {
                throw new ArgumentException("Each edit must provide non-empty oldText.");
            }

            if (edit.AllOccurrences)
            {
                var count = CountOccurrences(content, edit.OldText);
                if (count == 0)
                {
                    throw new InvalidOperationException($"Pattern not found: {edit.OldText}");
                }

                content = content.Replace(edit.OldText, edit.NewText ?? string.Empty, StringComparison.Ordinal);
                continue;
            }

            var index = content.IndexOf(edit.OldText, StringComparison.Ordinal);
            if (index < 0)
            {
                throw new InvalidOperationException($"Pattern not found: {edit.OldText}");
            }

            var duplicateIndex = content.IndexOf(edit.OldText, index + edit.OldText.Length, StringComparison.Ordinal);
            if (duplicateIndex >= 0)
            {
                throw new InvalidOperationException($"Pattern is ambiguous because it appears multiple times: {edit.OldText}");
            }

            content = content[..index] + (edit.NewText ?? string.Empty) + content[(index + edit.OldText.Length)..];
        }

        if (content == original)
        {
            return $"No changes applied to {GetRelativePath(filePath)}";
        }

        File.WriteAllText(filePath, content, Encoding.UTF8);
        return $"Updated {GetRelativePath(filePath)} ({content.Length} chars)";
    }

    public IReadOnlyList<SearchHit> SearchText(string query, string? relativePath, int maxResults)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Query must not be empty.", nameof(query));
        }

        var searchRoot = string.IsNullOrWhiteSpace(relativePath)
            ? RootPath
            : ResolvePath(relativePath);

        var hits = new List<SearchHit>();
        foreach (var file in EnumerateTextFiles(searchRoot))
        {
            if (hits.Count >= maxResults)
            {
                break;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(file, Encoding.UTF8);
            }
            catch
            {
                continue;
            }

            for (var i = 0; i < lines.Length && hits.Count < maxResults; i++)
            {
                if (lines[i].IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var snippetStart = Math.Max(0, i - 1);
                var snippetEnd = Math.Min(lines.Length - 1, i + 1);
                var snippet = string.Join(Environment.NewLine, lines[snippetStart..(snippetEnd + 1)]);

                hits.Add(new SearchHit(
                    GetRelativePath(file),
                    i + 1,
                    snippet));
            }
        }

        return hits;
    }

    private void EnumerateDirectory(
        string directory,
        int depth,
        int maxDepth,
        bool includeHidden,
        int maxEntries,
        List<string> results)
    {
        if (results.Count >= maxEntries || depth > maxDepth)
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if (results.Count >= maxEntries)
            {
                return;
            }

            var name = Path.GetFileName(entry);
            if (IsHidden(name) && !includeHidden)
            {
                continue;
            }

            if (Directory.Exists(entry))
            {
                if (IgnoredDirectoryNames.Contains(name))
                {
                    continue;
                }

                results.Add($"{new string(' ', depth * 2)}[dir] {GetRelativePath(entry)}");
                EnumerateDirectory(entry, depth + 1, maxDepth, includeHidden, maxEntries, results);
            }
            else
            {
                results.Add($"{new string(' ', depth * 2)}{GetRelativePath(entry)}");
            }
        }
    }

    private IEnumerable<string> EnumerateTextFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var relative = GetRelativePath(file);
            if (relative.Split('/').Any(part => IgnoredDirectoryNames.Contains(part)))
            {
                continue;
            }

            if (!IsLikelyTextFile(file))
            {
                continue;
            }

            yield return file;
        }
    }

    private static bool IsHidden(string name)
        => name.StartsWith('.');

    private static bool IsLikelyTextFile(string path)
    {
        var extension = Path.GetExtension(path);
        if (TextExtensions.Contains(extension))
        {
            return true;
        }

        return string.IsNullOrEmpty(extension) || File.ReadAllBytes(path).Take(4096).All(IsTextByte);
    }

    private static bool IsTextByte(byte value)
        => value is 9 or 10 or 13 or >= 32 and <= 126;

    private bool IsInsideRoot(string path)
    {
        var root = EnsureTrailingSeparator(Path.GetFullPath(RootPath));
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;

    private static int CountOccurrences(string content, string pattern)
    {
        var count = 0;
        var index = 0;

        while ((index = content.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }

    private static string NormalizeSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("Slug must not be empty.", nameof(slug));
        }

        var normalized = slug.Trim().ToLowerInvariant();
        var builder = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                continue;
            }

            if (ch is '-' or '_' or ' ')
            {
                builder.Append('-');
            }
        }

        var result = string.Join(
            "-",
            builder.ToString()
                .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (string.IsNullOrWhiteSpace(result))
        {
            throw new ArgumentException("Slug must contain at least one alphanumeric character.", nameof(slug));
        }

        return result;
    }

    private static string BuildMainContextTemplate() =>
        """
        # Main Context

        ## Goals

        - Document the product goal, boundaries, and non-goals here.

        ## Rules

        - Add architectural, domain, and delivery rules that must remain stable.
        - Keep this file short and durable.

        ## Constraints

        - List constraints that must hold across all features.

        ## Decisions

        - Record durable decisions that future changes should respect.
        """;

    private static string BuildFeatureSpecTemplate(FeatureSpecRequest request)
        => $"""
        # {request.Title}

        ## Summary

        {request.Summary}

        ## Problem

        Describe the problem this feature solves.

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
        """;

    private static string BuildFeatureTasksTemplate(FeatureSpecRequest request)
        => $"""
        # Tasks for {request.Title}

        - [ ] Review main context
        - [ ] Refine spec
        - [ ] Implement feature
        - [ ] Validate behavior
        """;

    private static string BuildFeatureNotesTemplate(FeatureSpecRequest request)
        => $"""
        # Notes for {request.Title}

        - Track implementation notes here.
        - Record tradeoffs, open questions, and follow-ups.
        """;

    private static string BuildTaskDocTemplate(TaskDocRequest request)
        => $"""
        # {request.Title}

        ## Summary

        {request.Summary}

        ## Context

        - Related feature or issue:

        ## Steps

        - [ ] Step 1
        - [ ] Step 2

        ## Done When

        - [ ] Task is validated
        """;

    private static string BuildDocsReadmeTemplate(DocsPackRequest request)
        => $"""
        # {request.Title}

        ## Summary

        {request.Summary}

        ## Included Documents

        - architecture.md
        - runbook.md
        - decisions.md

        ## Purpose

        Keep durable project documentation close to the code and aligned with the main context.
        """;

    private static string BuildArchitectureDocTemplate(DocsPackRequest request)
        => $"""
        # Architecture - {request.Title}

        ## Overview

        {request.Summary}

        ## Components

        - Application layers
        - MCP integration
        - Editor integration

        ## Constraints

        - Keep the design modular.
        - Avoid coupling business rules to transport concerns.
        """;

    private static string BuildRunbookDocTemplate(DocsPackRequest request)
        => $"""
        # Runbook - {request.Title}

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
        """;

    private static string BuildDecisionsDocTemplate(DocsPackRequest request)
        => $"""
        # Decisions - {request.Title}

        - Record durable architecture and workflow decisions here.
        - Prefer short entries that explain why a choice was made.
        - Capture the date, context, and follow-up if needed.
        """;
}

public sealed record SearchHit(string Path, int Line, string Snippet);

public sealed record FeatureSpecRequest(
    string Slug,
    string Title,
    string Summary,
    bool OverwriteExisting = false);

public sealed record TaskDocRequest(
    string Slug,
    string Title,
    string Summary,
    bool OverwriteExisting = false);

public sealed record DocsPackRequest(
    string Slug,
    string Title,
    string Summary,
    bool OverwriteExisting = false);
