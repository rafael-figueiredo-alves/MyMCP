using System.Diagnostics;
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

    public string ReadTextFilePage(string relativePath, int startLine, int lineCount)
    {
        var content = ReadTextFile(relativePath, startLine, lineCount);
        var filePath = ResolvePath(relativePath);
        var totalLines = File.ReadLines(filePath).Count();
        var nextLine = Math.Max(1, startLine) + Math.Max(1, lineCount);
        return $"{content}\n\npageStartLine: {Math.Max(1, startLine)}\npageSize: {Math.Max(1, lineCount)}\nhasMore: {nextLine <= totalLines}\nnextStartLine: {nextLine}";
    }

    public string WriteTextFile(string relativePath, string content, bool createDirectories, string? approvalToken = null)
    {
        EnsureWriteAllowed(approvalToken);
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

        BackupFile(filePath);
        File.WriteAllText(filePath, content, Encoding.UTF8);
        WriteAudit("write", GetRelativePath(filePath), $"chars={content.Length}");
        return $"Wrote {GetRelativePath(filePath)} ({content.Length} chars)";
    }

    public string WriteTextFilePage(string relativePath, string content, int page, bool append, bool createDirectories, string? approvalToken = null)
    {
        EnsureWriteAllowed(approvalToken);
        if (page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(page), "Page must be at least 1.");
        }

        if (page > 1 && !append)
        {
            throw new InvalidOperationException("Pages after the first must set append=true.");
        }

        if (page == 1 && append)
        {
            throw new InvalidOperationException("The first page must set append=false.");
        }

        var filePath = ResolvePath(relativePath);
        var directory = Path.GetDirectoryName(filePath);
        if (page > 1 && !File.Exists(filePath))
        {
            throw new FileNotFoundException("The first page must be written before later pages.", filePath);
        }

        if (!string.IsNullOrEmpty(directory) && createDirectories)
        {
            Directory.CreateDirectory(directory);
        }

        BackupFile(filePath);
        File.AppendAllText(filePath, page == 1 ? content : Environment.NewLine + content, Encoding.UTF8);
        WriteAudit("write-page", GetRelativePath(filePath), $"page={page};chars={content.Length}");
        return $"Wrote page {page} to {GetRelativePath(filePath)} ({content.Length} chars)";
    }

    public string GetContextBudget()
    {
        var budget = 12000;
        var configured = Environment.GetEnvironmentVariable("MYMCP_CONTEXT_TOKEN_BUDGET");
        if (int.TryParse(configured, out var parsed) && parsed > 0)
        {
            budget = parsed;
        }

        var contextFiles = new[]
        {
            MainContextRelativePath,
            Path.Combine(SpecsRootRelativePath, "README.md"),
            Path.Combine(DocsRootRelativePath, "README.md")
        };
        var chars = contextFiles
            .Select(ResolvePath)
            .Where(File.Exists)
            .Sum(path => new FileInfo(path).Length);
        var estimatedTokens = (int)Math.Ceiling(chars / 4.0);

        return string.Join(Environment.NewLine,
            $"configuredContextTokenBudget: {budget}",
            $"estimatedCoreContextTokens: {estimatedTokens}",
            $"estimatedRemainingConfiguredTokens: {Math.Max(0, budget - estimatedTokens)}",
            "modelPrivateTokenBalance: unavailable",
            "recommendation: use ReadFilePage for large files and refresh this report before the next large task.");
    }

    public string GetWorkspacePermissions()
    {
        var approval = string.Equals(Environment.GetEnvironmentVariable("MYMCP_REQUIRE_WRITE_APPROVAL"), "true", StringComparison.OrdinalIgnoreCase);
        var tokenConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MYMCP_WRITE_APPROVAL_TOKEN"));
        return string.Join(Environment.NewLine,
            $"workspaceRoot: {RootPath}",
            "read: allowed",
            $"write: {(approval ? "allowed with approval token" : "allowed")}",
            "delete: disabled",
            "gitWrite: disabled",
            $"approvalTokenConfigured: {tokenConfigured}");
    }

    public string RollbackLastChange(string? approvalToken = null)
    {
        EnsureWriteAllowed(approvalToken);
        var backupRoot = Path.Combine(RootPath, ".mymcp", "backups");
        if (!Directory.Exists(backupRoot)) throw new InvalidOperationException("No backup is available.");
        var backup = Directory.EnumerateFiles(backupRoot, "*.bak", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        if (backup is null) throw new InvalidOperationException("No backup is available.");
        var relative = Path.GetRelativePath(backupRoot, backup);
        var original = Path.Combine(RootPath, relative[..^4]);
        Directory.CreateDirectory(Path.GetDirectoryName(original)!);
        File.Copy(backup, original, true);
        WriteAudit("rollback", GetRelativePath(original), $"backup={GetRelativePath(backup)}");
        return $"Restored {GetRelativePath(original)} from backup.";
    }

    public string ValidateFeatureSdd(string featureSlug)
    {
        var slug = NormalizeSlug(featureSlug);
        var root = Path.Combine(FeatureSpecsRootRelativePath, slug);
        var required = new[] { "spec.md", "tasks.md", "notes.md", "tests.md" };
        var missing = required.Where(name => !File.Exists(ResolvePath(Path.Combine(root, name)))).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException($"SDD validation failed for '{slug}'. Missing: {string.Join(", ", missing)}");
        }

        var spec = File.ReadAllText(ResolvePath(Path.Combine(root, "spec.md")), Encoding.UTF8);
        if (!spec.Contains("## Acceptance Criteria", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"SDD validation failed for '{slug}'. spec.md has no Acceptance Criteria section.");
        }

        var criteriaCount = spec.Split("## Acceptance Criteria", 2, StringSplitOptions.None)[1]
            .Split("## ", 2, StringSplitOptions.None)[0]
            .Split('\n')
            .Count(line => line.TrimStart().StartsWith("- ", StringComparison.Ordinal));
        var testPlan = File.ReadAllText(ResolvePath(Path.Combine(root, "tests.md")), Encoding.UTF8);
        var coverageRows = testPlan.Split('\n').Count(line => line.TrimStart().StartsWith("|", StringComparison.Ordinal) && !line.Contains("---", StringComparison.Ordinal));
        if (criteriaCount > 0 && coverageRows < criteriaCount)
        {
            throw new InvalidOperationException($"SDD validation failed for '{slug}'. tests.md must map every acceptance criterion in its Coverage Matrix.");
        }

        return $"SDD validation passed for feature '{slug}'. Required artifacts are present.";
    }

    public string RunProjectTests(string command, string kind)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("Test command must not be empty.", nameof(command));
        }

        var trimmed = command.Trim();
        var allowed = new[] { "dotnet test", "npm test", "npm run test", "cargo test", "pytest", "python -m pytest" };
        var extra = Environment.GetEnvironmentVariable("MYMCP_ALLOWED_TEST_COMMANDS")?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        if (!allowed.Concat(extra).Any(prefix => trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) || trimmed.Any(ch => "&|><;".Contains(ch)))
        {
            throw new InvalidOperationException("Test command rejected. Use a known test command or authorize its prefix with MYMCP_ALLOWED_TEST_COMMANDS.");
        }

        var shell = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";
        var shellArguments = OperatingSystem.IsWindows() ? $"/c {trimmed}" : $"-lc \"{trimmed.Replace("\"", "\\\"")}\"";
        var startInfo = new ProcessStartInfo(shell, shellArguments)
        {
            WorkingDirectory = RootPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start test process.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        var result = $"kind={kind};command={trimmed};exitCode={process.ExitCode}\n{output}\n{error}".Trim();
        WriteAudit("test", kind, result);
        return result;
    }

    public string GetTestRunHistory(int maxEntries)
    {
        var path = ResolvePath(Path.Combine(".mymcp", "audit", "operations.log"));
        if (!File.Exists(path)) return "No test executions recorded.";
        var lines = File.ReadAllLines(path, Encoding.UTF8)
            .Where(line => line.Contains("|test|", StringComparison.OrdinalIgnoreCase))
            .TakeLast(Math.Clamp(maxEntries, 1, 100))
            .ToArray();
        return lines.Length == 0 ? "No test executions recorded." : string.Join(Environment.NewLine, lines);
    }

    public string GetGitStatus() => RunGit("status --short");

    public string GetGitDiff() => RunGit("diff --");

    public string DetectProjectLanguages()
    {
        var extensions = EnumerateTextFiles(RootPath).Select(Path.GetExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var languages = new List<string>();
        if (extensions.Any(e => e is ".cs" or ".csproj" or ".sln" or ".slnx")) languages.Add("C#/.NET (dotnet test)");
        if (extensions.Any(e => e is ".pas" or ".dpr" or ".dproj" or ".dfm")) languages.Add("Delphi/Object Pascal (configure test command)");
        if (File.Exists(Path.Combine(RootPath, "Cargo.toml"))) languages.Add("Rust (cargo test)");
        if (File.Exists(Path.Combine(RootPath, "package.json"))) languages.Add("JavaScript/TypeScript (npm test)");
        if (extensions.Any(e => e is ".py")) languages.Add("Python (pytest)");
        return languages.Count == 0 ? "No supported project markers detected." : string.Join(Environment.NewLine, languages);
    }

    public string GetIncrementalContext()
    {
        var changed = RunGit("diff --name-only");
        var core = new[] { MainContextRelativePath, Path.Combine(SpecsRootRelativePath, "README.md") }
            .Where(path => File.Exists(ResolvePath(path)));
        return string.Join(Environment.NewLine,
            "# Incremental context",
            "Read these durable rules first:",
            string.Join(Environment.NewLine, core.Select(path => $"- {path}")),
            "Changed files from git:",
            string.IsNullOrWhiteSpace(changed) ? "(working tree clean)" : changed,
            "Use ReadFilePage for changed files larger than the context budget.");
    }

    public string ListAgentProfiles() => "analysis\nimplementation\ntests\nreview\ndocumentation";

    public string ReadAgentProfile(string profile)
        => profile.Trim().ToLowerInvariant() switch
        {
            "analysis" => "Read context, specs and git diff. Do not write files. Return risks, dependencies and questions.",
            "implementation" => "Read the full SDD literature, implement the smallest coherent change, update tasks and generate tests.",
            "tests" => "Read tests.md and acceptance criteria, create meaningful unit tests, run the configured command and validate the feature gate.",
            "review" => "Inspect git diff, security boundaries, regressions, SDD completeness and test evidence. Do not modify files.",
            "documentation" => "Read the current code and decisions, then update concise durable documentation without duplicating transient details.",
            _ => throw new ArgumentException("Unknown profile. Use analysis, implementation, tests, review or documentation.", nameof(profile))
        };

    public string ReadDecisionMemory()
    {
        var path = Path.Combine(".mymcp", "context", "decisions.md");
        return File.Exists(ResolvePath(path)) ? ReadMarkdownFile(path) : "No durable decisions recorded.";
    }

    public string WriteDecisionMemory(string decision, string area, string? approvalToken = null)
    {
        if (string.IsNullOrWhiteSpace(decision)) throw new ArgumentException("Decision must not be empty.", nameof(decision));
        EnsureWriteAllowed(approvalToken);
        var path = ResolvePath(Path.Combine(".mymcp", "context", "decisions.md"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        BackupFile(path);
        File.AppendAllText(path, $"\n## {area.Trim()} - {DateTimeOffset.UtcNow:yyyy-MM-dd}\n\n{decision.Trim()}\n", Encoding.UTF8);
        WriteAudit("decision", GetRelativePath(path), area);
        return $"Recorded decision in {GetRelativePath(path)}.";
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
        WriteAudit("create-docs", GetRelativePath(docsRoot), $"slug={slug}");

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
        WriteAudit("create-feature", GetRelativePath(featureRoot), $"slug={slug}");

        var testPlanResult = CreateFeatureTestPlan(new FeatureTestPlanRequest(
            request.Slug,
            request.Title,
            request.Summary,
            []));

        return string.Join(
            Environment.NewLine,
            new[]
            {
                $"Created feature spec: {GetRelativePath(specPath)}",
                $"Created task plan: {GetRelativePath(tasksPath)}",
                $"Created notes: {GetRelativePath(notesPath)}",
                testPlanResult
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
        WriteAudit("create-task", GetRelativePath(taskPath), $"slug={slug}");
        return $"Created task doc: {GetRelativePath(taskPath)}";
    }

    public string CreateFeatureTestPlan(FeatureTestPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var slug = NormalizeSlug(request.Slug);
        var featureRoot = ResolvePath(Path.Combine(FeatureSpecsRootRelativePath, slug));
        Directory.CreateDirectory(featureRoot);

        var planPath = Path.Combine(featureRoot, "tests.md");
        if (File.Exists(planPath) && !request.OverwriteExisting)
        {
            throw new InvalidOperationException($"Feature test plan already exists: {slug}");
        }

        File.WriteAllText(planPath, BuildFeatureTestPlanTemplate(request, slug), Encoding.UTF8);

        var tasksPath = Path.Combine(featureRoot, "tasks.md");
        var tasks = File.Exists(tasksPath) ? File.ReadAllText(tasksPath, Encoding.UTF8) : $"# Tasks for {request.Title}\n";
        var requiredTasks = "\n## Mandatory Unit Tests\n\n- [ ] Create unit tests for the feature behavior\n- [ ] Cover acceptance criteria and failure cases\n- [ ] Run the project test command\n- [ ] Run ValidateFeatureTests and resolve every failure\n";
        if (!tasks.Contains("## Mandatory Unit Tests", StringComparison.Ordinal))
        {
            File.WriteAllText(tasksPath, tasks.TrimEnd() + Environment.NewLine + requiredTasks, Encoding.UTF8);
        }

        return string.Join(Environment.NewLine,
            $"Created mandatory test plan: {GetRelativePath(planPath)}",
            "Added mandatory unit-test tasks to the feature task list.",
            "Implement the tests, then call ValidateFeatureTests before completing the feature.");
    }

    public string ValidateFeatureTests(string featureSlug, IReadOnlyList<string> testPaths)
    {
        var slug = NormalizeSlug(featureSlug);
        var planPath = ResolvePath(Path.Combine(FeatureSpecsRootRelativePath, slug, "tests.md"));
        if (!File.Exists(planPath))
        {
            throw new InvalidOperationException($"Missing mandatory test plan: {GetRelativePath(planPath)}. Call CreateFeatureTestPlan first.");
        }

        if (testPaths is null || testPaths.Count == 0)
        {
            throw new InvalidOperationException("No unit-test paths were provided. Create the tests and pass their workspace-relative paths.");
        }

        var missing = testPaths
            .Where(string.IsNullOrWhiteSpace)
            .Concat(testPaths.Where(path => !string.IsNullOrWhiteSpace(path) && !File.Exists(ResolvePath(path))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidOperationException($"Unit-test gate failed. Missing test files: {string.Join(", ", missing)}");
        }

        return $"Unit-test gate passed for feature '{slug}'. Validated {testPaths.Count} test file(s). Run the project's test command before final delivery.";
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

    public string ApplyTextEdits(string relativePath, IReadOnlyList<TextEditRequest> edits, string? approvalToken = null)
    {
        EnsureWriteAllowed(approvalToken);
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

        BackupFile(filePath);
        File.WriteAllText(filePath, content, Encoding.UTF8);
        WriteAudit("edit", GetRelativePath(filePath), $"chars={content.Length};edits={edits.Count}");
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

    private string RunGit(string arguments)
    {
        var startInfo = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = RootPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(error.Trim());
        return string.IsNullOrWhiteSpace(output) ? "(clean)" : output.TrimEnd();
    }

    private void WriteAudit(string operation, string target, string details)
    {
        if (target.StartsWith(".mymcp/audit", StringComparison.OrdinalIgnoreCase)) return;
        var auditPath = Path.Combine(RootPath, ".mymcp", "audit", "operations.log");
        Directory.CreateDirectory(Path.GetDirectoryName(auditPath)!);
        var safeDetails = details.Replace(Environment.NewLine, " ").Replace("\r", " ").Replace("\n", " ");
        File.AppendAllText(auditPath, $"{DateTimeOffset.UtcNow:O}|{operation}|{target}|{safeDetails}{Environment.NewLine}", Encoding.UTF8);
    }

    private void EnsureWriteAllowed(string? approvalToken)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MYMCP_REQUIRE_WRITE_APPROVAL"), "true", StringComparison.OrdinalIgnoreCase)) return;
        var expected = Environment.GetEnvironmentVariable("MYMCP_WRITE_APPROVAL_TOKEN");
        if (string.IsNullOrWhiteSpace(expected) || !string.Equals(expected, approvalToken, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Write approval is required. Configure MYMCP_WRITE_APPROVAL_TOKEN and pass its value as approvalToken.");
        }
    }

    private void BackupFile(string filePath)
    {
        if (!File.Exists(filePath)) return;
        var backupRoot = Path.Combine(RootPath, ".mymcp", "backups");
        var relative = GetRelativePath(filePath);
        var backupPath = Path.Combine(backupRoot, relative + ".bak");
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.Copy(filePath, backupPath, true);
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

    private static string BuildFeatureTestPlanTemplate(FeatureTestPlanRequest request, string slug)
    {
        var paths = request.ExpectedTestPaths.Count == 0
            ? "- Add the relative path of every unit-test file created for this feature."
            : string.Join(Environment.NewLine, request.ExpectedTestPaths.Select(path => $"- [ ] `{path}`"));

        return $"""
        # Unit Test Plan - {request.Title}

        Feature slug: `{slug}`

        ## Behavior Under Test

        {request.Summary}

        ## Required Coverage

        - [ ] Happy path for each acceptance criterion
        - [ ] Invalid input and boundary cases
        - [ ] Error handling and side effects
        - [ ] Regression case for the original problem

        ## Expected Test Files

        {paths}

        ## Coverage Matrix

        | Acceptance criterion | Test name or file | Status |
        | --- | --- | --- |
        | Criterion 1 | Add a test reference | TODO |
        | Criterion 2 | Add a test reference | TODO |

        ## Completion Gate

        - [ ] Unit tests are implemented and meaningful.
        - [ ] The project's test command passes.
        - [ ] `ValidateFeatureTests` passes for every test file.
        """;
    }

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

public sealed record FeatureTestPlanRequest(
    string Slug,
    string Title,
    string Summary,
    IReadOnlyList<string> ExpectedTestPaths,
    bool OverwriteExisting = false);

public sealed record DocsPackRequest(
    string Slug,
    string Title,
    string Summary,
    bool OverwriteExisting = false);
