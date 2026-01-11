using Entity;
using DükkanBulutSitesi.Models;
using DükkanBulutSitesi.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DükkanBulutSitesi.Controllers
{
    public class AuthController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IPasswordHasher<User> _hasher;
        private readonly IAuditLogger _audit;

        public AuthController(AppDbContext db, IPasswordHasher<User> hasher, IAuditLogger audit)
        {
            _db = db;
            _hasher = hasher;
            _audit = audit;
        }

        private string NormalizeReturnUrl(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "/";

            var r = raw.Trim();

            // Look for any "...ReturnUrl=something" pattern and extract "something"
            const string key = "ReturnUrl=";
            var idx = r.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var valuePart = r.Substring(idx + key.Length);

                // cut off trailing parameters if any
                var ampIdx = valuePart.IndexOf('&');
                if (ampIdx >= 0)
                    valuePart = valuePart.Substring(0, ampIdx);

                // strip any leading ? or &
                while (valuePart.StartsWith("?") || valuePart.StartsWith("&"))
                    valuePart = valuePart.Substring(1);

                // decode %2F etc
                valuePart = Uri.UnescapeDataString(valuePart);

                // ensure leading slash
                if (!string.IsNullOrWhiteSpace(valuePart) && !valuePart.StartsWith("/"))
                    valuePart = "/" + valuePart;

                r = valuePart;
            }

            if (string.IsNullOrWhiteSpace(r))
                r = "/";

            return r;
        }

        private async Task SignIn(User user)
        {
            string? roleName = null;

            if (user.RoleID.HasValue)
            {
                roleName = await _db.Roles
                    .AsNoTracking()
                    .Where(r => r.RoleID == user.RoleID.Value)
                    .Select(r => r.RoleName)
                    .FirstOrDefaultAsync();
            }

            var claims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, user.UserID.ToString()),
        new Claim(ClaimTypes.Name, user.UserName ?? user.Email),
        new Claim(ClaimTypes.Email, user.Email)
    };

            // If user has a roleName from Roles table, add it
            if (!string.IsNullOrEmpty(roleName))
                claims.Add(new Claim(ClaimTypes.Role, roleName));

            // Owner role
            var isOwner = await _db.StoreOwners.AnyAsync(o => o.UserID == user.UserID);
            if (isOwner)
                claims.Add(new Claim(ClaimTypes.Role, "Owner"));

            // ✅ Personnel detection (DB-based)
            var isActivePersonnel = await _db.Personels
                .AsNoTracking()
                .AnyAsync(p => p.UserID == user.UserID && p.IsActive);

            if (isActivePersonnel)
            {
                claims.Add(new Claim("IsPersonnel", "true"));

                if (!claims.Any(c => c.Type == ClaimTypes.Role && c.Value == "Manager"))
                    claims.Add(new Claim(ClaimTypes.Role, "Personnel"));
            }

            var identity = new ClaimsIdentity(
                claims, CookieAuthenticationDefaults.AuthenticationScheme);

            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme, principal);
        }


        private void SetStoreCookie(int storeId)
        {
            const string cookieName = "CurrentStoreId";

            Response.Cookies.Append(
                cookieName,
                storeId.ToString(),
                new CookieOptions
                {
                    HttpOnly = true,
                    IsEssential = true,
                    Expires = DateTimeOffset.UtcNow.AddDays(30)
                });
        }

        private async Task<IActionResult> RedirectAfterLogin(User user, string returnUrl)
        {
            var ownerStores = await _db.StoreOwners
                .AsNoTracking()
                .Include(o => o.Store)
                .Where(o => o.UserID == user.UserID)
                .ToListAsync();

            if (ownerStores.Any())
            {
                var firstStore = ownerStores.First();

                SetStoreCookie(firstStore.StoreID);

                return RedirectToAction("Dashboard", "Owner");
            }

            var personel = await _db.Personels
                .AsNoTracking()
                .Include(p => p.Role)
                .FirstOrDefaultAsync(p => p.UserID == user.UserID);

            if (personel != null)
            {
                SetStoreCookie(personel.StoreID);

                var roleName = personel.Role?.RoleName ?? string.Empty;

                if (string.Equals(roleName, "Manager", StringComparison.OrdinalIgnoreCase))
                {
                    return RedirectToAction("Dashboard", "Manager");
                }
                else
                {
                    return RedirectToAction("Dashboard", "Personnel");
                }
            }

            if (string.IsNullOrWhiteSpace(returnUrl) || returnUrl == "/")
            {
                return RedirectToAction("Welcome", "Home");
            }

            return LocalRedirect(returnUrl);
        }

        // ---------- actions ----------

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterVm vm)
        {
            var returnUrl = NormalizeReturnUrl(vm.ReturnUrl);

            if (!ModelState.IsValid)
            {
                TempData["OpenModal"] = "register";
                TempData["AuthError"] = string.Join("<br/>", ModelState.Values
                    .SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return LocalRedirect(returnUrl);
            }

            var exists = await _db.Users.AnyAsync(u => u.Email == vm.Email);
            if (exists)
            {
                TempData["OpenModal"] = "register";
                TempData["AuthError"] = "Bu e-posta ile bir hesap zaten var.";
                return LocalRedirect(returnUrl);
            }

            var user = new User
            {
                Email = vm.Email,
                UserName = vm.UserName,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            user.PasswordHash = _hasher.HashPassword(user, vm.Password);

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            await _audit.LogAsync(
                $"User registered. UserID={user.UserID}, Email={user.Email}",
                HttpContext,
                explicitUserId: user.UserID,
                explicitUserName: user.UserName);

            await SignIn(user);

            await _audit.LogAsync(
                $"User logged in immediately after registration. UserID={user.UserID}, Email={user.Email}",
                HttpContext,
                explicitUserId: user.UserID,
                explicitUserName: user.UserName);

            return await RedirectAfterLogin(user, returnUrl);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginVm vm)
        {
            var returnUrl = NormalizeReturnUrl(vm.ReturnUrl);

            if (!ModelState.IsValid)
            {
                TempData["OpenModal"] = "login";
                TempData["AuthError"] = "E-posta ve şifre gerekli.";
                return LocalRedirect(returnUrl);
            }

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == vm.Email && u.IsActive);
            if (user == null)
            {
                TempData["OpenModal"] = "login";
                TempData["AuthError"] = "Kullanıcı bulunamadı.";

                await _audit.LogAsync(
                    $"Failed login attempt. Email={vm.Email}",
                    HttpContext);

                return LocalRedirect(returnUrl);
            }

            var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, vm.Password);
            if (result == PasswordVerificationResult.Failed)
            {
                TempData["OpenModal"] = "login";
                TempData["AuthError"] = "Şifre yanlış.";

                await _audit.LogAsync(
                    $"Failed login attempt (wrong password). UserID={user.UserID}, Email={user.Email}",
                    HttpContext,
                    explicitUserId: user.UserID,
                    explicitUserName: user.UserName);

                return LocalRedirect(returnUrl);
            }

            await SignIn(user);

            var managerStoreId = await _db.Personels
                .AsNoTracking()
                .Where(p => p.UserID == user.UserID)
                .Select(p => p.StoreID)
                .FirstOrDefaultAsync();

            if (managerStoreId != 0)
            {
                Response.Cookies.Append(
                    "CurrentStoreId",
                    managerStoreId.ToString(),
                    new CookieOptions
                    {
                        HttpOnly = true,
                        IsEssential = true,
                        Expires = DateTimeOffset.UtcNow.AddDays(30)
                    });
            }

            await _audit.LogAsync(
                $"User logged in. UserID={user.UserID}, Email={user.Email}",
                HttpContext,
                explicitUserId: user.UserID,
                explicitUserName: user.UserName);

            return await RedirectAfterLogin(user, returnUrl);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeRegister(EmployeeRegisterVm vm, string? ReturnUrl)
        {
            var returnUrl = NormalizeReturnUrl(ReturnUrl);

            if (!ModelState.IsValid)
            {
                TempData["OpenModal"] = "employee";
                TempData["AuthError"] = string.Join("<br/>",
                    ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return LocalRedirect(returnUrl);
            }

            var code = await _db.PersonnelNumberCodes
                .Include(c => c.Store)
                .FirstOrDefaultAsync(c => c.Code == vm.EmployeeNumber);

            if (code == null || code.IsUsed)
            {
                TempData["OpenModal"] = "employee";
                TempData["AuthError"] = "Bu personel numarası geçersiz veya zaten kullanılmış.";

                await _audit.LogAsync(
                    $"Failed personnel registration. EmployeeNumber={vm.EmployeeNumber}, Reason=Invalid or used code",
                    HttpContext);

                return LocalRedirect(returnUrl);
            }

            var exists = await _db.Users.AnyAsync(u => u.Email == vm.Email);
            if (exists)
            {
                TempData["OpenModal"] = "employee";
                TempData["AuthError"] = "Bu e-posta ile bir kullanıcı zaten kayıtlı.";

                await _audit.LogAsync(
                    $"Failed personnel registration. EmployeeNumber={vm.EmployeeNumber}, Email={vm.Email}, Reason=Email in use",
                    HttpContext);

                return LocalRedirect(returnUrl);
            }

            var user = new User
            {
                Email = vm.Email,
                UserName = vm.UserName,
                RoleID = code.RoleID,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            user.PasswordHash = _hasher.HashPassword(user, vm.Password);

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            var personel = new Personel
            {
                UserID = user.UserID,
                RoleID = code.RoleID,
                PersonelNumber = vm.EmployeeNumber,
                PersonelName = vm.UserName,
                PersonelSurname = "",
                PersonelEmail = vm.Email,
                PersonelTC = "",
                PersonelBirthDate = DateTime.UtcNow,
                PersonelCreatedAt = DateTime.UtcNow,
                StoreID = code.StoreID 
            };

            _db.Personels.Add(personel);

            code.IsUsed = true;
            code.UsedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            await _audit.LogAsync(
                $"Employee registered. UserID={user.UserID}, PersonelID={personel.PersonelID}, Email={user.Email}, PersonnelNumber={personel.PersonelNumber}, StoreID={personel.StoreID}",
                HttpContext,
                explicitUserId: user.UserID,
                explicitUserName: user.UserName);

            await SignIn(user);

            await _audit.LogAsync(
                $"Employee logged in after registration. UserID={user.UserID}, PersonelID={personel.PersonelID}",
                HttpContext,
                explicitUserId: user.UserID,
                explicitUserName: user.UserName);

            return await RedirectAfterLogin(user, returnUrl);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout(string? returnUrl = "/")
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUserName = User.Identity?.Name;

            await _audit.LogAsync(
                $"User logged out.",
                HttpContext,
                explicitUserId: int.TryParse(currentUserId, out var id) ? id : null,
                explicitUserName: currentUserName);

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return LocalRedirect(returnUrl ?? "/");
        }
    }
}