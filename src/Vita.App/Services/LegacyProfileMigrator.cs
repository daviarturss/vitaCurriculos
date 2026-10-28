using Microsoft.EntityFrameworkCore;

namespace Vita.App.Services;

// Copies accounts from the old single-database layout (App_Data/vita.db) into
// one folder per profile. The old file is kept as a backup; a marker prevents
// importing it twice.
public static class LegacyProfileMigrator
{
    public static async Task RunAsync(ProfileManager profiles, string dataDir, ILogger? logger = null)
    {
        var legacyPath = Path.Combine(dataDir, "vita.db");
        var markerPath = Path.Combine(profiles.ProfilesDir, ".legacy-migrated");
        if (!File.Exists(legacyPath)) return;

        // Migration already happened in an earlier run: just retire the old file
        // so the app never opens it again.
        if (File.Exists(markerPath))
        {
            try
            {
                var options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite($"Data Source={legacyPath}")
                    .Options;
                await using var legacy = new AppDbContext(options);
                await ProfileInitializer.RemoveLegacyDemoDataAsync(legacy);
            }
            catch
            {
                // A damaged old database must not stop the app from starting.
            }

            MoveLegacyFiles(legacyPath, dataDir);
            return;
        }

        try
        {
            var legacyOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={legacyPath}")
                .Options;

            await using (var legacy = new AppDbContext(legacyOptions))
            {
                await legacy.Database.EnsureCreatedAsync();

                var users = await legacy.Users.AsNoTracking()
                    .Where(u => !u.IsDemo && u.FullName != "")
                    .ToListAsync();

                foreach (var oldUser in users)
                {
                    var profile = profiles.Ensure(oldUser.FullName);
                    var targetOptions = new DbContextOptionsBuilder<AppDbContext>()
                        .UseSqlite($"Data Source={profile.DatabasePath}")
                        .Options;

                    await using var target = new AppDbContext(targetOptions);
                    await target.Database.EnsureCreatedAsync();
                    if (await target.Users.AnyAsync()) continue;

                    target.Users.Add(new AppUser
                    {
                        Id = oldUser.Id,
                        FullName = profile.Name,
                        Email = profile.Email,
                        NormalizedEmail = profile.Email,
                        PasswordHash = string.Empty,
                        PreferredLanguage = oldUser.PreferredLanguage,
                        CreatedAt = oldUser.CreatedAt,
                        IsDemo = false
                    });

                    target.Resumes.AddRange(await legacy.Resumes.AsNoTracking()
                        .Where(r => r.UserId == oldUser.Id).ToListAsync());
                    target.Applications.AddRange(await legacy.Applications.AsNoTracking()
                        .Where(a => a.UserId == oldUser.Id).ToListAsync());
                    target.CoverLetters.AddRange(await legacy.CoverLetters.AsNoTracking()
                        .Where(c => c.UserId == oldUser.Id).ToListAsync());
                    target.SavedJobs.AddRange(await legacy.SavedJobs.AsNoTracking()
                        .Where(j => j.UserId == oldUser.Id).ToListAsync());

                    var threads = await legacy.ChatThreads.AsNoTracking()
                        .Where(t => t.UserId == oldUser.Id).ToListAsync();
                    target.ChatThreads.AddRange(threads);

                    var threadIds = threads.Select(t => t.Id).ToList();
                    if (threadIds.Count > 0)
                    {
                        target.ChatMessages.AddRange(await legacy.ChatMessages.AsNoTracking()
                            .Where(m => threadIds.Contains(m.ThreadId)).ToListAsync());
                    }

                    var settings = await legacy.AiSettings.AsNoTracking()
                        .FirstOrDefaultAsync(s => s.UserId == oldUser.Id);
                    if (settings is not null) target.AiSettings.Add(settings);

                    await target.SaveChangesAsync();
                }

                // Never keep the old demo account around, not even in the backup.
                await ProfileInitializer.RemoveLegacyDemoDataAsync(legacy);
                logger?.LogInformation("Perfis migrados do banco antigo: {Count}", users.Count);
            }

            MoveLegacyFiles(legacyPath, dataDir);
            await File.WriteAllTextAsync(markerPath, DateTimeOffset.Now.ToString("O"));
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Falha ao migrar o banco antigo para perfis.");
            try
            {
                await File.AppendAllTextAsync(Path.Combine(dataDir, "migracao-perfis.log"),
                    $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch
            {
                // The app can still create new profiles from scratch.
            }
        }
    }

    // Keeps a dated copy of the old database outside the active data path, then
    // removes the sqlite side files so they cannot be mistaken for live data.
    private static void MoveLegacyFiles(string legacyPath, string dataDir)
    {
        try
        {
            var backupDir = Path.Combine(dataDir, "backup", $"banco-antigo-{DateTime.Now:yyyyMMdd-HHmmss}");
            Directory.CreateDirectory(backupDir);

            foreach (var path in new[] { legacyPath, legacyPath + "-shm", legacyPath + "-wal" })
            {
                if (!File.Exists(path)) continue;
                var target = Path.Combine(backupDir, Path.GetFileName(path));
                try
                {
                    File.Move(path, target, overwrite: true);
                }
                catch (IOException)
                {
                    File.Copy(path, target, overwrite: true);
                    File.Delete(path);
                }
            }
        }
        catch
        {
            // A locked backup file is not fatal: profile data is already copied.
        }
    }
}
