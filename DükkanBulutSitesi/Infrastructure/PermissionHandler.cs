using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Entity;

public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly AppDbContext _db;
    public PermissionHandler(AppDbContext db) => _db = db;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var userIdStr = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
            return;

        // Get user's role
        var roleId = await _db.Users
            .AsNoTracking()
            .Where(u => u.UserID == userId && u.IsActive)
            .Select(u => u.RoleID)
            .FirstOrDefaultAsync();

        if (!roleId.HasValue) return;

        // Check RolePermissions -> PermissionKey
        var has = await _db.RolePermissions
            .AsNoTracking()
            .Where(rp => rp.RoleID == roleId.Value)
            .AnyAsync(rp => rp.Permission.PermissionKey == requirement.PermissionKey);

        if (has)
            context.Succeed(requirement);
    }
}
