using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Vita.App.Services;

/// <summary>Resolves (and caches) the signed-in user for the current Blazor circuit.</summary>
public sealed class UserSession(AuthenticationStateProvider authenticationStateProvider, IDbContextFactory<AppDbContext> dbFactory)
{
    private AppUser? _user;
    private Guid? _userId;

    public async Task<Guid?> GetUserIdAsync()
    {
        if (_userId is not null) return _userId;
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var value = state.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(value, out var id)) _userId = id;
        return _userId;
    }

    public async Task<AppUser?> GetUserAsync()
    {
        if (_user is not null) return _user;
        var id = await GetUserIdAsync();
        if (id is null) return null;
        await using var db = await dbFactory.CreateDbContextAsync();
        _user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        return _user;
    }

    public async Task<Guid> RequireUserIdAsync() =>
        await GetUserIdAsync() ?? throw new UnauthorizedAccessException("Usuário não autenticado.");
}
