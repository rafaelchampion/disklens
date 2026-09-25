using DiskTree.Core.Tree;

namespace DiskTree.Core.Classify;

public static class Classifier
{
    public static Category? CategoryOfName(string name)
    {
        string lower = name.ToLowerInvariant();
        return lower switch
        {
            "src" or "code" or "projects" or "repos" or "dev" or "work"
            or "workspace" or "workspaces" or "github.com" or "gitlab.com"
            or "sites" or "development" or "solutions" => Category.Code,

            ".codex" or ".claude" or ".herdr" or ".pi" or ".cursor" or ".aider"
            or ".gemini" or ".continue" or ".windsurf" or ".microsandbox" or ".omp"
            or ".agents" or ".openai" or "tries" or "worktrees" or "experiments"
            or "scratch" or "playground" => Category.AgentScratch,

            ".cargo" or ".rustup" or ".local" or ".npm" or ".pnpm-store" or "pnpm"
            or ".bun" or ".deno" or "go" or ".gradle" or ".m2" or ".platformio"
            or "mise" or ".mise" or ".pyenv" or ".nvm" or ".gem" or "gem" or ".rbenv"
            or ".espressif" or ".arduino15" or ".config" or ".vscode" or ".zig"
            or ".rye" or ".conda" or "anaconda3" or "miniconda3" or ".opam"
            or ".ghcup" or ".stack" or ".julia" or ".dotnet" or ".android"
            or ".sdkman" or ".volta" or ".yarn" or ".java" or "nuget" => Category.Toolchain,

            "sync" or "dropbox" or "nextcloud" or "google drive" or "onedrive"
            or "pclouddrive" or "mega" or ".stversions" or "icloud" => Category.Synced,

            ".git" => Category.Git,

            "pictures" or "photos" or "music" or "videos" or "movies" or "steam"
            or "steamapps" or "models" or ".ollama" or ".lmstudio" or "games" => Category.Media,

            "documents" or "desktop" or "downloads" or "books" or "notes"
            or "obsidian" or "public" or "templates" or "saved games" => Category.Documents,

            ".cache" or "cache" or "caches" or ".ccache" or ".sccache" or "_cacache"
            or "__pycache__" or "node_modules" or "trash" or ".trash" or "$recycle.bin"
            or "tmp" or ".tmp" or "temp" or "crashdumps" or ".vs" => Category.Cache,

            _ => null
        };
    }

    public static Reclaim? ReclaimOf(string name, Category parent, Func<string, bool> hasSibling)
    {
        string lower = name.ToLowerInvariant();
        return lower switch
        {
            ".cache" or "cache" or "caches" or ".ccache" or ".sccache" or "_cacache" => Reclaim.Regenerable,
            ".stversions" => Reclaim.SyncHistory,
            ".pnpm-store" or "pnpm" => Reclaim.PackageStore,
            "__pycache__" or ".pytest_cache" or ".mypy_cache" or ".ruff_cache"
            or ".next" or ".turbo" or ".parcel-cache" or ".vs" => Reclaim.BuildOutput,

            "target" when hasSibling("Cargo.toml") => Reclaim.BuildOutput,
            "bin" or "obj" when hasSibling(".sln") || hasSibling(".csproj") => Reclaim.BuildOutput,
            "node_modules" when hasSibling("package.json") => Reclaim.Reinstallable,
            "packages" when hasSibling(".sln") || hasSibling("packages.config") => Reclaim.Reinstallable,

            "layers" when parent == Category.AgentScratch => Reclaim.SandboxLayers,
            "snapshots" when parent == Category.AgentScratch => Reclaim.Snapshots,

            "trash" or ".trash" or "$recycle.bin" => Reclaim.Trash,
            "tmp" or ".tmp" or "temp" => Reclaim.Temporary,

            _ => null
        };
    }

