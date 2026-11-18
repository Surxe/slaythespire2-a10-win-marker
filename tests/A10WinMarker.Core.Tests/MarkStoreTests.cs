using System.Text.Json.Nodes;

namespace A10WinMarker.Core.Tests;

public sealed class MarkStoreTests : IDisposable
{
    private static readonly Scope Ironclad = Scope.Character("CHARACTER.IRONCLAD");
    private static readonly Scope Silent = Scope.Character("CHARACTER.SILENT");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "a10mark-tests-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _warnings = new();
    private string _profileDir;

    public MarkStoreTests()
    {
        _profileDir = Path.Combine(_root, "profile1");
        Directory.CreateDirectory(_profileDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string MarkPath => Path.Combine(_profileDir, MarkStore.FileName);

    private MarkStore Store() => new(() => MarkPath, _warnings.Add);

    private JsonObject FileJson() => JsonNode.Parse(File.ReadAllText(MarkPath))!.AsObject();

    // ---- read ----------------------------------------------------------------------------

    [Fact]
    public void Absent_file_means_no_marks_and_reading_writes_nothing()
    {
        var snap = Store().Read();
        Assert.Equal(MarkFileStatus.Absent, snap.Status);
        Assert.Empty(snap.Marks);
        Assert.False(snap.IsMarked(Ironclad));
        Assert.False(File.Exists(MarkPath));
    }

    [Fact]
    public void Reads_the_spec_example()
    {
        File.WriteAllText(MarkPath, """
            { "version": 1, "marks": { "character:CHARACTER.IRONCLAD": true, "multiplayer": true, "blitz": false } }
            """);
        var snap = Store().Read();
        Assert.Equal(MarkFileStatus.Ok, snap.Status);
        Assert.Equal(1, snap.Version);
        Assert.True(snap.IsMarked(Ironclad));
        Assert.True(snap.IsMarked(Scope.Multiplayer));
        Assert.False(snap.IsMarked(Scope.Blitz));
        Assert.False(snap.IsMarked(Silent));
        Assert.Equal(3, snap.Marks.Count);
    }

    [Theory]
    [InlineData("""{ "marks": { "blitz": true } }""", null)]
    [InlineData("""{ "version": 7, "marks": { "blitz": true } }""", 7)]
    [InlineData("""{ "version": "x", "marks": { "blitz": true } }""", null)]
    public void Missing_or_unknown_version_is_read_best_effort(string json, int? version)
    {
        File.WriteAllText(MarkPath, json);
        var snap = Store().Read();
        Assert.Equal(MarkFileStatus.Ok, snap.Status);
        Assert.Equal(version, snap.Version);
        Assert.True(snap.IsMarked(Scope.Blitz));
    }

    [Fact]
    public void File_without_marks_is_ok_and_empty()
    {
        File.WriteAllText(MarkPath, """{ "version": 1 }""");
        var snap = Store().Read();
        Assert.Equal(MarkFileStatus.Ok, snap.Status);
        Assert.Empty(snap.Marks);
    }

    [Fact]
    public void Non_boolean_mark_values_read_as_unmarked()
    {
        File.WriteAllText(MarkPath, """{ "version": 1, "marks": { "blitz": "yes", "multiplayer": 1 } }""");
        var snap = Store().Read();
        Assert.False(snap.IsMarked(Scope.Blitz));
        Assert.False(snap.IsMarked(Scope.Multiplayer));
    }

    // ---- toggle / set --------------------------------------------------------------------

    [Fact]
    public void Toggle_marks_then_unmarks_and_saves_each_time()
    {
        var store = Store();
        Assert.True(store.Toggle(Ironclad));
        Assert.True(Store().IsMarked(Ironclad));
        Assert.False(store.Toggle(Ironclad));
        Assert.False(Store().IsMarked(Ironclad));
        Assert.False(FileJson()["marks"]!["character:CHARACTER.IRONCLAD"]!.GetValue<bool>());
    }

    [Fact]
    public void Toggle_only_touches_its_own_scope()
    {
        var store = Store();
        store.Set(Scope.Multiplayer, true);
        store.Toggle(Ironclad);
        store.Toggle(Scope.Blitz);
        store.Toggle(Scope.Blitz);
        var snap = store.Read();
        Assert.True(snap.IsMarked(Scope.Multiplayer));
        Assert.True(snap.IsMarked(Ironclad));
        Assert.False(snap.IsMarked(Scope.Blitz));
    }

    [Fact]
    public void Set_on_and_off()
    {
        var store = Store();
        store.Set(Silent, true);
        Assert.True(store.IsMarked(Silent));
        store.Set(Silent, false);
        Assert.False(store.IsMarked(Silent));
    }

    [Fact]
    public void Set_to_the_current_value_does_not_rewrite()
    {
        var store = Store();
        store.Set(Silent, true);
        var stamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(MarkPath, stamp);
        store.Set(Silent, true);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(MarkPath));
    }

    // ---- add-only record -----------------------------------------------------------------

    [Fact]
    public void Record_adds_a_mark()
    {
        Assert.Equal(RecordOutcome.Added, Store().Record(Scope.Blitz));
        Assert.True(Store().IsMarked(Scope.Blitz));
    }

    [Fact]
    public void Record_on_an_existing_mark_does_not_rewrite()
    {
        var store = Store();
        store.Record(Ironclad);
        var stamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(MarkPath, stamp);
        Assert.Equal(RecordOutcome.AlreadyMarked, store.Record(Ironclad));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(MarkPath));
    }

