using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Vita.App.Services;

public sealed record ProfileInfo(string Name, string Slug, string Directory, string DatabasePath)
{
    public string Email => $"{Slug}@perfil.local";
}

// A profile is a person using Vita on this Windows account. Every profile has
// its own folder and its own SQLite file, so switching names never mixes data.
public sealed class ProfileManager
{
    private readonly object _gate = new();
    private ProfileInfo? _current;

    public ProfileManager(string dataDir)
    {
        DataDir = dataDir;
        ProfilesDir = Path.Combine(dataDir, "perfis");
        Directory.CreateDirectory(ProfilesDir);
    }

    public string DataDir { get; }
    public string ProfilesDir { get; }
    public ProfileInfo? Current => _current;

    public void SetCurrent(ProfileInfo? profile) => _current = profile;

    public IReadOnlyList<ProfileInfo> List()
    {
        lock (_gate) return ListUnlocked();
    }

    public ProfileInfo? FindByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var clean = TextAnalysis.Clean(name);
        return List().FirstOrDefault(p => string.Equals(p.Name, clean, StringComparison.CurrentCultureIgnoreCase));
    }

    public ProfileInfo? Resolve(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;
        var dir = Path.Combine(ProfilesDir, slug);
        return Directory.Exists(dir) ? Read(dir) : null;
    }

    public ProfileInfo Ensure(string name)
    {
        name = TextAnalysis.Clean(name);
        lock (_gate)
        {
            var existing = ListUnlocked()
                .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
            if (existing is not null) return existing;

            var baseSlug = Slugify(name);
            var slug = baseSlug;
            var suffix = 2;
            while (Directory.Exists(Path.Combine(ProfilesDir, slug)))
                slug = $"{baseSlug}-{suffix++}";

            var dir = Path.Combine(ProfilesDir, slug);
            Directory.CreateDirectory(dir);
            var profile = new ProfileInfo(name, slug, dir, Path.Combine(dir, "vita.db"));

            var metadata = JsonSerializer.Serialize(new
            {
                name = profile.Name,
                slug = profile.Slug,
                createdAt = DateTimeOffset.Now
            }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(dir, "perfil.json"), metadata);
            return profile;
        }
    }

    public static string Slugify(string value)
    {
        var normalized = (value ?? string.Empty).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();

        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }

        var slug = sb.ToString().Trim('-');
        if (slug.Length > 48) slug = slug[..48].Trim('-');
        if (string.IsNullOrWhiteSpace(slug)) slug = "perfil";

        var reserved = new[] { "con", "prn", "aux", "nul", "com1", "com2", "com3", "com4", "lpt1", "lpt2", "lpt3" };
        if (reserved.Contains(slug, StringComparer.OrdinalIgnoreCase)) slug = "perfil-" + slug;

        return slug;
    }

    private IReadOnlyList<ProfileInfo> ListUnlocked()
    {
        if (!Directory.Exists(ProfilesDir)) return Array.Empty<ProfileInfo>();

        return Directory.EnumerateDirectories(ProfilesDir)
            .Select(Read)
            .Where(p => p is not null)
            .Cast<ProfileInfo>()
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static ProfileInfo? Read(string dir)
    {
        var slug = Path.GetFileName(dir);
        var name = slug;

        var metadataPath = Path.Combine(dir, "perfil.json");
        if (File.Exists(metadataPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(metadataPath));
                if (doc.RootElement.TryGetProperty("name", out var nameElement) &&
                    nameElement.GetString() is { Length: > 0 } stored)
                    name = stored;
            }
            catch
            {
                // A damaged metadata file should not hide the profile; the folder still works.
            }
        }

        return new ProfileInfo(name, slug, dir, Path.Combine(dir, "vita.db"));
    }
}
