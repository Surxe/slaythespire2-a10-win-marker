using System.Text.Json;
using System.Text.Json.Nodes;

namespace A10WinMarker.Core;

/// <summary>State of the mark file when it was read.</summary>
public enum MarkFileStatus
{
    /// <summary>No file: no marks.</summary>
    Absent,

    /// <summary>File read. A missing or unknown version is read best-effort.</summary>
    Ok,

    /// <summary>File present but unreadable as marks. Shown as no marks; never overwritten in place.</summary>
    Corrupt,
}

/// <summary>What <see cref="MarkStore.Read"/> found.</summary>
/// <param name="Status">Absent, Ok or Corrupt.</param>
/// <param name="Path">The path that was read (resolved at that moment).</param>
/// <param name="Marks">Every stored entry under <c>marks</c> that has a boolean value, by key.</param>
/// <param name="Version">The file's <c>version</c>, if it has a numeric one.</param>
/// <param name="Error">Why the file is corrupt, when it is.</param>
public sealed record MarkSnapshot(
    MarkFileStatus Status,
    string Path,
    IReadOnlyDictionary<string, bool> Marks,
    int? Version,
    string? Error)
{
    public bool IsMarked(Scope scope) => Marks.TryGetValue(scope.Key, out var on) && on;
}

/// <summary>Outcome of an add-only <see cref="MarkStore.Record"/>.</summary>
public enum RecordOutcome
{
    Added,
    AlreadyMarked,
}

/// <summary>
/// The mod's own mark file (spec 7): one small JSON file per profile, written with plain file IO.
/// <para>
/// The path is resolved through the given function on every read and write, so a profile switch
/// is followed without any cache. Writes go to a temporary file in the same folder and then
/// replace the real one. A corrupt file is moved aside (<c>&lt;name&gt;.corrupt</c>) before the
/// first write after it, never overwritten. Unknown keys, at the top level and under
/// <c>marks</c>, are kept on write.
/// </para>
/// </summary>
public sealed class MarkStore
{
    public const int CurrentVersion = 1;
    public const string FileName = "a10_win_marker.json";

    private const string MarksProperty = "marks";
    private const string VersionProperty = "version";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly Func<string> _resolvePath;
    private readonly Action<string> _warn;
    private readonly object _gate = new();

    /// <param name="resolvePath">Returns the absolute path of the active profile's mark file.</param>
    /// <param name="warn">Receives warnings (corrupt file, file moved aside).</param>
    public MarkStore(Func<string> resolvePath, Action<string>? warn = null)
    {
        _resolvePath = resolvePath ?? throw new ArgumentNullException(nameof(resolvePath));
        _warn = warn ?? (_ => { });
    }

    /// <summary>Reads the file. IO errors other than "absent" propagate; bad content reports Corrupt.</summary>
    public MarkSnapshot Read()
    {
        lock (_gate)
        {
            var path = _resolvePath();
            var loaded = Load(path);
            if (loaded.Status == MarkFileStatus.Corrupt)
            {
                _warn($"Mark file is corrupt, showing no marks until it is replaced: {path} ({loaded.Error})");
            }
            return ToSnapshot(path, loaded);
        }
    }

    public bool IsMarked(Scope scope) => Read().IsMarked(scope);

    /// <summary>Flips the scope's mark, saves, and returns the new state.</summary>
    public bool Toggle(Scope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        lock (_gate)
        {
            var path = _resolvePath();
            var loaded = Load(path);
            var next = !(loaded.Status == MarkFileStatus.Ok && IsOn(loaded.Marks, scope.Key));
            Write(path, loaded, scope.Key, next);
            return next;
        }
    }

    /// <summary>Sets or clears the scope's mark and saves. Skips the write when nothing changes.</summary>
    public void Set(Scope scope, bool marked)
    {
        ArgumentNullException.ThrowIfNull(scope);
        lock (_gate)
        {
            var path = _resolvePath();
            var loaded = Load(path);
            if (loaded.Status == MarkFileStatus.Ok
                && loaded.Marks!.TryGetPropertyValue(scope.Key, out var node)
                && node is JsonValue v && v.TryGetValue<bool>(out var current) && current == marked)
            {
                return;
            }
            Write(path, loaded, scope.Key, marked);
        }
    }

