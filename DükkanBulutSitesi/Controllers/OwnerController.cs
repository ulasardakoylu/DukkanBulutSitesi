using DükkanBulutSitesi.Infrastructure;
using DükkanBulutSitesi.Models;
using DükkanBulutSitesi.Models.Manager;
using DükkanBulutSitesi.Models.Owner;
using Entity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;
using System.Security.Claims;

namespace DükkanBulutSitesi.Controllers
{
    [Authorize]
    public class OwnerController : Controller
    {
        private readonly AppDbContext _db;
        private readonly CurrentStoreAccessor _storeAccessor;
        private readonly IWebHostEnvironment _env;
        private readonly IPasswordHasher<User> _hasher;
        private readonly IAuditLogger _audit;

        public OwnerController(
            AppDbContext db,
            CurrentStoreAccessor storeAccessor,
            IWebHostEnvironment env,
            IPasswordHasher<User> hasher,
            IAuditLogger audit)
        {
            _db = db;
            _storeAccessor = storeAccessor;
            _env = env;
            _hasher = hasher;
            _audit = audit;
        }

        // -------------------------------------------------
        //  HELPER: ensure we have a valid store context
        // -------------------------------------------------
        private async Task<Store?> EnsureStoreContextAsync(int? storeId = null)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out var userId))
                return null;

            var effectiveStoreId = storeId ?? _storeAccessor.CurrentStoreId;
            if (effectiveStoreId == 0)
                return null;

            var store = await _db.Stores
                .Join(_db.StoreOwners,
                      s => s.StoreID,
                      o => o.StoreID,
                      (s, o) => new { s, o })
                .Where(x => x.o.UserID == userId &&
                            x.o.IsActive &&
                            x.s.StoreID == effectiveStoreId)
                .Select(x => x.s)
                .FirstOrDefaultAsync();

            if (store == null)
                return null;

            // cookie + accessor
            Response.Cookies.Append("CurrentStoreId", effectiveStoreId.ToString(), new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                Expires = DateTimeOffset.UtcNow.AddDays(30)
            });
            _storeAccessor.ForceSet(effectiveStoreId);

            // layout info
            ViewBag.StoreId = store.StoreID;
            ViewBag.StoreName = store.StoreName;
            ViewBag.StoreDescription = store.StoreDescription ?? "";

            return store;
        }

        // -------------------------------------------------
        //  TOP LEVEL OWNER DASHBOARD (list of stores)
        // -------------------------------------------------
        public async Task<IActionResult> Dashboard()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out var userId))
            {
                return RedirectToAction("Welcome", "Home");
            }

            var ownerStores = await _db.StoreOwners
                .Include(o => o.Store)
                .Where(o => o.UserID == userId
                            && o.IsActive
                            && o.Store.IsActive)
                .ToListAsync();

            if (!ownerStores.Any())
            {
                return RedirectToAction("Welcome", "Home");
            }

            var storeIds = ownerStores.Select(o => o.StoreID).ToList();

            // which roles count as "manager"
            var managerRoleIds = await _db.Roles
                .Where(r => r.RoleName == "Manager" && storeIds.Contains(r.StoreID))
                .Select(r => r.RoleID)
                .ToListAsync();

            // all managers for ALL those stores
            var managers = await _db.Personels
                .Where(p => storeIds.Contains(p.StoreID)
                            && managerRoleIds.Contains(p.RoleID)
                            && p.IsActive)
                .ToListAsync();

            var vm = new OwnerDashboardVm
            {
                OwnerName = User.Identity?.Name ?? "Dükkan Sahibi",
                Stores = ownerStores.Select(o =>
                {
                    var s = o.Store;

                    // all managers for THIS store
                    var storeManagers = managers
                        .Where(p => p.StoreID == s.StoreID)
                        .ToList();

                    var managerNames = storeManagers
                        .Select(p => $"{p.PersonelName} {p.PersonelSurname}".Trim())
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Distinct()
                        .ToList();

                    return new OwnerStoreSummaryVm
                    {
                        StoreID = s.StoreID,
                        StoreName = s.StoreName,
                        StoreDescription = s.StoreDescription,
                        StoreAddress = s.StoreAddress,
                        StorePictureLink = s.StorePictureLink,
                        StoreOpenedAt = s.StoreOpenedAt,

                        HasManager = managerNames.Any(),
                        ManagerName = managerNames.FirstOrDefault(),
                        ManagerCount = managerNames.Count,
                        ManagerNames = managerNames
                    };
                }).ToList()
            };

            return View(vm);
        }


        // -------------------------------------------------
        //  CREATE / EDIT STORE 
        // -------------------------------------------------
        [HttpGet]
        public IActionResult CreateStore()
        {
            var vm = new OwnerEditStoreVm
            {
                IsActive = true,
                StoreOpenedAt = DateTime.Today
            };
            return View("EditStore", vm);
        }

        [HttpGet]
        public async Task<IActionResult> EditStore(int id)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var store = await _db.Stores
                .Join(_db.StoreOwners,
                      s => s.StoreID,
                      o => o.StoreID,
                      (s, o) => new { s, o })
                .Where(x => x.o.UserID == userId && x.s.StoreID == id)
                .Select(x => x.s)
                .FirstOrDefaultAsync();

            if (store == null)
                return NotFound();

            var vm = new OwnerEditStoreVm
            {
                StoreID = store.StoreID,
                StoreName = store.StoreName,
                StoreDescription = store.StoreDescription,
                StoreAddress = store.StoreAddress,
                StoreOpenedAt = store.StoreOpenedAt,
                IsActive = store.IsActive,
                ExistingPictureLink = store.StorePictureLink
            };

            return View("EditStore", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveStore(OwnerEditStoreVm vm)
        {
            if (!ModelState.IsValid)
            {
                return View("EditStore", vm);
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            Store store;

            // upload picture if provided
            var newPicUrl = await SaveStorePictureAsync(vm.StorePictureFile);

            if (vm.StoreID == null)
            {
                // CREATE
                store = new Store
                {
                    StoreName = vm.StoreName,
                    StoreDescription = vm.StoreDescription,
                    StoreAddress = vm.StoreAddress,
                    StoreOpenedAt = vm.StoreOpenedAt ?? DateTime.UtcNow,
                    StorePictureLink = newPicUrl,
                    IsActive = vm.IsActive,
                    CreatedAt = DateTime.UtcNow
                };

                _db.Stores.Add(store);
                await _db.SaveChangesAsync();

                var userEntity = await _db.Users.FirstOrDefaultAsync(u => u.UserID == userId);
                var ownerDisplayName = userEntity?.UserName;

                if (string.IsNullOrWhiteSpace(ownerDisplayName))
                    ownerDisplayName = userEntity?.Email;
                if (string.IsNullOrWhiteSpace(ownerDisplayName))
                    ownerDisplayName = "Dükkan Sahibi";

                _db.StoreOwners.Add(new StoreOwner
                {
                    UserID = userId,
                    StoreID = store.StoreID,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true,
                    OwnerName = ownerDisplayName
                });

                await EnsureManagerRoleForStoreAsync(store.StoreID);

            }
            else
            {
                // UPDATE (only if this user owns it)
                store = await _db.Stores
                    .Join(_db.StoreOwners,
                          s => s.StoreID,
                          o => o.StoreID,
                          (s, o) => new { s, o })
                    .Where(x => x.o.UserID == userId && x.s.StoreID == vm.StoreID.Value)
                    .Select(x => x.s)
                    .FirstOrDefaultAsync();

                if (store == null)
                    return NotFound();

                store.StoreName = vm.StoreName;
                store.StoreDescription = vm.StoreDescription;
                store.StoreAddress = vm.StoreAddress;
                if (vm.StoreOpenedAt.HasValue)
                    store.StoreOpenedAt = vm.StoreOpenedAt.Value;
                store.IsActive = vm.IsActive;

                if (!string.IsNullOrEmpty(newPicUrl))
                {
                    var oldPic = store.StorePictureLink;
                    store.StorePictureLink = newPicUrl;

                    if (!string.IsNullOrWhiteSpace(oldPic))
                    {
                        DeletePhysicalFile(oldPic);
                    }
                }
            }

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Dashboard));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteStore(int id)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out var userId))
                return Forbid();

            var ownerLink = await _db.StoreOwners
                .Include(o => o.Store)
                .FirstOrDefaultAsync(o =>
                    o.UserID == userId &&
                    o.StoreID == id);

            if (ownerLink == null || ownerLink.Store == null)
                return NotFound();

            var allLinks = await _db.StoreOwners
                .Where(o => o.StoreID == id)
                .ToListAsync();
            _db.StoreOwners.RemoveRange(allLinks);

            var roles = await _db.Roles
                .Include(r => r.RolePermissions)
                .Include(r => r.PersonnelNumberCodes)
                .Where(r => r.StoreID == id)
                .ToListAsync();

            if (roles.Any())
            {
                _db.RolePermissions.RemoveRange(roles.SelectMany(r => r.RolePermissions));
                _db.PersonnelNumberCodes.RemoveRange(roles.SelectMany(r => r.PersonnelNumberCodes));
                _db.Roles.RemoveRange(roles);
            }

            _db.Stores.Remove(ownerLink.Store);

            await _db.SaveChangesAsync();

            const string cookieName = "CurrentStoreId";
            if (Request.Cookies.TryGetValue(cookieName, out var cookieVal)
                && int.TryParse(cookieVal, out var currentId)
                && currentId == id)
            {
                Response.Cookies.Delete(cookieName);
                _storeAccessor.ForceSet(0);
            }

            return RedirectToAction(nameof(Dashboard));
        }

        private async Task<string?> SaveStorePictureAsync(IFormFile? file)
        {
            if (file == null || file.Length == 0)
                return null;

            var root = Path.Combine(_env.WebRootPath, "img", "stores");
            Directory.CreateDirectory(root);

            var ext = Path.GetExtension(file.FileName);
            var fileName = $"{Guid.NewGuid()}{ext}";
            var fullPath = Path.Combine(root, fileName);

            using (var stream = System.IO.File.Create(fullPath))
            {
                await file.CopyToAsync(stream);
            }

            return $"/img/stores/{fileName}";
        }


        // -------------------------------------------------
        //  STORE DASHBOARD
        // -------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> StoreDashboard(int storeId)
        {
            var store = await EnsureStoreContextAsync(storeId);
            if (store == null)
                return RedirectToAction(nameof(Dashboard));

            var vm = new OwnerStoreDashboardVm
            {
                StoreID = store.StoreID,
                StoreName = store.StoreName,
                StoreDescription = store.StoreDescription,
                StoreAddress = store.StoreAddress
            };

            var now = DateTime.UtcNow;
            var monthStart = now.AddDays(-30);
            var weekStart = now.AddDays(-7);
            var dayStart = now.AddDays(-1); // last 24h

            // -------------------------------------------------
            //  Base data: all purchases for this store (last 30 days)
            // -------------------------------------------------
            var boughtLast30 = await _db.BoughtUserItems
                .AsNoTracking()
                .Where(b => b.BoughtAt >= monthStart)
                .Join(
                    _db.InventoryItems.Where(ii => ii.StoreID == store.StoreID && ii.IsActive),
                    b => b.ItemID,
                    ii => ii.ItemID,
                    (b, ii) => b
                )
                .Include(b => b.Item)
                .ToListAsync();

            // ---------- LINE CHART DATA (in memory) ----------

            // Month – by day
            vm.MonthSales = boughtLast30
                .GroupBy(b => b.BoughtAt.Date)
                .OrderBy(g => g.Key)
                .Select(g => new SalesPointVm
                {
                    Label = g.Key.ToString("dd.MM.yyyy"),
                    Value = g.Sum(x => x.Item.ItemPrice)
                })
                .ToList();

            // Week – last 7 days, by day
            vm.WeekSales = boughtLast30
                .Where(b => b.BoughtAt >= weekStart)
                .GroupBy(b => b.BoughtAt.Date)
                .OrderBy(g => g.Key)
                .Select(g => new SalesPointVm
                {
                    Label = g.Key.ToString("dd.MM.yyyy"),
                    Value = g.Sum(x => x.Item.ItemPrice)
                })
                .ToList();

            // Day – last 24h, by hour
            vm.DaySales = boughtLast30
                .Where(b => b.BoughtAt >= dayStart)
                .GroupBy(b => new { b.BoughtAt.Date, b.BoughtAt.Hour })
                .OrderBy(g => g.Key.Date).ThenBy(g => g.Key.Hour)
                .Select(g =>
                {
                    var dt = g.Key.Date.AddHours(g.Key.Hour);
                    return new SalesPointVm
                    {
                        Label = dt.ToString("HH:mm"),
                        Value = g.Sum(x => x.Item.ItemPrice)
                    };
                })
                .ToList();

            // -------------------------------------------------
            //  PIE CHARTS
            // -------------------------------------------------

            // Category breakdown – keep EVERYTHING in SQL, then materialize
            var categoryRaw = await (
                from b in _db.BoughtUserItems
                where b.BoughtAt >= monthStart
                join ii in _db.InventoryItems
                     .Where(ii => ii.StoreID == store.StoreID && ii.IsActive)
                     on b.ItemID equals ii.ItemID
                join c in _db.CategoryItems on b.ItemID equals c.ItemID
                select new { c.CategoryName, b.Item.ItemPrice }
            ).ToListAsync();

            vm.CategoryBreakdown = categoryRaw
                .GroupBy(x => x.CategoryName)
                .OrderByDescending(g => g.Sum(x => x.ItemPrice))
                .Take(6)
                .Select(g => new PieSliceVm
                {
                    Label = g.Key,
                    Value = g.Sum(x => x.ItemPrice)
                })
                .ToList();

            // Top-selling items – reuse boughtLast30
            vm.TopItemsBreakdown = boughtLast30
                .GroupBy(b => new { b.ItemID, b.Item.ItemName })
                .OrderByDescending(g => g.Sum(x => x.Item.ItemPrice))
                .Take(6)
                .Select(g => new PieSliceVm
                {
                    Label = g.Key.ItemName,
                    Value = g.Sum(x => x.Item.ItemPrice)
                })
                .ToList();

            return View("StoreDashboard", vm);
        }



        // -------------------------------------------------
        //  ITEMS
        // -------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> StoreItems(int? storeId)
        {
            var store = await EnsureStoreContextAsync(storeId);
            if (store == null)
                return RedirectToAction(nameof(Dashboard));

            var invList = await _db.InventoryItems
                .AsNoTracking()
                .Include(ii => ii.Item)
                .Where(ii => ii.StoreID == store.StoreID && ii.IsActive)
                .OrderBy(ii => ii.Item.ItemName)
                .ToListAsync();

            var vm = new ManagerItemListVm
            {
                Items = invList.Select(ii => new ManagerItemListRowVm
                {
                    ItemID = ii.ItemID,
                    ItemName = ii.Item.ItemName,
                    ItemPrice = ii.Item.ItemPrice,
                    PictureLink = ii.Item.PictureLink,
                    IsActive = ii.Item.IsActive,
                    OnHand = ii.OnHand
                }).ToList()
            };

            return View("StoreItems", vm);
        }

        [HttpGet]
        public async Task<IActionResult> CreateItem()
        {
            var store = await EnsureStoreContextAsync();
            if (store == null)
                return RedirectToAction(nameof(Dashboard));

            var vm = new ManagerItemEditVm
            {
                IsActive = true,
                OnHand = 1,
                ItemPrice = 0.01m,
                Description = ""
            };

            return View("EditItem", vm);
        }

        [HttpGet]
        public async Task<IActionResult> EditItem(int id)
        {
            var store = await EnsureStoreContextAsync();
            if (store == null)
                return RedirectToAction(nameof(Dashboard));

            // ✅ Scope check: item must exist in THIS store inventory
            var inv = await _db.InventoryItems
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.StoreID == store.StoreID && i.ItemID == id);

            if (inv == null)
                return Forbid();

            var item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.ItemID == id);
            if (item == null) return NotFound();

            var cats = await _db.CategoryItems
                .AsNoTracking()
                .Where(c => c.ItemID == id)
                .Select(c => c.CategoryName)
                .ToListAsync();

            var kws = await _db.KeywordsOfItems
                .AsNoTracking()
                .Where(k => k.ItemID == id)
                .Select(k => k.KeywordName)
                .ToListAsync();

            var vm = new ManagerItemEditVm
            {
                ItemID = item.ItemID,
                ItemName = item.ItemName,
                ItemPrice = item.ItemPrice,
                ExistingPictureLink = item.PictureLink,
                Description = item.Description ?? "",
                IsActive = item.IsActive,
                OnHand = inv.OnHand,
                CategoriesCsv = string.Join(", ", cats),
                KeywordsCsv = string.Join(", ", kws)
            };

            return View("EditItem", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateItem(ManagerItemEditVm vm)
        {
            var store = await EnsureStoreContextAsync();
            if (store == null)
                return RedirectToAction(nameof(Dashboard));

            // normalize
            vm.ItemName = (vm.ItemName ?? "").Trim();
            vm.Description = (vm.Description ?? "").Trim();
            vm.CategoriesCsv = (vm.CategoriesCsv ?? "").Trim();
            vm.KeywordsCsv = (vm.KeywordsCsv ?? "").Trim();

            // enforce rules (server-side)
            if (string.IsNullOrWhiteSpace(vm.ItemName))
                ModelState.AddModelError(nameof(vm.ItemName), "Ürün adı zorunludur.");

            if (string.IsNullOrWhiteSpace(vm.Description))
                ModelState.AddModelError(nameof(vm.Description), "Açıklama zorunludur.");

            if (vm.ItemPrice < 0.01m)
                ModelState.AddModelError(nameof(vm.ItemPrice), "Fiyat 0'dan büyük olmalıdır.");

            if (vm.ItemPrice > MaxPrice)
                ModelState.AddModelError(nameof(vm.ItemPrice), $"Fiyat en fazla {MaxPrice} olabilir.");

            if (vm.OnHand < 1)
                ModelState.AddModelError(nameof(vm.OnHand), "Stok en az 1 olmalıdır.");

            if (vm.OnHand > MaxOnHand)
                ModelState.AddModelError(nameof(vm.OnHand), $"Stok en fazla {MaxOnHand} olabilir.");

            // CSV token checks (at least 1)
            var cats = ParseCsvTokens(vm.CategoriesCsv, maxCount: 20, maxLen: 50);
            var kws = ParseCsvTokens(vm.KeywordsCsv, maxCount: 30, maxLen: 50);

            if (cats.Count < 1)
                ModelState.AddModelError(nameof(vm.CategoriesCsv), "En az 1 kategori girilmelidir.");

            if (kws.Count < 1)
                ModelState.AddModelError(nameof(vm.KeywordsCsv), "En az 1 anahtar kelime girilmelidir.");

            if (!ModelState.IsValid)
                return View("EditItem", vm);

            var picUrl = await SaveItemPicture(vm.PictureFile);

            var item = new Item
            {
                ItemName = vm.ItemName,
                ItemPrice = vm.ItemPrice,
                Description = vm.Description,
                IsActive = vm.IsActive,
                CreatedAt = DateTime.UtcNow,
                PictureLink = picUrl
            };

            _db.Items.Add(item);
            await _db.SaveChangesAsync();

            _db.InventoryItems.Add(new InventoryItem
            {
                StoreID = store.StoreID,
                ItemID = item.ItemID,
                OnHand = vm.OnHand,
                IsActive = true
            });

            _db.ItemStocks.Add(new ItemStock
            {
                ItemID = item.ItemID,
                ChangedAt = DateTime.UtcNow,
                StockCount = vm.OnHand
            });

            foreach (var c in cats)
            {
                _db.CategoryItems.Add(new CategoryItem
                {
                    ItemID = item.ItemID,
                    CategoryName = c
                });
            }

            foreach (var k in kws)
            {
                _db.KeywordsOfItems.Add(new KeywordOfItem
                {
                    ItemID = item.ItemID,
                    KeywordName = k
                });
            }

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(StoreItems), new { storeId = store.StoreID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditItem(ManagerItemEditVm vm)
        {
            var store = await EnsureStoreContextAsync();
            if (store == null)
                return RedirectToAction(nameof(Dashboard));

            if (!vm.ItemID.HasValue)
                return BadRequest();

            // normalize
            vm.ItemName = (vm.ItemName ?? "").Trim();
            vm.Description = (vm.Description ?? "").Trim();
            vm.CategoriesCsv = (vm.CategoriesCsv ?? "").Trim();
            vm.KeywordsCsv = (vm.KeywordsCsv ?? "").Trim();

            // enforce rules (server-side)
            if (string.IsNullOrWhiteSpace(vm.ItemName))
                ModelState.AddModelError(nameof(vm.ItemName), "Ürün adı zorunludur.");

            if (string.IsNullOrWhiteSpace(vm.Description))
                ModelState.AddModelError(nameof(vm.Description), "Açıklama zorunludur.");

            if (vm.ItemPrice < 0.01m)
                ModelState.AddModelError(nameof(vm.ItemPrice), "Fiyat 0'dan büyük olmalıdır.");

            if (vm.ItemPrice > MaxPrice)
                ModelState.AddModelError(nameof(vm.ItemPrice), $"Fiyat en fazla {MaxPrice} olabilir.");

            if (vm.OnHand < 1)
                ModelState.AddModelError(nameof(vm.OnHand), "Stok en az 1 olmalıdır.");

            if (vm.OnHand > MaxOnHand)
                ModelState.AddModelError(nameof(vm.OnHand), $"Stok en fazla {MaxOnHand} olabilir.");

            var cats = ParseCsvTokens(vm.CategoriesCsv, maxCount: 20, maxLen: 50);
            var kws = ParseCsvTokens(vm.KeywordsCsv, maxCount: 30, maxLen: 50);

            if (cats.Count < 1)
                ModelState.AddModelError(nameof(vm.CategoriesCsv), "En az 1 kategori girilmelidir.");

            if (kws.Count < 1)
                ModelState.AddModelError(nameof(vm.KeywordsCsv), "En az 1 anahtar kelime girilmelidir.");

            if (!ModelState.IsValid)
                return View("EditItem", vm);

            var itemId = vm.ItemID.Value;

            // ✅ Scope check: must exist in this store inventory
            var inv = await _db.InventoryItems
                .FirstOrDefaultAsync(x => x.StoreID == store.StoreID && x.ItemID == itemId);

            if (inv == null)
                return Forbid();

            var item = await _db.Items.FirstOrDefaultAsync(i => i.ItemID == itemId);
            if (item == null) return NotFound();

            item.ItemName = vm.ItemName;
            item.ItemPrice = vm.ItemPrice;
            item.Description = vm.Description;
            item.IsActive = vm.IsActive;

            var newPic = await SaveItemPicture(vm.PictureFile);
            if (!string.IsNullOrEmpty(newPic))
                item.PictureLink = newPic;

            inv.OnHand = vm.OnHand;
            inv.IsActive = true;

            _db.ItemStocks.Add(new ItemStock
            {
                ItemID = item.ItemID,
                ChangedAt = DateTime.UtcNow,
                StockCount = vm.OnHand
            });

            _db.CategoryItems.RemoveRange(_db.CategoryItems.Where(c => c.ItemID == item.ItemID));
            _db.KeywordsOfItems.RemoveRange(_db.KeywordsOfItems.Where(k => k.ItemID == item.ItemID));

            foreach (var c in cats)
            {
                _db.CategoryItems.Add(new CategoryItem
                {
                    ItemID = item.ItemID,
                    CategoryName = c
                });
            }

            foreach (var k in kws)
            {
                _db.KeywordsOfItems.Add(new KeywordOfItem
                {
                    ItemID = item.ItemID,
                    KeywordName = k
                });
            }

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(StoreItems), new { storeId = store.StoreID });
        }

        private const int MaxOnHand = 1_000_000;
        private const decimal MaxPrice = 9_999_999m;

        private static List<string> ParseCsvTokens(string? csv, int maxCount, int maxLen)
        {
            if (string.IsNullOrWhiteSpace(csv)) return new List<string>();

            return csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Length > maxLen ? x[..maxLen] : x)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(maxCount)
                .ToList();
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteItem(int id)
        {
            var store = await EnsureStoreContextAsync();
            if (store == null)
                return RedirectToAction(nameof(Dashboard));

            await using var tx = await _db.Database.BeginTransactionAsync();
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
        UPDATE dbo.ComplaintActionLogs
        SET ItemID = NULL
        WHERE ItemID = {id};
    ");

            await _db.Database.ExecuteSqlInterpolatedAsync($@"
        UPDATE dbo.ComplaintActionLogs
        SET CommentID = NULL
        WHERE CommentID IN (SELECT CommentID FROM dbo.ItemComments WHERE ItemID = {id});
    ");

            // 2) Delete children (raw SQL = faster + no OUTPUT clause issues)
            await _db.Database.ExecuteSqlInterpolatedAsync($@"DELETE FROM dbo.InventoryItems WHERE ItemID = {id};");
            await _db.Database.ExecuteSqlInterpolatedAsync($@"DELETE FROM dbo.ItemStocks WHERE ItemID = {id};");
            await _db.Database.ExecuteSqlInterpolatedAsync($@"DELETE FROM dbo.CategoryItems WHERE ItemID = {id};");
            await _db.Database.ExecuteSqlInterpolatedAsync($@"DELETE FROM dbo.KeywordsOfItems WHERE ItemID = {id};");
            await _db.Database.ExecuteSqlInterpolatedAsync($@"DELETE FROM dbo.ItemComments WHERE ItemID = {id};");
            await _db.Database.ExecuteSqlInterpolatedAsync($@"DELETE FROM dbo.SpecialOfferItems WHERE ItemID = {id};");
            await _db.Database.ExecuteSqlInterpolatedAsync($@"DELETE FROM dbo.Items WHERE ItemID = {id};");

            await tx.CommitAsync();

            return RedirectToAction(nameof(StoreItems), new { storeId = store.StoreID });
        }


        [Authorize]
        public async Task<IActionResult> StoreSpecialOffers(int storeId, int? id, bool newOffer = false)
        {
            var store = await EnsureStoreContextAsync(storeId);
            if (store == null)
                return RedirectToAction(nameof(Dashboard));

            ViewBag.StoreId = store.StoreID;
            ViewBag.StoreName = store.StoreName;
            ViewBag.StoreDescription = store.StoreDescription;

            var now = DateTime.Now;
            var vm = new SpecialOfferManagementVm();

            // all offers for this store
            var offers = await _db.SpecialOffers
                .AsNoTracking()
                .Where(o => o.StoreID == storeId)
                .OrderBy(o => o.SpecialOfferDateStart)
                .ToListAsync();

            vm.Offers = offers
                .Select(o => new SpecialOfferSummaryVm
                {
                    SpecialOfferID = o.SpecialOfferID,
                    Name = o.SpecialOfferName,
                    DateStart = o.SpecialOfferDateStart,
                    DateEnd = o.SpecialOfferDateEnd,
                    IsCurrent = o.SpecialOfferDateStart <= now &&
                                o.SpecialOfferDateEnd > now &&
                                !o.IsCancelled,
                    IsCancelled = o.IsCancelled,
                    IsEnded = !o.IsCancelled && o.SpecialOfferDateEnd <= now
                })
                .ToList();

            // selected offer
            vm.SelectedOfferID = id ?? (newOffer ? (int?)null : vm.Offers.FirstOrDefault()?.SpecialOfferID);

            var allItems = await _db.InventoryItems
                .AsNoTracking()
                .Include(ii => ii.Item)
                .Where(ii =>
                    ii.StoreID == storeId &&
                    ii.IsActive &&
                    ii.Item.IsActive)
                .OrderBy(ii => ii.Item.ItemName)
                .Select(ii => ii.Item)
                .ToListAsync();

            if (newOffer || !vm.SelectedOfferID.HasValue)
            {
                // NEW CAMPAIGN
                var start = DateTime.Now;
                var end = start.AddDays(7);

                vm.SelectedOffer = new SpecialOfferEditVm
                {
                    SpecialOfferID = null,
                    Name = "",
                    Description = null,
                    Start = start,
                    End = end,
                    IsCancelled = false,
                    Items = allItems.Select(i => new SpecialOfferItemVm
                    {
                        ItemID = i.ItemID,
                        ItemName = i.ItemName,
                        OriginalPrice = i.ItemPrice,
                        Included = false,
                        NewPrice = null
                    }).ToList()
                };
            }
            else
            {
                // EXISTING CAMPAIGN
                var selected = offers.First(o => o.SpecialOfferID == vm.SelectedOfferID.Value);

                var selectedItems = await _db.SpecialOfferItems
                    .AsNoTracking()
                    .Where(x => x.SpecialOfferID == selected.SpecialOfferID)
                    .ToListAsync();

                vm.SelectedOffer = new SpecialOfferEditVm
                {
                    SpecialOfferID = selected.SpecialOfferID,
                    Name = selected.SpecialOfferName,
                    Description = selected.SpecialOfferDescription,
                    Start = selected.SpecialOfferDateStart,
                    End = selected.SpecialOfferDateEnd,
                    IsCancelled = selected.IsCancelled,
                    Items = allItems.Select(i =>
                    {
                        var soItem = selectedItems.FirstOrDefault(x => x.ItemID == i.ItemID);
                        return new SpecialOfferItemVm
                        {
                            ItemID = i.ItemID,
                            ItemName = i.ItemName,
                            OriginalPrice = i.ItemPrice,
                            Included = soItem != null,
                            NewPrice = soItem?.NewPrice
                        };
                    }).ToList()
                };
            }

            vm.NextCampaignStart = offers
                .Where(o => o.SpecialOfferDateStart > now && !o.IsCancelled)
                .OrderBy(o => o.SpecialOfferDateStart)
                .Select(o => (DateTime?)o.SpecialOfferDateStart)
                .FirstOrDefault();

            return View("StoreSpecialOffers", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StoreSaveSpecialOffer(
            int storeId,
            [Bind(Prefix = "SelectedOffer")] SpecialOfferEditVm vm)
        {
            var store = await EnsureStoreContextAsync(storeId);
            if (store == null)
                return RedirectToAction(nameof(Dashboard));

            if (!vm.Start.HasValue || !vm.End.HasValue || vm.End <= vm.Start)
            {
                TempData["SpecialOfferError"] = "Geçerli bir başlangıç ve bitiş tarihi giriniz.";
                return RedirectToAction(nameof(StoreSpecialOffers), new { storeId, id = vm.SpecialOfferID });
            }

            vm.Name = (vm.Name ?? "").Trim();

            if (string.IsNullOrWhiteSpace(vm.Name))
            {
                TempData["SpecialOfferError"] = "Kampanya adı boş olamaz.";
                return RedirectToAction(nameof(StoreSpecialOffers), new { storeId, id = vm.SpecialOfferID });
            }

            var includedItems = (vm.Items ?? new List<SpecialOfferItemVm>())
                .Where(x => x.Included)
                .ToList();

            if (!includedItems.Any())
            {
                TempData["SpecialOfferError"] = "Kampanya oluşturmak için en az 1 ürün seçmelisiniz.";
                return RedirectToAction(nameof(StoreSpecialOffers), new { storeId, id = vm.SpecialOfferID });
            }

            var priceErrors = new List<string>();

            foreach (var it in includedItems)
            {
                // NewPrice required
                if (!it.NewPrice.HasValue)
                {
                    priceErrors.Add($"{it.ItemName}: Yeni fiyat girilmedi.");
                    continue;
                }

                //must be > 0
                if (it.NewPrice.Value <= 0)
                {
                    priceErrors.Add($"{it.ItemName}: Yeni fiyat 0'dan büyük olmalı.");
                    continue;
                }

                // must be below original
                if (it.NewPrice.Value >= it.OriginalPrice)
                {
                    priceErrors.Add($"{it.ItemName}: Yeni fiyat eski fiyattan düşük olmalı. (Eski: {it.OriginalPrice:0.00})");
                    continue;
                }
            }

            if (priceErrors.Any())
            {
                var msg = "Seçilen ürünlerde fiyat hatası var:\n" + string.Join("\n", priceErrors.Take(10));
                TempData["SpecialOfferError"] = msg;
                return RedirectToAction(nameof(StoreSpecialOffers), new { storeId, id = vm.SpecialOfferID });
            }

            SpecialOffer entity;

            if (vm.SpecialOfferID == null)
            {
                entity = new SpecialOffer { StoreID = storeId };
                _db.SpecialOffers.Add(entity);
            }
            else
            {
                entity = await _db.SpecialOffers
                    .FirstAsync(o => o.SpecialOfferID == vm.SpecialOfferID.Value && o.StoreID == storeId);
            }

            var startRaw = vm.Start.Value;
            var endRaw = vm.End.Value;

            var startRounded = new DateTime(
                startRaw.Year, startRaw.Month, startRaw.Day,
                startRaw.Hour, startRaw.Minute, 0, startRaw.Kind);

            var endRounded = new DateTime(
                endRaw.Year, endRaw.Month, endRaw.Day,
                endRaw.Hour, endRaw.Minute, 0, endRaw.Kind);

            entity.SpecialOfferName = vm.Name ?? "";
            entity.SpecialOfferDescription = vm.Description;
            entity.SpecialOfferDateStart = startRounded;
            entity.SpecialOfferDateEnd = endRounded;
            entity.IsCancelled = vm.IsCancelled;

            await _db.SaveChangesAsync();

            // replace items (your existing code)
            var existingItems = await _db.SpecialOfferItems
                .Where(x => x.SpecialOfferID == entity.SpecialOfferID)
                .ToListAsync();

            _db.SpecialOfferItems.RemoveRange(existingItems);

            var allowedItemIds = await _db.InventoryItems
                .Where(ii => ii.StoreID == storeId && ii.IsActive)
                .Select(ii => ii.ItemID)
                .ToListAsync();

            var allowedSet = allowedItemIds.ToHashSet();

            var toAdd = vm.Items
                .Where(i =>
                    i.Included &&
                    i.NewPrice.HasValue &&
                    allowedSet.Contains(i.ItemID))
                .Select(i => new SpecialOfferItem
                {
                    SpecialOfferID = entity.SpecialOfferID,
                    ItemID = i.ItemID,
                    NewPrice = i.NewPrice.Value
                })
                .ToList();

            if (toAdd.Count > 0)
                _db.SpecialOfferItems.AddRange(toAdd);

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(StoreSpecialOffers), new { storeId, id = entity.SpecialOfferID });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StoreEndSpecialOfferEarly(int storeId, int id)
        {
            var store = await EnsureStoreContextAsync(storeId);
            if (store == null)
                return RedirectToAction(nameof(Dashboard));

            var now = DateTime.Now;

            var current = await _db.SpecialOffers
                .FirstOrDefaultAsync(o => o.SpecialOfferID == id && o.StoreID == storeId);

            if (current == null)
                return RedirectToAction(nameof(StoreSpecialOffers), new { storeId });

            if (current.IsCancelled || current.SpecialOfferDateEnd <= now)
            {
                TempData["SpecialOfferError"] = "Bu kampanya zaten sona ermiş veya iptal edilmiş.";
                return RedirectToAction(nameof(StoreSpecialOffers), new { storeId, id });
            }

            current.SpecialOfferDateEnd = now;
            current.IsCancelled = true;

            var next = await _db.SpecialOffers
                .Where(o => o.StoreID == storeId &&
                            o.SpecialOfferDateStart > current.SpecialOfferDateStart &&
                            !o.IsCancelled)
                .OrderBy(o => o.SpecialOfferDateStart)
                .FirstOrDefaultAsync();

            if (next != null)
                next.SpecialOfferDateStart = now;

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(StoreSpecialOffers), new { storeId, id });
        }

        // -------------------------------------------------
        //  PERSONNEL
        // -------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> StorePersonnel(int? storeId, int? id, string status = "all")
        {
            var store = await EnsureStoreContextAsync(storeId);
            if (store == null)
                return RedirectToAction(nameof(Dashboard));

            var vm = new PersonnelManagementVm();
            vm.StatusFilter = status;

            var q = _db.Personels
                .AsNoTracking()
                .Include(p => p.Role)
                .Where(p => p.StoreID == store.StoreID);

            if (status == "active")
                q = q.Where(p => p.IsActive);
            else if (status == "inactive")
                q = q.Where(p => !p.IsActive);

            vm.Personnel = await q
                .OrderBy(p => p.PersonelNumber)
                .Select(p => new PersonnelListItemVm
                {
                    PersonelID = p.PersonelID,
                    DisplayName = (p.PersonelName + " " + p.PersonelSurname).Trim() != ""
                        ? (p.PersonelName + " " + p.PersonelSurname).Trim()
                        : p.PersonelNumber,
                    RoleName = p.Role.RoleName,
                    IsActive = p.IsActive
                })
                .ToListAsync();

            var selectedId = id ?? vm.Personnel.FirstOrDefault()?.PersonelID;

            if (selectedId != null)
            {
                vm.Selected = await BuildPersonnelEditVm(selectedId.Value);

                // If the selected record no longer exists (deleted), fall back to first in list
                if (vm.Selected == null)
                {
                    var fallbackId = vm.Personnel.FirstOrDefault()?.PersonelID;
                    if (fallbackId != null)
                        vm.Selected = await BuildPersonnelEditVm(fallbackId.Value);
                }
            }

            return View("StorePersonnel", vm);
        }


        [HttpGet]
        public async Task<IActionResult> StorePersonnelPdf(int id)
        {
            var store = await EnsureStoreContextAsync();
            if (store == null)
                return RedirectToAction(nameof(Dashboard));

            // make sure this person belongs to the current store
            var belongsToStore = await _db.Personels
                .AnyAsync(p => p.PersonelID == id && p.StoreID == store.StoreID);

            if (!belongsToStore)
                return NotFound();

            var vm = await BuildPersonnelEditVm(id);

            // same profile-picture resolution logic as ManagerController.PersonnelPdf
            string? profileImagePath = null;
            if (!string.IsNullOrWhiteSpace(vm.ProfilePictureLink) &&
                !vm.ProfilePictureLink.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var rel = vm.ProfilePictureLink.TrimStart('~').TrimStart('/');
                profileImagePath = Path.Combine(
                    _env.WebRootPath,
                    rel.Replace('/', Path.DirectorySeparatorChar));
            }

            var document = new PersonnelProfileDocument(vm, profileImagePath);
            var pdfBytes = document.GeneratePdf();

            var fileName = $"personel_{vm.PersonelNumber}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SavePersonnel(PersonnelManagementVm pageVm)
        {
            if (!await UserOwnsCurrentStoreAsync())
                return Forbid();

            var vm = pageVm.Selected;
            if (vm == null) return RedirectToAction(nameof(StorePersonnel));

            var p = await _db.Personels.FirstAsync(x => x.PersonelID == vm.PersonelID);
            var user = await _db.Users.FirstOrDefaultAsync(u => u.UserID == p.UserID);

            // Personel fields
            p.PersonelName = vm.FirstName ?? "";
            p.PersonelSurname = vm.LastName ?? "";
            p.PersonelTC = vm.TcKimlikNo ?? "";
            p.Salary = vm.Salary;

            if (vm.ExitDate.HasValue)
            {
                p.PersonelExitAt = vm.ExitDate.Value.Date;
                p.PersonelCreatedAt = null;
            }
            else if (vm.HireDate.HasValue)
            {
                p.PersonelCreatedAt = vm.HireDate.Value.Date;
                p.PersonelExitAt = null;
            }
            else
            {
                p.PersonelCreatedAt = null;
                p.PersonelExitAt = null;
            }



            // User fields
            if (user != null)
            {
                if (!string.IsNullOrWhiteSpace(vm.UserEmail))
                {
                    user.Email = vm.UserEmail;
                    p.PersonelEmail = vm.UserEmail;
                }

                if (!string.IsNullOrWhiteSpace(vm.NewPassword))
                    user.PasswordHash = _hasher.HashPassword(user, vm.NewPassword);

                // profile picture upload
                if (vm.ProfilePictureFile != null && vm.ProfilePictureFile.Length > 0)
                {
                    var oldPic = user.ProfilePictureLink;

                    var picUrl = await SavePersonnelPictureAsync(vm.ProfilePictureFile);
                    if (!string.IsNullOrEmpty(picUrl))
                    {
                        user.ProfilePictureLink = picUrl;

                        if (!string.IsNullOrWhiteSpace(oldPic))
                        {
                            DeletePhysicalFile(oldPic);
                            await _audit.LogAsync(
                                $"[Owner] Personnel picture changed. UserID={user.UserID}, Old={oldPic}, New={picUrl}",
                                HttpContext);
                        }
                        else
                        {
                            await _audit.LogAsync(
                                $"[Owner] Personnel picture set for the first time. UserID={user.UserID}, New={picUrl}",
                                HttpContext);
                        }
                    }
                }
            }

            await _db.SaveChangesAsync();

            await _audit.LogAsync(
                $"[Owner] Personnel updated. PersonelID={p.PersonelID}, Name={p.PersonelName} {p.PersonelSurname}",
                HttpContext);

            TempData["PersonnelSaved"] = "Personel bilgileri kaydedildi.";
            return RedirectToAction(nameof(StorePersonnel), new { id = p.PersonelID });
        }

        private async Task<PersonnelEditVm> BuildPersonnelEditVm(int personelId)
        {
            var p = await _db.Personels
                .AsNoTracking()
                .AsSplitQuery()
                .Include(p => p.Role)
                .Include(p => p.Histories)
                .Include(p => p.Educations)
                .Include(p => p.Certificates)
                .Include(p => p.Warnings)
                .FirstOrDefaultAsync(p => p.PersonelID == personelId);

            if (p == null)
                return null;

            var user = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserID == p.UserID);

            var memberships = await _db.Personels
            .Include(x => x.Role)
            .Include(x => x.Store)
            .Where(x => x.UserID == p.UserID)
            .Select(x => new PersonnelEditVm.ActiveStoreVm
                {
                    StoreID = x.StoreID,
                    StoreName = x.Store.StoreName,
                    RoleName = x.Role.RoleName,
                    PersonelNumber = x.PersonelNumber
                })
                .ToListAsync();

            var ownerIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int.TryParse(ownerIdStr, out var ownerUserId);

            var ownedStores = await _db.StoreOwners
            .Include(o => o.Store)
            .Where(o => o.UserID == ownerUserId && o.IsActive && o.Store.IsActive)
            .Select(o => new PersonnelEditVm.OwnerStoreOptionVm
                {
                    StoreID = o.StoreID,
                    StoreName = o.Store.StoreName
                })
                .ToListAsync();

            var assignedStoreIds = memberships.Select(m => m.StoreID).ToHashSet();
            ownedStores = ownedStores.Where(s => !assignedStoreIds.Contains(s.StoreID)).ToList();

            return new PersonnelEditVm
            {
                PersonelID = p.PersonelID,
                UserID = p.UserID,
                RoleID = p.RoleID,
                RoleName = p.Role.RoleName,
                PersonelNumber = p.PersonelNumber,
                FirstName = p.PersonelName,
                LastName = p.PersonelSurname,
                TcKimlikNo = p.PersonelTC,
                BirthDate = p.PersonelBirthDate,
                HireDate = p.PersonelCreatedAt,
                ExitDate = p.PersonelExitAt,
                UserEmail = user?.Email,
                ProfilePictureLink = user?.ProfilePictureLink,
                ActiveStores = memberships,
                OwnerStores = ownedStores,
                IsActive = p.IsActive,
                Salary = p.Salary,

                Histories = p.Histories
                    .OrderByDescending(h => h.PersonelWorkHistoryDateStart)
                    .Select(h => new PersonnelHistoryVm
                    {
                        PersonelHistoryID = h.PersonelWorkHistoryID,
                        PersonelID = h.PersonelID,
                        Title = h.PersonelWorkHistoryName,
                        Start = h.PersonelWorkHistoryDateStart,
                        End = h.PersonelWorkHistoryDateEnd,
                        Description = h.PersonelWorkHistoryDescription
                    }).ToList(),

                Educations = p.Educations
                    .OrderByDescending(e => e.PersonelEducationDateStart)
                    .Select(e => new PersonnelEducationVm
                    {
                        PersonelEducationID = e.PersonelEducationID,
                        PersonelID = e.PersonelID,
                        School = e.PersonelEducationName,
                        Start = e.PersonelEducationDateStart,
                        End = e.PersonelEducationDateEnd,
                        Description = e.PersonelEducationDescription
                    }).ToList(),

                Certificates = p.Certificates
                    .OrderByDescending(c => c.PersonelCertificateObtainedDateStart)
                    .Select(c => new PersonnelCertificateVm
                    {
                        PersonelCertificateID = c.PersonelCertificateID,
                        PersonelID = c.PersonelID,
                        CertificateName = c.PersonelCertificateName,
                        Place = c.PersonelCertificateObtainedAtPlace,
                        Start = c.PersonelCertificateObtainedDateStart,
                        End = c.PersonelCertificateObtainedDateEnd,
                        Description = c.PersonelCertificateDescription
                    }).ToList(),

                Warnings = p.Warnings
                    .OrderByDescending(w => w.CreatedAt)
                    .Select(w => new PersonnelWarningVm
                    {
                        PersonelWarningID = w.PersonelWarningID,
                        PersonelID = w.PersonelID,
                        Date = w.CreatedAt,
                        WarningText = w.WarningDescription,
                        Level = w.WarningLevel,
                        GivenBy = w.GivenBy
                    }).ToList()
            };
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeactivatePersonnel(int personelId)
        {
            if (!await UserOwnsCurrentStoreAsync())
                return Forbid();

            var storeId = _storeAccessor.CurrentStoreId;
            var p = await _db.Personels.FirstOrDefaultAsync(x => x.PersonelID == personelId && x.StoreID == storeId);
            if (p == null) return RedirectToAction(nameof(StorePersonnel));

            p.IsActive = false;
            await _db.SaveChangesAsync();

            TempData["PersonnelSaved"] = "Personel pasifleştirildi.";
            return RedirectToAction(nameof(StorePersonnel), new { id = p.PersonelID, status = "all" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReactivatePersonnel(int personelId)
        {
            if (!await UserOwnsCurrentStoreAsync())
                return Forbid();

            var storeId = _storeAccessor.CurrentStoreId;
            var p = await _db.Personels.FirstOrDefaultAsync(x => x.PersonelID == personelId && x.StoreID == storeId);
            if (p == null) return RedirectToAction(nameof(StorePersonnel));

            p.IsActive = true;
            await _db.SaveChangesAsync();

            TempData["PersonnelSaved"] = "Personel tekrar aktif edildi.";
            return RedirectToAction(nameof(StorePersonnel), new { id = p.PersonelID, status = "all" });
        }

        [HttpGet]
        public async Task<IActionResult> StoreManagerActivityLog(
            int storeId,
            int? filterStoreId,
            int? filterManagerUserId,
            string? filterAction,
            int page = 1,
            int pageSize = 50)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 10, 200);

            var ownerUserIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(ownerUserIdStr, out var ownerUserId))
                return Forbid();

            // Stores owned by this owner
            var ownedStores = await _db.Stores
                .Where(s => _db.StoreOwners.Any(so =>
                    so.StoreID == s.StoreID &&
                    so.UserID == ownerUserId &&
                    so.IsActive))
                .OrderBy(s => s.StoreName)
                .Select(s => new { s.StoreID, s.StoreName, s.StoreDescription })
                .ToListAsync();

            var ownedStoreIds = ownedStores.Select(s => s.StoreID).ToHashSet();

            // storeId is layout context; must be owned if nonzero
            if (storeId != 0 && !ownedStoreIds.Contains(storeId))
                return Forbid();

            // Layout ViewBags
            if (storeId == 0)
            {
                ViewBag.StoreId = 0;
                ViewBag.StoreName = "Tüm Dükkanlar";
                ViewBag.StoreDescription = "";
            }
            else
            {
                var active = ownedStores.First(s => s.StoreID == storeId);
                ViewBag.StoreId = active.StoreID;
                ViewBag.StoreName = active.StoreName;
                ViewBag.StoreDescription = active.StoreDescription ?? "";
            }

            // If filterStoreId is null, default to storeId context (but allow All)
            int? effectiveStoreFilter = filterStoreId;
            if (!effectiveStoreFilter.HasValue && storeId != 0)
                effectiveStoreFilter = storeId;

            IQueryable<ManagerAuditLog> universeQuery = _db.ManagerAuditLogs
                .AsNoTracking()
                .Where(l => ownedStoreIds.Contains(l.StoreID));

            if (effectiveStoreFilter.HasValue && effectiveStoreFilter.Value != 0)
                universeQuery = universeQuery.Where(l => l.StoreID == effectiveStoreFilter.Value);

            if (filterManagerUserId.HasValue && filterManagerUserId.Value != 0)
                universeQuery = universeQuery.Where(l => l.ManagerUserID == filterManagerUserId.Value);

            var actionKeysForDropdown = await universeQuery
                .Select(l => l.Action)
                .Where(a => a != null && a != "")
                .Distinct()
                .OrderBy(a => a)
                .ToListAsync();

            var resultsQuery = universeQuery;

            if (!string.IsNullOrWhiteSpace(filterAction))
                resultsQuery = resultsQuery.Where(l => l.Action == filterAction);

            // Pagination uses resultsQuery
            var totalCount = await resultsQuery.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            if (totalPages <= 0) totalPages = 1;
            if (page > totalPages) page = totalPages;

            var logs = await resultsQuery
                .OrderByDescending(l => l.CreatedAtUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // manager dropdown should come from universeQuery, not resultsQuery
            var managerIdsForDropdown = await universeQuery
                .Select(x => x.ManagerUserID)
                .Distinct()
                .ToListAsync();

            var users = await _db.Users
                .AsNoTracking()
                .Where(u => managerIdsForDropdown.Contains(u.UserID))
                .Select(u => new { u.UserID, u.UserName, u.Email })
                .ToListAsync();

            var userById = users.ToDictionary(u => u.UserID, u => u);

            var storeNameById = ownedStores.ToDictionary(s => s.StoreID, s => s.StoreName);

            // lookup maps used by FriendlyDetail (owned stores only, no leak)
            var personelNameById = await _db.Personels
                .AsNoTracking()
                .Where(p => ownedStoreIds.Contains(p.StoreID))
                .Select(p => new
                {
                    p.PersonelID,
                    Name = (p.PersonelName + " " + p.PersonelSurname).Trim(),
                    p.PersonelNumber
                })
                .ToDictionaryAsync(
                    x => x.PersonelID,
                    x => string.IsNullOrWhiteSpace(x.Name) ? (x.PersonelNumber ?? $"#{x.PersonelID}") : x.Name
                );

            var itemNameById = await _db.Items
                .AsNoTracking()
                .Select(i => new { i.ItemID, i.ItemName })
                .ToDictionaryAsync(x => x.ItemID, x => x.ItemName);

            // ✅ NEW: Only comments that belong to owned stores’ items
            var ownedItemIds = await _db.InventoryItems
                .AsNoTracking()
                .Where(ii => ownedStoreIds.Contains(ii.StoreID))
                .Select(ii => ii.ItemID)
                .Distinct()
                .ToListAsync();

            var commentPreviewById = await _db.ItemComments
                .AsNoTracking()
                .Where(c => ownedItemIds.Contains(c.ItemID))
                .Select(c => new
                {
                    c.CommentID,
                    Text = (c.PendingDescription ?? c.CommentDescription) ?? ""
                })
                .ToDictionaryAsync(x => x.CommentID, x => x.Text);

            string? ResolvePersonelName(int id) => personelNameById.TryGetValue(id, out var n) ? n : null;
            string? ResolveItemName(int id) => itemNameById.TryGetValue(id, out var n) ? n : null;
            string? ResolveCommentPreview(int id) => commentPreviewById.TryGetValue(id, out var t) && !string.IsNullOrWhiteSpace(t) ? t : null;

            var vm = new ManagerActivityLogVm
            {
                StoreIdContext = storeId,
                FilterStoreId = effectiveStoreFilter,
                FilterManagerUserId = filterManagerUserId,
                FilterAction = filterAction,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount
            };

            // Store dropdown
            vm.StoreOptions.Add(new SelectListItem("Tüm Dükkanlar", "0")
            {
                Selected = (effectiveStoreFilter ?? 0) == 0
            });
            vm.StoreOptions.AddRange(ownedStores.Select(s =>
                new SelectListItem(s.StoreName, s.StoreID.ToString())
                {
                    Selected = (effectiveStoreFilter ?? 0) == s.StoreID
                }));

            // Manager dropdown
            vm.ManagerOptions.Add(new SelectListItem("Tüm Yöneticiler", "0")
            {
                Selected = (filterManagerUserId ?? 0) == 0
            });
            vm.ManagerOptions.AddRange(
                managerIdsForDropdown
                    .OrderBy(id => id)
                    .Select(id =>
                    {
                        userById.TryGetValue(id, out var u);
                        var name = u?.UserName ?? u?.Email ?? $"User#{id}";
                        return new SelectListItem(name, id.ToString())
                        {
                            Selected = (filterManagerUserId ?? 0) == id
                        };
                    })
            );

            // Action dropdown
            vm.ActionOptions.Add(new SelectListItem("Tüm Aksiyonlar", "")
            {
                Selected = string.IsNullOrWhiteSpace(filterAction)
            });
            vm.ActionOptions.AddRange(
                actionKeysForDropdown.Select(a => new SelectListItem(HumanizeActionForDropdown(a!), a!)
                {
                    Selected = (filterAction ?? "") == a
                })
            );

            // Rows (current page only)
            vm.Rows = logs.Select(l =>
            {
                userById.TryGetValue(l.ManagerUserID, out var u);

                var args = ParseArgs(l.Description);

                var (friendlyAction, friendlyDetail) = ToFriendly(
                    controller: l.Controller ?? "Manager",
                    action: l.Action ?? "",
                    httpMethod: l.HttpMethod ?? "",
                    args: args,
                    resolvePersonelName: ResolvePersonelName,
                    resolveItemName: ResolveItemName,
                    resolveCommentPreview: ResolveCommentPreview
                );

                var localTime = ToTurkeyLocalTime(l.CreatedAtUtc);

                return new ManagerActivityLogRowVm
                {
                    LogId = l.ManagerAuditLogID,
                    CreatedAtUtc = l.CreatedAtUtc,
                    FriendlyTime = localTime.ToString("dd.MM.yyyy HH:mm:ss"),

                    StoreId = l.StoreID,
                    StoreName = storeNameById.TryGetValue(l.StoreID, out var sn) ? sn : $"Store#{l.StoreID}",

                    ManagerUserId = l.ManagerUserID,
                    ManagerName = u?.UserName ?? u?.Email ?? $"User#{l.ManagerUserID}",

                    FriendlyAction = friendlyAction,
                    FriendlyDetail = friendlyDetail,

                    Action = l.Action
                };
            }).ToList();

            ViewData["Title"] = "Yönetici Logları";
            return View(vm);
        }




        private static Dictionary<string, string> ParseArgs(string? desc)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(desc)) return dict;

            // Accept dot keys (vm.ItemID) and both "=" and ":" separators (id: 28)
            var matches = System.Text.RegularExpressions.Regex.Matches(
                desc,
                @"(?<k>[A-Za-z_][A-Za-z0-9_\.]*)\s*(?:=|:)\s*(?<v>""[^""]*""|[^,]+)"
            );

            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                var key = m.Groups["k"].Value.Trim();
                var val = m.Groups["v"].Value.Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(key))
                    dict[key] = val;
            }

            return dict;
        }



        private static DateTime ToTurkeyLocalTime(DateTime utc)
        {
            var u = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            try
            {
                // Windows
                var tz = TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");
                return TimeZoneInfo.ConvertTimeFromUtc(u, tz);
            }
            catch
            {
                try
                {
                    // Linux
                    var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
                    return TimeZoneInfo.ConvertTimeFromUtc(u, tz);
                }
                catch
                {
                    return u;
                }
            }
        }

        private static (string actionText, string detailText) ToFriendly(
            string controller,
            string action,
            string httpMethod,
            Dictionary<string, string> args,
            Func<int, string?> resolvePersonelName,
            Func<int, string?> resolveItemName,
            Func<int, string?> resolveCommentPreview)

        {
            // Normalize action key (supports "Manager.PendingComments" etc)
            var key = (action ?? "").Trim();
            var lastDot = key.LastIndexOf('.');
            if (lastDot >= 0 && lastDot < key.Length - 1)
                key = key.Substring(lastDot + 1);

            var method = (httpMethod ?? "").Trim().ToUpperInvariant();

            // Helpers
            static int ArgInt(Dictionary<string, string> a, params string[] keys)
            {
                foreach (var k in keys)
                    if (a.TryGetValue(k, out var s) && int.TryParse(s, out var v))
                        return v;
                return 0;
            }

            static string ArgStr(Dictionary<string, string> a, params string[] keys)
            {
                foreach (var k in keys)
                    if (a.TryGetValue(k, out var s) && !string.IsNullOrWhiteSpace(s))
                        return s.Trim();
                return "";
            }

            static string TrimPreview(string s, int max = 80)
            {
                s ??= "";
                s = s.Trim();
                if (s.Length <= max) return s;
                return s.Substring(0, max) + "…";
            }

            // =========================================================
            // DASHBOARD
            // =========================================================
            if (key.Equals("Dashboard", StringComparison.OrdinalIgnoreCase))
                return ("Paneli Açtı", "Yönetici panelini görüntüledi.");

            // =========================================================
            // SHIFTS
            // =========================================================
            if (key.Equals("Shifts", StringComparison.OrdinalIgnoreCase))
                return ("Vardiya Yönetimi", "Vardiya yönetimi sayfasını görüntüledi.");

            if (key.Equals("ShiftsDay", StringComparison.OrdinalIgnoreCase))
                return ("Günlük Vardiya", "Günlük vardiya planını görüntüledi.");

            if (key.Equals("ShiftsDayPdf", StringComparison.OrdinalIgnoreCase))
                return ("Günlük Vardiya PDF", "Günlük vardiya planını PDF olarak oluşturdu.");

            if (key.Equals("ShiftsPersonWeek", StringComparison.OrdinalIgnoreCase))
            {
                var pid = ArgInt(args, "SelectedPersonelId", "selectedPersonelId", "personelId", "PersonelID");
                var name = pid > 0 ? (resolvePersonelName(pid) ?? $"#{pid}") : null;
                return ("Haftalık Vardiya",
                    name != null ? $"{name} için haftalık vardiya planını görüntüledi." : "Personel için haftalık vardiya planını görüntüledi.");
            }

            if (key.Equals("ShiftsPersonWeekPdf", StringComparison.OrdinalIgnoreCase))
            {
                var pid = ArgInt(args, "selectedPersonelId", "SelectedPersonelId", "personelId", "PersonelID");
                var name = pid > 0 ? (resolvePersonelName(pid) ?? $"#{pid}") : null;
                return ("Haftalık Vardiya PDF",
                    name != null ? $"{name} için haftalık vardiya planını PDF olarak oluşturdu." : "Haftalık vardiya planını PDF olarak oluşturdu.");
            }

            if (key.Equals("CreateShift", StringComparison.OrdinalIgnoreCase))
            {
                var pid = ArgInt(args, "PersonelId", "personelId");
                var name = pid > 0 ? (resolvePersonelName(pid) ?? $"#{pid}") : null;
                return ("Vardiya Oluşturdu", name != null ? $"{name} için yeni vardiya oluşturdu." : "Yeni vardiya oluşturdu.");
            }

            if (key.Equals("UpdateShift", StringComparison.OrdinalIgnoreCase))
            {
                var pid = ArgInt(args, "PersonelId", "personelId");
                var name = pid > 0 ? (resolvePersonelName(pid) ?? $"#{pid}") : null;
                return ("Vardiya Güncelledi", name != null ? $"{name} için vardiya bilgisini güncelledi." : "Vardiya bilgisini güncelledi.");
            }

            if (key.Equals("DeleteShift", StringComparison.OrdinalIgnoreCase))
            {
                var pid = ArgInt(args, "PersonelId", "personelId");
                var name = pid > 0 ? (resolvePersonelName(pid) ?? $"#{pid}") : null;
                return ("Vardiya Sildi", name != null ? $"{name} için vardiyayı sildi." : "Vardiyayı sildi.");
            }

            if (key.Equals("AddPersonelTimeOff", StringComparison.OrdinalIgnoreCase))
            {
                var pid = ArgInt(args, "PersonelID", "personelId");
                var name = pid > 0 ? (resolvePersonelName(pid) ?? $"#{pid}") : null;
                return ("İzin Ekledi", name != null ? $"{name} için izin kaydı ekledi." : "İzin kaydı ekledi.");
            }

            if (key.Equals("DeletePersonelTimeOff", StringComparison.OrdinalIgnoreCase))
            {
                var pid = ArgInt(args, "personelId", "PersonelID");
                var name = pid > 0 ? (resolvePersonelName(pid) ?? $"#{pid}") : null;
                return ("İzin Sildi", name != null ? $"{name} için izin kaydını sildi." : "İzin kaydını sildi.");
            }

            // =========================================================
            // PERSONNEL
            // =========================================================
            if (key.Equals("Personnel", StringComparison.OrdinalIgnoreCase))
                return ("Personel Yönetimi", "Personel yönetimi sayfasını görüntüledi.");

            if (key.Equals("PersonnelPdf", StringComparison.OrdinalIgnoreCase))
            {
                var pid = ArgInt(args, "id", "personelId", "PersonelID");
                var name = pid > 0 ? (resolvePersonelName(pid) ?? $"#{pid}") : null;
                return ("Personel PDF", name != null ? $"{name} için personel PDF raporu oluşturdu." : "Personel PDF raporu oluşturdu.");
            }

            if (key.Equals("SavePersonnel", StringComparison.OrdinalIgnoreCase))
            {
                var pid = ArgInt(args, "PersonelID", "personelId", "id");
                var name = pid > 0 ? (resolvePersonelName(pid) ?? $"#{pid}") : null;
                return ("Personel Güncelledi", name != null ? $"{name} personelinin bilgilerini güncelledi." : "Personel bilgilerini güncelledi.");
            }

            if (key.Equals("SaveHistory", StringComparison.OrdinalIgnoreCase))
            {
                var pid = ArgInt(args, "PersonelID", "personelId");
                var name = pid > 0 ? (resolvePersonelName(pid) ?? $"#{pid}") : null;
                return ("İş Geçmişi", name != null ? $"{name} için iş geçmişi kaydı ekledi/güncelledi." : "İş geçmişi kaydı ekledi/güncelledi.");
            }

            if (key.Equals("DeleteHistory", StringComparison.OrdinalIgnoreCase))
            {
                var pid = ArgInt(args, "personelId", "PersonelID");
                var name = pid > 0 ? (resolvePersonelName(pid) ?? $"#{pid}") : null;
                return ("İş Geçmişi Sildi", name != null ? $"{name} için iş geçmişi kaydını sildi." : "İş geçmişi kaydını sildi.");
            }

            if (key.Equals("SaveEducation", StringComparison.OrdinalIgnoreCase))
            {
                var pid = ArgInt(args, "PersonelID", "personelId");
                var name = pid > 0 ? (resolvePersonelName(pid) ?? $"#{pid}") : null;
                return ("Eğitim", name != null ? $"{name} için eğitim kaydı ekledi/güncelledi." : "Eğitim kaydı ekledi/güncelledi.");
            }

            if (key.Equals("DeleteEducation", StringComparison.OrdinalIgnoreCase))
            {
                var pid = ArgInt(args, "personelId", "PersonelID");
                var name = pid > 0 ? (resolvePersonelName(pid) ?? $"#{pid}") : null;
                return ("Eğitim Sildi", name != null ? $"{name} için eğitim kaydını sildi." : "Eğitim kaydını sildi.");
            }

            if (key.Equals("SaveCertificate", StringComparison.OrdinalIgnoreCase))
            {
                var pid = ArgInt(args, "PersonelID", "personelId");
                var name = pid > 0 ? (resolvePersonelName(pid) ?? $"#{pid}") : null;
                return ("Sertifika", name != null ? $"{name} için sertifika kaydı ekledi/güncelledi." : "Sertifika kaydı ekledi/güncelledi.");
            }

            if (key.Equals("DeleteCertificate", StringComparison.OrdinalIgnoreCase))
            {
                var pid = ArgInt(args, "personelId", "PersonelID");
                var name = pid > 0 ? (resolvePersonelName(pid) ?? $"#{pid}") : null;
                return ("Sertifika Sildi", name != null ? $"{name} için sertifika kaydını sildi." : "Sertifika kaydını sildi.");
            }

            if (key.Equals("SaveWarning", StringComparison.OrdinalIgnoreCase))
            {
                var pid = ArgInt(args, "PersonelID", "personelId");
                var name = pid > 0 ? (resolvePersonelName(pid) ?? $"#{pid}") : null;
                return ("Uyarı", name != null ? $"{name} için uyarı kaydı ekledi/güncelledi." : "Uyarı kaydı ekledi/güncelledi.");
            }

            if (key.Equals("DeleteWarning", StringComparison.OrdinalIgnoreCase))
            {
                var pid = ArgInt(args, "personelId", "PersonelID");
                var name = pid > 0 ? (resolvePersonelName(pid) ?? $"#{pid}") : null;
                return ("Uyarı Sildi", name != null ? $"{name} için uyarı kaydını sildi." : "Uyarı kaydını sildi.");
            }

            // =========================================================
            // ROLES
            // =========================================================
            if (key.Equals("Roles", StringComparison.OrdinalIgnoreCase))
                return ("Rol Yönetimi", "Roller sayfasını görüntüledi.");

            if (key.Equals("SaveRole", StringComparison.OrdinalIgnoreCase))
            {
                var roleId = ArgInt(args, "RoleID", "roleId");
                if (roleId > 0) return ("Rol Güncelledi", $"Rol bilgilerini güncelledi. (Rol #{roleId})");
                return ("Rol Oluşturdu", "Yeni rol oluşturdu.");
            }

            if (key.Equals("DeleteRole", StringComparison.OrdinalIgnoreCase))
                return ("Rol Sildi", "Seçilen rolü sildi.");

            if (key.Equals("GeneratePersonnelNumber", StringComparison.OrdinalIgnoreCase))
                return ("Personel Kodu Oluşturdu", "Yeni personel numarası (davet kodu) oluşturdu.");

            // =========================================================
            // COMMENTS MODERATION
            // =========================================================
            if (key.Equals("PendingComments", StringComparison.OrdinalIgnoreCase))
                return ("Bekleyen Yorumlar", "Bekleyen yorumlar sayfasını görüntüledi.");

            if (key.Equals("ApprovePendingComment", StringComparison.OrdinalIgnoreCase))
            {
                var cid = ArgInt(args, "commentId", "CommentID", "id");

                // Try from log args first (if ever present), otherwise DB lookup by id
                var preview =
                    ArgStr(args, "commentPreview", "CommentPreview", "preview", "text");

                if (string.IsNullOrWhiteSpace(preview) && cid > 0)
                    preview = resolveCommentPreview(cid) ?? "";

                preview = TrimPreview(preview, 120);

                if (!string.IsNullOrWhiteSpace(preview))
                    return ("Yorumu Onayladı", $"Bekleyen yorumu onayladı: \"{preview}\"");

                return ("Yorumu Onayladı",
                    cid > 0 ? $"Bekleyen yorumu onayladı. (Yorum #{cid})" : "Bekleyen yorumu onayladı.");
            }

            if (key.Equals("RejectPendingComment", StringComparison.OrdinalIgnoreCase))
            {
                var cid = ArgInt(args, "commentId", "CommentID", "id");

                var preview =
                    ArgStr(args, "commentPreview", "CommentPreview", "preview", "text");

                if (string.IsNullOrWhiteSpace(preview) && cid > 0)
                    preview = resolveCommentPreview(cid) ?? "";

                preview = TrimPreview(preview, 120);

                if (!string.IsNullOrWhiteSpace(preview))
                    return ("Yorumu Reddetti", $"Bekleyen yorumu reddetti: \"{preview}\"");

                return ("Yorumu Reddetti",
                    cid > 0 ? $"Bekleyen yorumu reddetti. (Yorum #{cid})" : "Bekleyen yorumu reddetti.");
            }



            if (key.Equals("DeleteCommentFromComplaint", StringComparison.OrdinalIgnoreCase))
            {
                var compId = ArgInt(args, "complaintId", "ComplaintID", "id");
                return ("Yorumu Sildi", compId > 0 ? $"Şikayet üzerinden yorumu sildi. (Şikayet #{compId})" : "Şikayet üzerinden yorumu sildi.");
            }

            // =========================================================
            // COMPLAINTS
            // =========================================================
            if (key.Equals("Complaints", StringComparison.OrdinalIgnoreCase))
                return ("Şikayetler", "Şikayetler sayfasını görüntüledi.");

            if (key.Equals("UpdateComplaint", StringComparison.OrdinalIgnoreCase))
            {
                var compId = ArgInt(args, "id", "complaintId", "ComplaintID");
                var status = ArgStr(args, "status", "Status");

                if (compId > 0 && !string.IsNullOrWhiteSpace(status))
                    return ("Şikayeti Güncelledi", $"Şikayet #{compId} durumunu \"{status}\" olarak güncelledi.");

                if (compId > 0)
                    return ("Şikayeti Güncelledi", $"Şikayet #{compId} bilgilerini güncelledi.");

                return ("Şikayeti Güncelledi", "Şikayet bilgilerini güncelledi.");
            }

            if (key.Equals("IgnoreCommentComplaint", StringComparison.OrdinalIgnoreCase))
            {
                var compId = ArgInt(args, "complaintId", "id", "ComplaintID");
                return ("Şikayeti Yoksaydı", compId > 0 ? $"Yorum şikayetini yoksaydı. (Şikayet #{compId})" : "Yorum şikayetini yoksaydı.");
            }

            if (key.Equals("IgnoreItemComplaint", StringComparison.OrdinalIgnoreCase))
            {
                var compId = ArgInt(args, "complaintId", "id", "ComplaintID");
                return ("Şikayeti Yoksaydı", compId > 0 ? $"Ürün şikayetini yoksaydı. (Şikayet #{compId})" : "Ürün şikayetini yoksaydı.");
            }

            if (key.Equals("DeleteItemFromComplaint", StringComparison.OrdinalIgnoreCase))
            {
                var compId = ArgInt(args, "complaintId", "id", "ComplaintID");
                return ("Ürünü Sildi", compId > 0 ? $"Şikayet üzerinden ürünü sildi. (Şikayet #{compId})" : "Şikayet üzerinden ürünü sildi.");
            }

            // =========================================================
            // CONVERSATIONS
            // =========================================================
            if (key.Equals("Conversations", StringComparison.OrdinalIgnoreCase))
                return ("Konuşmalar", "Konuşmalar sayfasını görüntüledi.");

            if (key.Equals("StartConversation", StringComparison.OrdinalIgnoreCase))
            {
                var pid = ArgInt(args, "targetPersonelId", "TargetPersonelId", "personelId");
                var msg = TrimPreview(ArgStr(args, "firstMessage", "message"));

                var name = pid > 0 ? (resolvePersonelName(pid) ?? $"#{pid}") : null;
                if (name != null && !string.IsNullOrWhiteSpace(msg))
                    return ("Konuşma Başlattı", $"{name} isimli personele \"{msg}\" mesajı ile konuşma başlattı.");
                if (name != null)
                    return ("Konuşma Başlattı", $"{name} isimli personele konuşma başlattı.");
                return ("Konuşma Başlattı", "Yeni konuşma başlattı.");
            }

            if (key.Equals("StartConversationWithOwner", StringComparison.OrdinalIgnoreCase))
            {
                var msg = TrimPreview(ArgStr(args, "firstMessage", "message"));
                if (!string.IsNullOrWhiteSpace(msg))
                    return ("Sahibe Mesaj Gönderdi", $"Dükkan sahibine \"{msg}\" mesajı gönderdi.");
                return ("Sahibe Mesaj Gönderdi", "Dükkan sahibine mesaj gönderdi.");
            }

            if (key.Equals("SendConversationMessage", StringComparison.OrdinalIgnoreCase))
            {
                var pid = ArgInt(args, "personelId", "PersonelID", "targetPersonelId");
                var msg = TrimPreview(ArgStr(args, "message", "text", "firstMessage"));
                var name = pid > 0 ? (resolvePersonelName(pid) ?? $"#{pid}") : null;

                if (name != null && !string.IsNullOrWhiteSpace(msg))
                    return ("Mesaj Gönderdi", $"{name} isimli personele \"{msg}\" mesajını gönderdi.");
                if (!string.IsNullOrWhiteSpace(msg))
                    return ("Mesaj Gönderdi", $"\"{msg}\" mesajını gönderdi.");
                return ("Mesaj Gönderdi", "Mesaj gönderdi.");
            }

            if (key.Equals("DeleteConversation", StringComparison.OrdinalIgnoreCase))
                return ("Sohbeti Kapattı", "Konuşmayı kapattı (arşive aldı).");

            if (key.Equals("AcceptIssueRequest", StringComparison.OrdinalIgnoreCase))
            {
                var rid = ArgInt(args, "issueRequestId", "IssueRequestID", "id");
                return ("İsteği Kabul Etti", rid > 0 ? $"İletişim isteğini kabul etti. (İstek #{rid})" : "İletişim isteğini kabul etti.");
            }

            if (key.Equals("RejectIssueRequest", StringComparison.OrdinalIgnoreCase))
            {
                var rid = ArgInt(args, "issueRequestId", "IssueRequestID", "id");
                return ("İsteği Reddetti", rid > 0 ? $"İletişim isteğini reddetti. (İstek #{rid})" : "İletişim isteğini reddetti.");
            }

            // =========================================================
            // ITEMS (GET vs POST difference!)
            // =========================================================
            if (key.Equals("Items", StringComparison.OrdinalIgnoreCase))
                return ("Ürünler", "Ürünler sayfasını görüntüledi.");

            if (key.Equals("CreateItem", StringComparison.OrdinalIgnoreCase))
            {
                if (method == "GET")
                    return ("Ürün Ekleme", "Yeni ürün ekleme ekranını açtı.");

                // POST
                var itemName = ArgStr(args, "ItemName", "itemName");
                if (!string.IsNullOrWhiteSpace(itemName))
                    return ("Ürün Ekledi", $"\"{itemName}\" isimli ürünü ekledi.");
                return ("Ürün Ekledi", "Yeni ürün ekledi.");
            }

            if (key.Equals("EditItem", StringComparison.OrdinalIgnoreCase))
            {
                var itemId = ArgInt(args, "id", "ItemID", "ItemId", "itemId", "vm.ItemID", "model.ItemID");
                var nameFromLog = ArgStr(args, "ItemName", "itemName", "Name", "vm.ItemName", "model.ItemName");

                var itemName =
                    !string.IsNullOrWhiteSpace(nameFromLog) ? nameFromLog :
                    (itemId > 0 ? (resolveItemName(itemId) ?? $"#{itemId}") : null);

                if (method == "GET")
                    return ("Ürün Düzenleme",
                        itemName != null
                            ? $"\"{itemName}\" ürününün düzenleme ekranını açtı."
                            : "Ürün düzenleme ekranını açtı.");

                return ("Ürün Güncelledi",
                    itemName != null
                        ? $"\"{itemName}\" ürününü güncelledi."
                        : "Ürün bilgilerini güncelledi.");
            }


            if (key.Equals("DeleteItem", StringComparison.OrdinalIgnoreCase))
            {
                var itemId = ArgInt(args, "itemId", "ItemID", "id", "itemId");
                var nameFromLog = ArgStr(args, "itemName", "Name", "ItemName");

                var itemName =
                    !string.IsNullOrWhiteSpace(nameFromLog) ? nameFromLog :
                    (itemId > 0 ? (resolveItemName(itemId) ?? $"#{itemId}") : null);

                return ("Ürün Sildi", itemName != null ? $"\"{itemName}\" ürününü sildi." : "Ürünü sildi.");
            }


            // =========================================================
            // SPECIAL OFFERS
            // =========================================================
            if (key.Equals("SpecialOffers", StringComparison.OrdinalIgnoreCase))
                return ("Kampanyalar", "Kampanya yönetimi sayfasını görüntüledi.");

            if (key.Equals("SaveSpecialOffer", StringComparison.OrdinalIgnoreCase))
                return ("Kampanya Kaydetti", "Kampanya oluşturdu / kampanyayı güncelledi.");

            if (key.Equals("EndSpecialOfferEarly", StringComparison.OrdinalIgnoreCase))
                return ("Kampanyayı Bitirdi", "Kampanyayı erken bitirdi.");

            // Fallback (should almost never show now)
            var nice = HumanizeActionForDropdown(key);
            return (nice, $"{nice} işlemini gerçekleştirdi.");
        }

        private static string HumanizeActionForDropdown(string actionKey)
        {
            if (string.IsNullOrWhiteSpace(actionKey)) return "İşlem";

            var a = actionKey.Trim();
            var lastDot = a.LastIndexOf('.');
            if (lastDot >= 0 && lastDot < a.Length - 1)
                a = a.Substring(lastDot + 1);

            return a switch
            {
                "Dashboard" => "Paneli Açtı",

                "Shifts" => "Vardiya Yönetimi",
                "ShiftsDay" => "Günlük Vardiya",
                "ShiftsDayPdf" => "Günlük Vardiya PDF",
                "ShiftsPersonWeek" => "Haftalık Vardiya",
                "ShiftsPersonWeekPdf" => "Haftalık Vardiya PDF",
                "CreateShift" => "Vardiya Oluşturdu",
                "UpdateShift" => "Vardiya Güncelledi",
                "DeleteShift" => "Vardiya Sildi",
                "AddPersonelTimeOff" => "İzin Ekledi",
                "DeletePersonelTimeOff" => "İzin Sildi",

                "Personnel" => "Personel Yönetimi",
                "PersonnelPdf" => "Personel PDF",
                "SavePersonnel" => "Personel Güncelledi",
                "SaveHistory" => "İş Geçmişi",
                "DeleteHistory" => "İş Geçmişi Sildi",
                "SaveEducation" => "Eğitim",
                "DeleteEducation" => "Eğitim Sildi",
                "SaveCertificate" => "Sertifika",
                "DeleteCertificate" => "Sertifika Sildi",
                "SaveWarning" => "Uyarı",
                "DeleteWarning" => "Uyarı Sildi",

                "Roles" => "Rol Yönetimi",
                "SaveRole" => "Rol Kaydetti",
                "DeleteRole" => "Rol Sildi",
                "GeneratePersonnelNumber" => "Personel Kodu Oluşturdu",

                "PendingComments" => "Bekleyen Yorumlar",
                "ApprovePendingComment" => "Yorumu Onayladı",
                "RejectPendingComment" => "Yorumu Reddetti",
                "DeleteCommentFromComplaint" => "Yorumu Sildi",

                "Complaints" => "Şikayetler",
                "UpdateComplaint" => "Şikayeti Güncelledi",
                "IgnoreCommentComplaint" => "Şikayeti Yoksaydı",
                "IgnoreItemComplaint" => "Şikayeti Yoksaydı",
                "DeleteItemFromComplaint" => "Ürünü Sildi",

                "Conversations" => "Konuşmalar",
                "StartConversation" => "Konuşma Başlattı",
                "StartConversationWithOwner" => "Sahibe Mesaj Gönderdi",
                "SendConversationMessage" => "Mesaj Gönderdi",
                "DeleteConversation" => "Sohbeti Kapattı",
                "AcceptIssueRequest" => "İsteği Kabul Etti",
                "RejectIssueRequest" => "İsteği Reddetti",

                "Items" => "Ürünler",
                "CreateItem" => "Ürün Ekleme",
                "EditItem" => "Ürün Düzenleme",
                "DeleteItem" => "Ürün Sildi",

                "SpecialOffers" => "Kampanyalar",
                "SaveSpecialOffer" => "Kampanya Kaydetti",
                "EndSpecialOfferEarly" => "Kampanyayı Bitirdi",

                _ => a
            };
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveHistory(PersonnelHistoryVm vm)
        {
            if (!await UserOwnsCurrentStoreAsync())
                return Forbid();

            PersonelWorkHistory entity;
            bool isNew = vm.PersonelHistoryID == 0;

            if (isNew)
            {
                entity = new PersonelWorkHistory
                {
                    PersonelID = vm.PersonelID
                };
                _db.PersonelHistories.Add(entity);
            }
            else
            {
                entity = await _db.PersonelHistories
                    .FirstAsync(h => h.PersonelWorkHistoryID == vm.PersonelHistoryID);
            }

            entity.PersonelWorkHistoryName = vm.Title ?? string.Empty;
            entity.PersonelWorkHistoryDateStart = vm.Start;
            entity.PersonelWorkHistoryDateEnd = vm.End;
            entity.PersonelWorkHistoryDescription = vm.Description ?? string.Empty;

            await _db.SaveChangesAsync();

            var action = isNew ? "created" : "updated";
            await _audit.LogAsync(
                $"[Owner] Work history {action}. PersonelID={vm.PersonelID}, HistoryID={entity.PersonelWorkHistoryID}, Title={vm.Title}",
                HttpContext);

            return RedirectToAction(nameof(StorePersonnel), new { id = vm.PersonelID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteHistory(int id, int personelId)
        {
            if (!await UserOwnsCurrentStoreAsync())
                return Forbid();

            var h = await _db.PersonelHistories
                .FirstOrDefaultAsync(x => x.PersonelWorkHistoryID == id);
            if (h != null)
            {
                _db.PersonelHistories.Remove(h);
                await _db.SaveChangesAsync();

                await _audit.LogAsync(
                    $"[Owner] Work history deleted. PersonelID={personelId}, HistoryID={id}, Title={h.PersonelWorkHistoryName}",
                    HttpContext);
            }
            return RedirectToAction(nameof(StorePersonnel), new { id = personelId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveEducation(PersonnelEducationVm vm)
        {
            if (!await UserOwnsCurrentStoreAsync())
                return Forbid();

            PersonelEducation entity;
            bool isNew = vm.PersonelEducationID == 0;

            if (isNew)
            {
                entity = new PersonelEducation
                {
                    PersonelID = vm.PersonelID
                };
                _db.PersonelEducations.Add(entity);
            }
            else
            {
                entity = await _db.PersonelEducations
                    .FirstAsync(e => e.PersonelEducationID == vm.PersonelEducationID);
            }

            entity.PersonelEducationName = vm.School;
            entity.PersonelEducationDateStart = vm.Start;
            entity.PersonelEducationDateEnd = vm.End;
            entity.PersonelEducationDescription = vm.Description ?? "";
            entity.PersonelEducationFinished = vm.End.HasValue;

            await _db.SaveChangesAsync();

            var action = isNew ? "created" : "updated";
            await _audit.LogAsync(
                $"[Owner] Education {action}. PersonelID={vm.PersonelID}, EducationID={entity.PersonelEducationID}, School={vm.School}",
                HttpContext);

            return RedirectToAction(nameof(StorePersonnel), new { id = vm.PersonelID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteEducation(int id, int personelId)
        {
            if (!await UserOwnsCurrentStoreAsync())
                return Forbid();

            var e = await _db.PersonelEducations
                .FirstOrDefaultAsync(x => x.PersonelEducationID == id);
            if (e != null)
            {
                _db.PersonelEducations.Remove(e);
                await _db.SaveChangesAsync();

                await _audit.LogAsync(
                    $"[Owner] Education deleted. PersonelID={personelId}, EducationID={id}, School={e.PersonelEducationName}",
                    HttpContext);
            }
            return RedirectToAction(nameof(StorePersonnel), new { id = personelId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveCertificate(PersonnelCertificateVm vm)
        {
            if (!await UserOwnsCurrentStoreAsync())
                return Forbid();

            PersonelCertificate entity;
            bool isNew = vm.PersonelCertificateID == 0;

            if (isNew)
            {
                entity = new PersonelCertificate
                {
                    PersonelID = vm.PersonelID
                };
                _db.PersonelCertificates.Add(entity);
            }
            else
            {
                entity = await _db.PersonelCertificates
                    .FirstAsync(c => c.PersonelCertificateID == vm.PersonelCertificateID);
            }

            entity.PersonelCertificateName = vm.CertificateName;
            entity.PersonelCertificateObtainedAtPlace = vm.Place;
            entity.PersonelCertificateObtainedDateStart = vm.Start;
            entity.PersonelCertificateObtainedDateEnd = vm.End;
            entity.PersonelCertificateDescription = vm.Description ?? "";

            await _db.SaveChangesAsync();

            var action = isNew ? "created" : "updated";
            await _audit.LogAsync(
                $"[Owner] Certificate {action}. PersonelID={vm.PersonelID}, CertificateID={entity.PersonelCertificateID}, Name={vm.CertificateName}",
                HttpContext);

            return RedirectToAction(nameof(StorePersonnel), new { id = vm.PersonelID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCertificate(int id, int personelId)
        {
            if (!await UserOwnsCurrentStoreAsync())
                return Forbid();

            var c = await _db.PersonelCertificates
                .FirstOrDefaultAsync(x => x.PersonelCertificateID == id);
            if (c != null)
            {
                _db.PersonelCertificates.Remove(c);
                await _db.SaveChangesAsync();

                await _audit.LogAsync(
                    $"[Owner] Certificate deleted. PersonelID={personelId}, CertificateID={id}, Name={c.PersonelCertificateName}",
                    HttpContext);
            }
            return RedirectToAction(nameof(StorePersonnel), new { id = personelId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveWarning(PersonnelWarningVm vm)
        {
            if (!await UserOwnsCurrentStoreAsync())
                return Forbid();

            PersonelWarning entity;
            bool isNew = vm.PersonelWarningID == 0;

            if (isNew)
            {
                entity = new PersonelWarning
                {
                    PersonelID = vm.PersonelID
                };
                _db.PersonelWarnings.Add(entity);
            }
            else
            {
                entity = await _db.PersonelWarnings
                    .FirstAsync(w => w.PersonelWarningID == vm.PersonelWarningID);
            }

            entity.CreatedAt = vm.Date;
            entity.WarningDescription = vm.WarningText ?? string.Empty;
            entity.WarningLevel = string.IsNullOrWhiteSpace(vm.Level) ? "warning" : vm.Level;
            entity.GivenBy = string.IsNullOrWhiteSpace(vm.GivenBy)
                ? (User.Identity?.Name ?? "Dükkan Sahibi")
                : vm.GivenBy;

            await _db.SaveChangesAsync();

            var action = isNew ? "created" : "updated";
            await _audit.LogAsync(
                $"[Owner] Warning {action}. PersonelID={vm.PersonelID}, WarningID={entity.PersonelWarningID}, Level={entity.WarningLevel}",
                HttpContext);

            return RedirectToAction(nameof(StorePersonnel), new { id = vm.PersonelID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteWarning(int id, int personelId)
        {
            if (!await UserOwnsCurrentStoreAsync())
                return Forbid();

            var w = await _db.PersonelWarnings
                .FirstOrDefaultAsync(x => x.PersonelWarningID == id);
            if (w != null)
            {
                _db.PersonelWarnings.Remove(w);
                await _db.SaveChangesAsync();

                await _audit.LogAsync(
                    $"[Owner] Warning deleted. PersonelID={personelId}, WarningID={id}, Level={w.WarningLevel}",
                    HttpContext);
            }

            return RedirectToAction(nameof(StorePersonnel), new { id = personelId });
        }

        private async Task<string?> SavePersonnelPictureAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return null;

            var root = Path.Combine(_env.WebRootPath, "img", "personnel");
            Directory.CreateDirectory(root);

            var ext = Path.GetExtension(file.FileName);
            var fileName = $"{Guid.NewGuid()}{ext}";
            var fullPath = Path.Combine(root, fileName);

            using (var stream = System.IO.File.Create(fullPath))
            {
                await file.CopyToAsync(stream);
            }

            return $"/img/personnel/{fileName}";
        }

        private void DeletePhysicalFile(string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return;

            try
            {
                if (relativePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    return;

                var trimmed = relativePath.TrimStart('~').TrimStart('/');

                var fullPath = Path.Combine(
                    _env.WebRootPath,
                    trimmed.Replace('/', Path.DirectorySeparatorChar)
                );

                if (System.IO.File.Exists(fullPath))
                {
                    System.IO.File.Delete(fullPath);
                }
            }
            catch
            {
                // placeholder
            }
        }

        [HttpGet]
        public async Task<IActionResult> StoreComplaints(int storeId, int? id)
        {
            var store = await EnsureStoreContextAsync(storeId);
            if (store == null)
                return RedirectToAction(nameof(Dashboard));

            ViewBag.StoreId = store.StoreID;
            ViewBag.StoreName = store.StoreName;
            ViewBag.StoreDescription = store.StoreDescription;

            var vm = new ComplaintManagementVm();

            // complaint IDs for this store (via Item or Comment -> Item)
            var complaintIdsForStore = await (
                from c in _db.Complaints
                join inv in _db.InventoryItems on c.ItemID equals inv.ItemID
                where inv.StoreID == storeId
                select c.ComplaintID
            )
            .Union(
                from c in _db.Complaints
                join ic in _db.ItemComments on c.CommentID equals ic.CommentID
                join inv in _db.InventoryItems on ic.ItemID equals inv.ItemID
                where inv.StoreID == storeId
                select c.ComplaintID
            )
            .Distinct()
            .ToListAsync();

            // if this store has no related complaints, just render empty list
            if (!complaintIdsForStore.Any())
                return View("StoreComplaints", vm);

            // load only those complaints
            vm.Complaints = await _db.Complaints
                .Where(c => complaintIdsForStore.Contains(c.ComplaintID))
                .Include(c => c.ReporterUser)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new ComplaintListItemVm
                {
                    ComplaintID = c.ComplaintID,
                    Title = c.Title == "" ? $"Rapor #{c.ComplaintID}" : c.Title,
                    Summary = c.Description.Length > 40
                        ? c.Description.Substring(0, 40) + "..."
                        : c.Description,
                    CreatedAt = c.CreatedAt,
                    Status = c.Status
                })
                .ToListAsync();

            // pick selected complaint, but only if it belongs to this store
            int? selectedId = id;
            if (!selectedId.HasValue || !complaintIdsForStore.Contains(selectedId.Value))
            {
                selectedId = vm.Complaints.FirstOrDefault()?.ComplaintID;
            }

            if (selectedId.HasValue)
            {
                vm.Selected = await BuildComplaintDetailVm(selectedId.Value);
            }

            return View("StoreComplaints", vm);
        }

        private async Task<ComplaintDetailVm> BuildComplaintDetailVm(int complaintId)
        {
            var c = await _db.Complaints
                .Include(c => c.ReporterUser)
                .Include(c => c.TargetUser)
                .Include(c => c.ComplaintType)
                .Include(c => c.Item)
                .Include(c => c.Comment)
                .FirstAsync(c => c.ComplaintID == complaintId);

            string area;
            if (c.CommentID.HasValue) area = "Yorum";
            else if (c.ItemID.HasValue) area = "Ürün";
            else area = "Diğer";

            return new ComplaintDetailVm
            {
                ComplaintID = c.ComplaintID,
                Title = c.Title == "" ? $"Rapor #{c.ComplaintID}" : c.Title,
                Area = area,
                ReporterName = c.ReporterUser.UserName ?? c.ReporterUser.Email,
                TargetName = c.TargetUser != null ? (c.TargetUser.UserName ?? c.TargetUser.Email) : null,
                CreatedAt = c.CreatedAt,
                ComplaintTypeName = c.ComplaintType.Name,
                Description = c.Description,
                Status = c.Status,
                ManagerNotes = c.ManagerNotes,
                ItemID = c.ItemID,
                CommentID = c.CommentID
            };
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StoreUpdateComplaint(
            int storeId,
            int id,
            string status,
            string? managerNotes)
        {
            var store = await EnsureStoreContextAsync(storeId);
            if (store == null)
                return RedirectToAction(nameof(Dashboard));

            var c = await _db.Complaints.FirstOrDefaultAsync(x => x.ComplaintID == id);
            if (c == null) return NotFound();

            c.Status = status;
            c.ManagerNotes = managerNotes;
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(StoreComplaints), new { storeId, id });
        }

        [HttpGet]
        public async Task<IActionResult> GetRolesForStore(int targetStoreId)
        {
            if (!await UserOwnsStoreAsync(targetStoreId))
                return Forbid();

            var roles = await _db.Roles
                .Where(r => r.StoreID == targetStoreId)
                .OrderBy(r => r.RoleName)
                .Select(r => new { r.RoleID, r.RoleName })
                .ToListAsync();

            return Json(roles);
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignPersonnelToStore(int sourcePersonelId, int targetStoreId, int targetRoleId)
        {
            if (!await UserOwnsCurrentStoreAsync())
                return Forbid();

            // owner must own the target store too
            if (!await UserOwnsStoreAsync(targetStoreId))
                return Forbid();

            var source = await _db.Personels
                .FirstOrDefaultAsync(p => p.PersonelID == sourcePersonelId);

            if (source == null) return NotFound();

            var user = await _db.Users
            .FirstOrDefaultAsync(u => u.UserID == source.UserID);

            var email = source.PersonelEmail;
            if (string.IsNullOrWhiteSpace(email))
                email = user?.Email;

            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["PersonnelSaved"] = "Bu personelin e-postası boş olduğu için başka mağazaya eklenemiyor.";
                return RedirectToAction(nameof(StorePersonnel), new { id = sourcePersonelId });
            }

            // already exists in that store?
            bool already = await _db.Personels.AnyAsync(p => p.UserID == source.UserID && p.StoreID == targetStoreId);
            if (already)
            {
                TempData["PersonnelSaved"] = "Bu kullanıcı zaten seçili mağazada mevcut.";
                return RedirectToAction(nameof(StorePersonnel), new { id = sourcePersonelId });
            }

            // role must belong to target store
            var role = await _db.Roles.FirstOrDefaultAsync(r => r.RoleID == targetRoleId && r.StoreID == targetStoreId);
            if (role == null)
            {
                TempData["PersonnelSaved"] = "Seçilen rol bu mağazaya ait değil.";
                return RedirectToAction(nameof(StorePersonnel), new { id = sourcePersonelId });
            }

            // generate a NEW personnel number code for that role (unique)
            string newCode;
            bool exists;
            if (role.RoleName == "Manager" && role.IsSystem)
            {
                do
                {
                    newCode = GenerateManagerInviteCode();
                    exists = await _db.PersonnelNumberCodes.AnyAsync(x => x.Code == newCode);
                } while (exists);
            }
            else
            {
                do
                {
                    newCode = GenerateInviteCode();
                    exists = await _db.PersonnelNumberCodes.AnyAsync(x => x.Code == newCode);
                } while (exists);
            }

            // create code record (mark used, since we're directly assigning)
            _db.PersonnelNumberCodes.Add(new PersonnelNumberCode
            {
                RoleID = targetRoleId,
                StoreID = targetStoreId,
                Code = newCode,
                IsUsed = true,
                CreatedAt = DateTime.UtcNow
            });

            // create a NEW Personel row for target store, same user
            var newPersonel = new Personel
            {
                UserID = source.UserID,
                StoreID = targetStoreId,
                RoleID = targetRoleId,
                PersonelNumber = newCode,

                PersonelEmail = email,
                PersonelName = source.PersonelName,
                PersonelSurname = source.PersonelSurname,
                PersonelTC = source.PersonelTC,
                PersonelBirthDate = source.PersonelBirthDate,

                PersonelCreatedAt = DateTime.Now
            };

            _db.Personels.Add(newPersonel);
            await _db.SaveChangesAsync();

            await _audit.LogAsync(
                $"[Owner] Personnel assigned to another store. SourcePersonelID={sourcePersonelId}, UserID={source.UserID}, TargetStoreID={targetStoreId}, RoleID={targetRoleId}, NewPersonelID={newPersonel.PersonelID}, Code={newCode}",
                HttpContext);

            TempData["PersonnelSaved"] = "Personel seçilen mağazaya eklendi.";
            return RedirectToAction(nameof(StorePersonnel), new { id = sourcePersonelId });
        }



        // -------------------------------------------------
        //  CONVERSATIONS
        // -------------------------------------------------
        [Authorize]
        public async Task<IActionResult> StoreConversations(int? storeId, int? id)
        {
            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            // ----- store picker for the left "new conversation" box -----
            var ownerStores = await _db.StoreOwners
                .Include(o => o.Store)
                .Where(o => o.UserID == currentUserId && o.IsActive)
                .Select(o => o.Store)
                .OrderBy(s => s.StoreName)
                .ToListAsync();

            if (!ownerStores.Any())
                return RedirectToAction(nameof(Dashboard));

            var selectedStoreId = storeId ?? _storeAccessor.CurrentStoreId;
            if (selectedStoreId == 0 || !ownerStores.Any(s => s.StoreID == selectedStoreId))
                selectedStoreId = ownerStores.First().StoreID;

            await EnsureStoreContextAsync();

            ViewBag.OwnerStores = ownerStores
                .Select(s => new SelectListItem
                {
                    Value = s.StoreID.ToString(),
                    Text = s.StoreName
                })
                .ToList();
            ViewBag.SelectedStoreId = selectedStoreId;

            var vm = new ConversationPageVm();

            // Personnel list for starting a NEW conversation – filtered by selected store
            vm.PersonelChoices = await _db.Personels
                .Where(p => p.UserID != currentUserId && p.StoreID == selectedStoreId)
                .OrderBy(p => p.PersonelName)
                .Select(p => new PersonelLookupVm
                {
                    PersonelID = p.PersonelID,
                    DisplayName = (p.PersonelName + " " + p.PersonelSurname).Trim()
                })
                .ToListAsync();

            // ----- conversations -----
            var convoQuery = _db.Conversations
                .AsNoTracking()
                .AsSplitQuery()
                .Include(c => c.StarterUser)
                .Include(c => c.TargetUser)
                .Include(c => c.Messages);

            var allConvos = await convoQuery
                .Where(c => c.StarterUserID == currentUserId || c.TargetUserID == currentUserId)
                .ToListAsync();

            var activeConvos = allConvos
                .Where(c =>
                    (c.StarterUserID == currentUserId && !c.IsDeletedByStarter) ||
                    (c.TargetUserID == currentUserId && !c.IsDeletedByTarget))
                .ToList();

            var archivedConvos = allConvos
                .Where(c =>
                    (c.StarterUserID == currentUserId && c.IsDeletedByStarter) ||
                    (c.TargetUserID == currentUserId && c.IsDeletedByTarget))
                .ToList();

            var otherUserIds = allConvos
                .Select(c => c.StarterUserID == currentUserId ? c.TargetUserID : c.StarterUserID)
                .Distinct()
                .ToList();

            var relatedPersonels = await _db.Personels
                .AsNoTracking()
                .Include(p => p.Store)
                .Where(p => otherUserIds.Contains(p.UserID))
                .OrderByDescending(p => p.StoreID == selectedStoreId)
                .ToListAsync();

            var personelByUserId = relatedPersonels
                .GroupBy(p => p.UserID)
                .ToDictionary(
                    g => g.Key,
                    g => g.FirstOrDefault(p => p.StoreID == selectedStoreId) ?? g.First()
                );

            vm.Conversations = activeConvos
                .Select(c =>
                {
                    var otherUser = c.StarterUserID == currentUserId ? c.TargetUser : c.StarterUser;
                    var otherUserId = otherUser.UserID;
                    personelByUserId.TryGetValue(otherUserId, out var per);

                    var lastMsg = c.Messages
                        .OrderByDescending(m => m.SentAt)
                        .FirstOrDefault();

                    return new ConversationSummaryVm
                    {
                        ConversationID = c.ConversationID,
                        PersonelID = per?.PersonelID ?? 0,
                        PersonelName = per != null
                            ? (per.PersonelName + " " + per.PersonelSurname).Trim()
                            : (otherUser.UserName ?? otherUser.Email),
                        LastMessage = lastMsg?.Text ?? "",
                        LastSentAt = lastMsg?.SentAt ?? c.CreatedAt
                    };
                })
                .OrderByDescending(s => s.LastSentAt)
                .ToList();

            vm.ArchivedConversations = archivedConvos
                .Select(c =>
                {
                    var otherUser = c.StarterUserID == currentUserId ? c.TargetUser : c.StarterUser;
                    var otherUserId = otherUser.UserID;
                    personelByUserId.TryGetValue(otherUserId, out var per);

                    var personelId = per?.PersonelID ?? 0;
                    var personelDisplayName = per != null
                        ? (per.PersonelName + " " + per.PersonelSurname).Trim()
                        : (otherUser.UserName ?? otherUser.Email);

                    var lastMsg = c.Messages
                        .OrderByDescending(m => m.SentAt)
                        .FirstOrDefault();

                    var msgs = c.Messages
                        .OrderBy(m => m.SentAt)
                        .Select(m => new ConversationMessageVm
                        {
                            ConversationID = m.ConversationID,
                            PersonelID = personelId,
                            IsFromManager = (m.SenderUserID == currentUserId),
                            Message = m.Text,
                            SentAt = m.SentAt,
                            IsSystem = m.Text == "Karşı taraf sohbeti kapattı."
                        })
                        .ToList();

                    return new ArchivedConversationVm
                    {
                        ConversationID = c.ConversationID,
                        PersonelID = personelId,
                        PersonelName = personelDisplayName,
                        LastMessage = lastMsg?.Text ?? "",
                        LastSentAt = lastMsg?.SentAt ?? c.CreatedAt,
                        Messages = msgs
                    };
                })
                .OrderByDescending(a => a.LastSentAt)
                .ToList();

            vm.SelectedConversationID = id ?? vm.Conversations.FirstOrDefault()?.ConversationID;

            if (vm.SelectedConversationID.HasValue)
            {
                var selectedId = vm.SelectedConversationID.Value;

                var convo = activeConvos.FirstOrDefault(c => c.ConversationID == selectedId);
                if (convo != null)
                {
                    var otherUser = convo.StarterUserID == currentUserId ? convo.TargetUser : convo.StarterUser;
                    var otherUserId = otherUser.UserID;
                    personelByUserId.TryGetValue(otherUserId, out var per);

                    var personelId = per?.PersonelID ?? 0;
                    var personelDisplayName = per != null
                        ? (per.PersonelName + " " + per.PersonelSurname).Trim()
                        : (otherUser.UserName ?? otherUser.Email);

                    vm.SelectedPersonelID = personelId;
                    vm.SelectedPersonelName = personelDisplayName;
                    vm.SelectedPersonelStoreName = per?.Store?.StoreName; // <- store shown in header

                    vm.Messages = await _db.ConversationMessages
                        .Where(m => m.ConversationID == selectedId)
                        .OrderBy(m => m.SentAt)
                        .Select(m => new ConversationMessageVm
                        {
                            ConversationID = m.ConversationID,
                            PersonelID = personelId,
                            IsFromManager = (m.SenderUserID == currentUserId),
                            Message = m.Text,
                            SentAt = m.SentAt,
                            IsSystem = m.Text == "Karşı taraf sohbeti kapattı."
                        })
                        .ToListAsync();
                }
            }

            return View("StoreConversations", vm);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> StartConversation(int targetPersonelId, string firstMessage, int? storeId)
        {
            const string SystemClosedText = "Karşı taraf sohbeti kapattı.";

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            // Require a valid storeId (since this is StoreConversations flow)
            if (storeId == null || storeId.Value == 0)
                return RedirectToAction(nameof(StoreConversations), new { storeId });

            // Owner must own THIS store (not just any store)
            var ownsThisStore = await _db.StoreOwners
                .AnyAsync(o => o.UserID == currentUserId && o.IsActive && o.StoreID == storeId.Value);

            if (!ownsThisStore)
                return RedirectToAction(nameof(Dashboard));

            if (targetPersonelId == 0 || string.IsNullOrWhiteSpace(firstMessage))
                return RedirectToAction(nameof(StoreConversations), new { storeId });

            var targetPersonel = await _db.Personels
                .FirstOrDefaultAsync(p => p.PersonelID == targetPersonelId);

            if (targetPersonel == null)
                return RedirectToAction(nameof(StoreConversations), new { storeId });

            // Must belong to this store
            if (targetPersonel.StoreID != storeId.Value)
                return RedirectToAction(nameof(StoreConversations), new { storeId });

            var targetUserId = targetPersonel.UserID;

            // Find an existing ACTIVE convo BETWEEN the same users, BUT ONLY if it is NOT closed.
            var existing = await _db.Conversations
                .Where(c =>
                    (
                        (c.StarterUserID == currentUserId && c.TargetUserID == targetUserId && !c.IsDeletedByStarter) ||
                        (c.StarterUserID == targetUserId && c.TargetUserID == currentUserId && !c.IsDeletedByTarget)
                    )
                )
                .Select(c => new
                {
                    Convo = c,
                    IsClosed = _db.ConversationMessages.Any(m => m.ConversationID == c.ConversationID && m.Text == SystemClosedText)
                })
                .FirstOrDefaultAsync(x => !x.IsClosed);

            Conversation convo;
            if (existing == null)
            {
                // Create a fresh conversation if none exists OR all existing ones are closed.
                convo = new Conversation
                {
                    StarterUserID = currentUserId,
                    TargetUserID = targetUserId,
                    CreatedAt = DateTime.UtcNow,
                    IsDeletedByStarter = false,
                    IsDeletedByTarget = false
                };

                _db.Conversations.Add(convo);
                await _db.SaveChangesAsync();
            }
            else
            {
                convo = existing.Convo;
            }

            var msg = new ConversationMessage
            {
                ConversationID = convo.ConversationID,
                SenderUserID = currentUserId,
                Text = firstMessage.Trim(),
                SentAt = DateTime.UtcNow
            };

            _db.ConversationMessages.Add(msg);
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(StoreConversations),
                new { storeId, id = convo.ConversationID });
        }


        public async Task<IActionResult> SendConversationMessage(int conversationId, int personelId, string message, int? storeId)
        {
            const string SystemClosedText = "Karşı taraf sohbeti kapattı.";

            if (conversationId == 0 || string.IsNullOrWhiteSpace(message))
                return RedirectToAction(nameof(StoreConversations), new { storeId, id = conversationId });

            var convo = await _db.Conversations
                .FirstOrDefaultAsync(c => c.ConversationID == conversationId);

            if (convo == null)
            {
                TempData["ConvError"] = "Konuşma bulunamadı.";
                return RedirectToAction(nameof(StoreConversations), new { storeId });
            }

            var isClosed = await _db.ConversationMessages
               .AnyAsync(m => m.ConversationID == conversationId && m.Text == SystemClosedText);

            if (isClosed)
            {
                TempData["ConvError"] = "Bu sohbet kapatıldığı için mesaj gönderemezsiniz.";
                return RedirectToAction(nameof(StoreConversations), new { storeId, id = conversationId });
            }

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var msg = new ConversationMessage
            {
                ConversationID = conversationId,
                SenderUserID = currentUserId,
                Text = message.Trim(),
                SentAt = DateTime.UtcNow
            };

            _db.ConversationMessages.Add(msg);
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(StoreConversations),
                new { storeId, id = conversationId });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConversation(int conversationId, int? storeId)
        {
            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var convo = await _db.Conversations
                .FirstOrDefaultAsync(c => c.ConversationID == conversationId);

            if (convo == null)
                return RedirectToAction(nameof(StoreConversations), new { storeId });

            // (optional) make sure the current user is part of this convo
            if (convo.StarterUserID != currentUserId && convo.TargetUserID != currentUserId)
                return Forbid();

            bool changed = false;

            if (convo.StarterUserID == currentUserId && !convo.IsDeletedByStarter)
            {
                convo.IsDeletedByStarter = true;
                changed = true;
            }
            else if (convo.TargetUserID == currentUserId && !convo.IsDeletedByTarget)
            {
                convo.IsDeletedByTarget = true;
                changed = true;
            }

            if (changed)
            {
                var noticeText = "Karşı taraf sohbeti kapattı.";

                _db.ConversationMessages.Add(new ConversationMessage
                {
                    ConversationID = conversationId,
                    SenderUserID = currentUserId,
                    Text = noticeText,
                    SentAt = DateTime.UtcNow
                });

                await _audit.LogAsync(
                    $"[Owner] Conversation soft-deleted. ConversationID={conversationId}, UserID={currentUserId}",
                    HttpContext);
            }

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(StoreConversations), new { storeId });
        }





        // -------------------------------------------------
        //  PERMISSIONS HELPER FOR OWNER
        // -------------------------------------------------
        private async Task<bool> UserOwnsCurrentStoreAsync()
        {
            var storeId = _storeAccessor.CurrentStoreId;
            if (storeId == 0)
                return false;

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out var userId))
                return false;

            return await _db.StoreOwners.AnyAsync(o =>
                o.UserID == userId &&
                o.StoreID == storeId &&
                o.IsActive);
        }

        private async Task<bool> UserOwnsStoreAsync(int storeId)
        {
            if (storeId == 0) return false;

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out var userId))
                return false;

            return await _db.StoreOwners.AnyAsync(o =>
                o.UserID == userId &&
                o.StoreID == storeId &&
                o.IsActive);
        }


        // -------------------------------------------------
        //  FILE SAVE HELPER (items)
        // -------------------------------------------------
        private async Task<string?> SaveItemPicture(IFormFile? file)
        {
            if (file == null || file.Length == 0)
                return null;

            var uploads = Path.Combine(_env.WebRootPath, "img", "products");
            Directory.CreateDirectory(uploads);

            var ext = Path.GetExtension(file.FileName);
            var filename = $"{Guid.NewGuid()}{ext}";
            var fullPath = Path.Combine(uploads, filename);

            using (var stream = System.IO.File.Create(fullPath))
                await file.CopyToAsync(stream);

            return $"/img/products/{filename}";
        }

        // -------------------------------------------------
        //  STORE ROLES
        // -------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> StoreRoles(int? storeId, int? id, bool newRole = false)
        {
            var store = await EnsureStoreContextAsync(storeId);
            if (store == null)
                return RedirectToAction(nameof(Dashboard));

            await EnsureManagerRoleForStoreAsync(store.StoreID);

            var vm = new RoleManagementVm();

            vm.Roles = await _db.Roles
                .Where(r => r.StoreID == store.StoreID)
                .OrderBy(r => r.HierarchyLevel)
                .ThenBy(r => r.RoleName)
                .Select(r => new RoleSummaryVm
                {
                    RoleID = r.RoleID,
                    RoleName = r.RoleName,
                    IsSystem = r.IsSystem
                })
                .ToListAsync();

            if (newRole)
            {
                vm.SelectedRole = new RoleEditVm
                {
                    Permissions = await _db.Permissions
                        .OrderBy(p => p.DisplayName)
                        .Select(p => new PermissionCheckboxVm
                        {
                            PermissionID = p.PermissionID,
                            PermissionName = p.DisplayName,
                            Granted = false
                        })
                        .ToListAsync()
                };
            }
            else
            {
                var selectedRoleId = id ?? vm.Roles.FirstOrDefault()?.RoleID;

                vm.SelectedRole = selectedRoleId.HasValue
                    ? await BuildOwnerRoleEditVm(store.StoreID, selectedRoleId.Value)
                    : null;
            }

            return View("StoreRoles", vm);
        }

        private async Task<RoleEditVm> BuildOwnerRoleEditVm(int storeId, int roleId)
        {
            var role = await _db.Roles
                .Include(r => r.RolePermissions)
                .Include(r => r.PersonnelNumberCodes)
                .FirstAsync(r => r.RoleID == roleId && r.StoreID == storeId);

            var allPerms = await _db.Permissions
                .OrderBy(p => p.DisplayName)
                .ToListAsync();

            return new RoleEditVm
            {
                RoleID = role.RoleID,
                RoleName = role.RoleName,
                StartTime = role.StartTime,
                ExitTime = role.ExitTime,
                IsSystem = role.IsSystem,
                Permissions = allPerms.Select(p => new PermissionCheckboxVm
                {
                    PermissionID = p.PermissionID,
                    PermissionName = p.DisplayName,
                    Granted = role.RolePermissions.Any(rp => rp.PermissionID == p.PermissionID)
                }).ToList(),
                PersonnelCodes = role.PersonnelNumberCodes
                    .Where(c => c.StoreID == storeId)
                    .OrderByDescending(c => c.CreatedAt)
                    .Select(c => new PersonnelNumberCodeVm
                    {
                        PersonnelNumberCodeID = c.PersonnelNumberCodeID,
                        Code = c.Code,
                        IsUsed = c.IsUsed,
                        CreatedAt = c.CreatedAt,
                        UsedAt = c.UsedAt
                    }).ToList()
            };
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StoreSaveRole(RoleManagementVm pageVm)
        {
            var store = await EnsureStoreContextAsync();
            if (store == null)
                return RedirectToAction(nameof(Dashboard));

            var vm = pageVm.SelectedRole;
            if (vm == null)
                return RedirectToAction(nameof(StoreRoles), new { storeId = store.StoreID });

            // CREATE
            if (vm.RoleID == null)
            {
                if (string.IsNullOrWhiteSpace(vm.RoleName))
                {
                    TempData["RoleError"] = "Rol adı gerekli.";
                    return RedirectToAction(nameof(StoreRoles), new { storeId = store.StoreID, newRole = true });
                }

                var role = new Role
                {
                    StoreID = store.StoreID,
                    RoleName = vm.RoleName,
                    StartTime = vm.StartTime,
                    ExitTime = vm.ExitTime,
                    IsSystem = false,
                    HierarchyLevel = 50
                };

                foreach (var p in vm.Permissions.Where(p => p.Granted))
                {
                    role.RolePermissions.Add(new RolePermission
                    {
                        PermissionID = p.PermissionID
                    });
                }

                _db.Roles.Add(role);
                await _db.SaveChangesAsync();

                await _audit.LogAsync(
                    $"[Owner] Role created. RoleID={role.RoleID}, StoreID={store.StoreID}, Name={role.RoleName}",
                    HttpContext);

                return RedirectToAction(nameof(StoreRoles), new { storeId = store.StoreID, id = role.RoleID });
            }
            else
            {
                // UPDATE
                var role = await _db.Roles
                    .Include(r => r.RolePermissions)
                    .FirstOrDefaultAsync(r => r.RoleID == vm.RoleID.Value && r.StoreID == store.StoreID);

                if (role == null)
                {
                    TempData["RoleError"] = "Rol bulunamadı veya bu mağazaya ait değil.";
                    return RedirectToAction(nameof(StoreRoles), new { storeId = store.StoreID });
                }

                role.RoleName = vm.RoleName;
                role.StartTime = vm.StartTime;
                role.ExitTime = vm.ExitTime;

                // permissions reset
                role.RolePermissions.Clear();
                foreach (var p in vm.Permissions.Where(p => p.Granted))
                {
                    role.RolePermissions.Add(new RolePermission
                    {
                        RoleID = role.RoleID,
                        PermissionID = p.PermissionID
                    });
                }

                await _db.SaveChangesAsync();

                await _audit.LogAsync(
                    $"[Owner] Role updated. RoleID={role.RoleID}, StoreID={store.StoreID}, Name={role.RoleName}, IsSystem={role.IsSystem}",
                    HttpContext);

                return RedirectToAction(nameof(StoreRoles), new { storeId = store.StoreID, id = role.RoleID });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StoreDeleteRole(int id)
        {
            var store = await EnsureStoreContextAsync();
            if (store == null)
                return RedirectToAction(nameof(Dashboard));

            var role = await _db.Roles
                .FirstOrDefaultAsync(r => r.RoleID == id && r.StoreID == store.StoreID);

            if (role == null || role.IsSystem)
            {
                TempData["RoleError"] = "Bu rol silinemez.";
                return RedirectToAction(nameof(StoreRoles), new { storeId = store.StoreID, id });
            }

            _db.Roles.Remove(role);
            await _db.SaveChangesAsync();

            await _audit.LogAsync(
                $"[Owner] Role deleted. RoleID={id}, StoreID={store.StoreID}, Name={role.RoleName}",
                HttpContext);

            return RedirectToAction(nameof(StoreRoles), new { storeId = store.StoreID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePersonnelFromCurrentStore(int personelId)
        {
            if (!await UserOwnsCurrentStoreAsync())
                return Forbid();

            // You are deleting the currently selected "membership row"
            var current = await _db.Personels.FirstOrDefaultAsync(p => p.PersonelID == personelId);
            if (current == null)
            {
                TempData["PersonnelDeleteError"] = "Personel kaydı bulunamadı.";
                return RedirectToAction(nameof(StorePersonnel));
            }

            // Find how many store-memberships this same user has
            var membershipCount = await _db.Personels.CountAsync(p => p.UserID == current.UserID);

            // IMPORTANT: If only one store, DON'T delete. Show error.
            if (membershipCount <= 1)
            {
                TempData["PersonnelDeleteError"] = "Bu personel sadece 1 mağazada kayıtlı. Tamamen silmek için önce başka mağazaya ekleyin veya pasifleştirin.";
                return RedirectToAction(nameof(StorePersonnel), new { id = personelId });
            }

            // Delete PersonnelNumberCode for THIS store + this personnel number
            var codeRow = await _db.PersonnelNumberCodes
                .FirstOrDefaultAsync(c => c.StoreID == current.StoreID && c.Code == current.PersonelNumber);

            if (codeRow != null)
                _db.PersonnelNumberCodes.Remove(codeRow);

            // Cleanup dependent records tied to THIS PersonelID (this store membership row)
            _db.PersonelHistories.RemoveRange(_db.PersonelHistories.Where(x => x.PersonelID == current.PersonelID));
            _db.PersonelEducations.RemoveRange(_db.PersonelEducations.Where(x => x.PersonelID == current.PersonelID));
            _db.PersonelCertificates.RemoveRange(_db.PersonelCertificates.Where(x => x.PersonelID == current.PersonelID));
            _db.PersonelWarnings.RemoveRange(_db.PersonelWarnings.Where(x => x.PersonelID == current.PersonelID));
            _db.PersonelNotes.RemoveRange(_db.PersonelNotes.Where(x => x.PersonelID == current.PersonelID));
            _db.PersonelShifts.RemoveRange(_db.PersonelShifts.Where(x => x.PersonelID == current.PersonelID));
            _db.PersonelTimeOffs.RemoveRange(_db.PersonelTimeOffs.Where(x => x.PersonelID == current.PersonelID));
            _db.PersonnelQuickNotes.RemoveRange(_db.PersonnelQuickNotes.Where(x => x.PersonelID == current.PersonelID));
            _db.PersonnelDailyChecklistStates.RemoveRange(_db.PersonnelDailyChecklistStates.Where(x => x.PersonelID == current.PersonelID));
            _db.MessagesDirectly.RemoveRange(_db.MessagesDirectly.Where(x => x.PersonelID == current.PersonelID));
            _db.SuspiciousActivityDatabase.RemoveRange(_db.SuspiciousActivityDatabase.Where(x => x.PersonelID == current.PersonelID));
            _db.SuspiciousPeopleDatabase.RemoveRange(_db.SuspiciousPeopleDatabase.Where(x => x.PersonelID == current.PersonelID));
            _db.ReportCreation.RemoveRange(_db.ReportCreation.Where(x => x.PersonelID == current.PersonelID));

            // Delete membership row
            _db.Personels.Remove(current);

            await _db.SaveChangesAsync();

            TempData["PersonnelDeleteOk"] = "Personel bu mağazadan silindi.";
            return RedirectToAction(nameof(StorePersonnel)); // no id => prevents selecting a deleted record
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemovePersonnelFromStore(int sourcePersonelId, int targetStoreId)
        {
            if (!await UserOwnsCurrentStoreAsync())
                return Forbid();

            // Find the "source" record (the one you're currently editing)
            var source = await _db.Personels.FirstOrDefaultAsync(p => p.PersonelID == sourcePersonelId);
            if (source == null)
            {
                TempData["PersonnelStoreRemoveError"] = "Personel bulunamadı.";
                return RedirectToAction(nameof(StorePersonnel));
            }

            // Find the membership in the target store (same user, different store row)
            var membership = await _db.Personels
                .FirstOrDefaultAsync(p => p.UserID == source.UserID && p.StoreID == targetStoreId);

            if (membership == null)
            {
                TempData["PersonnelStoreRemoveError"] = "Bu mağaza için personel kaydı bulunamadı.";
                TempData["ReopenAssignStoreModal"] = "1";
                return RedirectToAction(nameof(StorePersonnel), new { id = sourcePersonelId });
            }

            // Must own the target store too (important: modal lists only owned stores, but enforce server-side)
            var ownerIdStr = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
            int.TryParse(ownerIdStr, out var ownerUserId);

            var ownsTargetStore = await _db.StoreOwners
                .AnyAsync(o => o.UserID == ownerUserId && o.StoreID == targetStoreId && o.IsActive);

            if (!ownsTargetStore)
                return Forbid();

            // If the user only exists in ONE store, do NOT allow removal
            var membershipCount = await _db.Personels.CountAsync(p => p.UserID == source.UserID);
            if (membershipCount <= 1)
            {
                TempData["PersonnelStoreRemoveError"] = "Personel sadece 1 mağazada kayıtlı. Son mağazadan silinemez.";
                TempData["ReopenAssignStoreModal"] = "1";
                return RedirectToAction(nameof(StorePersonnel), new { id = sourcePersonelId });
            }

            // Delete the personnel number code for THAT STORE too (your request)
            // match Code + StoreID
            var codeRow = await _db.PersonnelNumberCodes
                .FirstOrDefaultAsync(c => c.StoreID == membership.StoreID && c.Code == membership.PersonelNumber);

            if (codeRow != null)
                _db.PersonnelNumberCodes.Remove(codeRow);

            // Delete dependent records (safe cleanup; prevents FK issues)
            _db.PersonelHistories.RemoveRange(_db.PersonelHistories.Where(x => x.PersonelID == membership.PersonelID));
            _db.PersonelEducations.RemoveRange(_db.PersonelEducations.Where(x => x.PersonelID == membership.PersonelID));
            _db.PersonelCertificates.RemoveRange(_db.PersonelCertificates.Where(x => x.PersonelID == membership.PersonelID));
            _db.PersonelWarnings.RemoveRange(_db.PersonelWarnings.Where(x => x.PersonelID == membership.PersonelID));
            _db.PersonelNotes.RemoveRange(_db.PersonelNotes.Where(x => x.PersonelID == membership.PersonelID));
            _db.PersonelShifts.RemoveRange(_db.PersonelShifts.Where(x => x.PersonelID == membership.PersonelID));
            _db.PersonelTimeOffs.RemoveRange(_db.PersonelTimeOffs.Where(x => x.PersonelID == membership.PersonelID));
            _db.PersonnelQuickNotes.RemoveRange(_db.PersonnelQuickNotes.Where(x => x.PersonelID == membership.PersonelID));
            _db.PersonnelDailyChecklistStates.RemoveRange(_db.PersonnelDailyChecklistStates.Where(x => x.PersonelID == membership.PersonelID));
            _db.MessagesDirectly.RemoveRange(_db.MessagesDirectly.Where(x => x.PersonelID == membership.PersonelID));
            _db.SuspiciousActivityDatabase.RemoveRange(_db.SuspiciousActivityDatabase.Where(x => x.PersonelID == membership.PersonelID));
            _db.SuspiciousPeopleDatabase.RemoveRange(_db.SuspiciousPeopleDatabase.Where(x => x.PersonelID == membership.PersonelID));
            _db.ReportCreation.RemoveRange(_db.ReportCreation.Where(x => x.PersonelID == membership.PersonelID));

            // Remove the membership itself
            _db.Personels.Remove(membership);

            await _db.SaveChangesAsync();

            TempData["PersonnelStoreRemoveOk"] = "Personel bu mağazadan kaldırıldı.";

            // If you removed the record you're currently viewing (same PersonelID), don't redirect to its id
            if (membership.PersonelID == sourcePersonelId)
                return RedirectToAction(nameof(StorePersonnel));

            return RedirectToAction(nameof(StorePersonnel), new { id = sourcePersonelId });
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StoreGeneratePersonnelNumber(int roleId)
        {
            var store = await EnsureStoreContextAsync();
            if (store == null)
                return RedirectToAction(nameof(Dashboard));

            var role = await _db.Roles
                .FirstOrDefaultAsync(r => r.RoleID == roleId && r.StoreID == store.StoreID);

            if (role == null)
            {
                TempData["RoleError"] = "Rol bulunamadı veya bu mağazaya ait değil.";
                return RedirectToAction(nameof(StoreRoles), new { storeId = store.StoreID });
            }

            string code;
            bool exists;

            if (role.RoleName == "Manager" && role.IsSystem)
            {
                do
                {
                    code = GenerateManagerInviteCode();
                    exists = await _db.PersonnelNumberCodes
                        .AnyAsync(p => p.Code == code);
                } while (exists);
            }
            else
            {
                do
                {
                    code = GenerateInviteCode();
                    exists = await _db.PersonnelNumberCodes
                        .AnyAsync(p => p.Code == code);
                } while (exists);
            }

            var pn = new PersonnelNumberCode
            {
                RoleID = roleId,
                Code = code,
                IsUsed = false,
                CreatedAt = DateTime.UtcNow,
                StoreID = store.StoreID
            };

            _db.PersonnelNumberCodes.Add(pn);
            await _db.SaveChangesAsync();

            TempData["LastGeneratedCode"] = code;

            await _audit.LogAsync(
                $"[Owner] Personnel number generated. RoleID={roleId}, StoreID={store.StoreID}, Code={code}",
                HttpContext);

            return RedirectToAction(nameof(StoreRoles), new { storeId = store.StoreID, id = roleId });
        }


        private string GenerateInviteCode()
        {
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            var bytes = new byte[4];
            rng.GetBytes(bytes);
            var num = BitConverter.ToUInt32(bytes, 0) % 100000000;
            return num.ToString("D8");
        }

        private string GenerateManagerInviteCode()
        {
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            var bytes = new byte[4];
            rng.GetBytes(bytes);
            var num = BitConverter.ToUInt32(bytes, 0) % 10000000;
            return "M" + num.ToString("D7");
        }

        private async Task EnsureManagerRoleForStoreAsync(int storeId)
        {
            // If this store already has a system Manager role, nothing to do.
            var existing = await _db.Roles
                .Include(r => r.RolePermissions)
                .FirstOrDefaultAsync(r =>
                    r.StoreID == storeId &&
                    r.RoleName == "Manager" &&
                    r.IsSystem);

            if (existing != null)
                return;

            // Try to use an existing Manager role as a template (example: from another store)
            var template = await _db.Roles
                .Include(r => r.RolePermissions)
                .Where(r => r.IsSystem && r.RoleName == "Manager")
                .OrderBy(r => r.StoreID)
                .FirstOrDefaultAsync();

            Role newRole;

            if (template != null)
            {
                newRole = new Role
                {
                    StoreID = storeId,
                    RoleName = template.RoleName,
                    StartTime = template.StartTime,
                    ExitTime = template.ExitTime,
                    HierarchyLevel = template.HierarchyLevel,
                    IsSystem = true
                };

                // Copy permissions
                foreach (var rp in template.RolePermissions)
                {
                    newRole.RolePermissions.Add(new RolePermission
                    {
                        PermissionID = rp.PermissionID
                    });
                }
            }
            else
            {
                // Fallback defaults if no template exists
                newRole = new Role
                {
                    StoreID = storeId,
                    RoleName = "Manager",
                    StartTime = new TimeSpan(12, 0, 0),
                    ExitTime = new TimeSpan(18, 0, 0),
                    HierarchyLevel = 10,
                    IsSystem = true
                };
            }

            _db.Roles.Add(newRole);
            await _db.SaveChangesAsync();
        }

        [HttpGet]
        public async Task<IActionResult> StoreSettings(int? storeId)
        {
            var store = await EnsureStoreContextAsync(storeId);
            if (store == null)
                return RedirectToAction(nameof(Dashboard));

            var vm = new OwnerEditStoreVm
            {
                StoreID = store.StoreID,
                StoreName = store.StoreName,
                StoreDescription = store.StoreDescription,
                StoreAddress = store.StoreAddress,
                StoreOpenedAt = store.StoreOpenedAt,
                IsActive = store.IsActive,
                ExistingPictureLink = store.StorePictureLink
            };

            return View("StoreSettings", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StoreSettings(OwnerEditStoreVm vm)
        {
            var store = await EnsureStoreContextAsync(vm.StoreID);
            if (store == null)
                return RedirectToAction(nameof(Dashboard));

            if (!ModelState.IsValid)
            {
                // stay on same page with validation messages
                return View("StoreSettings", vm);
            }

            // upload new picture if provided
            var newPicUrl = await SaveStorePictureAsync(vm.StorePictureFile);

            // update fields
            store.StoreName = vm.StoreName;
            store.StoreDescription = vm.StoreDescription;
            store.StoreAddress = vm.StoreAddress;

            if (vm.StoreOpenedAt.HasValue)
                store.StoreOpenedAt = vm.StoreOpenedAt.Value;

            store.IsActive = vm.IsActive;

            if (!string.IsNullOrEmpty(newPicUrl))
            {
                var oldPic = store.StorePictureLink;
                store.StorePictureLink = newPicUrl;

                if (!string.IsNullOrWhiteSpace(oldPic))
                {
                    DeletePhysicalFile(oldPic);
                }
            }

            await _db.SaveChangesAsync();

            TempData["StoreSettingsSaved"] = "Mağaza ayarları kaydedildi.";
            return RedirectToAction(nameof(StoreSettings), new { storeId = store.StoreID });
        }

        private sealed class PersonnelProfileDocument : IDocument
        {
            private readonly PersonnelEditVm _m;
            private readonly string? _profileImagePath;

            public PersonnelProfileDocument(PersonnelEditVm model, string? profileImagePath)
            {
                _m = model;
                _profileImagePath = profileImagePath;
            }

            public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

            public void Compose(IDocumentContainer container)
            {
                container.Page(page =>
                {
                    page.Margin(40);
                    page.Size(PageSizes.A4);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(11));

                    page.Header().Element(ComposeHeader);

                    page.Content().Column(col =>
                    {
                        col.Spacing(10);

                        // basic info
                        col.Item().Text(t =>
                        {
                            t.Span("Ad Soyad: ").SemiBold();
                            t.Span($"{_m.FirstName} {_m.LastName}".Trim());
                        });

                        col.Item().Text(t =>
                        {
                            t.Span("Personel Numarası: ").SemiBold();
                            t.Span(_m.PersonelNumber);
                        });

                        col.Item().Text(t =>
                        {
                            t.Span("T.C. Kimlik No: ").SemiBold();
                            t.Span(string.IsNullOrWhiteSpace(_m.TcKimlikNo) ? "-" : _m.TcKimlikNo);
                        });

                        col.Item().Text(t =>
                        {
                            t.Span("E-posta: ").SemiBold();
                            t.Span(string.IsNullOrWhiteSpace(_m.UserEmail) ? "-" : _m.UserEmail);
                        });

                        col.Item().Text(t =>
                        {
                            t.Span("Doğum Tarihi: ").SemiBold();
                            t.Span(_m.BirthDate?.ToString("dd.MM.yyyy") ?? "-");
                        });

                        col.Item().Text(t =>
                        {
                            t.Span("İşe Girdiği Tarih: ").SemiBold();
                            t.Span(_m.HireDate?.ToString("dd.MM.yyyy") ?? "-");
                        });
                        col.Item().Text(t =>
                        {
                            t.Span("Maaş: ").SemiBold();

                            if (_m.Salary.HasValue)
                            {
                                // (1.234,56)
                                var tr = new CultureInfo("tr-TR");
                                t.Span(_m.Salary.Value.ToString("N2", tr) + " ₺");
                            }
                            else
                            {
                                t.Span("-");
                            }
                        });

                        // histories
                        if (_m.Histories.Any())
                        {
                            col.Item().Element(e =>
                            {
                                e.PaddingTop(15).Text("İş Geçmişi").SemiBold().FontSize(14);
                            });

                            col.Item().Table(table =>
                            {
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.RelativeColumn(2); // Başlık
                                    cols.RelativeColumn(1); // Tarihler
                                    cols.RelativeColumn(3); // Açıklama
                                });

                                table.Header(h =>
                                {
                                    h.Cell().Element(CellHeader).Text("Pozisyon");
                                    h.Cell().Element(CellHeader).Text("Tarih");
                                    h.Cell().Element(CellHeader).Text("Açıklama");
                                });

                                foreach (var h in _m.Histories.OrderByDescending(x => x.Start))
                                {
                                    table.Cell().Element(CellBody).Text(h.Title ?? "-");
                                    table.Cell().Element(CellBody).Text(
                                        $"{h.Start:dd.MM.yyyy}" +
                                        (h.End.HasValue ? $" - {h.End:dd.MM.yyyy}" : "")
                                    );
                                    table.Cell().Element(CellBody).Text(h.Description ?? "");
                                }
                            });
                        }

                        // educations
                        if (_m.Educations.Any())
                        {
                            col.Item().Element(e =>
                            {
                                e.PaddingTop(15).Text("Eğitim Geçmişi").SemiBold().FontSize(14);
                            });

                            col.Item().Table(table =>
                            {
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.RelativeColumn(2); // Okul
                                    cols.RelativeColumn(1); // Tarih
                                    cols.RelativeColumn(3); // Açıklama
                                });

                                table.Header(h =>
                                {
                                    h.Cell().Element(CellHeader).Text("Okul");
                                    h.Cell().Element(CellHeader).Text("Tarih");
                                    h.Cell().Element(CellHeader).Text("Açıklama");
                                });

                                foreach (var e in _m.Educations.OrderByDescending(x => x.Start))
                                {
                                    table.Cell().Element(CellBody).Text(e.School ?? "-");
                                    table.Cell().Element(CellBody).Text(
                                        (e.Start != default ? e.Start.ToString("dd.MM.yyyy") : "") +
                                        (e.End.HasValue ? $" - {e.End:dd.MM.yyyy}" : "")
                                    );
                                    table.Cell().Element(CellBody).Text(e.Description ?? "");
                                }
                            });
                        }

                        // certificates
                        if (_m.Certificates.Any())
                        {
                            col.Item().Element(e =>
                            {
                                e.PaddingTop(15).Text("Sertifikalar").SemiBold().FontSize(14);
                            });

                            col.Item().Table(table =>
                            {
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.RelativeColumn(2); // Sertifika
                                    cols.RelativeColumn(1); // Tarih
                                    cols.RelativeColumn(3); // Açıklama
                                });

                                table.Header(h =>
                                {
                                    h.Cell().Element(CellHeader).Text("Sertifika");
                                    h.Cell().Element(CellHeader).Text("Tarih");
                                    h.Cell().Element(CellHeader).Text("Açıklama");
                                });

                                foreach (var c in _m.Certificates.OrderByDescending(x => x.Start))
                                {
                                    table.Cell().Element(CellBody).Text(c.CertificateName ?? "-");

                                    var dateStr = (c.Start != default ? c.Start.ToString("dd.MM.yyyy") : "");
                                    if (c.End.HasValue)
                                        dateStr += $" - {c.End:dd.MM.yyyy}";
                                    if (!string.IsNullOrWhiteSpace(c.Place))
                                        dateStr += $"  •  {c.Place}";

                                    table.Cell().Element(CellBody).Text(dateStr);
                                    table.Cell().Element(CellBody).Text(c.Description ?? "");
                                }
                            });
                        }

                        // warnings summary
                        if (_m.Warnings.Any())
                        {
                            col.Item().Element(e =>
                            {
                                e.PaddingTop(15).Text("Uyarılar").SemiBold().FontSize(14);
                            });

                            col.Item().Table(table =>
                            {
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.RelativeColumn(1); // Tarih
                                    cols.RelativeColumn(4); // Metin
                                    cols.RelativeColumn(1); // Seviye
                                    cols.RelativeColumn(2); // Veren
                                });

                                table.Header(h =>
                                {
                                    h.Cell().Element(CellHeader).Text("Tarih");
                                    h.Cell().Element(CellHeader).Text("Detay");
                                    h.Cell().Element(CellHeader).Text("Derece");
                                    h.Cell().Element(CellHeader).Text("Veren");
                                });

                                foreach (var w in _m.Warnings.OrderByDescending(x => x.Date))
                                {
                                    table.Cell().Element(CellBody).Text(w.Date.ToString("dd.MM.yyyy"));
                                    table.Cell().Element(CellBody).Text(w.WarningText ?? "");
                                    table.Cell().Element(CellBody).Text(
                                        w.Level == "danger" ? "Kritik" : "Uyarı"
                                    );
                                    table.Cell().Element(CellBody).Text(
                                        string.IsNullOrWhiteSpace(w.GivenBy) ? "-" : w.GivenBy
                                    );
                                }
                            });
                        }
                    });
                });
            }

            private void ComposeHeader(IContainer container)
            {
                container.Row(row =>
                {
                    // LEFT: title
                    row.RelativeColumn().Column(col =>
                    {
                        col.Item().Text($"Personel Bilgileri - {_m.FirstName} {_m.LastName}")
                            .SemiBold()
                            .FontSize(18)
                            .FontColor(Colors.Blue.Darken2);
                    });

                    // RIGHT: profile picture (if available)
                    if (!string.IsNullOrWhiteSpace(_profileImagePath) &&
                        System.IO.File.Exists(_profileImagePath))
                    {
                        row.ConstantColumn(80)
                           .AlignRight()
                           .AlignMiddle()
                           .Height(80)
                           .Width(80)
                           .Image(_profileImagePath)
                           .FitArea();
                    }
                });
            }

            private static IContainer CellHeader(IContainer container) =>
                container.PaddingVertical(5).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).DefaultTextStyle(x => x.SemiBold());

            private static IContainer CellBody(IContainer container) =>
                container.PaddingVertical(3);
        }

        public class ShiftApiDto
        {
            public int? Id { get; set; }
            public int PersonelId { get; set; }
            public DateTime Start { get; set; }
            public DateTime End { get; set; }
            public string? Color { get; set; }
        }

        // -------------------------------------------------
        //  SHIFTS (per store, owner view)
        // -------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> StoreShifts(int storeId)
        {
            var store = await EnsureStoreContextAsync(storeId);
            if (store == null)
                return RedirectToAction(nameof(Dashboard));

            if (storeId == 0)
            {
                return RedirectToAction("Dashboard", "Owner");
            }

            ViewBag.StoreId = storeId;

            var vm = new ShiftLandingVm
            {
                SelectedDate = DateTime.Today
            };

            vm.PersonelOptions = await _db.Personels
                .Include(p => p.Role)
                .Where(p => p.StoreID == storeId && p.Role.RoleName != "Manager")
                .OrderBy(p => p.PersonelName)
                .ThenBy(p => p.PersonelSurname)
                .Select(p => new SelectListItem
                {
                    Value = p.PersonelID.ToString(),
                    Text = (p.PersonelName + " " + p.PersonelSurname).Trim()
                })
                .ToListAsync();

            return View("StoreShifts", vm);
        }


        [HttpGet]
        public async Task<IActionResult> StoreShiftsDay(int storeId,DateTime? date,int? selectedPersonelId)
        {
            var store = await EnsureStoreContextAsync(storeId);
            if (store == null)
                return RedirectToAction(nameof(Dashboard));

            if (storeId == 0)
                return RedirectToAction("Dashboard", "Owner");

            ViewBag.StoreId = storeId;

            var day = (date ?? DateTime.Today).Date;
            var nextDay = day.AddDays(1);

            var personnel = await _db.Personels
                .AsNoTracking()
                .Include(p => p.Role)
                .Where(p => p.StoreID == storeId && p.Role.RoleName != "Manager")
                .OrderBy(p => p.PersonelName)
                .ThenBy(p => p.PersonelSurname)
                .ToListAsync();

            var personelIds = personnel.Select(p => p.PersonelID).ToList();

            var shifts = await _db.PersonelShifts
                .AsNoTracking()
                .Where(s =>
                    s.IsActive &&
                    personelIds.Contains(s.PersonelID) &&
                    s.ShiftDateStart < nextDay &&
                    s.ShiftDateEnd > day)
                .ToListAsync();

            var timeOffs = await _db.PersonelTimeOffs
                .AsNoTracking()
                .Where(t => personelIds.Contains(t.PersonelID) &&
                            t.DateStart <= day &&
                            t.DateEnd >= day)
                .ToListAsync();

            var vm = new ShiftDayVm
            {
                SelectedDate = day,
                Personnel = personnel.Select(p => new ShiftResourceVm
                {
                    PersonelID = p.PersonelID,
                    DisplayName = $"{p.PersonelName} {p.PersonelSurname}".Trim(),
                    RoleName = p.Role.RoleName
                }).ToList(),
                Shifts = new List<ShiftEventVm>()
            };

            // normal shifts
            vm.Shifts.AddRange(shifts.Select(s =>
            {
                var p = personnel.FirstOrDefault(x => x.PersonelID == s.PersonelID);
                var roleName = p?.Role?.RoleName ?? "";

                var color = string.IsNullOrWhiteSpace(s.ShiftColor)
                    ? "#e74c3c"
                    : s.ShiftColor;

                return new ShiftEventVm
                {
                    ShiftID = s.ShiftID,
                    PersonelID = s.PersonelID,
                    Start = s.ShiftDateStart,
                    End = s.ShiftDateEnd,
                    Text = string.IsNullOrWhiteSpace(roleName) ? "" : $"[{roleName}]",
                    Color = color
                };
            }));

            // full-day time-off blocks
            foreach (var t in timeOffs)
            {
                string izinTipi = t.TimeOffType switch
                {
                    "PaidLeave" => "Yıllık izin",
                    "SickLeave" => "Raporlu",
                    "UnpaidLeave" => "Ücretsiz izin",
                    "OffDay" => "İdari izin",
                    _ => "Diğer"
                };

                vm.Shifts.Add(new ShiftEventVm
                {
                    ShiftID = -t.TimeOffID,
                    PersonelID = t.PersonelID,
                    Start = day,
                    End = nextDay,
                    Text = $"[İZİN - {izinTipi}]",
                    Color = "#95a5a6"
                });
            }

            ViewBag.SelectedPersonelId = selectedPersonelId;

            return View("StoreShiftsDay", vm);
        }

        [HttpGet]
        public async Task<IActionResult> StoreCommentManagement(int storeId)
        {
            if (!await UserOwnsCurrentStoreAsync())
                return Forbid();

            var store = await _db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.StoreID == storeId);
            if (store == null) return NotFound();

            // Pending comments only for items in this store
            var pending = await _db.ItemComments
                .AsNoTracking()
                .Include(c => c.Item)
                .Where(c =>
                    c.ModerationStatus == CommentModerationStatus.Pending &&
                    c.PendingDescription != null &&
                    _db.InventoryItems.Any(ii => ii.ItemID == c.ItemID && ii.StoreID == storeId)
                )
                .OrderBy(c => c.CreatedAt)
                .Select(c => new DükkanBulutSitesi.Models.Manager.PendingCommentVm
                {
                    CommentID = c.CommentID,
                    ItemID = c.ItemID,
                    ItemName = c.Item!.ItemName,
                    PendingText = c.PendingDescription!,
                    CreatedAt = c.CreatedAt
                })
                .ToListAsync();

            // History from logs: only entries whose Item belongs to this store
            var logs = await _db.ComplaintActionLogs
                .AsNoTracking()
                .Include(l => l.PerformedByUser)
                .Where(l =>
                    l.CommentID != null &&
                    _db.InventoryItems.Any(ii =>
                        ii.StoreID == storeId &&
                        ii.ItemID == l.ItemID
                    )
                )
                .OrderByDescending(l => l.PerformedAt)
                .Select(l => new OwnerCommentLogVm
                {
                    PerformedAt = l.PerformedAt,
                    ManagerName = l.PerformedByUser.UserName,
                    Action = l.Action,
                    Notes = l.Notes ?? ""
                })
                .ToListAsync();

            var vm = new OwnerCommentManagementVm
            {
                StoreID = storeId,
                StoreName = store.StoreName,
                Pending = pending,
                Logs = logs.Select(x => new OwnerCommentLogVm
                {
                    Action = x.Action,
                    PerformedAt = x.PerformedAt,
                    ManagerName = x.ManagerName,
                    Notes = x.Notes ?? ""
                }).ToList()
            };

            ViewBag.StoreId = storeId;
            ViewBag.StoreName = store.StoreName;
            ViewBag.StoreDescription = store.StoreDescription;

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveCommentAsOwner(int commentId)
        {
            var storeId = _storeAccessor.CurrentStoreId;
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var comment = await _db.ItemComments
                .Include(c => c.Item)
                .FirstOrDefaultAsync(c => c.CommentID == commentId);

            if (comment == null)
                return NotFound();

            // ensure comment belongs to THIS store
            var allowed = await _db.InventoryItems.AnyAsync(ii =>
                ii.StoreID == storeId && ii.ItemID == comment.ItemID);

            if (!allowed)
                return Forbid();

            // snapshot BEFORE clearing
            var pendingText = comment.PendingDescription ?? "";

            // APPLY pending -> live
            comment.CommentDescription = pendingText.Length > 0 ? pendingText : comment.CommentDescription;
            comment.PendingDescription = null;

            comment.ModerationStatus = CommentModerationStatus.Approved;
            comment.IsDescriptionApproved = true;
            comment.ModerationDecidedAt = DateTime.UtcNow;

            // ✅ Human readable notes (same style as manager side)
            var itemName = (comment.Item?.ItemName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(itemName))
                itemName = $"#{comment.ItemID}";

            var text = (pendingText ?? "").Trim();
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " "); // normalize whitespace
            const int maxLen = 250;
            if (text.Length > maxLen) text = text[..maxLen] + "…";

            var notes = $"Ürün: {itemName} | Yorum: {text}";

            _db.ComplaintActionLogs.Add(new ComplaintActionLog
            {
                Action = "CommentApproved",
                CommentID = comment.CommentID,
                ItemID = comment.ItemID,
                PerformedByUserID = userId,
                PerformedAt = DateTime.UtcNow,
                Notes = notes
            });

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(StoreCommentManagement), new { storeId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectCommentAsOwner(int commentId)
        {
            var storeId = _storeAccessor.CurrentStoreId;
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var comment = await _db.ItemComments
                .Include(c => c.Item)
                .FirstOrDefaultAsync(c => c.CommentID == commentId);

            if (comment == null)
                return NotFound();

            var allowed = await _db.InventoryItems.AnyAsync(ii =>
                ii.StoreID == storeId && ii.ItemID == comment.ItemID);

            if (!allowed)
                return Forbid();

            // snapshot BEFORE clearing
            var rejectedText = comment.PendingDescription ?? "";

            comment.PendingDescription = null;
            comment.ModerationStatus = CommentModerationStatus.Rejected;
            comment.IsDescriptionApproved = false;
            comment.ModerationDecidedAt = DateTime.UtcNow;

            // ✅ Human readable notes (same style as manager side)
            var itemName = (comment.Item?.ItemName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(itemName))
                itemName = $"#{comment.ItemID}";

            var text = (rejectedText ?? "").Trim();
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
            const int maxLen = 250;
            if (text.Length > maxLen) text = text[..maxLen] + "…";

            var notes = $"Ürün: {itemName} | Yorum: {text}";

            _db.ComplaintActionLogs.Add(new ComplaintActionLog
            {
                Action = "CommentRejected",
                CommentID = comment.CommentID,
                ItemID = comment.ItemID,
                PerformedByUserID = userId,
                PerformedAt = DateTime.UtcNow,
                Notes = notes
            });

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(StoreCommentManagement), new { storeId });
        }




        [HttpGet]
        public async Task<IActionResult> StoreShiftsPersonWeek(int storeId,int selectedPersonelId,DateTime? selectedDate)
        {
            var store = await EnsureStoreContextAsync(storeId);
            if (store == null)
                return RedirectToAction(nameof(Dashboard));

            if (storeId == 0)
                return RedirectToAction("Dashboard", "Owner");

            ViewBag.StoreId = storeId;

            var allPersonnel = await _db.Personels
                .AsNoTracking()
                .Include(p => p.Role)
                .Where(p => p.StoreID == storeId && p.Role.RoleName != "Manager")
                .OrderBy(p => p.PersonelName)
                .ThenBy(p => p.PersonelSurname)
                .Select(p => new SelectListItem
                {
                    Value = p.PersonelID.ToString(),
                    Text = (p.PersonelName + " " + p.PersonelSurname).Trim()
                })
                .ToListAsync();

            ViewBag.PersonelOptions = allPersonnel;

            var personelId = selectedPersonelId;
            var anchor = (selectedDate ?? DateTime.Today).Date;

            var personel = await _db.Personels
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PersonelID == personelId && p.StoreID == storeId);

            if (personel == null)
                return NotFound();

            // ----- week calculation (Monday-based) -----
            var dow = (int)anchor.DayOfWeek;          // Sunday = 0
            var diff = (dow == 0 ? 6 : dow - 1);      // 0..6 from Monday
            var weekStart = anchor.AddDays(-diff);
            var weekEnd = weekStart.AddDays(6);

            // ----- shifts overlapping this week -----
            var shifts = await _db.PersonelShifts
                .AsNoTracking()
                .Where(s => s.PersonelID == personelId
                            && s.IsActive
                            && s.ShiftDateStart <= weekEnd
                            && s.ShiftDateEnd >= weekStart)
                .ToListAsync();

            // ----- time off overlapping this week -----
            var timeOffs = await _db.PersonelTimeOffs
                .AsNoTracking()
                .Where(t => t.PersonelID == personelId
                            && t.DateStart <= weekEnd
                            && t.DateEnd >= weekStart)
                .ToListAsync();

            var vm = new PersonelWeekShiftVm
            {
                PersonelID = personel.PersonelID,
                PersonelName = (personel.PersonelName + " " + personel.PersonelSurname).Trim(),
                WeekStart = weekStart,
                WeekEnd = weekEnd
            };

            for (int i = 0; i < 7; i++)
            {
                var day = weekStart.AddDays(i);

                var dayShifts = shifts
                    .Where(s => s.ShiftDateStart.Date <= day && s.ShiftDateEnd.Date >= day)
                    .OrderBy(s => s.ShiftDateStart)
                    .Select(s => new ShiftSummaryVm
                    {
                        ShiftID = s.ShiftID,
                        Start = s.ShiftDateStart,
                        End = s.ShiftDateEnd,
                        Color = s.ShiftColor
                    })
                    .ToList();

                var dayTimeOffs = timeOffs
                    .Where(t => t.DateStart <= day && t.DateEnd >= day)
                    .OrderBy(t => t.DateStart)
                    .Select(t => new TimeOffSummaryVm
                    {
                        TimeOffID = t.TimeOffID,
                        DateStart = t.DateStart,
                        DateEnd = t.DateEnd,
                        Type = t.TimeOffType,
                        IsPaid = t.IsPaid,
                        IsApproved = t.IsApproved,
                        Description = t.Description
                    })
                    .ToList();

                vm.Days.Add(new PersonelWeekDayVm
                {
                    Date = day,
                    Shifts = dayShifts,
                    TimeOffs = dayTimeOffs
                });
            }

            return View("StoreShiftsPersonWeek", vm);
        }

        [HttpGet]
        public async Task<IActionResult> StoreShiftsPersonWeekPdf(int storeId,int selectedPersonelId,DateTime? selectedDate)
        {
            var store = await EnsureStoreContextAsync(storeId);
            if (store == null)
                return RedirectToAction(nameof(Dashboard));

            if (storeId == 0)
                return RedirectToAction("Dashboard", "Owner");

            var personelId = selectedPersonelId;
            var anchor = (selectedDate ?? DateTime.Today).Date;

            var personel = await _db.Personels
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PersonelID == personelId && p.StoreID == storeId);

            if (personel == null)
                return NotFound();

            var dow = (int)anchor.DayOfWeek;
            var diff = (dow == 0 ? 6 : dow - 1);
            var weekStart = anchor.AddDays(-diff);
            var weekEnd = weekStart.AddDays(6);

            var shifts = await _db.PersonelShifts
                .AsNoTracking()
                .Where(s => s.PersonelID == personelId
                            && s.IsActive
                            && s.ShiftDateStart <= weekEnd
                            && s.ShiftDateEnd >= weekStart)
                .ToListAsync();

            var timeOffs = await _db.PersonelTimeOffs
                .AsNoTracking()
                .Where(t => t.PersonelID == personelId
                            && t.DateStart <= weekEnd
                            && t.DateEnd >= weekStart)
                .ToListAsync();

            var vm = new PersonelWeekShiftVm
            {
                PersonelID = personel.PersonelID,
                PersonelName = (personel.PersonelName + " " + personel.PersonelSurname).Trim(),
                WeekStart = weekStart,
                WeekEnd = weekEnd
            };

            for (int i = 0; i < 7; i++)
            {
                var day = weekStart.AddDays(i);

                var dayShifts = shifts
                    .Where(s => s.ShiftDateStart.Date <= day && s.ShiftDateEnd.Date >= day)
                    .OrderBy(s => s.ShiftDateStart)
                    .Select(s => new ShiftSummaryVm
                    {
                        ShiftID = s.ShiftID,
                        Start = s.ShiftDateStart,
                        End = s.ShiftDateEnd,
                        Color = s.ShiftColor
                    })
                    .ToList();

                var dayTimeOffs = timeOffs
                    .Where(t => t.DateStart <= day && t.DateEnd >= day)
                    .OrderBy(t => t.DateStart)
                    .Select(t => new TimeOffSummaryVm
                    {
                        TimeOffID = t.TimeOffID,
                        DateStart = t.DateStart,
                        DateEnd = t.DateEnd,
                        Type = t.TimeOffType,
                        IsPaid = t.IsPaid,
                        IsApproved = t.IsApproved,
                        Description = t.Description
                    })
                    .ToList();

                vm.Days.Add(new PersonelWeekDayVm
                {
                    Date = day,
                    Shifts = dayShifts,
                    TimeOffs = dayTimeOffs
                });
            }

            var personelNumber = personel.PersonelNumber;

            var document = new PersonelWeekShiftDocument(vm, personelNumber);
            var pdfBytes = document.GeneratePdf();

            var fileName = $"haftalik_vardiya_{personelNumber}_{weekStart:yyyyMMdd}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }



        [HttpGet]
        public async Task<IActionResult> StoreShiftsDayPdf(int storeId, DateTime? date)
        {
            var store = await EnsureStoreContextAsync(storeId);
            if (store == null)
                return RedirectToAction(nameof(Dashboard));

            if (storeId == 0)
                return RedirectToAction("Dashboard", "Owner");

            var day = (date ?? DateTime.Today).Date;
            var nextDay = day.AddDays(1);

            var personnel = await _db.Personels
                .Include(p => p.Role)
                .Where(p => p.StoreID == storeId && p.Role.RoleName != "Manager")
                .OrderBy(p => p.PersonelName)
                .ThenBy(p => p.PersonelSurname)
                .ToListAsync();

            var personelIds = personnel.Select(p => p.PersonelID).ToList();

            var shifts = await _db.PersonelShifts
                .Where(s =>
                    s.IsActive &&
                    personelIds.Contains(s.PersonelID) &&
                    s.ShiftDateStart < nextDay &&
                    s.ShiftDateEnd > day)
                .ToListAsync();

            var vm = new ShiftDayVm
            {
                SelectedDate = day,
                Personnel = personnel.Select(p => new ShiftResourceVm
                {
                    PersonelID = p.PersonelID,
                    DisplayName = $"{p.PersonelName} {p.PersonelSurname}".Trim(),
                    RoleName = p.Role.RoleName
                }).ToList(),
                Shifts = shifts.Select(s =>
                {
                    var p = personnel.FirstOrDefault(x => x.PersonelID == s.PersonelID);
                    var roleName = p?.Role?.RoleName ?? "";

                    var color = string.IsNullOrWhiteSpace(s.ShiftColor)
                        ? "#e74c3c"
                        : s.ShiftColor;

                    return new ShiftEventVm
                    {
                        ShiftID = s.ShiftID,
                        PersonelID = s.PersonelID,
                        Start = s.ShiftDateStart,
                        End = s.ShiftDateEnd,
                        Text = string.IsNullOrWhiteSpace(roleName) ? "" : $"[{roleName}]",
                        Color = color
                    };
                }).ToList()
            };

            var document = new ShiftDayDocument(vm);
            var pdfBytes = document.GeneratePdf();

            var fileName = $"vardiya_{day:yyyyMMdd}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }


        [HttpPost]
        public async Task<IActionResult> StoreCreateShift([FromBody] ShiftApiDto dto)
        {
            if (!await UserOwnsCurrentStoreAsync())
                return Forbid();

            if (dto.End <= dto.Start)
                return BadRequest("Bitiş saati başlangıçtan sonra olmalıdır.");

            var hasTimeOff = await _db.PersonelTimeOffs
                .AnyAsync(t => t.PersonelID == dto.PersonelId &&
                               t.DateStart <= dto.End.Date &&
                               t.DateEnd >= dto.Start.Date);

            if (hasTimeOff)
                return BadRequest("Bu personel seçilen tarihlerde izinli.");

            var shift = new PersonelShift
            {
                PersonelID = dto.PersonelId,
                ShiftDay = dto.Start.ToString("dddd", new CultureInfo("tr-TR")),
                ShiftDateStart = dto.Start,
                ShiftDateEnd = dto.End,
                IsActive = true,
                ShiftColor = string.IsNullOrWhiteSpace(dto.Color) ? "#e74c3c" : dto.Color
            };

            _db.PersonelShifts.Add(shift);
            await _db.SaveChangesAsync();

            return Json(new { id = shift.ShiftID });
        }

        [HttpPost]
        public async Task<IActionResult> StoreUpdateShift([FromBody] ShiftApiDto dto)
        {
            if (!await UserOwnsCurrentStoreAsync())
                return Forbid();

            if (!dto.Id.HasValue)
                return BadRequest("Missing ID.");

            var shift = await _db.PersonelShifts
                .FirstOrDefaultAsync(s => s.ShiftID == dto.Id.Value);
            if (shift == null)
                return NotFound();

            if (dto.End <= dto.Start)
                return BadRequest("End must be after start.");

            var hasTimeOff = await _db.PersonelTimeOffs
                .AnyAsync(t => t.PersonelID == dto.PersonelId &&
                               t.DateStart <= dto.End.Date &&
                               t.DateEnd >= dto.Start.Date);

            if (hasTimeOff)
                return BadRequest("This person has time off during the selected period.");

            shift.PersonelID = dto.PersonelId;
            shift.ShiftDay = dto.Start.ToString("dddd", new CultureInfo("tr-TR"));
            shift.ShiftDateStart = dto.Start;
            shift.ShiftDateEnd = dto.End;
            if (!string.IsNullOrWhiteSpace(dto.Color))
                shift.ShiftColor = dto.Color;

            await _db.SaveChangesAsync();

            return Json(new { ok = true });
        }

        [HttpPost]
        public async Task<IActionResult> StoreDeleteShift([FromBody] ShiftApiDto dto)
        {
            if (!await UserOwnsCurrentStoreAsync())
                return Forbid();

            if (!dto.Id.HasValue)
                return BadRequest("Missing ID.");

            var shift = await _db.PersonelShifts
                .FirstOrDefaultAsync(s => s.ShiftID == dto.Id.Value);
            if (shift == null)
                return NotFound();

            _db.PersonelShifts.Remove(shift);
            await _db.SaveChangesAsync();

            return Json(new { ok = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddPersonelTimeOff(int storeId,PersonelTimeOffCreateVm vm)
        {
            if (!await UserOwnsCurrentStoreAsync())
                return Forbid();

            if (vm.DateEnd < vm.DateStart)
            {
                TempData["TimeOffError"] = "End date must be after start date.";
                return RedirectToAction(nameof(StoreShiftsPersonWeek),
                    new { storeId, selectedPersonelId = vm.PersonelID, selectedDate = vm.AnchorDate });
            }

            vm.IsPaid = vm.TimeOffType != "UnpaidLeave";

            var entity = new PersonelTimeOff
            {
                PersonelID = vm.PersonelID,
                DateStart = vm.DateStart.Date,
                DateEnd = vm.DateEnd.Date,
                TimeOffType = vm.TimeOffType,
                IsPaid = vm.IsPaid,
                IsApproved = true,
                Description = vm.Description
            };

            _db.PersonelTimeOffs.Add(entity);

            var endPlus1 = vm.DateEnd.Date.AddDays(1);

            var overlappingShifts = await _db.PersonelShifts
                .Where(s => s.PersonelID == vm.PersonelID
                            && s.IsActive
                            && s.ShiftDateStart < endPlus1
                            && s.ShiftDateEnd > vm.DateStart.Date)
                .ToListAsync();

            foreach (var s in overlappingShifts)
            {
                s.IsActive = false;
            }

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(StoreShiftsPersonWeek),
                new { storeId, selectedPersonelId = vm.PersonelID, selectedDate = vm.AnchorDate });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePersonelTimeOff(int storeId,int timeOffId,int personelId,DateTime anchorDate)
        {
            if (!await UserOwnsCurrentStoreAsync())
                return Forbid();

            var entity = await _db.PersonelTimeOffs
                .FirstOrDefaultAsync(t => t.TimeOffID == timeOffId);

            if (entity != null)
            {
                _db.PersonelTimeOffs.Remove(entity);
                await _db.SaveChangesAsync();
            }

            return RedirectToAction(nameof(StoreShiftsPersonWeek),
                new { storeId, selectedPersonelId = personelId, selectedDate = anchorDate });
        }

        private sealed class ShiftDayDocument : IDocument
        {
            private readonly ShiftDayVm _m;
            private readonly Dictionary<int, ShiftResourceVm> _personById;

            public ShiftDayDocument(ShiftDayVm model)
            {
                _m = model;
                _personById = _m.Personnel.ToDictionary(p => p.PersonelID, p => p);
            }

            public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

            public void Compose(IDocumentContainer container)
            {
                container.Page(page =>
                {
                    page.Margin(40);
                    page.Size(PageSizes.A4.Landscape());
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header()
                        .Text($"Vardiya Planı - {_m.SelectedDate:dd.MM.yyyy}")
                        .SemiBold().FontSize(18).FontColor(Colors.Blue.Darken2);

                    page.Content().Column(col =>
                    {
                        col.Spacing(10);

                        col.Item().Text($"Toplam vardiya sayısı: {_m.Shifts.Count}");

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(2); // Personel
                                cols.RelativeColumn(2); // Rol
                                cols.RelativeColumn(1); // Başlangıç
                                cols.RelativeColumn(1); // Bitiş
                                cols.RelativeColumn(1); // Süre
                            });

                            table.Header(h =>
                            {
                                h.Cell().Element(CellHeader).Text("Personel");
                                h.Cell().Element(CellHeader).Text("Rol");
                                h.Cell().Element(CellHeader).Text("Başlangıç");
                                h.Cell().Element(CellHeader).Text("Bitiş");
                                h.Cell().Element(CellHeader).Text("Süre");
                            });

                            foreach (var s in _m.Shifts.OrderBy(x => x.Start))
                            {
                                _personById.TryGetValue(s.PersonelID, out var per);

                                var name = per?.DisplayName ?? $"#{s.PersonelID}";
                                var role = per?.RoleName ?? "";

                                var duration = s.End - s.Start;
                                var hours = Math.Round(duration.TotalHours, 2);

                                table.Cell().Element(CellBody).Text(name);
                                table.Cell().Element(CellBody).Text(role);
                                table.Cell().Element(CellBody).Text(s.Start.ToString("HH:mm"));
                                table.Cell().Element(CellBody).Text(s.End.ToString("HH:mm"));
                                table.Cell().Element(CellBody).Text($"{hours:0.##} saat");
                            }
                        });
                    });
                });
            }

            private static IContainer CellHeader(IContainer container) =>
                container.PaddingVertical(5).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).DefaultTextStyle(x => x.SemiBold());

            private static IContainer CellBody(IContainer container) =>
                container.PaddingVertical(3);
        }

        private sealed class PersonelWeekShiftDocument : IDocument
        {
            private readonly PersonelWeekShiftVm _m;
            private readonly string _personelNumber;

            public PersonelWeekShiftDocument(PersonelWeekShiftVm model, string personelNumber)
            {
                _m = model;
                _personelNumber = personelNumber ?? "";
            }

            public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

            public void Compose(IDocumentContainer container)
            {
                container.Page(page =>
                {
                    page.Margin(40);
                    page.Size(PageSizes.A4);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Element(ComposeHeader);

                    page.Content().Column(col =>
                    {
                        col.Spacing(10);

                        // Özet satırları
                        col.Item().Text(t =>
                        {
                            t.Span("Personel: ").SemiBold();
                            t.Span($"{_m.PersonelName}");
                        });

                        col.Item().Text(t =>
                        {
                            t.Span("Personel Numarası: ").SemiBold();
                            t.Span(string.IsNullOrWhiteSpace(_personelNumber) ? "-" : _personelNumber);
                        });

                        col.Item().Text(t =>
                        {
                            t.Span("Hafta: ").SemiBold();
                            t.Span($"{_m.WeekStart:dd.MM.yyyy} - {_m.WeekEnd:dd.MM.yyyy}");
                        });

                        var totalShifts = _m.Days.Sum(d => d.Shifts.Count);
                        var totalTimeOffDays = _m.Days.Count(d => d.TimeOffs.Any());

                        col.Item().Text(t =>
                        {
                            t.Span("Toplam vardiya sayısı: ").SemiBold();
                            t.Span(totalShifts.ToString());
                            t.Span("   •   İzin bulunan gün sayısı: ").SemiBold();
                            t.Span(totalTimeOffDays.ToString());
                        });

                        if (!_m.Days.Any(d => d.Shifts.Any()) && !_m.Days.Any(d => d.TimeOffs.Any()))
                        {
                            col.Item().Border(1).BorderColor(Colors.Orange.Medium)
                                .Padding(6)
                                .Background(Colors.Orange.Lighten5)
                                .Text($"Bu hafta için {_m.PersonelName} adına atanmış herhangi bir vardiya veya izin kaydı bulunamadı.");
                        }
                        else if (_m.Days.All(d => !d.Shifts.Any()) && _m.Days.Any(d => d.TimeOffs.Any()))
                        {
                            col.Item().Border(1).BorderColor(Colors.Blue.Medium)
                                .Padding(6)
                                .Background(Colors.Blue.Lighten5)
                                .Text($"Bu hafta için {_m.PersonelName} tamamen izinli görünüyor.");
                        }

                        // Asıl tablo
                        col.Item().Element(ComposeWeekTable);
                    });
                });
            }

            private void ComposeHeader(IContainer container)
            {
                container.Row(row =>
                {
                    row.RelativeColumn().Column(col =>
                    {
                        col.Item().Text($"Haftalık Vardiya Planı - {_m.PersonelName}")
                            .SemiBold()
                            .FontSize(18)
                            .FontColor(Colors.Blue.Darken2);

                        col.Item().Text($"{_m.WeekStart:dd.MM.yyyy} - {_m.WeekEnd:dd.MM.yyyy} haftası")
                            .FontSize(11)
                            .FontColor(Colors.Grey.Darken1);
                    });
                });
            }

            private void ComposeWeekTable(IContainer container)
            {
                container.Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(1.2f); // Gün
                        cols.RelativeColumn(1.0f); // Tarih
                        cols.RelativeColumn(2.0f); // Vardiyalar
                        cols.RelativeColumn(2.3f); // İzinler / Mazeretler
                    });

                    table.Header(h =>
                    {
                        h.Cell().Element(CellHeader).Text("Gün");
                        h.Cell().Element(CellHeader).Text("Tarih");
                        h.Cell().Element(CellHeader).Text("Vardiyalar");
                        h.Cell().Element(CellHeader).Text("İzinler / Mazeretler");
                    });

                    foreach (var d in _m.Days.OrderBy(x => x.Date))
                    {
                        var culture = new System.Globalization.CultureInfo("tr-TR");
                        var gunAdi = d.Date.ToString("dddd", culture);

                        // Gün
                        table.Cell().Element(CellBody).Text(gunAdi);

                        // Tarih
                        table.Cell().Element(CellBody).Text(d.Date.ToString("dd.MM.yyyy"));

                        // Vardiyalar
                        table.Cell().Element(CellBody).Text(text =>
                        {
                            if (d.Shifts.Any())
                            {
                                foreach (var s in d.Shifts.OrderBy(x => x.Start))
                                {
                                    text.Line($"{s.Start:HH:mm} - {s.End:HH:mm}");
                                }
                            }
                            else if (!d.TimeOffs.Any())
                            {
                                text.Line("Vardiya yok (izin kaydı da yok)");
                            }
                            else
                            {
                                text.Line("Vardiya yok");
                            }
                        });

                        // İzinler / Mazeretler
                        table.Cell().Element(CellBody).Text(text =>
                        {
                            if (d.TimeOffs.Any())
                            {
                                foreach (var t in d.TimeOffs)
                                {
                                    string izinTipi = t.Type switch
                                    {
                                        "PaidLeave" => "Yıllık izin",
                                        "SickLeave" => "Raporlu",
                                        "UnpaidLeave" => "Ücretsiz izin",
                                        "OffDay" => "İdari izin",
                                        _ => "Diğer"
                                    };

                                    string ucretDurumu = t.IsPaid ? "Ücretli" : "Ücretsiz";
                                    string onayMetni = t.IsApproved ? "" : " • Onay bekliyor";

                                    var line = $"{izinTipi}  {t.DateStart:dd.MM} - {t.DateEnd:dd.MM} ({ucretDurumu}){onayMetni}";
                                    text.Line(line);

                                    if (!string.IsNullOrWhiteSpace(t.Description))
                                    {
                                        text.Line("  " + t.Description);
                                    }
                                }
                            }
                            else
                            {
                                text.Line("Kayıt yok");
                            }
                        });
                    }
                });
            }

            private static IContainer CellHeader(IContainer container) =>
                container.PaddingVertical(5)
                         .BorderBottom(1)
                         .BorderColor(Colors.Grey.Lighten2)
                         .DefaultTextStyle(x => x.SemiBold());

            private static IContainer CellBody(IContainer container) =>
                container.PaddingVertical(3);
        }

    }
}