    [Fact]
    public void Record_never_removes_other_marks()
    {
        var store = Store();
        store.Set(Scope.Multiplayer, true);
        store.Set(Silent, true);
        store.Record(Ironclad);
        var snap = store.Read();
        Assert.True(snap.IsMarked(Scope.Multiplayer));
        Assert.True(snap.IsMarked(Silent));
        Assert.True(snap.IsMarked(Ironclad));
    }

    [Fact]
    public void Record_re_marks_a_cleared_scope()
    {
        var store = Store();
        store.Set(Ironclad, false);
        Assert.Equal(RecordOutcome.Added, store.Record(Ironclad));
        Assert.True(store.IsMarked(Ironclad));
    }

    // ---- JSON format ---------------------------------------------------------------------

    [Fact]
    public void Writes_version_1_format()
    {
        Store().Set(Ironclad, true);
        var json = FileJson();
        Assert.Equal(1, json["version"]!.GetValue<int>());
        Assert.True(json["marks"]!["character:CHARACTER.IRONCLAD"]!.GetValue<bool>());
        Assert.Equal(new[] { "version", "marks" }, json.Select(p => p.Key));
    }

    [Fact]
    public void Round_trips_every_scope_kind()
    {
        var store = Store();
        store.Set(Ironclad, true);
        store.Set(Scope.Character("CHARACTER.HELLOSPIRE-PALADIN"), true);
        store.Set(Scope.Multiplayer, true);
        store.Set(Scope.Blitz, false);

        var snap = Store().Read();
        Assert.Equal(MarkFileStatus.Ok, snap.Status);
        Assert.Equal(
            new Dictionary<string, bool>
            {
                ["blitz"] = false,
                ["character:CHARACTER.HELLOSPIRE-PALADIN"] = true,
                ["character:CHARACTER.IRONCLAD"] = true,
                ["multiplayer"] = true,
            },
            snap.Marks);
    }

    [Fact]
    public void Unknown_keys_are_kept_on_write()
    {
        File.WriteAllText(MarkPath, """
            {
              "version": 1,
              "note": { "by": "future" },
              "marks": { "future:thing": 3, "character:CHARACTER.SILENT": true },
              "extra": [1, 2]
            }
            """);
        Store().Toggle(Ironclad);

        var json = FileJson();
        Assert.Equal("future", json["note"]!["by"]!.GetValue<string>());
        Assert.Equal(2, json["extra"]!.AsArray().Count);
        Assert.Equal(3, json["marks"]!["future:thing"]!.GetValue<int>());
        Assert.True(json["marks"]!["character:CHARACTER.SILENT"]!.GetValue<bool>());
        Assert.True(json["marks"]!["character:CHARACTER.IRONCLAD"]!.GetValue<bool>());
    }

