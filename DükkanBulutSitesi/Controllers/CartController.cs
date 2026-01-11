using System.Security.Claims;
using DükkanBulutSitesi.Infrastructure;
using DükkanBulutSitesi.Models.Cart;
using Entity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace DükkanBulutSitesi.Controllers
{
    [Authorize]
    public class CartController : Controller
    {
        private readonly AppDbContext _db;
        private readonly CurrentStoreAccessor _storeAccessor;

        public CartController(AppDbContext db, CurrentStoreAccessor storeAccessor)
        {
            _db = db;
            _storeAccessor = storeAccessor;
        }

        // ---------- helpers ----------

        private int GetCurrentUserId()
        {
            var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(idStr))
                throw new InvalidOperationException("User ID claim is missing.");

            return int.Parse(idStr);
        }

        private async Task<ShoppingCart> GetOrCreateCartAsync()
        {
            var userId = GetCurrentUserId();
            var storeId = _storeAccessor.CurrentStoreId;

            if (storeId == 0)
                throw new InvalidOperationException("No current store selected for cart.");

            var cart = await _db.ShoppingCarts
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.UserID == userId
                                       && c.StoreID == storeId
                                       && c.IsActive);

            if (cart == null)
            {
                cart = new ShoppingCart
                {
                    UserID = userId,
                    StoreID = storeId,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };
                _db.ShoppingCarts.Add(cart);
                await _db.SaveChangesAsync();
            }

            return cart;
        }

        // ---------- actions ----------

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(int itemId, int qty = 1, string? returnUrl = null, bool fromQuickCart = false)
        {
            var storeId = GetCurrentStoreIdOrRedirect(out var redir);
            if (redir != null) return redir;

            if (qty < 1) qty = 1;

            // ✅ Verify item is actually sellable in this store + get stock + active flags
            var inv = await _db.InventoryItems
                .Include(ii => ii.Item)
                .FirstOrDefaultAsync(ii =>
                    ii.StoreID == storeId &&
                    ii.ItemID == itemId &&
                    ii.IsActive &&
                    ii.Item.IsActive);

            if (inv == null)
            {
                TempData["CartError"] = "Ürün bu mağazada bulunamadı veya aktif değil.";
                return SafeReturn(returnUrl);
            }

            // ✅ Cap requested qty by stock
            if (inv.OnHand <= 0)
            {
                TempData["CartError"] = "Bu ürün stokta yok.";
                return SafeReturn(returnUrl);
            }

            if (qty > inv.OnHand) qty = inv.OnHand;

            var cart = await GetOrCreateCartAsync();

            var line = cart.Items.FirstOrDefault(i => i.ItemID == itemId);
            if (line == null)
            {
                line = new ShoppingCartItem
                {
                    CartID = cart.CartID,
                    ItemID = itemId,
                    Qty = qty,
                    AddedToCartAt = DateTime.UtcNow
                };
                _db.ShoppingCartItems.Add(line);
            }
            else
            {
                var newQty = line.Qty + qty;
                line.Qty = Math.Min(newQty, inv.OnHand);
                line.AddedToCartAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();

            if (fromQuickCart)
                TempData["OpenQuickCart"] = "1";

            return SafeReturn(returnUrl);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveItem(int cartItemId, string? returnUrl = null, bool fromQuickCart = false)
        {
            var storeId = GetCurrentStoreIdOrRedirect(out var redir);
            if (redir != null) return redir;

            var userId = GetCurrentUserId();

            var item = await _db.ShoppingCartItems
                .Include(ci => ci.Cart)
                .FirstOrDefaultAsync(ci =>
                    ci.CartItemID == cartItemId &&
                    ci.Cart.UserID == userId &&
                    ci.Cart.StoreID == storeId && 
                    ci.Cart.IsActive);

            if (item != null)
            {
                _db.ShoppingCartItems.Remove(item);
                await _db.SaveChangesAsync();
            }

            if (fromQuickCart)
                TempData["OpenQuickCart"] = "1";

            return SafeReturn(returnUrl);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Clear(string? returnUrl = null, bool fromQuickCart = false)
        {
            var storeId = GetCurrentStoreIdOrRedirect(out var redir);
            if (redir != null) return redir;

            var userId = GetCurrentUserId();

            var cart = await _db.ShoppingCarts
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c =>
                    c.UserID == userId &&
                    c.StoreID == storeId &&
                    c.IsActive);

            if (cart != null && cart.Items.Any())
            {
                _db.ShoppingCartItems.RemoveRange(cart.Items);
                await _db.SaveChangesAsync();
            }

            if (fromQuickCart)
                TempData["OpenQuickCart"] = "1";

            return SafeReturn(returnUrl);
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var userId = GetCurrentUserId();
            var storeId = _storeAccessor.CurrentStoreId;
            var now = DateTime.Now;

            var cart = await _db.ShoppingCarts
                .AsNoTracking()
                .AsSplitQuery()
                .Include(c => c.Items)
                    .ThenInclude(ci => ci.Item)
                .FirstOrDefaultAsync(c => c.UserID == userId
                                       && c.StoreID == storeId
                                       && c.IsActive);

            var cartItems = cart?.Items ?? new List<ShoppingCartItem>();

            var discountDict = new Dictionary<int, decimal>();

            if (storeId != 0 && cartItems.Any())
            {
                var itemIds = cartItems
                    .Select(ci => ci.ItemID)
                    .Distinct()
                    .ToList();

                discountDict = await _db.SpecialOfferItems
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
            }

            var items = cartItems
                .OrderByDescending(i => i.AddedToCartAt)
                .Select(ci =>
                {
                    var original = ci.Item.ItemPrice;
                    var effective = original;

                    if (discountDict.TryGetValue(ci.ItemID, out var discounted) && discounted < original)
                    {
                        effective = discounted;
                    }

                    return new CartItemVm
                    {
                        CartItemID = ci.CartItemID,
                        ItemID = ci.ItemID,
                        ItemName = ci.Item.ItemName,
                        PictureLink = ci.Item.PictureLink,
                        OriginalUnitPrice = original,
                        UnitPrice = effective,
                        Qty = ci.Qty,
                        AddedAt = ci.AddedToCartAt
                    };
                })
                .ToList();

            var cards = await _db.SavedCards
                .AsNoTracking()
                .Where(c => c.UserID == userId && c.IsActive)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new SavedCardVm
                {
                    CardID = c.CardID,
                    Brand = c.Brand,
                    Last4 = c.Last4,
                    ExpMonth = c.CardExpMonth,
                    ExpYear = c.CardExpYear
                }).ToListAsync();

            var addresses = await _db.SavedAddresses
                .AsNoTracking()
                .Where(a => a.UserID == userId && a.IsActive)
                .Select(a => new SavedAddressVm
                {
                    AddressID = a.AddressID,
                    AddressInformation = a.AddressInformation,
                    AddressName = a.AddressName
                }).ToListAsync();

            List<SuggestedProductVm> suggestions = new();

            if (storeId != 0)
            {
                if (cartItems.Any())
                {
                    var cartItemIds = cartItems
                        .Select(ci => ci.ItemID)
                        .Distinct()
                        .ToList();

                    // categories & keywords of items currently in cart
                    var cartCategories = await _db.CategoryItems
                        .AsNoTracking()
                        .Where(c => cartItemIds.Contains(c.ItemID))
                        .Select(c => c.CategoryName)
                        .Distinct()
                        .ToListAsync();

                    var cartKeywords = await _db.KeywordsOfItems
                        .AsNoTracking()
                        .Where(k => cartItemIds.Contains(k.ItemID))
                        .Select(k => k.KeywordName)
                        .Distinct()
                        .ToListAsync();

                    // base inventory for this store, excluding items already in cart
                    IQueryable<InventoryItem> invBase = _db.InventoryItems
                        .Where(ii => ii.StoreID == storeId
                                     && ii.IsActive
                                     && ii.Item.IsActive
                                     && !cartItemIds.Contains(ii.ItemID));

                    // filter by "shares at least 1 category OR 1 keyword"
                    if (cartCategories.Any() || cartKeywords.Any())
                    {
                        invBase = invBase.Where(ii =>
                            (cartCategories.Any() &&
                             _db.CategoryItems.Any(c =>
                                 c.ItemID == ii.ItemID &&
                                 cartCategories.Contains(c.CategoryName)))
                            ||
                            (cartKeywords.Any() &&
                             _db.KeywordsOfItems.Any(k =>
                                 k.ItemID == ii.ItemID &&
                                 cartKeywords.Contains(k.KeywordName)))
                        );
                    }

                    var query = invBase
                        .OrderBy(ii => ii.Item.ItemName)
                        .Take(5)
                        .Select(ii => new
                        {
                            ii.Item,
                            MinCampaignPrice = (
                                from soi in _db.SpecialOfferItems
                                join so in _db.SpecialOffers on soi.SpecialOfferID equals so.SpecialOfferID
                                where soi.ItemID == ii.ItemID
                                      && so.StoreID == storeId
                                      && !so.IsCancelled
                                      && so.SpecialOfferDateStart <= now
                                      && so.SpecialOfferDateEnd > now
                                select (decimal?)soi.NewPrice
                            ).Min()
                        });

                    suggestions = await query
                        .Select(x => new SuggestedProductVm
                        {
                            ItemID = x.Item.ItemID,
                            ItemName = x.Item.ItemName,
                            PictureLink = x.Item.PictureLink,
                            Price = x.MinCampaignPrice ?? x.Item.ItemPrice,
                            OriginalPrice = x.Item.ItemPrice,
                            HasCampaignPrice = x.MinCampaignPrice.HasValue
                        })
                        .ToListAsync();

                    // fallback: if tags exist but query gave nothing, show generic 5 items
                    if (!suggestions.Any())
                    {
                        suggestions = await _db.InventoryItems
                            .Where(ii => ii.StoreID == storeId &&
                                         ii.IsActive &&
                                         ii.Item.IsActive)
                            .OrderBy(ii => ii.Item.ItemName)
                            .Take(5)
                            .Select(ii => new
                            {
                                ii.Item,
                                MinCampaignPrice = (
                                    from soi in _db.SpecialOfferItems
                                    join so in _db.SpecialOffers on soi.SpecialOfferID equals so.SpecialOfferID
                                    where soi.ItemID == ii.ItemID
                                          && so.StoreID == storeId
                                          && !so.IsCancelled
                                          && so.SpecialOfferDateStart <= now
                                          && so.SpecialOfferDateEnd > now
                                    select (decimal?)soi.NewPrice
                                ).Min()
                            })
                            .Select(x => new SuggestedProductVm
                            {
                                ItemID = x.Item.ItemID,
                                ItemName = x.Item.ItemName,
                                PictureLink = x.Item.PictureLink,
                                Price = x.MinCampaignPrice ?? x.Item.ItemPrice,
                                OriginalPrice = x.Item.ItemPrice,
                                HasCampaignPrice = x.MinCampaignPrice.HasValue
                            })
                            .ToListAsync();
                    }
                }
                else
                {
                    // cart empty, generic 5 items for this store
                    suggestions = await _db.InventoryItems
                        .Where(ii => ii.StoreID == storeId &&
                                     ii.IsActive &&
                                     ii.Item.IsActive)
                        .OrderBy(ii => ii.Item.ItemName)
                        .Take(5)
                        .Select(ii => new
                        {
                            ii.Item,
                            MinCampaignPrice = (
                                from soi in _db.SpecialOfferItems
                                join so in _db.SpecialOffers on soi.SpecialOfferID equals so.SpecialOfferID
                                where soi.ItemID == ii.ItemID
                                      && so.StoreID == storeId
                                      && !so.IsCancelled
                                      && so.SpecialOfferDateStart <= now
                                      && so.SpecialOfferDateEnd > now
                                select (decimal?)soi.NewPrice
                            ).Min()
                        })
                        .Select(x => new SuggestedProductVm
                        {
                            ItemID = x.Item.ItemID,
                            ItemName = x.Item.ItemName,
                            PictureLink = x.Item.PictureLink,
                            Price = x.MinCampaignPrice ?? x.Item.ItemPrice,
                            OriginalPrice = x.Item.ItemPrice,
                            HasCampaignPrice = x.MinCampaignPrice.HasValue
                        })
                        .ToListAsync();
                }
            }

            var vm = new CartPageVm
            {
                Items = items,
                SavedCards = cards,
                SavedAddresses = addresses,
                Suggestions = suggestions
            };

            return View(vm);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateQuantity(int cartItemId, int change, string? returnUrl, bool fromQuickCart = false)
        {
            var storeId = GetCurrentStoreIdOrRedirect(out var redir);
            if (redir != null) return redir;

            var userId = GetCurrentUserId();

            // ✅ Only allow -1 or +1
            if (change != -1 && change != 1)
                change = change < 0 ? -1 : 1;

            var line = await _db.ShoppingCartItems
                .Include(ci => ci.Cart)
                .FirstOrDefaultAsync(ci =>
                    ci.CartItemID == cartItemId &&
                    ci.Cart.UserID == userId &&
                    ci.Cart.StoreID == storeId &&   // ✅ add
                    ci.Cart.IsActive);

            if (line != null)
            {
                // ✅ Check current stock in this store
                var inv = await _db.InventoryItems
                    .Include(ii => ii.Item)
                    .FirstOrDefaultAsync(ii =>
                        ii.StoreID == storeId &&
                        ii.ItemID == line.ItemID &&
                        ii.IsActive &&
                        ii.Item.IsActive);

                if (inv == null || inv.OnHand <= 0)
                {
                    // If product no longer sellable, remove from cart
                    _db.ShoppingCartItems.Remove(line);
                    TempData["CartError"] = "Ürün artık satışta değil veya stokta yok.";
                }
                else
                {
                    var newQty = line.Qty + change;

                    if (newQty <= 0)
                    {
                        _db.ShoppingCartItems.Remove(line);
                    }
                    else
                    {
                        line.Qty = Math.Min(newQty, inv.OnHand);
                        if (newQty > inv.OnHand)
                            TempData["CartError"] = "Stok sayısı güncellendi. Miktar stokla sınırlandı.";
                    }
                }

                await _db.SaveChangesAsync();
            }

            if (fromQuickCart)
                TempData["OpenQuickCart"] = "1";

            return SafeReturn(returnUrl);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveCard(SavedCardFormVm model, string? returnUrl)
        {
            var userId = GetCurrentUserId();

            if (!ModelState.IsValid)
            {
                TempData["CardFormError"] = "Kart bilgilerini kontrol edin.";
                TempData["OpenCardModal"] = "1";
                return Redirect(returnUrl ?? Url.Action(nameof(Index))!);
            }

            SavedCard? card = null;

            if (model.CardID.HasValue)
            {
                card = await _db.SavedCards
                    .FirstOrDefaultAsync(c => c.CardID == model.CardID.Value
                                           && c.UserID == userId
                                           && c.IsActive);
                if (card == null)
                    return Redirect(returnUrl ?? Url.Action(nameof(Index))!);
            }
            else
            {
                card = new SavedCard
                {
                    UserID = userId,
                    Provider = "Manual",
                    Token = "MANUAL-" + Guid.NewGuid(),
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };
                _db.SavedCards.Add(card);
            }

            card.Brand = model.Brand;
            card.Last4 = model.Last4;
            card.CardExpMonth = model.ExpMonth;
            card.CardExpYear = model.ExpYear;

            await _db.SaveChangesAsync();

            return Redirect(returnUrl ?? Url.Action(nameof(Index))!);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCard(int id, string? returnUrl)
        {
            var userId = GetCurrentUserId();

            var card = await _db.SavedCards
                .FirstOrDefaultAsync(c => c.CardID == id && c.UserID == userId && c.IsActive);

            if (card != null)
            {
                card.IsActive = false;  
                await _db.SaveChangesAsync();
            }

            return Redirect(returnUrl ?? Url.Action(nameof(Index))!);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveAddress(SavedAddressFormVm model, string? returnUrl)
        {
            var userId = GetCurrentUserId();

            if (!ModelState.IsValid)
            {
                TempData["AddressFormError"] = "Adres bilgilerini kontrol edin.";
                TempData["OpenAddressModal"] = "1";
                return Redirect(returnUrl ?? Url.Action(nameof(Index))!);
            }

            SavedAddress? address = null;

            if (model.AddressID.HasValue)
            {
                address = await _db.SavedAddresses
                    .FirstOrDefaultAsync(a => a.AddressID == model.AddressID.Value
                                           && a.UserID == userId
                                           && a.IsActive);
                if (address == null)
                    return Redirect(returnUrl ?? Url.Action(nameof(Index))!);
            }
            else
            {
                address = new SavedAddress
                {
                    UserID = userId,
                    IsActive = true
                };
                _db.SavedAddresses.Add(address);
            }

            address.AddressName = model.AddressName;
            address.AddressInformation = model.AddressInformation;

            await _db.SaveChangesAsync();

            return Redirect(returnUrl ?? Url.Action(nameof(Index))!);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAddress(int id, string? returnUrl)
        {
            var userId = GetCurrentUserId();

            var address = await _db.SavedAddresses
                .FirstOrDefaultAsync(a => a.AddressID == id && a.UserID == userId && a.IsActive);

            if (address != null)
            {
                address.IsActive = false;
                await _db.SaveChangesAsync();
            }

            return Redirect(returnUrl ?? Url.Action(nameof(Index))!);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Complete(string? returnUrl = null)
        {
            var storeId = GetCurrentStoreIdOrRedirect(out var redir);
            if (redir != null) return redir;

            var userId = GetCurrentUserId();

            var strategy = _db.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

                var cart = await _db.ShoppingCarts
                    .Include(c => c.Items)
                        .ThenInclude(ci => ci.Item)
                    .FirstOrDefaultAsync(c =>
                        c.UserID == userId &&
                        c.StoreID == storeId &&
                        c.IsActive);

                if (cart == null || !cart.Items.Any())
                {
                    TempData["CartError"] = "Sepetiniz boş.";
                    return SafeReturn(returnUrl);
                }

                var itemIds = cart.Items.Select(ci => ci.ItemID).Distinct().ToList();

                // Load inventory rows for this store/items
                var inventory = await _db.InventoryItems
                    .Include(ii => ii.Item)
                    .Where(ii =>
                        ii.StoreID == storeId &&
                        itemIds.Contains(ii.ItemID) &&
                        ii.IsActive &&
                        ii.Item.IsActive)
                    .ToDictionaryAsync(ii => ii.ItemID);

                // Stock check at checkout time
                foreach (var ci in cart.Items)
                {
                    if (!inventory.TryGetValue(ci.ItemID, out var inv))
                    {
                        TempData["CartError"] = $"{ci.Item.ItemName} ürünü bu mağazada bulunamadı veya aktif değil.";
                        return SafeReturn(returnUrl);
                    }

                    if (inv.OnHand < ci.Qty)
                    {
                        TempData["CartError"] =
                            $"{ci.Item.ItemName} için yeterli stok yok. " +
                            $"Stok: {inv.OnHand}, İstenen: {ci.Qty}.";
                        return SafeReturn(returnUrl);
                    }
                }

                var nowUtc = DateTime.UtcNow;

                // Apply stock + write sales rows
                foreach (var ci in cart.Items)
                {
                    var inv = inventory[ci.ItemID];

                    inv.OnHand -= ci.Qty;

                    _db.BoughtUserItems.Add(new BoughtUserItem
                    {
                        UserID = userId,
                        ItemID = ci.ItemID,
                        StoreID = storeId,
                        Qty = ci.Qty,
                        BoughtAt = nowUtc
                    });
                }

                _db.ShoppingCartItems.RemoveRange(cart.Items);

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                TempData["OrderSuccess"] = "Siparişiniz alındı. Teşekkürler!";
                return SafeReturn(returnUrl);
            });
        }



        private IActionResult SafeReturn(string? returnUrl)
        {
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction(nameof(Index));
        }

        private int GetCurrentStoreIdOrRedirect(out IActionResult? redirect)
        {
            var storeId = _storeAccessor.CurrentStoreId;
            if (storeId == 0)
            {
                TempData["CartError"] = "Herhangi bir mağaza seçilmemiş.";
                redirect = RedirectToAction(nameof(Index));
                return 0;
            }

            redirect = null;
            return storeId;
        }


    }
}
