using System.Text.Json;
using System.Text.Json.Serialization;

namespace G613Keys.Core.Profiles;

/// <summary>
/// Loads and saves profiles as JSON under %LOCALAPPDATA%\G613Keys\profiles.json.
/// A missing or invalid file yields a default-only config and never throws, so
/// first run and a hand-edited bad file both start cleanly. Hot-reloads on
/// external change.
/// </summary>
public sealed class ProfileStore : IDisposable
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private FileSystemWatcher? _watcher;

    /// <summary>Raised when the file changes on disk (debounced by the caller as needed).</summary>
    public event Action? Changed;

    public ProfileStore(string? path = null)
    {
        _path = path ?? DefaultPath;
    }

    public string Path => _path;

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "G613Keys", "profiles.json");

    /// <summary>Load profiles; on any problem return a single empty default profile.</summary>
    public IReadOnlyList<Profile> Load()
    {
        try
        {
            if (!File.Exists(_path)) return [Profile.EmptyDefault()];
            var json = File.ReadAllText(_path);
            var profiles = JsonSerializer.Deserialize<List<Profile>>(json, JsonOptions);
            if (profiles is null || profiles.Count == 0) return [Profile.EmptyDefault()];
            if (!profiles.Any(p => p.IsDefault)) profiles.Add(Profile.EmptyDefault());
            return profiles;
        }
        catch
        {
            return [Profile.EmptyDefault()];
        }
    }

    public void Save(IReadOnlyList<Profile> profiles)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(profiles, JsonOptions);
        File.WriteAllText(_path, json);
    }

    /// <summary>Pick the profile for a foreground executable: exact match, else default.</summary>
    public static Profile Resolve(IReadOnlyList<Profile> profiles, string exeName)
    {
        foreach (var p in profiles)
            if (!p.IsDefault && string.Equals(p.Match, exeName, StringComparison.OrdinalIgnoreCase))
                return p;
        return profiles.FirstOrDefault(p => p.IsDefault) ?? Profile.EmptyDefault();
    }

    /// <summary>Begin watching the file for external edits.</summary>
    public void StartWatching()
    {
        if (_watcher is not null) return;
        var dir = System.IO.Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(dir);
        _watcher = new FileSystemWatcher(dir, System.IO.Path.GetFileName(_path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += (_, _) => Changed?.Invoke();
        _watcher.Created += (_, _) => Changed?.Invoke();
    }

    public void Dispose() => _watcher?.Dispose();
}