    [Fact]
    public void Existing_unknown_version_is_kept_and_missing_version_is_added()
    {
        File.WriteAllText(MarkPath, """{ "version": 2, "marks": {} }""");
        Store().Toggle(Scope.Blitz);
        Assert.Equal(2, FileJson()["version"]!.GetValue<int>());

        File.WriteAllText(MarkPath, """{ "marks": {} }""");
        Store().Toggle(Scope.Blitz);
        Assert.Equal(1, FileJson()["version"]!.GetValue<int>());
    }

    // ---- corrupt files -------------------------------------------------------------------

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("[1, 2]")]
    [InlineData("""{ "version": 1, "marks": [ "blitz" ] }""")]
    public void Corrupt_file_reads_as_no_marks_and_is_not_overwritten_by_reading(string content)
    {
        File.WriteAllText(MarkPath, content);
        var snap = Store().Read();
        Assert.Equal(MarkFileStatus.Corrupt, snap.Status);
        Assert.Empty(snap.Marks);
        Assert.NotNull(snap.Error);
        Assert.Equal(content, File.ReadAllText(MarkPath));
        Assert.Contains(_warnings, w => w.Contains("corrupt"));
    }

    [Fact]
    public void First_write_after_corruption_moves_the_file_aside()
    {
        const string bad = "{ \"marks\": { \"blitz\": tru";
        File.WriteAllText(MarkPath, bad);

        Assert.True(Store().Toggle(Ironclad));

        Assert.Equal(bad, File.ReadAllText(MarkPath + ".corrupt"));
        var snap = Store().Read();
        Assert.Equal(MarkFileStatus.Ok, snap.Status);
        Assert.Equal(new[] { "character:CHARACTER.IRONCLAD" }, snap.Marks.Keys);
        Assert.Contains(_warnings, w => w.Contains("Moved corrupt"));
    }

    [Fact]
    public void Earlier_corrupt_copies_are_not_overwritten()
    {
        File.WriteAllText(MarkPath + ".corrupt", "first");
        File.WriteAllText(MarkPath, "second");
        Store().Record(Scope.Blitz);
        Assert.Equal("first", File.ReadAllText(MarkPath + ".corrupt"));
        Assert.Equal("second", File.ReadAllText(MarkPath + ".corrupt.2"));
    }

    [Fact]
    public void Record_on_a_corrupt_file_also_moves_it_aside()
    {
        File.WriteAllText(MarkPath, "garbage");
        Assert.Equal(RecordOutcome.Added, Store().Record(Scope.Multiplayer));
        Assert.True(File.Exists(MarkPath + ".corrupt"));
        Assert.True(Store().IsMarked(Scope.Multiplayer));
    }

    // ---- writing mechanics ---------------------------------------------------------------

    [Fact]
    public void Leaves_no_temporary_file()
    {
        Store().Toggle(Ironclad);
        Assert.Equal(new[] { MarkStore.FileName }, Directory.GetFiles(_profileDir).Select(Path.GetFileName));
    }

    [Fact]
    public void Creates_the_profile_folder_if_needed()
    {
        _profileDir = Path.Combine(_root, "modded", "profile2");
        Store().Toggle(Scope.Blitz);
        Assert.True(File.Exists(MarkPath));
    }

    [Fact]
    public void Path_is_resolved_on_every_call()
    {
        var store = Store();
        store.Set(Ironclad, true);

        _profileDir = Path.Combine(_root, "profile2");
        Directory.CreateDirectory(_profileDir);
        Assert.False(store.IsMarked(Ironclad));
        store.Set(Silent, true);

        _profileDir = Path.Combine(_root, "profile1");
        var snap = store.Read();
        Assert.True(snap.IsMarked(Ironclad));
        Assert.False(snap.IsMarked(Silent));
        Assert.EndsWith(Path.Combine("profile1", MarkStore.FileName), snap.Path);
    }
}
