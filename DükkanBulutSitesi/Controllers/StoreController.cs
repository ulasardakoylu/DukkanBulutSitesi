using Microsoft.AspNetCore.Mvc;

namespace DükkanBulutSitesi.Controllers {
    public class StoreController : Controller
    {
        [HttpPost]
        public IActionResult Select(int storeId, string? returnUrl = "/")
        {
            Response.Cookies.Append("CurrentStoreId", storeId.ToString(),
                new CookieOptions { HttpOnly = true, IsEssential = true, Expires = DateTimeOffset.UtcNow.AddDays(30) });

            return LocalRedirect(returnUrl ?? "/");
        }
    }
}
