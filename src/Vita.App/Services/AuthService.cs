using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace Vita.App.Services;

public sealed class AuthService(
    IDbContextFactory<AppDbContext> dbFactory,
    ProfileManager profiles)
{
    public Task<bool> HasAnyUserAsync(CancellationToken ct = default) =>
        Task.FromResult(profiles.List().Count > 0);

    public Task<IReadOnlyList<ProfileInfo>> ListProfilesAsync(CancellationToken ct = default) =>
        Task.FromResult(profiles.List());

    public async Task<(AppUser? User, ProfileInfo? Profile, string? Error)> LoginAsync(string name, CancellationToken ct = default)
    {
        name = TextAnalysis.Clean(name);
        if (name.Length < 2) return (null, null, "Digite pelo menos 2 caracteres no nome do perfil.");
        if (name.Length > 60) return (null, null, "Use um nome com até 60 caracteres.");
        if (name.Contains('/') || name.Contains('\\'))
            return (null, null, "O nome não pode conter barras.");

        try
        {
            var profile = profiles.Ensure(name);
            profiles.SetCurrent(profile);

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var user = await ProfileInitializer.EnsureAsync(db, profile, ct);
            return (user, profile, null);
        }
        catch (Exception ex)
        {
            profiles.SetCurrent(null);
            return (null, null, $"Não foi possível abrir o perfil: {ex.Message}");
        }
    }

    public static ClaimsPrincipal BuildPrincipal(AppUser user, ProfileInfo profile)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim("vita_profile", profile.Slug)
        }, "VitaCookie");
        return new ClaimsPrincipal(identity);
    }
}
