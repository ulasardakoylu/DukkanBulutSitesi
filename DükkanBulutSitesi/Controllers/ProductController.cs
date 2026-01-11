using DükkanBulutSitesi.Models;
using Entity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DükkanBulutSitesi.Controllers
{
    public class ProductController : Controller
    {
        private readonly AppDbContext _db;
        public ProductController(AppDbContext db) => _db = db;

        // ---------- helpers ----------

        private int? TryGetCurrentUserId()
        {
            var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(idStr)) return null;
            if (!int.TryParse(idStr, out var id)) return null;
            return id;
        }

        private int? TryGetCurrentStoreId()
        {
            if (HttpContext?.Items.TryGetValue("CurrentStoreId", out var v) == true && v is int storeId && storeId > 0)
                return storeId;

            if (Request.Cookies.TryGetValue("CurrentStoreId", out var raw) &&
                int.TryParse(raw, out var storeId2) &&
                storeId2 > 0)
                return storeId2;

            return null;
        }

        private async Task<bool> CanManageStoreAsync(int storeId)
        {
            var userId = TryGetCurrentUserId();
            if (!userId.HasValue) return false;

            var userIsActive = await _db.Users
                .AsNoTracking()
                .AnyAsync(u => u.UserID == userId.Value && u.IsActive);
            if (!userIsActive) return false;

            if (User.IsInRole("Owner"))
            {
                return await _db.StoreOwners
                    .AsNoTracking()
                    .AnyAsync(x => x.UserID == userId.Value && x.StoreID == storeId && x.IsActive);
            }

            if (User.IsInRole("Manager"))
            {
                return await _db.Personels
                    .AsNoTracking()
                    .Include(p => p.Role)
                    .AnyAsync(p =>
                        p.UserID == userId.Value &&
                        p.StoreID == storeId &&
                        p.IsActive &&
                        p.Role != null &&
                        p.Role.RoleName == "Manager");
            }

            return false;
        }


        private async Task<bool> IsCurrentUserActiveAsync()
        {
            var userId = TryGetCurrentUserId();
            if (!userId.HasValue) return false;

            return await _db.Users
                .AsNoTracking()
                .AnyAsync(u => u.UserID == userId.Value && u.IsActive);
        }


        private async Task<bool> ItemBelongsToStoreAsync(int storeId, int itemId)
        {
            return await _db.InventoryItems.AnyAsync(ii =>
                ii.StoreID == storeId &&
                ii.ItemID == itemId &&
                ii.IsActive);
        }


        private async Task LogItemViewAsync(int itemId)
        {
            var userId = TryGetCurrentUserId();
            if (!userId.HasValue) return;

            _db.LookedAtItems.Add(new LookedAtItem
            {
                UserID = userId.Value,
                ItemID = itemId,
                ViewedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
        }

        private async Task<List<ProductMiniDto>> GetFallbackRelatedAsync(int currentItemId, int take)
        {
            var itemsQuery = _db.Items.AsNoTracking()
                .Where(i => i.IsActive && i.ItemID != currentItemId)
                .OrderBy(i => i.ItemName)
                .Take(take);

            return await ProjectItemsToMiniDto(itemsQuery).ToListAsync();
        }

        private IQueryable<ProductMiniDto> ProjectItemsToMiniDto(IQueryable<Item> items)
        {
            var now = DateTime.UtcNow;

            return items.Select(i => new ProductMiniDto
            {
                ItemID = i.ItemID,
                ItemName = i.ItemName,
                PictureLink = i.PictureLink,

                OriginalPrice = i.ItemPrice,

                // Effective (campaign) price
                Price = (
                    from soi in _db.SpecialOfferItems
                    join so in _db.SpecialOffers
                        on soi.SpecialOfferID equals so.SpecialOfferID
                    where soi.ItemID == i.ItemID
                          && !so.IsCancelled
                          && so.SpecialOfferDateStart <= now
                          && so.SpecialOfferDateEnd > now
                    select (decimal?)soi.NewPrice
                ).Min() ?? i.ItemPrice,

                // Does this item currently have an active campaign?
                HasCampaignPrice = (
                    from soi in _db.SpecialOfferItems
                    join so in _db.SpecialOffers
                        on soi.SpecialOfferID equals so.SpecialOfferID
                    where soi.ItemID == i.ItemID
                          && !so.IsCancelled
                          && so.SpecialOfferDateStart <= now
                          && so.SpecialOfferDateEnd > now
                    select soi.SpecialOfferID
                ).Any()
            });
        }

        private async Task<List<ProductMiniDto>> GetTagBasedRelatedAsync(int currentItemId, int take = 8)
        {
            // categories of the current item
            var categories = await _db.CategoryItems
                .AsNoTracking()
                .Where(c => c.ItemID == currentItemId)
                .Select(c => c.CategoryName)
                .Distinct()
                .ToListAsync();

            // keywords of the current item
            var keywords = await _db.KeywordsOfItems
                .AsNoTracking()
                .Where(k => k.ItemID == currentItemId)
                .Select(k => k.KeywordName)
                .Distinct()
                .ToListAsync();

            if (!categories.Any() && !keywords.Any())
            {
                return await GetFallbackRelatedAsync(currentItemId, take);
            }

            IQueryable<Item>? categoryQuery = null;
            IQueryable<Item>? keywordQuery = null;

            if (categories.Any())
            {
                categoryQuery =
                    from i in _db.Items.AsNoTracking()
                    join c in _db.CategoryItems on i.ItemID equals c.ItemID
                    where i.IsActive
                          && i.ItemID != currentItemId
                          && categories.Contains(c.CategoryName)
                    select i;
            }

            if (keywords.Any())
            {
                keywordQuery =
                    from i in _db.Items.AsNoTracking()
                    join k in _db.KeywordsOfItems on i.ItemID equals k.ItemID
                    where i.IsActive
                          && i.ItemID != currentItemId
                          && keywords.Contains(k.KeywordName)
                    select i;
            }

            IQueryable<Item> unionQuery;

            if (categoryQuery != null && keywordQuery != null)
                unionQuery = categoryQuery.Union(keywordQuery);
            else if (categoryQuery != null)
                unionQuery = categoryQuery;
            else
                unionQuery = keywordQuery!;

            var itemsQuery = unionQuery
                .Where(i => i.IsActive && i.ItemID != currentItemId)
                .Distinct()
                .OrderBy(i => i.ItemName)
                .Take(take);

            var related = await ProjectItemsToMiniDto(itemsQuery).ToListAsync();

            if (!related.Any())
            {
                related = await GetFallbackRelatedAsync(currentItemId, take);
            }

            return related;
        }

        private async Task<List<ProductMiniDto>> GetViewedSuggestionsAsync(int currentItemId, int take = 4)
        {
            var userId = TryGetCurrentUserId();
            if (!userId.HasValue)
                return new List<ProductMiniDto>();

            var since = DateTime.UtcNow.AddDays(-30);

            // all views for this user in last 30 days
            var views = await _db.LookedAtItems
                .AsNoTracking()
                .Where(l => l.UserID == userId.Value && l.ViewedAt >= since)
                .ToListAsync();

            if (!views.Any())
                return new List<ProductMiniDto>();

            var viewedItemIds = views
                .Select(v => v.ItemID)
                .Distinct()
                .ToList();

            // --- stats over categories and keywords of viewed items ---
            var keywordStats = await _db.KeywordsOfItems
                .AsNoTracking()
                .Where(k => viewedItemIds.Contains(k.ItemID))
                .GroupBy(k => k.KeywordName)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToListAsync();

            var categoryStats = await _db.CategoryItems
                .AsNoTracking()
                .Where(c => viewedItemIds.Contains(c.ItemID))
                .GroupBy(c => c.CategoryName)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToListAsync();

            var topKeywordNames = keywordStats.Select(x => x.Name).ToList();
            var topCategoryNames = categoryStats.Select(x => x.Name).ToList();

            if (!topKeywordNames.Any() && !topCategoryNames.Any())
                return new List<ProductMiniDto>();

            // items that share at least one of the top keywords
            IQueryable<Item>? keywordQuery = null;
            if (topKeywordNames.Any())
            {
                keywordQuery =
                    from i in _db.Items.AsNoTracking()
                    join k in _db.KeywordsOfItems on i.ItemID equals k.ItemID
                    where i.IsActive
                          && i.ItemID != currentItemId
                          && topKeywordNames.Contains(k.KeywordName)
                    select i;
            }

            // items that share at least one of the top categories
            IQueryable<Item>? categoryQuery = null;
            if (topCategoryNames.Any())
            {
                categoryQuery =
                    from i in _db.Items.AsNoTracking()
                    join c in _db.CategoryItems on i.ItemID equals c.ItemID
                    where i.IsActive
                          && i.ItemID != currentItemId
                          && topCategoryNames.Contains(c.CategoryName)
                    select i;
            }

            IQueryable<Item> unionQuery;
            if (keywordQuery != null && categoryQuery != null)
                unionQuery = keywordQuery.Union(categoryQuery);
            else
                unionQuery = keywordQuery ?? categoryQuery!;

            // get candidate items that at least match on tags
            var candidatesQuery = unionQuery
                .Where(i => i.IsActive)
                .Distinct()
                .Take(60);

            var candidates = await ProjectItemsToMiniDto(candidatesQuery).ToListAsync();
            if (!candidates.Any())
                return new List<ProductMiniDto>();

            var candidateIds = candidates.Select(c => c.ItemID).ToList();

            // per-item matched categories and keywords
            var catMatchesRaw = await _db.CategoryItems
                .AsNoTracking()
                .Where(c => candidateIds.Contains(c.ItemID) && topCategoryNames.Contains(c.CategoryName))
                .ToListAsync();

            var keyMatchesRaw = await _db.KeywordsOfItems
                .AsNoTracking()
                .Where(k => candidateIds.Contains(k.ItemID) && topKeywordNames.Contains(k.KeywordName))
                .ToListAsync();

            var catMatches = catMatchesRaw
                .GroupBy(c => c.ItemID)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.CategoryName).Distinct().ToList()
                );

            var keyMatches = keyMatchesRaw
                .GroupBy(k => k.ItemID)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.KeywordName).Distinct().ToList()
                );

            int GetMatchScore(int itemId)
            {
                var catCount = catMatches.TryGetValue(itemId, out var cats) ? cats.Count : 0;
                var keyCount = keyMatches.TryGetValue(itemId, out var keys) ? keys.Count : 0;
                return catCount + keyCount;
            }

            // attach matched tags to DTOs
            foreach (var c in candidates)
            {
                if (catMatches.TryGetValue(c.ItemID, out var cats))
                    c.MatchedCategories = cats;

                if (keyMatches.TryGetValue(c.ItemID, out var keys))
                    c.MatchedKeywords = keys;
            }

            // only keep items that have at least 1 match
            var filtered = candidates
                .Where(c => GetMatchScore(c.ItemID) > 0)
                .OrderByDescending(c => GetMatchScore(c.ItemID))
                .ThenBy(c => c.ItemName)
                .Take(take)
                .ToList();

            // if nothing actually shares a tag, don't show the block at all
            if (!filtered.Any())
                return new List<ProductMiniDto>();

            return filtered;
        }


        private async Task<(List<string> Categories, List<string> Keywords)> GetViewedTagBasisAsync(int currentItemId)
        {
            var userId = TryGetCurrentUserId();
            if (!userId.HasValue)
                return (new List<string>(), new List<string>());

            var since = DateTime.UtcNow.AddDays(-30);

            // all views for this user in last 30 days
            var views = await _db.LookedAtItems
                .Where(l => l.UserID == userId.Value && l.ViewedAt >= since)
                .ToListAsync();

            if (!views.Any())
                return (new List<string>(), new List<string>());

            var viewedItemIds = views
                .Select(v => v.ItemID)
                .Distinct()
                .ToList();

            // keyword stats for viewed items
            var keywordStats = await _db.KeywordsOfItems
                .Where(k => viewedItemIds.Contains(k.ItemID))
                .GroupBy(k => k.KeywordName)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToListAsync();

            // category stats for viewed items
            var categoryStats = await _db.CategoryItems
                .Where(c => viewedItemIds.Contains(c.ItemID))
                .GroupBy(c => c.CategoryName)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToListAsync();

            var topKeywordNames = keywordStats.Select(x => x.Name).ToList();
            var topCategoryNames = categoryStats.Select(x => x.Name).ToList();

            return (topCategoryNames, topKeywordNames);
        }


        // ---------- actions ----------

        [HttpGet("/product/{id:int}")]
        public async Task<IActionResult> Details(int id, int cpage = 1)
        {
            const int commentPageSize = 5;

            var item = await _db.Items.AsNoTracking()
                .FirstOrDefaultAsync(i => i.ItemID == id && i.IsActive);
            if (item == null) return NotFound();

            await LogItemViewAsync(id);

            var now = DateTime.UtcNow;

            var currentUserId = TryGetCurrentUserId();
            bool isFavourite = false;

            if (currentUserId.HasValue)
            {
                isFavourite = await _db.FavouriteUserItems
                    .AsNoTracking()
                    .AnyAsync(f => f.UserID == currentUserId.Value && f.ItemID == id);
            }

            var minCampaignPrice = await _db.SpecialOfferItems
                .AsNoTracking()
                .Include(x => x.SpecialOffer)
                .Where(x => x.ItemID == id
                            && x.SpecialOffer.SpecialOfferDateStart <= now
                            && x.SpecialOffer.SpecialOfferDateEnd >= now)
                .Select(x => (decimal?)x.NewPrice)
                .MinAsync();

            var hasCampaign = minCampaignPrice.HasValue;
            var effectivePrice = hasCampaign
                ? minCampaignPrice.Value
                : item.ItemPrice;

            // 1) Rating stats: count + average should include pending + approved
            var ratingQ = _db.ItemComments.AsNoTracking()
                .Where(x => x.ItemID == id
                            && x.IsActive
                            && x.ModerationStatus != CommentModerationStatus.Rejected);

            // 2) Visible reviews: only approved text should be shown
            var visibleQ = _db.ItemComments.AsNoTracking()
                .Where(x => x.ItemID == id
                            && x.IsActive
                            && x.ModerationStatus == CommentModerationStatus.Approved
                            && x.IsDescriptionApproved);

            var featured = await ratingQ
                .OrderByDescending(x => x.CommentStar)
                .ThenByDescending(x => x.CreatedAt)
                .Take(3)
                .Select(x => new ReviewVm
                {
                    ReviewID = x.CommentID,
                    OwnerName = x.OwnerName,
                    Stars = x.CommentStar,
                    CreatedAt = x.CreatedAt,

                    Text = (x.ModerationStatus == CommentModerationStatus.Approved && x.IsDescriptionApproved)
                        ? x.CommentDescription
                        : "Yorum içeriği yönetici onayı bekliyor."
                })
                .ToListAsync();

            var reviewCount = await ratingQ.CountAsync();
            var avg = reviewCount > 0
                ? Math.Round(await ratingQ.AverageAsync(x => (double)x.CommentStar), 1)
                : 0.0;

            var comments = await ratingQ
                .OrderByDescending(x => x.CreatedAt)
                .Skip((Math.Max(1, cpage) - 1) * commentPageSize)
                .Take(commentPageSize)
                .Select(x => new ReviewVm
                {
                    ReviewID = x.CommentID,
                    OwnerName = x.OwnerName,
                    Stars = x.CommentStar,
                    CreatedAt = x.CreatedAt,

                    Text = (x.ModerationStatus == CommentModerationStatus.Approved && x.IsDescriptionApproved)
                        ? x.CommentDescription
                        : "Yorum içeriği yönetici onayı bekliyor."
                })
                .ToListAsync();

            var related = await GetTagBasedRelatedAsync(id, int.MaxValue);

            var (viewedCats, viewedKeys) = await GetViewedTagBasisAsync(id);

            var viewedSuggestions = await GetViewedSuggestionsAsync(id, 4);

            var storeId = TryGetCurrentStoreId();
            var canManage = storeId.HasValue && await CanManageStoreAsync(storeId.Value);

            var vm = new ProductDetailsVm
            {
                ItemID = item.ItemID,
                ItemName = item.ItemName,

                Price = effectivePrice,
                OriginalPrice = item.ItemPrice,
                HasCampaignPrice = hasCampaign,
                IsFavourite = isFavourite,

                PictureLink = item.PictureLink,
                Description = item.Description,
                Featured = featured,
                AverageStars = avg,
                ReviewCount = reviewCount,
                Comments = comments,
                CommentPage = Math.Max(1, cpage),
                CommentPageSize = commentPageSize,
                CommentTotal = reviewCount,
                CanManageThisStore = canManage,

                ViewedSuggestionCategories = viewedCats,
                ViewedSuggestionKeywords = viewedKeys,

                Related = related,
                ViewedSuggestions = viewedSuggestions,
                NewComment = new NewCommentVm { ItemID = id }
            };

            return View(vm);
        }

        [HttpPost("product/add-comment")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddComment([FromForm] NewCommentVm form)
        {
            if (!ModelState.IsValid)
                return await Details(form.ItemID);

            var userId = TryGetCurrentUserId();
            if (!userId.HasValue)
                return Forbid();

            var name = User.Identity?.IsAuthenticated == true
                ? (User.Identity!.Name ?? "Müşteri")
                : "Misafir";

            var entity = new ItemComment
            {
                ItemID = form.ItemID,
                UserID = userId.Value,

                OwnerName = name,

                CommentDescription = "",
                PendingDescription = form.Text,

                CommentStar = form.Stars,
                CreatedAt = DateTime.UtcNow,
                IsActive = true,

                ModerationStatus = CommentModerationStatus.Pending,
                IsDescriptionApproved = false,
                ModerationDecidedAt = null
            };

            _db.ItemComments.Add(entity);
            await _db.SaveChangesAsync();

            TempData["CommentOk"] = "Yorumunuz alındı ve yönetici onayı bekliyor.";
            return RedirectToAction(nameof(Details), new { id = form.ItemID, cpage = 1 });
        }

        [Authorize(Roles = "Manager,Owner")]
        [HttpPost("product/delete-comment")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteComment([FromForm] int commentId, [FromForm] int itemId, [FromForm] string? returnUrl)
        {
            var storeId = TryGetCurrentStoreId();
            if (!storeId.HasValue) return Forbid();

            if (!await CanManageStoreAsync(storeId.Value)) return Forbid();
            if (!await ItemBelongsToStoreAsync(storeId.Value, itemId)) return Forbid();

            var comment = await _db.ItemComments.FirstOrDefaultAsync(c => c.CommentID == commentId && c.ItemID == itemId);
            if (comment == null) return NotFound();

            comment.IsActive = false;
            await _db.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(returnUrl)) return LocalRedirect(returnUrl);

            return RedirectToAction(nameof(Details), new { id = itemId, cpage = 1 });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleFavourite(int itemId, string? returnUrl)
        {
            var userId = TryGetCurrentUserId();
            if (!userId.HasValue)
            {
                return RedirectToAction(nameof(Details), new { id = itemId });
            }

            var fav = await _db.FavouriteUserItems
                .FirstOrDefaultAsync(f => f.UserID == userId.Value && f.ItemID == itemId);

            if (fav == null)
            {
                // Add to favourites
                fav = new FavouriteUserItem
                {
                    UserID = userId.Value,
                    ItemID = itemId,
                    CreatedAt = DateTime.UtcNow
                };
                _db.FavouriteUserItems.Add(fav);
            }
            else
            {
                // Remove from favourites (toggle)
                _db.FavouriteUserItems.Remove(fav);
            }

            await _db.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction(nameof(Details), new { id = itemId });
        }

        [Authorize(Roles = "Manager,Owner")]
        [HttpPost("product/delete-item")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteItem([FromForm] int itemId, [FromForm] string? returnUrl)
        {
            var storeId = TryGetCurrentStoreId();
            if (!storeId.HasValue) return Forbid();

            if (!await CanManageStoreAsync(storeId.Value)) return Forbid();
            if (!await ItemBelongsToStoreAsync(storeId.Value, itemId)) return Forbid();

            var item = await _db.Items.FirstOrDefaultAsync(i => i.ItemID == itemId);
            if (item == null) return NotFound();

            item.IsActive = false;

            var inv = await _db.InventoryItems.FirstOrDefaultAsync(ii => ii.StoreID == storeId.Value && ii.ItemID == itemId);
            if (inv != null) inv.IsActive = false;

            await _db.SaveChangesAsync();

            return RedirectToAction("Index", "Home");
        }


        [HttpGet("product/add-comment")]
        public IActionResult AddCommentGet([FromQuery] int id)
        {
            if (id == 0) return RedirectToAction("Index", "Home");
            return RedirectToAction(nameof(Details), new { id });
        }
    }
}
