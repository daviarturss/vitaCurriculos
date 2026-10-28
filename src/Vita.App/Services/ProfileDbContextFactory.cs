using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Vita.App.Services;

// The database factory follows the profile that owns the current session. In a
// Blazor circuit the auth cookie is the source of truth, so two browser tabs
// signed in with different names never open each other's database.
public sealed class ProfileDbContextFactory(
    ProfileManager profiles,
    AuthenticationStateProvider authState) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext()
    {
        var profile = profiles.Current
            ?? throw new InvalidOperationException("Nenhum perfil ativo. Entre com um nome para abrir seus dados.");

        return Create(profile);
    }

    public async Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        var profile = await ResolveAsync() ?? profiles.Current
            ?? throw new InvalidOperationException("Nenhum perfil ativo. Entre com um nome para abrir seus dados.");

        return Create(profile);
    }

    private async Task<ProfileInfo?> ResolveAsync()
    {
        try
        {
            var state = await authState.GetAuthenticationStateAsync();
            var slug = state.User.FindFirst("vita_profile")?.Value;
            return profiles.Resolve(slug);
        }
        catch
        {
            return null;
        }
    }

    private static AppDbContext Create(ProfileInfo profile)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={profile.DatabasePath}")
            .Options;

        return new AppDbContext(options);
    }
}
