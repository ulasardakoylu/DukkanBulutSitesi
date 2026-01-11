using DükkanBulutSitesi.Infrastructure;
using DükkanBulutSitesi.Models;
using DükkanBulutSitesi.Models.Store;
using Entity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using DükkanBulutSitesi.Models.Search;

namespace DükkanBulutSitesi.Controllers
{
    public class HomeController : Controller
    {
        private readonly AppDbContext _db;
        private readonly CurrentStoreAccessor _store;

        public HomeController(AppDbContext db, CurrentStoreAccessor store)
        {
            _db = db;
            _store = store;
        }

        [HttpGet]
        public async Task<IActionResult> Welcome()
        {
            var stores = await _db.Stores
                .AsNoTracking()
                .OrderBy(s => s.StoreName)
                .ToListAsync();

            return View(stores);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SelectStore(int storeId, string? returnUrl)
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

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? storeId, int page = 1, string sort = "new", string? category = null)
        {
            var vm = new HomeIndexVm();

            vm.Stores = await _db.Stores
                .AsNoTracking()
                .OrderBy(s => s.StoreName)
                .Select(s => new StoreDto { StoreID = s.StoreID, StoreName = s.StoreName })
                .ToListAsync();

            var effectiveStoreId = storeId ?? _store.CurrentStoreId;

            if (storeId.HasValue)
            {
                Response.Cookies.Append(
                    "CurrentStoreId",
                    effectiveStoreId.ToString(),
                    new CookieOptions
                    {
                        HttpOnly = true,
                        IsEssential = true,
                        Expires = DateTimeOffset.UtcNow.AddDays(30)
                    });

                _store.ForceSet(effectiveStoreId);
            }

            vm.SelectedStoreId = effectiveStoreId;

            if (vm.SelectedStoreId != 0)
            {
                const int pageSize = 4;   // items per page
                if (page < 1) page = 1;

                var now = DateTime.Now;

                // items for this store that are active
                var baseQuery = _db.InventoryItems
                    .AsNoTracking()
                    .Where(ii => ii.StoreID == vm.SelectedStoreId && ii.IsActive)
                    .Select(ii => ii.Item)
                    .Where(i => i.IsActive);

                // ----- CATEGORY FILTER -----
                if (!string.IsNullOrEmpty(category))
                {
                    baseQuery =
                        from i in baseQuery
                        join ci in _db.CategoryItems on i.ItemID equals ci.ItemID
                        where ci.CategoryName == category
                        select i;

                    baseQuery = baseQuery.Distinct();
                }

                // total AFTER category filter (for pager)
                var totalCount = await baseQuery.CountAsync();

                // ----- SORTING -----
                IQueryable<Item> sortedQuery;

                switch (sort)
                {
                    case "best":
                        // Most sold (overall), but only among items in baseQuery
                        var soldCounts = _db.BoughtUserItems
                            .GroupBy(b => b.ItemID)
                            .Select(g => new
                            {
                                ItemID = g.Key,
                                Count = (int?)g.Count() 
                            });

                        sortedQuery =
                            from i in baseQuery
                            join sc in soldCounts on i.ItemID equals sc.ItemID into soldLeft
                            from sc in soldLeft.DefaultIfEmpty()
                            orderby sc.Count descending, i.ItemName  
                            select i;
                        break;

                    case "view":
                        // Most viewed (overall), only for items in baseQuery
                        var viewCounts = _db.LookedAtItems
                            .GroupBy(v => v.ItemID)
                            .Select(g => new
                            {
                                ItemID = g.Key,
                                Count = (int?)g.Count() 
                            });

                        sortedQuery =
                            from i in baseQuery
                            join vc in viewCounts on i.ItemID equals vc.ItemID into viewLeft
                            from vc in viewLeft.DefaultIfEmpty()
                            orderby vc.Count descending, i.ItemName 
                            select i;
                        break;

                    case "new":
                    default:
                        // Yeni Eklenenler = newest items first
                        sortedQuery = baseQuery.OrderByDescending(i => i.CreatedAt);
                        break;
                }

                // clamp page if beyond max
                var maxPage = Math.Max(1, (int)Math.Ceiling((double)totalCount / pageSize));
                if (page > maxPage) page = maxPage;

                // page and project
                vm.PagedProducts = await sortedQuery
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(i => new
                    {
                        Item = i,
                        MinCampaignPrice = (
                            from soi in _db.SpecialOfferItems
                            join so in _db.SpecialOffers on soi.SpecialOfferID equals so.SpecialOfferID
                            where soi.ItemID == i.ItemID
                                  && so.StoreID == vm.SelectedStoreId
                                  && !so.IsCancelled
                                  && so.SpecialOfferDateStart <= now
                                  && so.SpecialOfferDateEnd > now
                            select (decimal?)soi.NewPrice
                        ).Min()
                    })
                    .Select(x => new ProductCardDto
                    {
                        ItemID = x.Item.ItemID,
                        ItemName = x.Item.ItemName,
                        Price = x.MinCampaignPrice ?? x.Item.ItemPrice,
                        PictureLink = x.Item.PictureLink,
                        OriginalPrice = x.Item.ItemPrice,
                        HasCampaignPrice = x.MinCampaignPrice.HasValue,
                    })
                    .ToListAsync();

                // ----- CATEGORY LIST FOR DROPDOWN -----
                vm.Categories = await _db.InventoryItems
                    .AsNoTracking()
                    .Where(ii => ii.StoreID == vm.SelectedStoreId && ii.IsActive)
                    .Join(
                        _db.CategoryItems,
                        ii => ii.ItemID,
                        ci => ci.ItemID,
                        (ii, ci) => ci.CategoryName
                    )
                    .Distinct()
                    .OrderBy(x => x)
                    .ToListAsync();

                vm.Page = page;
                vm.PageSize = pageSize;
                vm.TotalCount = totalCount;
            }

            // store which category is selected (even if no store selected yet)
            vm.SelectedCategory = category;

            vm.ShouldShowStoreModal = vm.SelectedStoreId == 0;
            return View(vm);
        }


