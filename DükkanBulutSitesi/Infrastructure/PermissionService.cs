using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Entity;

public interface IPermissionService
{
    Task<bool> HasAsync(ClaimsPrincipal user, string permissionKey);
}

public sealed class PermissionService : IPermissionService
{
    private readonly AppDbContext _db;
    public PermissionService(AppDbContext db) => _db = db;

    public async Task<bool> HasAsync(ClaimsPrincipal user, string permissionKey)
    {
        var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
            return false;

        var roleId = await _db.Users.AsNoTracking()
            .Where(u => u.UserID == userId && u.IsActive)
            .Select(u => u.RoleID)
            .FirstOrDefaultAsync();

        if (!roleId.HasValue) return false;

        return await _db.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleID == roleId.Value)
            .AnyAsync(rp => rp.Permission.PermissionKey == permissionKey);
    }
}
