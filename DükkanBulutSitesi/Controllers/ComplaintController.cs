using DükkanBulutSitesi.Models;
using Entity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DükkanBulutSitesi.Controllers
{
    [Authorize]
    public class ComplaintController : Controller
    {
        private readonly AppDbContext _db;

        public ComplaintController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Create(int? itemId, int? commentId, string? returnUrl)
        {
            var types = await _db.ComplaintTypes
                .AsNoTracking()
                .Where(t => t.IsActive)
                .OrderBy(t => t.Name)
                .Select(t => new SelectListItem
                {
                    Value = t.ComplaintTypeID.ToString(),
                    Text = t.Name
                }).ToListAsync();

            var vm = new ComplaintCreateVm
            {
                ItemID = itemId,
                CommentID = commentId,
                TypeOptions = types,
                ReturnUrl = returnUrl
            };

            return PartialView("_ComplaintModal", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ComplaintCreateVm vm)
        {
            var returnUrl = string.IsNullOrWhiteSpace(vm.ReturnUrl)
                ? "/"
                : vm.ReturnUrl;

            if (!ModelState.IsValid || (vm.ItemID == null && vm.CommentID == null))
            {
                TempData["ComplaintError"] = "Şikayet oluşturulurken bir hata oluştu. Lütfen tüm alanları doldurun.";
                TempData["OpenComplaintModal"] = "1";
                return LocalRedirect(returnUrl);
            }

            if (vm.ItemID.HasValue)
            {
                var exists = await _db.Items.AnyAsync(i => i.ItemID == vm.ItemID.Value);
                if (!exists)
                {
                    TempData["ComplaintError"] = "Şikayet edilen ürün bulunamadı.";
                    TempData["OpenComplaintModal"] = "1";
                    return LocalRedirect(returnUrl);
                }
            }

            if (vm.CommentID.HasValue)
            {
                var exists = await _db.ItemComments.AnyAsync(c => c.CommentID == vm.CommentID.Value);
                if (!exists)
                {
                    TempData["ComplaintError"] = "Şikayet edilen yorum bulunamadı.";
                    TempData["OpenComplaintModal"] = "1";
                    return LocalRedirect(returnUrl);
                }
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var complaint = new Personel.Complaint
            {
                ItemID = vm.ItemID,
                CommentID = vm.CommentID,
                ReporterUserID = userId,
                ComplaintTypeID = vm.ComplaintTypeID,
                Description = vm.Description,
                CreatedAt = DateTime.UtcNow,
                Status = "New",
                Title = ""
            };

            _db.Complaints.Add(complaint);
            await _db.SaveChangesAsync();

            complaint.Title = $"Rapor #{complaint.ComplaintID}";
            await _db.SaveChangesAsync();

            TempData["ComplaintSuccess"] = "Şikayetiniz alınmıştır. Teşekkür ederiz.";
            return LocalRedirect(returnUrl);
        }
    }
}