        [HttpGet]
        public async Task<IActionResult> SpecialOffers(int? id)
        {
            var storeId = _store.CurrentStoreId;
            if (storeId == 0)
            {
                return RedirectToAction("Welcome", "Home");
            }

            var now = DateTime.Now;

            // All active (not cancelled) offers for this store at this moment
            var activeOffers = await _db.SpecialOffers
                .AsNoTracking()
                .Where(o =>
                    o.StoreID == storeId &&
                    !o.IsCancelled &&
                    o.SpecialOfferDateStart <= now &&
                    o.SpecialOfferDateEnd > now)
                .OrderBy(o => o.SpecialOfferDateStart)
                .ToListAsync();

            // Next planned (future) offer start
            var nextPlannedStart = await _db.SpecialOffers
                .AsNoTracking()
                .Where(o =>
                    o.StoreID == storeId &&
                    !o.IsCancelled &&
                    o.SpecialOfferDateStart > now)
                .OrderBy(o => o.SpecialOfferDateStart)
                .Select(o => (DateTime?)o.SpecialOfferDateStart)
                .FirstOrDefaultAsync();

            var vm = new StoreSpecialOffersVm
            {
                NextPlannedOfferStart = nextPlannedStart
            };

            if (!activeOffers.Any())
            {
                // No active campaigns just show the empty page with "no active campaigns" message
                return View(vm);
            }

            var offerIds = activeOffers.Select(o => o.SpecialOfferID).ToList();

            // Items participating in any active campaign for this store
            var offerItems = await _db.SpecialOfferItems
                .AsNoTracking()
                .Where(x => offerIds.Contains(x.SpecialOfferID))
                .ToListAsync();

            var itemIds = offerItems.Select(x => x.ItemID).Distinct().ToList();

            var itemsDict = await _db.Items
                .AsNoTracking()
                .Where(i => itemIds.Contains(i.ItemID))
                .ToDictionaryAsync(i => i.ItemID, i => i);

            // Which offer is selected? If id is null, pick the first one
            var selectedId = id ?? activeOffers.First().SpecialOfferID;
            vm.SelectedOfferID = selectedId;

            // Build ActiveOffers list with their items
            vm.ActiveOffers = activeOffers.Select(o =>
            {
                var itemsForOffer = offerItems
                    .Where(soi => soi.SpecialOfferID == o.SpecialOfferID)
                    .ToList();

                var mappedItems = new List<ActiveSpecialOfferItemVm>();

                foreach (var oi in itemsForOffer)
                {
                    if (!itemsDict.TryGetValue(oi.ItemID, out var item))
                        continue;

                    mappedItems.Add(new ActiveSpecialOfferItemVm
                    {
                        ItemID = item.ItemID,
                        ItemName = item.ItemName,
                        OriginalPrice = item.ItemPrice,
                        NewPrice = oi.NewPrice,
                        PictureLink = item.PictureLink
                    });
                }

                return new ActiveSpecialOfferVm
                {
                    SpecialOfferID = o.SpecialOfferID,
                    Name = o.SpecialOfferName,
                    Description = o.SpecialOfferDescription,
                    Start = o.SpecialOfferDateStart,
                    End = o.SpecialOfferDateEnd,
                    Items = mappedItems
                };
            }).ToList();

            // Determine prev / next campaign for navigation
            var index = vm.ActiveOffers.FindIndex(a => a.SpecialOfferID == selectedId);
            if (index >= 0)
            {
                if (index > 0)
                    vm.PreviousOfferID = vm.ActiveOffers[index - 1].SpecialOfferID;

                if (index < vm.ActiveOffers.Count - 1)
                    vm.NextOfferID = vm.ActiveOffers[index + 1].SpecialOfferID;
            }

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> About()
        {
            var storeId = _store.CurrentStoreId;
            if (storeId == 0)
            {
                return RedirectToAction("Welcome", "Home");
            }

            var store = await _db.Stores
                .AsNoTracking()
                .Where(s => s.StoreID == storeId && s.IsActive)
                .Select(s => new StoreAboutVm
                {
                    StoreID = s.StoreID,
                    StoreName = s.StoreName,
                    StoreOpenedAt = s.StoreOpenedAt,
                    StoreDescription = s.StoreDescription,
                    StoreAddress = s.StoreAddress,
                    StorePictureLink = s.StorePictureLink
                })
                .FirstOrDefaultAsync();

            if (store == null)
                return RedirectToAction("Welcome");

            store.OwnerNames = await _db.StoreOwners
                .AsNoTracking()
                .Where(o => o.StoreID == store.StoreID && o.IsActive)
                .Select(o => o.OwnerName)
                .ToListAsync();

            return View(store);
        }

        [HttpGet]
        public async Task<IActionResult> Search(string? q)
        {
            var storeId = _store.CurrentStoreId;

            var vm = new SearchResultsVm
            {
                Query = q?.Trim() ?? ""
            };

            // no store or empty query just render “type something” state
            if (storeId == 0 || string.IsNullOrWhiteSpace(vm.Query))
            {
                return PartialView("_SearchResults", vm);
            }

            var now = DateTime.Now;
            var qLower = vm.Query.ToLower();

            // items for this store that are active and whose name includes query
            var baseQuery = _db.InventoryItems
                .Where(ii => ii.StoreID == storeId && ii.IsActive)
                .Select(ii => ii.Item)
                .Where(i => i.IsActive && i.ItemName.ToLower().Contains(qLower));

            var query = baseQuery
                .Select(i => new
                {
                    Item = i,

                    MinCampaignPrice = (
                        from soi in _db.SpecialOfferItems
                        join so in _db.SpecialOffers on soi.SpecialOfferID equals so.SpecialOfferID
                        where soi.ItemID == i.ItemID
                              && so.StoreID == storeId
                              && !so.IsCancelled
                              && so.SpecialOfferDateStart <= now
                              && so.SpecialOfferDateEnd > now
                        select (decimal?)soi.NewPrice
                    ).Min(),

                    AvgStars = _db.ItemComments
                        .Where(c => c.ItemID == i.ItemID && c.IsActive)
                        .Select(c => (double?)c.CommentStar)
                        .Average(),

                    ReviewCount = _db.ItemComments
                        .Count(c => c.ItemID == i.ItemID && c.IsActive)
                })
                .OrderByDescending(x => x.AvgStars ?? 0)
                .ThenByDescending(x => x.ReviewCount)
                .ThenBy(x => x.Item.ItemName)
                .Take(50);

            var data = await query.ToListAsync();

            vm.Results = data.Select(x =>
            {
                var avg = x.AvgStars ?? 0;
                var minPrice = x.MinCampaignPrice;
                var original = x.Item.ItemPrice;
                var effective = minPrice ?? original;

                return new SearchResultItemVm
                {
                    ItemID = x.Item.ItemID,
                    ItemName = x.Item.ItemName,
                    PictureLink = x.Item.PictureLink,
                    OriginalPrice = original,
                    Price = effective,
                    HasCampaignPrice = minPrice.HasValue && minPrice.Value < original,
                    AverageStars = avg,
                    ReviewCount = x.ReviewCount
                };
            }).ToList();

            return PartialView("_SearchResults", vm);
        }

    }
}