    /// <summary>
    /// Add-only recording for the post-run hook: marks the scope if it is not marked yet, and never
    /// clears anything. Writes only when it adds.
    /// </summary>
    public RecordOutcome Record(Scope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        lock (_gate)
        {
            var path = _resolvePath();
            var loaded = Load(path);
            if (loaded.Status == MarkFileStatus.Ok && IsOn(loaded.Marks, scope.Key))
            {
                return RecordOutcome.AlreadyMarked;
            }
            Write(path, loaded, scope.Key, true);
            return RecordOutcome.Added;
        }
    }

    // ---- file handling -------------------------------------------------------------------

    private sealed record Loaded(MarkFileStatus Status, JsonObject? Root, JsonObject? Marks, string? Error);

    private static Loaded Load(string path)
    {
        if (!File.Exists(path))
        {
            return new Loaded(MarkFileStatus.Absent, null, null, null);
        }

        var text = File.ReadAllText(path);
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
        }
        catch (JsonException e)
        {
            return Corrupt($"invalid JSON: {e.Message}");
        }

        if (node is not JsonObject root)
        {
            return Corrupt("top level is not an object");
        }
        if (!root.TryGetPropertyValue(MarksProperty, out var marksNode) || marksNode is null)
        {
            // Best-effort: a file without marks (or with "marks": null) simply has none yet.
            var marks = new JsonObject();
            root[MarksProperty] = marks;
            return new Loaded(MarkFileStatus.Ok, root, marks, null);
        }
        if (marksNode is not JsonObject marksObject)
        {
            return Corrupt("\"marks\" is not an object");
        }
        return new Loaded(MarkFileStatus.Ok, root, marksObject, null);

        static Loaded Corrupt(string why) => new(MarkFileStatus.Corrupt, null, null, why);
    }

    private static MarkSnapshot ToSnapshot(string path, Loaded loaded)
    {
        var marks = new SortedDictionary<string, bool>(StringComparer.Ordinal);
        int? version = null;
        if (loaded.Status == MarkFileStatus.Ok)
        {
            foreach (var (key, value) in loaded.Marks!)
            {
                if (value is JsonValue v && v.TryGetValue<bool>(out var on))
                {
                    marks[key] = on;
                }
            }
            if (loaded.Root![VersionProperty] is JsonValue ver && ver.TryGetValue<int>(out var n))
            {
                version = n;
            }
        }
        return new MarkSnapshot(loaded.Status, path, marks, version, loaded.Error);
    }

    private static bool IsOn(JsonObject? marks, string key) =>
        marks is not null
        && marks.TryGetPropertyValue(key, out var node)
        && node is JsonValue v && v.TryGetValue<bool>(out var on) && on;

    private void Write(string path, Loaded loaded, string key, bool value)
    {
        JsonObject root;
        JsonObject marks;
        if (loaded.Status == MarkFileStatus.Ok)
        {
            root = loaded.Root!;
            marks = loaded.Marks!;
        }
        else
        {
            if (loaded.Status == MarkFileStatus.Corrupt)
            {
                var aside = MoveAside(path);
                _warn($"Moved corrupt mark file aside to {aside}; starting a fresh one");
            }
            root = new JsonObject();
            marks = new JsonObject();
            root[MarksProperty] = marks;
        }

        // Keep an existing version (a newer file stays labelled as such); add one if missing.
        if (root[VersionProperty] is null)
        {
            root[VersionProperty] = CurrentVersion;
        }
        marks[key] = value;

        WriteAtomically(path, Order(root).ToJsonString(WriteOptions));
    }

    /// <summary>Puts <c>version</c> and <c>marks</c> first for readability; other keys follow in their order.</summary>
    private static JsonObject Order(JsonObject root)
    {
        var ordered = new JsonObject();
        foreach (var name in new[] { VersionProperty, MarksProperty })
        {
            if (root.TryGetPropertyValue(name, out var node))
            {
                root.Remove(name);
                ordered[name] = node;
            }
        }
        foreach (var (name, node) in root.ToList())
        {
            root.Remove(name);
            ordered[name] = node;
        }
        return ordered;
    }

    private static void WriteAtomically(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        var temp = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>Moves the corrupt file to <c>&lt;path&gt;.corrupt</c>, or <c>.corrupt.N</c> if that is taken.</summary>
    private static string MoveAside(string path)
    {
        var target = path + ".corrupt";
        for (var n = 2; File.Exists(target); n++)
        {
            target = $"{path}.corrupt.{n}";
        }
        File.Move(path, target);
        return target;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