    public static void Classify(Node root)
    {
        root.Category = Category.Other;
        root.Reclaim = null;

        var siblingNames = new HashSet<string>(root.Children.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var child in root.Children)
        {
            bool HasSibling(string wanted) => siblingNames.Contains(wanted) ||
                siblingNames.Any(s => s.EndsWith(wanted, StringComparison.OrdinalIgnoreCase));

            var category = CategoryOfName(child.Name)
                ?? (IsGitStore(child) ? (Category?)Category.Git : null)
                ?? DominantChildCategory(child)
                ?? Category.Other;

            var reclaim = child.IsDirectory
                ? ReclaimOf(child.Name, Category.Other, HasSibling)
                : null;

            ClassifyBelow(child, category, reclaim);
        }
    }

    private static void ClassifyBelow(Node node, Category category, Reclaim? reclaim)
    {
        node.Category = category;
        node.Reclaim = reclaim;

        if (node.Children.Count == 0)
            return;

        var siblingNames = new HashSet<string>(node.Children.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var child in node.Children)
        {
            bool HasSibling(string wanted) => siblingNames.Contains(wanted) ||
                siblingNames.Any(s => s.EndsWith(wanted, StringComparison.OrdinalIgnoreCase));

            var childCategory = child.IsDirectory
                ? (CategoryOfName(child.Name)
                    ?? (IsGitStore(child) ? (Category?)Category.Git : null)
                    ?? category)
                : CategoryOfExtension(Path.GetExtension(child.Name), category);

            var childReclaim = reclaim ?? (child.IsDirectory
                ? ReclaimOf(child.Name, category, HasSibling)
                : null);

            ClassifyBelow(child, childCategory, childReclaim);
        }
    }

    public static Category CategoryOfExtension(string extension, Category fallback = Category.Other)
    {
        if (string.IsNullOrEmpty(extension)) return fallback;
        string ext = extension.TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            // Media (Images, Video, Audio, 3D)
            "png" or "jpg" or "jpeg" or "gif" or "bmp" or "webp" or "svg"
            or "ico" or "tiff" or "tif" or "psd" or "ai" or "raw" or "xcf"
            or "mp4" or "mkv" or "avi" or "mov" or "wmv" or "flv" or "webm" or "m4v"
            or "mp3" or "wav" or "flac" or "aac" or "ogg" or "wma" or "m4a" or "mid"
            or "blend" or "fbx" or "obj" or "stl" or "3ds" => Category.Media,

            // Documents
            "pdf" or "doc" or "docx" or "xls" or "xlsx" or "ppt" or "pptx"
            or "txt" or "md" or "rtf" or "epub" or "csv" or "odt" or "ods" or "odp" => Category.Documents,

            // Code
            "cs" or "rs" or "py" or "js" or "ts" or "jsx" or "tsx" or "cpp" or "c"
            or "h" or "hpp" or "java" or "go" or "html" or "css" or "scss" or "sass"
            or "json" or "xml" or "yaml" or "yml" or "toml" or "sql" or "sh" or "bat"
            or "ps1" or "lua" or "rb" or "php" or "swift" or "kt" or "dart" or "zig" => Category.Code,

            // Toolchain / Binaries / Archives
            "exe" or "dll" or "sys" or "bin" or "msi" or "iso" or "img" or "vmdk" or "vhd" or "vhdx"
            or "zip" or "rar" or "7z" or "tar" or "gz" or "bz2" or "xz" or "cab"
            or "deb" or "rpm" or "apk" or "nupkg" or "whl" => Category.Toolchain,

            // Cache / Temp
            "cache" or "tmp" or "temp" or "log" or "bak" or "dmp" or "old" or "crdownload" => Category.Cache,

            _ => fallback
        };
    }


    private static bool IsGitStore(Node node)
    {
        if (!node.IsDirectory) return false;
        return node.ChildNamed("HEAD") != null && node.ChildNamed("objects") != null;
    }

    private static Category? DominantChildCategory(Node node)
    {
        Node current = node;
        for (int depth = 0; depth < 3; depth++)
        {
            foreach (var child in current.Children)
            {
                if (!child.IsDirectory) continue;

                var cat = CategoryOfName(child.Name);
                if (cat.HasValue) return cat;

                if (IsGitStore(child)) return Category.Git;
            }

            if (current.Children.Count > 0 && current.Children[0].IsDirectory)
                current = current.Children[0];
            else
                break;
        }
        return null;
    }
}
