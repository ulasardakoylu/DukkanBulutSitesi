using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DükkanBulutSitesi.Infrastructure;
using DükkanBulutSitesi.Models.Profile;
using Entity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DükkanBulutSitesi.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly AppDbContext _db;
        private readonly CurrentStoreAccessor _storeAccessor;

        public ProfileController(AppDbContext db, CurrentStoreAccessor storeAccessor)
        {
            _db = db;
            _storeAccessor = storeAccessor;
        }

        private int GetCurrentUserId()
        {
            var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(idStr))
                throw new InvalidOperationException("User ID claim is missing.");
            return int.Parse(idStr);
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var userId = GetCurrentUserId();
            var storeId = _storeAccessor.CurrentStoreId;

            if (storeId == 0)
                return RedirectToAction("Index", "Home");

            var user = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserID == userId && u.IsActive);

            if (user == null)
                return RedirectToAction("Index", "Home");

            // 1) Store-specific: total purchased count = sum of Qty (3 of same item counts as 3)
            var boughtCount = await _db.BoughtUserItems
                .AsNoTracking()
                .Where(b => b.UserID == userId && b.StoreID == storeId)
                .SumAsync(b => (int?)b.Qty) ?? 0;

            // 2) Store-specific: show each purchased item only once (latest BoughtAt per ItemID)
            // EF-friendly: GroupBy -> Max(BoughtAt) -> Join Items
            var purchased = await _db.BoughtUserItems
                .AsNoTracking()
                .Where(b => b.UserID == userId && b.StoreID == storeId)
                .GroupBy(b => b.ItemID)
                .Select(g => new
                {
                    ItemID = g.Key,
                    LastBoughtAt = g.Max(x => x.BoughtAt)
                })
                .Join(
                    _db.Items.AsNoTracking(),
                    lp => lp.ItemID,
                    i => i.ItemID,
                    (lp, i) => new ProfileItemVm
                    {
                        ItemID = i.ItemID,
                        ItemName = i.ItemName,
                        PictureLink = i.PictureLink,
                        OriginalPrice = i.ItemPrice,
                        Price = i.ItemPrice,
                        Date = lp.LastBoughtAt
                    }
                )
                .OrderByDescending(x => x.Date)
                .ToListAsync();

            var viewed = await _db.LookedAtItems
                .AsNoTracking()
                .Where(v => v.UserID == userId)
                .GroupBy(v => new
                {
                    v.ItemID,
                    v.Item.ItemName,
                    v.Item.PictureLink,
                    v.Item.ItemPrice
                })
                .Select(g => new ProfileItemVm
                {
                    ItemID = g.Key.ItemID,
                    ItemName = g.Key.ItemName,
                    PictureLink = g.Key.PictureLink,
                    OriginalPrice = g.Key.ItemPrice,
                    Price = g.Key.ItemPrice,
                    Date = g.Max(x => x.ViewedAt)
                })
                .OrderByDescending(x => x.Date)
                .Take(50)
                .ToListAsync();

            var favourites = await _db.FavouriteUserItems
                .AsNoTracking()
                .Where(f => f.UserID == userId)
                .OrderByDescending(f => f.CreatedAt)
                .Select(f => new ProfileItemVm
                {
                    ItemID = f.ItemID,
                    ItemName = f.Item.ItemName,
                    PictureLink = f.Item.PictureLink,
                    OriginalPrice = f.Item.ItemPrice,
                    Price = f.Item.ItemPrice,
                    Date = f.CreatedAt
                })
                .ToListAsync();

            await ApplyCampaignPricesAsync(storeId, purchased);
            await ApplyCampaignPricesAsync(storeId, viewed);
            await ApplyCampaignPricesAsync(storeId, favourites);

            var vm = new ProfilePageVm
            {
                UserName = user.UserName,
                CreatedAt = user.CreatedAt,
                BoughtItemCount = boughtCount,
                PurchasedItems = purchased,
                ViewedItems = viewed,
                FavouriteItems = favourites
            };

            return View(vm);
        }

        private async Task ApplyCampaignPricesAsync(int storeId, System.Collections.Generic.List<ProfileItemVm> items)
        {
            if (storeId == 0 || items == null || !items.Any())
                return;

            var itemIds = items.Select(i => i.ItemID).Distinct().ToList();
            var now = DateTime.UtcNow;

            var discountDict = await _db.SpecialOfferItems
                .AsNoTracking()
                .Where(soi => itemIds.Contains(soi.ItemID))
                .Join(
                    _db.SpecialOffers,
                    soi => soi.SpecialOfferID,
                    so => so.SpecialOfferID,
                    (soi, so) => new { soi.ItemID, soi.NewPrice, so }
                )
                .Where(x =>
                    x.so.StoreID == storeId &&
                    !x.so.IsCancelled &&
                    x.so.SpecialOfferDateStart <= now &&
                    x.so.SpecialOfferDateEnd > now
                )
                .GroupBy(x => x.ItemID)
                .Select(g => new { ItemID = g.Key, MinPrice = g.Min(x => x.NewPrice) })
                .ToDictionaryAsync(x => x.ItemID, x => x.MinPrice);

            foreach (var item in items)
            {
                item.Price = item.OriginalPrice;
                item.HasCampaignPrice = false;

                if (discountDict.TryGetValue(item.ItemID, out var campaignPrice) &&
                    campaignPrice < item.OriginalPrice)
                {
                    item.Price = campaignPrice;
                    item.HasCampaignPrice = true;
                }
            }
        }
    }
}
