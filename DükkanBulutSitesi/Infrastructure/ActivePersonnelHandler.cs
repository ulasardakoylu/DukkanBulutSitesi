using DükkanBulutSitesi.Infrastructure;
using Entity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

public sealed class ActivePersonnelHandler
    : AuthorizationHandler<ActivePersonnelRequirement>
{
    private readonly AppDbContext _db;
    private readonly CurrentStoreAccessor _store;

    public ActivePersonnelHandler(
        AppDbContext db,
        CurrentStoreAccessor store)
    {
        _db = db;
        _store = store;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActivePersonnelRequirement requirement)
    {
        var userIdStr = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId))
            return;

        var storeId = _store.CurrentStoreId;

        var ok = await _db.Personels
            .AsNoTracking()
            .AnyAsync(p =>
                p.UserID == userId &&
                p.IsActive &&
                (storeId == 0 || p.StoreID == storeId));

        if (ok)
            context.Succeed(requirement);
    }
}
