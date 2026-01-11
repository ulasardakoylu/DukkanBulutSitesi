using DükkanBulutSitesi.Infrastructure;
using DükkanBulutSitesi.Models;
using DükkanBulutSitesi.Models.Manager;
using Entity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DükkanBulutSitesi.Controllers
{
    [Authorize]
    public class ManagerController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IPasswordHasher<User> _hasher;
        private readonly CurrentStoreAccessor _storeAccessor;
        private readonly IWebHostEnvironment _env;
        private readonly IAuditLogger _audit;

        public ManagerController(
            AppDbContext db,
            IPasswordHasher<User> hasher,
            CurrentStoreAccessor storeAccessor,
            IWebHostEnvironment env,
            IAuditLogger audit)
        {
            _db = db;
            _hasher = hasher;
            _storeAccessor = storeAccessor;
            _env = env;
            _audit = audit;
        }

        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            try
            {
                var storeId = _storeAccessor.CurrentStoreId;
                if (storeId != 0)
                {
                    var logo = await _db.Stores
                        .AsNoTracking()
                        .Where(s => s.StoreID == storeId)
                        .Select(s => s.StorePictureLink)
                        .FirstOrDefaultAsync();

                    ViewBag.StoreLogo = logo;
                }
            }
            catch
            {
                // don't break the page if store/logo can't load
                ViewBag.StoreLogo = null;
            }

            await base.OnActionExecutionAsync(context, next);
        }

        [Authorize]
        public async Task<IActionResult> Dashboard()
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var storeId = _storeAccessor.CurrentStoreId;

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var isActiveHere = await _db.Personels.AnyAsync(p =>
                p.UserID == userId &&
                p.StoreID == storeId &&
                p.IsActive);

            if (!isActiveHere)
                return RedirectToAction("Index", "Home");

            var vm = new ManagerDashboardVm
            {
                ManagerName = User.Identity?.Name ?? "Yönetici"
            };

            // ----------------- Aktif şikayetler (left card) -----------------
            var storeItemIds = await _db.InventoryItems
                .Where(ii => ii.StoreID == storeId)
                .Select(ii => ii.ItemID)
                .ToListAsync();

            vm.Complaints = await (
                from c in _db.Complaints
                join inv in _db.InventoryItems on c.ItemID equals inv.ItemID into invJoin
                from inv in invJoin.DefaultIfEmpty()
                join ic in _db.ItemComments on c.CommentID equals ic.CommentID into icJoin
                from ic in icJoin.DefaultIfEmpty()
                join inv2 in _db.InventoryItems on ic.ItemID equals inv2.ItemID into inv2Join
                from inv2 in inv2Join.DefaultIfEmpty()
                where (inv != null && inv.StoreID == storeId)
                   || (inv2 != null && inv2.StoreID == storeId)
                orderby c.CreatedAt descending
                select new DashboardComplaintVm
                {
                    ComplaintID = c.ComplaintID,
                    Title = c.Title == "" ? $"Rapor #{c.ComplaintID}" : c.Title
                }
            ).Take(4).AsNoTracking().ToListAsync();

            // ----------------- Bilgisi eksik personeller (middle card) -----------------
            vm.IncompletePersonnel = new List<DashboardIncompletePersonnelVm>();

            if (storeId != 0)
            {
                var personels = await _db.Personels
                    .Include(p => p.Role)
                    .Where(p => p.StoreID == storeId && p.Role.RoleName != "Manager")
                    .OrderBy(p => p.PersonelName)
                    .ThenBy(p => p.PersonelSurname)
                    .ToListAsync();

                var userIds = personels.Select(p => p.UserID).Distinct().ToList();
                var users = await _db.Users
                    .Where(u => userIds.Contains(u.UserID))
                    .ToListAsync();
                var userById = users.ToDictionary(u => u.UserID, u => u);

                foreach (var p in personels)
                {
                    var missing = new List<string>();

                    if (string.IsNullOrWhiteSpace(p.PersonelSurname))
                        missing.Add("Soyad");
                    if (string.IsNullOrWhiteSpace(p.PersonelTC))
                        missing.Add("T.C. Kimlik No");

                    userById.TryGetValue(p.UserID, out var user);

                    if (user == null || string.IsNullOrWhiteSpace(user.Email))
                        missing.Add("E-posta");
                    if (user == null || string.IsNullOrWhiteSpace(user.ProfilePictureLink))
                        missing.Add("Profil fotoğrafı");

                    if (!p.Salary.HasValue) missing.Add("Maaş");

                    if (missing.Count > 0)
                    {
                        var name = (p.PersonelName + " " + p.PersonelSurname).Trim();
                        if (string.IsNullOrWhiteSpace(name))
                            name = p.PersonelNumber;

                        vm.IncompletePersonnel.Add(new DashboardIncompletePersonnelVm
                        {
                            PersonelID = p.PersonelID,
                            DisplayName = name,
                            MissingSummary = string.Join(", ", missing)
                        });
                    }
                }

                vm.IncompletePersonnel = vm.IncompletePersonnel
                    .OrderBy(x => x.DisplayName)
                    .Take(5)
                    .ToList();
            }

            // ----------------- En son konuşmalar (bottom card) -----------------
            var currentUserIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(currentUserIdStr, out var currentUserId))
            {
                var allConvos = await _db.Conversations
                    .AsNoTracking()
                    .Include(c => c.StarterUser)
                    .Include(c => c.TargetUser)
                    .Where(c => c.StarterUserID == currentUserId || c.TargetUserID == currentUserId)
                    .ToListAsync();

                var activeConvos = allConvos.Where(c =>
                    (c.StarterUserID == currentUserId && !c.IsDeletedByStarter) ||
                    (c.TargetUserID == currentUserId && !c.IsDeletedByTarget));

                vm.RecentConversations = activeConvos
                    .Select(c =>
                    {
                        var otherUser = c.StarterUserID == currentUserId ? c.TargetUser : c.StarterUser;
                        var displayName = otherUser.UserName ?? otherUser.Email;

                        var lastMsg = c.Messages
                            .OrderByDescending(m => m.SentAt)
                            .FirstOrDefault();

                        var preview = lastMsg?.Text ?? "";
                        if (preview.Length > 80)
                            preview = preview.Substring(0, 80) + "…";

                        return new DashboardConversationVm
                        {
                            ConversationID = c.ConversationID,
                            From = displayName,
                            Preview = preview,
                            LastSentAt = lastMsg?.SentAt ?? c.CreatedAt
                        };
                    })
                    .OrderByDescending(x => x.LastSentAt)
                    .Take(4)
                    .ToList();
            }

            return View(vm);
        }



        [HttpGet]
        public async Task<IActionResult> Shifts()
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var storeId = _storeAccessor.CurrentStoreId;
            if (storeId == 0)
            {
                return RedirectToAction("Index", "Home");
            }

            var vm = new ShiftLandingVm
            {
                SelectedDate = DateTime.Today
            };

            vm.PersonelOptions = await _db.Personels
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

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> ShiftsPersonWeek(int SelectedPersonelId, DateTime? SelectedDate)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var storeId = _storeAccessor.CurrentStoreId;
            if (storeId == 0)
                return RedirectToAction("Index", "Home");

            var allPersonnel = await _db.Personels
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

            var personelId = SelectedPersonelId;
            var anchor = (SelectedDate ?? DateTime.Today).Date;

            var personel = await _db.Personels
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
                .Where(s => s.PersonelID == personelId
                            && s.IsActive
                            && s.ShiftDateStart <= weekEnd
                            && s.ShiftDateEnd >= weekStart)
                .ToListAsync();

            // ----- time off overlapping this week -----
            var timeOffs = await _db.PersonelTimeOffs
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

            return View("ShiftsPersonWeek", vm);
        }


        [HttpGet]
        [Authorize]
        public async Task<IActionResult> ShiftsDay(DateTime? date, int? selectedPersonelId)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var storeId = _storeAccessor.CurrentStoreId;
            if (storeId == 0)
                return RedirectToAction("Index", "Home");

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
                .Where(s =>
                    s.IsActive &&
                    personelIds.Contains(s.PersonelID) &&
                    s.ShiftDateStart < nextDay &&
                    s.ShiftDateEnd > day)
                .ToListAsync();

            // time-offs that cover this specific day
            var timeOffs = await _db.PersonelTimeOffs
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

            // full-day "OFF" events for each time-off
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
                    ShiftID = -t.TimeOffID,           // negative so it won't clash (just in case)
                    PersonelID = t.PersonelID,
                    Start = day,
                    End = nextDay,
                    Text = $"[İZİN - {izinTipi}]",
                    Color = "#95a5a6"
                });
            }

            ViewBag.SelectedPersonelId = selectedPersonelId;

            return View(vm);
        }


        [HttpGet]
        public async Task<IActionResult> ShiftsDayPdf(DateTime? date)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var storeId = _storeAccessor.CurrentStoreId;
            if (storeId == 0)
                return RedirectToAction("Index", "Home");

            var day = (date ?? DateTime.Today).Date;
            var nextDay = day.AddDays(1);

            // same data as ShiftsDay
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

        [HttpGet]
        public async Task<IActionResult> ShiftsPersonWeekPdf(int selectedPersonelId, DateTime? selectedDate)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var storeId = _storeAccessor.CurrentStoreId;
            if (storeId == 0)
                return RedirectToAction("Index", "Home");

            var personelId = selectedPersonelId;
            var anchor = (selectedDate ?? DateTime.Today).Date;

            var personel = await _db.Personels
                .FirstOrDefaultAsync(p => p.PersonelID == personelId && p.StoreID == storeId);

            if (personel == null)
                return NotFound();

            var dow = (int)anchor.DayOfWeek;      // Sunday = 0
            var diff = (dow == 0 ? 6 : dow - 1);  // 0..6 from Monday
            var weekStart = anchor.AddDays(-diff);
            var weekEnd = weekStart.AddDays(6);

            var shifts = await _db.PersonelShifts
                .Where(s => s.PersonelID == personelId
                            && s.IsActive
                            && s.ShiftDateStart <= weekEnd
                            && s.ShiftDateEnd >= weekStart)
                .ToListAsync();

            var timeOffs = await _db.PersonelTimeOffs
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

            var personelNumber = personel.PersonelNumber;  // <-- uses existing PersonelNumber field

            var document = new PersonelWeekShiftDocument(vm, personelNumber);
            var pdfBytes = document.GeneratePdf();

            var fileName = $"haftalik_vardiya_{personelNumber}_{weekStart:yyyyMMdd}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }


        public class ShiftApiDto // I don't know why I put this HERE rather than a VM but we are using it and I don't want to move it. It stays.
        {
            public int? Id { get; set; }
            public int PersonelId { get; set; }
            public DateTime Start { get; set; }
            public DateTime End { get; set; }
            public string? Color { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> CreateShift([FromBody] ShiftApiDto dto)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var storeId = _storeAccessor.CurrentStoreId;

            var personelOk = await _db.Personels.AnyAsync(p =>
                p.PersonelID == dto.PersonelId && p.StoreID == storeId);

            if (!personelOk)
                return Forbid();

            if (dto.End <= dto.Start)
                return BadRequest("Bitiş saati başlangıçtan sonra olmalıdır.");

            // BLOCK if there is time off during this interval
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
        public async Task<IActionResult> UpdateShift([FromBody] ShiftApiDto dto)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            if (!dto.Id.HasValue)
                return BadRequest("Missing ID.");

            var shift = await _db.PersonelShifts
                .FirstOrDefaultAsync(s => s.ShiftID == dto.Id.Value);
            if (shift == null)
                return NotFound();

            var storeId = _storeAccessor.CurrentStoreId;

            var shiftBelongs = await _db.Personels.AnyAsync(p =>
                p.PersonelID == shift.PersonelID && p.StoreID == storeId);

            if (!shiftBelongs)
                return Forbid();

            var targetPersonelOk = await _db.Personels.AnyAsync(p =>
                p.PersonelID == dto.PersonelId && p.StoreID == storeId);

            if (!targetPersonelOk)
                return Forbid();


            if (dto.End <= dto.Start)
                return BadRequest("End must be after start.");

            // BLOCK if there is time off during this interval
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
        public async Task<IActionResult> DeleteShift([FromBody] ShiftApiDto dto)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            if (!dto.Id.HasValue)
                return BadRequest("Missing ID.");

            var shift = await _db.PersonelShifts.FirstOrDefaultAsync(s => s.ShiftID == dto.Id.Value);
            if (shift == null)
                return NotFound();

            var storeId = _storeAccessor.CurrentStoreId;

            var shiftBelongs = await _db.Personels.AnyAsync(p =>
                p.PersonelID == shift.PersonelID && p.StoreID == storeId);

            if (!shiftBelongs)
                return Forbid();


            _db.PersonelShifts.Remove(shift);
            await _db.SaveChangesAsync();

            return Json(new { ok = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddPersonelTimeOff(PersonelTimeOffCreateVm vm)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            if (vm.DateEnd < vm.DateStart)
            {
                TempData["TimeOffError"] = "End date must be after start date.";
                return RedirectToAction(nameof(ShiftsPersonWeek),
                    new { selectedPersonelId = vm.PersonelID, selectedDate = vm.AnchorDate });
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
                s.IsActive = false;

            await _db.SaveChangesAsync();

            await _audit.LogAsync(
                $"Time off added. PersonelID={vm.PersonelID}, TimeOffID={entity.TimeOffID}, " +
                $"Range={vm.DateStart:yyyy-MM-dd}..{vm.DateEnd:yyyy-MM-dd}",
                HttpContext);

            return RedirectToAction(nameof(ShiftsPersonWeek),
                new { selectedPersonelId = vm.PersonelID, selectedDate = vm.AnchorDate });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePersonelTimeOff(int timeOffId, int personelId, DateTime anchorDate)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var entity = await _db.PersonelTimeOffs
                .FirstOrDefaultAsync(t => t.TimeOffID == timeOffId);

            if (entity != null)
            {
                _db.PersonelTimeOffs.Remove(entity);
                await _db.SaveChangesAsync();

                await _audit.LogAsync(
                    $"Time off deleted. PersonelID={personelId}, TimeOffID={timeOffId}",
                    HttpContext);
            }

            return RedirectToAction(nameof(ShiftsPersonWeek),
                new { selectedPersonelId = personelId, selectedDate = anchorDate });
        }


        [HttpGet]
        public async Task<IActionResult> Personnel(int? id, string status = "all")
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var storeId = _storeAccessor.CurrentStoreId;
            if (storeId == 0)
                return RedirectToAction("Index", "Home");

            var vm = new PersonnelManagementVm();
            vm.StatusFilter = status;

            var q = _db.Personels
                .AsNoTracking()
                .Include(p => p.Role)
                .Where(p => p.StoreID == storeId && p.Role.RoleName != "Manager");

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
                vm.Selected = await BuildPersonnelEditVm(selectedId.Value);

            return View(vm);
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
                .FirstAsync(p => p.PersonelID == personelId);

            var user = await _db.Users.FirstOrDefaultAsync(u => u.UserID == p.UserID);

            var storeId = _storeAccessor.CurrentStoreId;
            var roles = await _db.Roles
                .Where(r => r.StoreID == storeId)
                .OrderBy(r => r.HierarchyLevel)
                .Select(r => new SelectListItem
                {
                    Value = r.RoleID.ToString(),
                    Text = r.RoleName
                })
                .ToListAsync();

            return new PersonnelEditVm
            {
                PersonelID = p.PersonelID,
                PersonelNumber = p.PersonelNumber,
                FirstName = p.PersonelName,
                LastName = p.PersonelSurname,
                TcKimlikNo = p.PersonelTC,
                BirthDate = p.PersonelBirthDate,

                HireDate = p.PersonelCreatedAt,
                ExitDate = p.PersonelExitAt,

                IsActive = p.IsActive,
                Salary = p.Salary,

                UserEmail = user?.Email,
                ProfilePictureLink = user?.ProfilePictureLink,

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


        [HttpGet]
        public async Task<IActionResult> PersonnelPdf(int id)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var storeId = _storeAccessor.CurrentStoreId;

            // Make sure this person belongs to the current store
            var belongsToStore = await _db.Personels
                .AnyAsync(p => p.PersonelID == id && p.StoreID == storeId);

            if (!belongsToStore)
                return NotFound();

            var vm = await BuildPersonnelEditVm(id);

            // --- resolve physical path for profile picture (if any) ---
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
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var vm = pageVm.Selected;
            if (vm == null) return RedirectToAction(nameof(Personnel));

            var p = await _db.Personels.FirstAsync(x => x.PersonelID == vm.PersonelID);
            var user = await _db.Users.FirstOrDefaultAsync(u => u.UserID == p.UserID);

            // Personel fields
            p.PersonelName = vm.FirstName ?? "";
            p.PersonelSurname = vm.LastName ?? "";
            p.PersonelTC = vm.TcKimlikNo ?? "";
            p.Salary = vm.Salary;

            if (vm.BirthDate.HasValue)
                p.PersonelBirthDate = vm.BirthDate.Value;

            // ✅ Mutual exclusion (same as Owner)
            if (vm.HireDate.HasValue)
            {
                p.PersonelCreatedAt = vm.HireDate.Value;
                p.PersonelExitAt = null;
            }
            else if (vm.ExitDate.HasValue)
            {
                p.PersonelExitAt = vm.ExitDate.Value;
                p.PersonelCreatedAt = null;
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
                    user.Email = vm.UserEmail;

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
                                $"Personnel picture changed. UserID={user.UserID}, Old={oldPic}, New={picUrl}",
                                HttpContext);
                        }
                        else
                        {
                            await _audit.LogAsync(
                                $"Personnel picture set for the first time. UserID={user.UserID}, New={picUrl}",
                                HttpContext);
                        }
                    }
                }
            }

            await _db.SaveChangesAsync();

            await _audit.LogAsync(
                $"Personnel updated. PersonelID={p.PersonelID}, Name={p.PersonelName} {p.PersonelSurname}",
                HttpContext);

            TempData["PersonnelSaved"] = "Personel bilgileri kaydedildi.";
            return RedirectToAction(nameof(Personnel), new { id = p.PersonelID });
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

        [HttpGet]
        public async Task<IActionResult> Roles(int? id, bool newRole = false)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var storeId = _storeAccessor.CurrentStoreId;
            if (storeId == 0)
            {
                TempData["RoleError"] = "Lütfen önce bir mağaza seçin.";
                return RedirectToAction("Index", "Home");
            }

            var vm = new RoleManagementVm();

            vm.Roles = await _db.Roles
                .Where(r => r.StoreID == storeId)
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
                    ? await BuildRoleEditVm(selectedRoleId.Value)
                    : null;
            }

            return View(vm);
        }


        private async Task<RoleEditVm> BuildRoleEditVm(int roleId)
        {
            var storeId = _storeAccessor.CurrentStoreId;

            var role = await _db.Roles
                .Include(r => r.RolePermissions)
                .Include(r => r.PersonnelNumberCodes)
                .FirstAsync(r => r.RoleID == roleId && r.StoreID == storeId);

            var allPerms = await _db.Permissions.ToListAsync();

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
        public async Task<IActionResult> SaveRole(RoleManagementVm pageVm)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var vm = pageVm.SelectedRole;
            var storeId = _storeAccessor.CurrentStoreId;
            if (storeId == 0)
            {
                TempData["RoleError"] = "Lütfen önce bir mağaza seçin.";
                return RedirectToAction(nameof(Roles));
            }

            if (vm.RoleID == null)
            {
                // CREATE
                if (string.IsNullOrWhiteSpace(vm.RoleName))
                {
                    TempData["RoleError"] = "Rol adı gerekli.";
                    return RedirectToAction(nameof(Roles));
                }

                var role = new Role
                {
                    StoreID = storeId, 
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
                    $"Role created. RoleID={role.RoleID}, StoreID={storeId}, Name={role.RoleName}",
                    HttpContext);

                return RedirectToAction(nameof(Roles), new { id = role.RoleID });
            }
            else
            {
                // UPDATE
                var role = await _db.Roles
                    .Include(r => r.RolePermissions)
                    .FirstOrDefaultAsync(r => r.RoleID == vm.RoleID.Value && r.StoreID == storeId);

                if (role == null)
                {
                    TempData["RoleError"] = "Rol bulunamadı veya bu mağazaya ait değil.";
                    return RedirectToAction(nameof(Roles));
                }

                if (role.IsSystem)
                {
                    TempData["RoleError"] = "Bu bir sistem rolüdür, üzerinde değişiklik yapılamaz.";
                    return RedirectToAction(nameof(Roles), new { id = role.RoleID });
                }

                role.RoleName = vm.RoleName;
                role.StartTime = vm.StartTime;
                role.ExitTime = vm.ExitTime;

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
                    $"Role updated. RoleID={role.RoleID}, StoreID={storeId}, Name={role.RoleName}",
                    HttpContext);

                return RedirectToAction(nameof(Roles), new { id = role.RoleID });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteRole(int id)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var storeId = _storeAccessor.CurrentStoreId;

            var role = await _db.Roles
                .FirstOrDefaultAsync(r => r.RoleID == id && r.StoreID == storeId);

            if (role == null || role.IsSystem)
            {
                TempData["RoleError"] = "Bu rol silinemez.";
                return RedirectToAction(nameof(Roles), new { id });
            }

            _db.Roles.Remove(role);
            await _db.SaveChangesAsync();

            await _audit.LogAsync(
                $"Role deleted. RoleID={id}, StoreID={storeId}, Name={role.RoleName}",
                HttpContext);

            return RedirectToAction(nameof(Roles));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GeneratePersonnelNumber(int roleId)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var storeId = _storeAccessor.CurrentStoreId;
            if (storeId == 0)
            {
                TempData["RoleError"] = "Önce bir mağaza seçmelisiniz.";
                return RedirectToAction(nameof(Roles), new { id = roleId });
            }

            var role = await _db.Roles
                .FirstOrDefaultAsync(r => r.RoleID == roleId && r.StoreID == storeId);
            if (role == null)
            {
                TempData["RoleError"] = "Rol bulunamadı veya bu mağazaya ait değil.";
                return RedirectToAction(nameof(Roles));
            }

            string code;
            bool exists;
            do
            {
                code = GenerateInviteCode();
                exists = await _db.PersonnelNumberCodes
                    .AnyAsync(p => p.Code == code);
            } while (exists);

            var pn = new PersonnelNumberCode
            {
                RoleID = roleId,
                Code = code,
                IsUsed = false,
                CreatedAt = DateTime.UtcNow,
                StoreID = storeId
            };

            _db.PersonnelNumberCodes.Add(pn);
            await _db.SaveChangesAsync();

            TempData["LastGeneratedCode"] = code;

            await _audit.LogAsync(
                $"Personnel number generated. RoleID={roleId}, StoreID={storeId}, Code={code}",
                HttpContext);

            return RedirectToAction(nameof(Roles), new { id = roleId });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveHistory(PersonnelHistoryVm vm)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var storeId = _storeAccessor.CurrentStoreId;
            var ok = await _db.Personels.AnyAsync(p => p.PersonelID == vm.PersonelID && p.StoreID == storeId);
            if (!ok) return Forbid();

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
                $"Work history {action}. PersonelID={vm.PersonelID}, HistoryID={entity.PersonelWorkHistoryID}, Title={vm.Title}",
                HttpContext);

            return RedirectToAction(nameof(Personnel), new { id = vm.PersonelID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteHistory(int id, int personelId)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var h = await _db.PersonelHistories
                .FirstOrDefaultAsync(x => x.PersonelWorkHistoryID == id);
            if (h != null)
            {
                _db.PersonelHistories.Remove(h);
                await _db.SaveChangesAsync();

                await _audit.LogAsync(
                    $"Work history deleted. PersonelID={personelId}, HistoryID={id}, Title={h.PersonelWorkHistoryName}",
                    HttpContext);
            }
            return RedirectToAction(nameof(Personnel), new { id = personelId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveEducation(PersonnelEducationVm vm)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var storeId = _storeAccessor.CurrentStoreId;
            var ok = await _db.Personels.AnyAsync(p => p.PersonelID == vm.PersonelID && p.StoreID == storeId);
            if (!ok) return Forbid();

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
                $"Education {action}. PersonelID={vm.PersonelID}, EducationID={entity.PersonelEducationID}, School={vm.School}",
                HttpContext);

            return RedirectToAction(nameof(Personnel), new { id = vm.PersonelID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteEducation(int id, int personelId)
        {

            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var e = await _db.PersonelEducations
                .FirstOrDefaultAsync(x => x.PersonelEducationID == id);
            if (e != null)
            {
                _db.PersonelEducations.Remove(e);
                await _db.SaveChangesAsync();

                await _audit.LogAsync(
                    $"Education deleted. PersonelID={personelId}, EducationID={id}, School={e.PersonelEducationName}",
                    HttpContext);
            }
            return RedirectToAction(nameof(Personnel), new { id = personelId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveCertificate(PersonnelCertificateVm vm)
        {

            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var storeId = _storeAccessor.CurrentStoreId;
            var ok = await _db.Personels.AnyAsync(p => p.PersonelID == vm.PersonelID && p.StoreID == storeId);
            if (!ok) return Forbid();

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
                $"Certificate {action}. PersonelID={vm.PersonelID}, CertificateID={entity.PersonelCertificateID}, Name={vm.CertificateName}",
                HttpContext);

            return RedirectToAction(nameof(Personnel), new { id = vm.PersonelID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCertificate(int id, int personelId)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var c = await _db.PersonelCertificates
                .FirstOrDefaultAsync(x => x.PersonelCertificateID == id);
            if (c != null)
            {
                _db.PersonelCertificates.Remove(c);
                await _db.SaveChangesAsync();

                await _audit.LogAsync(
                    $"Certificate deleted. PersonelID={personelId}, CertificateID={id}, Name={c.PersonelCertificateName}",
                    HttpContext);
            }
            return RedirectToAction(nameof(Personnel), new { id = personelId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveWarning(PersonnelWarningVm vm)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var storeId = _storeAccessor.CurrentStoreId;
            var ok = await _db.Personels.AnyAsync(p => p.PersonelID == vm.PersonelID && p.StoreID == storeId);
            if (!ok) return Forbid();

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
                ? (User.Identity?.Name ?? "Yönetici")
                : vm.GivenBy;

            await _db.SaveChangesAsync();

            var action = isNew ? "created" : "updated";
            await _audit.LogAsync(
                $"Warning {action}. PersonelID={vm.PersonelID}, WarningID={entity.PersonelWarningID}, Level={entity.WarningLevel}",
                HttpContext);

            return RedirectToAction(nameof(Personnel), new { id = vm.PersonelID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteWarning(int id, int personelId)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var w = await _db.PersonelWarnings
                .FirstOrDefaultAsync(x => x.PersonelWarningID == id);
            if (w != null)
            {
                _db.PersonelWarnings.Remove(w);
                await _db.SaveChangesAsync();

                await _audit.LogAsync(
                    $"Warning deleted. PersonelID={personelId}, WarningID={id}, Level={w.WarningLevel}",
                    HttpContext);
            }

            return RedirectToAction(nameof(Personnel), new { id = personelId });
        }

        [HttpGet]
        public async Task<IActionResult> PendingComments()
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var storeId = _storeAccessor.CurrentStoreId;

            var vm = await _db.ItemComments
                .AsNoTracking()
                .Include(c => c.Item)
                .Where(c =>
                    c.ModerationStatus == CommentModerationStatus.Pending &&
                    c.PendingDescription != null &&
                    _db.InventoryItems.Any(ii => ii.ItemID == c.ItemID && ii.StoreID == storeId)
                )
                .OrderBy(c => c.CreatedAt)
                .Select(c => new PendingCommentVm
                {
                    CommentID = c.CommentID,
                    ItemID = c.ItemID,
                    ItemName = c.Item.ItemName,
                    PendingText = c.PendingDescription!,
                    CreatedAt = c.CreatedAt
                })
                .ToListAsync();

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApprovePendingComment(int commentId)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var storeId = _storeAccessor.CurrentStoreId;

            var c = await _db.ItemComments
                .Include(x => x.Item)
                .FirstOrDefaultAsync(x => x.CommentID == commentId);

            if (c == null) return NotFound();

            var allowed = await _db.InventoryItems.AnyAsync(ii =>
                ii.StoreID == storeId && ii.ItemID == c.ItemID);

            if (!allowed) return Forbid();

            var pendingText = c.PendingDescription ?? ""; // snapshot BEFORE clearing

            // APPLY pending -> live
            c.CommentDescription = pendingText.Length > 0 ? pendingText : c.CommentDescription;
            c.PendingDescription = null;

            c.ModerationStatus = CommentModerationStatus.Approved;
            c.IsDescriptionApproved = true;
            c.ModerationDecidedAt = DateTime.UtcNow;

            // LOG (Owner history modal will read this)
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int.TryParse(userIdStr, out var performedByUserId);

            // ✅ Human readable notes
            var itemName = (c.Item?.ItemName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(itemName))
                itemName = $"#{c.ItemID}";

            var text = (pendingText ?? "").Trim();
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " "); // normalize whitespace
            const int maxLen = 250;
            if (text.Length > maxLen) text = text[..maxLen] + "…";

            var notes = $"Ürün: {itemName} | Yorum: {text}";

            _db.ComplaintActionLogs.Add(new ComplaintActionLog
            {
                CommentID = c.CommentID,
                ItemID = c.ItemID,
                Action = "CommentApproved",
                PerformedByUserID = performedByUserId,
                PerformedAt = DateTime.UtcNow,
                Notes = notes
            });

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(PendingComments));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectPendingComment(int commentId)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var storeId = _storeAccessor.CurrentStoreId;

            var c = await _db.ItemComments
                .Include(x => x.Item)
                .FirstOrDefaultAsync(x => x.CommentID == commentId);

            if (c == null) return NotFound();

            var allowed = await _db.InventoryItems.AnyAsync(ii =>
                ii.StoreID == storeId && ii.ItemID == c.ItemID);

            if (!allowed) return Forbid();

            // snapshot BEFORE clearing
            var pendingText = c.PendingDescription ?? "";

            // Reject: drop pending text
            c.PendingDescription = null;

            c.ModerationStatus = CommentModerationStatus.Rejected;
            c.IsDescriptionApproved = false;
            c.ModerationDecidedAt = DateTime.UtcNow;

            // -------- Owner modal logs (human readable Notes) --------
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int.TryParse(userIdStr, out var performedByUserId);

            var itemName = (c.Item?.ItemName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(itemName))
                itemName = $"#{c.ItemID}";

            var notesText = (pendingText ?? "").Trim();
            notesText = System.Text.RegularExpressions.Regex.Replace(notesText, @"\s+", " ");
            const int notesMax = 250;
            if (notesText.Length > notesMax) notesText = notesText[..notesMax] + "…";

            _db.ComplaintActionLogs.Add(new ComplaintActionLog
            {
                CommentID = c.CommentID,
                ItemID = c.ItemID,
                Action = "CommentRejected",
                PerformedByUserID = performedByUserId,
                PerformedAt = DateTime.UtcNow,
                Notes = $"Ürün: {itemName} | Yorum: {notesText}"
            });

            // -------- Manager activity log: MUST store preview here --------
            var preview = (pendingText ?? "").Trim();
            preview = System.Text.RegularExpressions.Regex.Replace(preview, @"\s+", " ");
            preview = preview.Replace("\"", "'"); // don't break ParseArgs
            preview = preview.Replace(",", " ");  // safe with your ParseArgs pattern
            if (preview.Length > 120) preview = preview[..120] + "…";

            // IMPORTANT: include the action name so your importer/action mapper stays consistent
            await _audit.LogAsync(
                $"RejectPendingComment, commentId={c.CommentID}, commentPreview=\"{preview}\"",
                HttpContext);

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(PendingComments));
        }






        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCommentFromComplaint(int complaintId)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var comp = await _db.Complaints
                .Include(c => c.Comment)
                .FirstOrDefaultAsync(c => c.ComplaintID == complaintId);

            if (comp?.Comment == null)
                return NotFound();

            _db.ItemComments.Remove(comp.Comment);
            comp.Status = "Resolved";

            await _db.SaveChangesAsync();

            await _audit.LogAsync(
                $"Comment deleted via complaint. ComplaintID={complaintId}, CommentID={comp.CommentID}",
                HttpContext);

            return RedirectToAction(nameof(Complaints), new { id = complaintId });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IgnoreCommentComplaint(int complaintId)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var comp = await _db.Complaints.FirstOrDefaultAsync(c => c.ComplaintID == complaintId);
            if (comp == null) return NotFound();

            comp.Status = "Ignored";
            await _db.SaveChangesAsync();

            await _audit.LogAsync(
                $"Comment complaint ignored. ComplaintID={complaintId}",
                HttpContext);

            return RedirectToAction(nameof(Complaints), new { id = complaintId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteItemFromComplaint(int complaintId)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var comp = await _db.Complaints.FirstOrDefaultAsync(c => c.ComplaintID == complaintId);
            if (comp?.ItemID == null)
                return NotFound();

            return await DeleteItem(comp.ItemID.Value);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IgnoreItemComplaint(int complaintId)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var comp = await _db.Complaints.FirstOrDefaultAsync(c => c.ComplaintID == complaintId);
            if (comp == null) return NotFound();

            comp.Status = "Ignored";
            await _db.SaveChangesAsync();

            await _audit.LogAsync(
                $"Item complaint ignored. ComplaintID={complaintId}",
                HttpContext);

            return RedirectToAction(nameof(Complaints), new { id = complaintId });
        }


        [HttpGet]
        public async Task<IActionResult> Complaints(int? id)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var storeId = _storeAccessor.CurrentStoreId;

            var vm = new ComplaintManagementVm();

            // complaint IDs for this store
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

            if (!complaintIdsForStore.Any())
                return View(vm);

            // load only those complaints
            vm.Complaints = await _db.Complaints
                .AsNoTracking()
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

            // selected complaint, but only inside this store
            int? selectedId = id;
            if (!selectedId.HasValue || !complaintIdsForStore.Contains(selectedId.Value))
            {
                selectedId = vm.Complaints.FirstOrDefault()?.ComplaintID;
            }

            if (selectedId.HasValue)
            {
                vm.Selected = await BuildComplaintDetailVm(selectedId.Value);
            }

            return View(vm);
        }

        private async Task<ComplaintDetailVm> BuildComplaintDetailVm(int complaintId)
        {
            var c = await _db.Complaints
                .AsNoTracking()
                .AsSplitQuery()
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
        public async Task<IActionResult> UpdateComplaint(int id, string status, string? managerNotes)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var c = await _db.Complaints.FirstOrDefaultAsync(x => x.ComplaintID == id);
            if (c == null) return NotFound();

            c.Status = status;
            c.ManagerNotes = managerNotes;
            await _db.SaveChangesAsync();

            await _audit.LogAsync(
                $"Complaint updated. ComplaintID={id}, Status={status}",
                HttpContext);

            return RedirectToAction(nameof(Complaints), new { id });
        }

        private string GenerateInviteCode()
        {
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[4];
            rng.GetBytes(bytes);
            var num = BitConverter.ToUInt32(bytes, 0) % 100000000;
            return num.ToString("D8");
        }

        [Authorize]
        public async Task<IActionResult> Conversations(int? id, int? issueRequestId)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var storeId = _storeAccessor.CurrentStoreId;

            var vm = new ConversationPageVm();

            // ------------------------------------------------------------
            // Issue Requests (Pending + Rejected) for this manager + store
            // ------------------------------------------------------------
            var reqsForManager = await _db.IssueRequests
                .Where(x => x.StoreID == storeId
                            && x.TargetManagerUserID == currentUserId
                            && (x.Status == IssueRequestStatus.Pending || x.Status == IssueRequestStatus.Rejected))
                .OrderByDescending(x => x.DecidedAt ?? x.CreatedAt)
                .ToListAsync();

            var reqUserIds = reqsForManager
                .Select(x => x.RequesterUserID)
                .Distinct()
                .ToList();

            var reqPersonels = await _db.Personels
                .Where(p => p.StoreID == storeId && reqUserIds.Contains(p.UserID))
                .ToListAsync();

            var reqPersonelByUserId = reqPersonels
                .GroupBy(p => p.UserID)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(p => p.IsActive)
                          .ThenByDescending(p => p.PersonelID)
                          .First()
                );

            var reqUsers = await _db.Users
                .Where(u => reqUserIds.Contains(u.UserID))
                .ToListAsync();

            var reqUserById = reqUsers.ToDictionary(u => u.UserID, u => u);

            IssueRequestSummaryVm MapReq(IssueRequest x)
            {
                reqPersonelByUserId.TryGetValue(x.RequesterUserID, out var per);
                reqUserById.TryGetValue(x.RequesterUserID, out var usr);

                var displayName =
                    per != null
                        ? (per.PersonelName + " " + per.PersonelSurname).Trim()
                        : (usr?.UserName ?? usr?.Email ?? $"User#{x.RequesterUserID}");

                return new IssueRequestSummaryVm
                {
                    IssueRequestID = x.IssueRequestID,
                    RequesterUserID = x.RequesterUserID,
                    RequesterPersonelID = per?.PersonelID ?? 0,
                    RequesterName = displayName,
                    Message = x.Message,
                    CreatedAt = x.CreatedAt
                };
            }

            vm.PendingIssueRequests = reqsForManager
                .Where(x => x.Status == IssueRequestStatus.Pending)
                .OrderByDescending(x => x.CreatedAt)
                .Select(MapReq)
                .ToList();

            vm.RejectedIssueRequests = reqsForManager
                .Where(x => x.Status == IssueRequestStatus.Rejected)
                .OrderByDescending(x => x.DecidedAt ?? x.CreatedAt)
                .Select(MapReq)
                .ToList();

            // If an issue request is selected, prepare right panel data
            // BUT do NOT return early; we still want conversation list on the left.
            if (issueRequestId.HasValue)
            {
                var req = reqsForManager.FirstOrDefault(r => r.IssueRequestID == issueRequestId.Value);
                if (req != null)
                {
                    reqPersonelByUserId.TryGetValue(req.RequesterUserID, out var per);
                    reqUserById.TryGetValue(req.RequesterUserID, out var usr);

                    vm.IsIssueRequestSelected = true;
                    vm.SelectedIssueRequestID = req.IssueRequestID;
                    vm.SelectedPersonelName =
                        per != null
                            ? (per.PersonelName + " " + per.PersonelSurname).Trim()
                            : (usr?.UserName ?? usr?.Email ?? $"User#{req.RequesterUserID}");

                    vm.SelectedIssueMessage = req.Message;
                    vm.SelectedIssueCreatedAt = req.CreatedAt;

                    // When an issue is selected, we intentionally do not select a conversation.
                    vm.SelectedConversationID = null;
                }
            }

            // ------------------------------
            // Normal conversation page load
            // ------------------------------
            vm.PersonelChoices = await _db.Personels
                .Where(p => p.UserID != currentUserId && p.StoreID == storeId)
                .OrderBy(p => p.PersonelName)
                .Select(p => new PersonelLookupVm
                {
                    PersonelID = p.PersonelID,
                    DisplayName = (p.PersonelName + " " + p.PersonelSurname).Trim()
                })
                .ToListAsync();

            vm.OwnerChoices = await _db.StoreOwners
                .Include(so => so.User)
                .Where(so => so.StoreID == storeId && so.IsActive)
                .Select(so => new SelectListItem
                {
                    Value = so.UserID.ToString(),
                    Text = (so.User.UserName ?? so.User.Email)
                })
                .ToListAsync();

            var convoQuery = _db.Conversations
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
                .Where(p => otherUserIds.Contains(p.UserID))
                .ToListAsync();

            var personelByUserId = relatedPersonels
                .GroupBy(p => p.UserID)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(p => p.IsActive)
                          .ThenByDescending(p => p.PersonelID)
                          .First()
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
                            IsSystem = (m.Text == "Karşı taraf sohbeti kapattı.")
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

            if (!vm.IsIssueRequestSelected)
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
                            IsSystem = (m.Text == "Karşı taraf sohbeti kapattı.")
                        })
                        .ToListAsync();
                }
            }

            return View(vm);
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> StartConversation(int targetPersonelId, string firstMessage)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            if (targetPersonelId == 0 || string.IsNullOrWhiteSpace(firstMessage))
                return RedirectToAction(nameof(Conversations));

            var targetPersonel = await _db.Personels
                .FirstOrDefaultAsync(p => p.PersonelID == targetPersonelId);

            if (targetPersonel == null)
                return RedirectToAction(nameof(Conversations));

            var targetUserId = targetPersonel.UserID;

            var convo = await _db.Conversations.FirstOrDefaultAsync(c =>
                (c.StarterUserID == currentUserId &&
                 c.TargetUserID == targetUserId &&
                 !c.IsDeletedByStarter) ||
                (c.StarterUserID == targetUserId &&
                 c.TargetUserID == currentUserId &&
                 !c.IsDeletedByTarget));

            if (convo == null)
            {
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

            var msg = new ConversationMessage
            {
                ConversationID = convo.ConversationID,
                SenderUserID = currentUserId,
                Text = firstMessage.Trim(),
                SentAt = DateTime.UtcNow
            };

            _db.ConversationMessages.Add(msg);
            await _db.SaveChangesAsync();

            await _audit.LogAsync(
                $"Conversation started or reused (active only). ConversationID={convo.ConversationID}, TargetPersonelID={targetPersonelId}, FirstMessageLength={firstMessage.Trim().Length}",
                HttpContext);

            return RedirectToAction(nameof(Conversations), new { id = convo.ConversationID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> StartConversationWithOwner(int targetOwnerUserId, string firstMessage)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var storeId = _storeAccessor.CurrentStoreId;

            if (targetOwnerUserId == 0 || string.IsNullOrWhiteSpace(firstMessage))
                return RedirectToAction(nameof(Conversations));

            // Ensure this user is actually an ACTIVE owner of the current store
            var isOwnerOfStore = await _db.StoreOwners.AnyAsync(so =>
                so.StoreID == storeId &&
                so.IsActive &&
                so.UserID == targetOwnerUserId);

            if (!isOwnerOfStore)
                return RedirectToAction(nameof(Conversations));

            // Find an existing ACTIVE conversation between manager(user) and owner(user)
            var convo = await _db.Conversations.FirstOrDefaultAsync(c =>
                (c.StarterUserID == currentUserId &&
                 c.TargetUserID == targetOwnerUserId &&
                 !c.IsDeletedByStarter) ||
                (c.StarterUserID == targetOwnerUserId &&
                 c.TargetUserID == currentUserId &&
                 !c.IsDeletedByTarget));

            if (convo == null)
            {
                convo = new Conversation
                {
                    StarterUserID = currentUserId,
                    TargetUserID = targetOwnerUserId,
                    CreatedAt = DateTime.UtcNow,
                    IsDeletedByStarter = false,
                    IsDeletedByTarget = false
                };

                _db.Conversations.Add(convo);
                await _db.SaveChangesAsync();
            }

            _db.ConversationMessages.Add(new ConversationMessage
            {
                ConversationID = convo.ConversationID,
                SenderUserID = currentUserId,
                Text = firstMessage.Trim(),
                SentAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            await _audit.LogAsync(
                $"Manager->Owner conversation started/reused. ConversationID={convo.ConversationID}, TargetOwnerUserId={targetOwnerUserId}",
                HttpContext);

            return RedirectToAction(nameof(Conversations), new { id = convo.ConversationID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> AcceptIssueRequest(int issueRequestId)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var storeId = _storeAccessor.CurrentStoreId;

            var req = await _db.IssueRequests.FirstOrDefaultAsync(x =>
                x.IssueRequestID == issueRequestId &&
                x.StoreID == storeId &&
                x.TargetManagerUserID == currentUserId &&
                x.Status == IssueRequestStatus.Pending);

            if (req == null)
            {
                TempData["ConvError"] = "İstek bulunamadı.";
                return RedirectToAction(nameof(Conversations));
            }

            var convo = new Conversation
            {
                StarterUserID = req.RequesterUserID,
                TargetUserID = currentUserId,
                CreatedAt = DateTime.UtcNow,
                IsDeletedByStarter = false,
                IsDeletedByTarget = false
            };

            _db.Conversations.Add(convo);
            await _db.SaveChangesAsync();

            _db.ConversationMessages.Add(new ConversationMessage
            {
                ConversationID = convo.ConversationID,
                SenderUserID = req.RequesterUserID,
                Text = req.Message,
                SentAt = DateTime.UtcNow
            });

            req.Status = IssueRequestStatus.Accepted;
            req.DecidedAt = DateTime.UtcNow;
            req.DecidedByUserID = currentUserId;
            req.CreatedConversationID = convo.ConversationID;

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Conversations), new { id = convo.ConversationID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> RejectIssueRequest(int issueRequestId)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var storeId = _storeAccessor.CurrentStoreId;

            var req = await _db.IssueRequests.FirstOrDefaultAsync(x =>
                x.IssueRequestID == issueRequestId &&
                x.StoreID == storeId &&
                x.TargetManagerUserID == currentUserId &&
                x.Status == IssueRequestStatus.Pending);

            if (req == null)
            {
                TempData["ConvError"] = "İstek bulunamadı.";
                return RedirectToAction(nameof(Conversations));
            }

            req.Status = IssueRequestStatus.Rejected;
            req.DecidedAt = DateTime.UtcNow;
            req.DecidedByUserID = currentUserId;

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Conversations));
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> SendConversationMessage(int conversationId, int personelId, string message)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            if (conversationId == 0 || string.IsNullOrWhiteSpace(message))
            {
                return RedirectToAction(nameof(Conversations), new { id = conversationId });
            }

            var convo = await _db.Conversations
                .FirstOrDefaultAsync(c => c.ConversationID == conversationId);

            if (convo == null)
            {
                TempData["ConvError"] = "Konuşma bulunamadı.";
                return RedirectToAction(nameof(Conversations));
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

            await _audit.LogAsync(
                $"Conversation message sent. ConversationID={conversationId}, PersonelID={personelId}, Length={message.Trim().Length}",
                HttpContext);

            return RedirectToAction(nameof(Conversations), new { id = conversationId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConversation(int conversationId)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var convo = await _db.Conversations
                .FirstOrDefaultAsync(c => c.ConversationID == conversationId);

            if (convo == null)
                return RedirectToAction(nameof(Conversations));

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
            else if (convo.StarterUserID != currentUserId && convo.TargetUserID != currentUserId)
            {
                return Forbid();
            }

            if (changed)
            {
                var noticeText = "Karşı taraf sohbeti kapattı.";

                var notice = new ConversationMessage
                {
                    ConversationID = conversationId,
                    SenderUserID = currentUserId, 
                    Text = noticeText,
                    SentAt = DateTime.UtcNow
                };

                _db.ConversationMessages.Add(notice);

                await _audit.LogAsync(
                    $"Conversation soft-deleted by user. ConversationID={conversationId}, UserID={currentUserId}",
                    HttpContext);
            }

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Conversations));
        }

        public async Task<IActionResult> Items()
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var storeId = _storeAccessor.CurrentStoreId;

            var invList = await _db.InventoryItems
                .AsNoTracking()
                .Include(ii => ii.Item)
                .Where(ii => ii.StoreID == storeId && ii.IsActive)
                .OrderBy(ii => ii.Item.ItemName)
                .ToListAsync();

            var vm = new ManagerItemListVm
            {
                Items = invList.Select(ii => new ManagerItemListRowVm
                {
                    ItemID = ii.ItemID,
                    ItemName = ii.Item.ItemName,
                    ItemPrice = ii.Item.ItemPrice,
                    IsActive = ii.Item.IsActive,
                    PictureLink = ii.Item.PictureLink,
                    OnHand = ii.OnHand
                }).ToList()
            };

            return View(vm);
        }

        public IActionResult CreateItem()
        {
            var vm = new ManagerItemEditVm
            {
                IsActive = true,
                OnHand = 1,
                ItemPrice = 0.01m,
                Description = ""
            };

            return View("EditItem", vm);
        }

        public async Task<IActionResult> EditItem(int id)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var storeId = _storeAccessor.CurrentStoreId;

            var inv = await _db.InventoryItems
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.StoreID == storeId && x.ItemID == id && x.IsActive);

            if (inv == null)
                return Forbid(); // or NotFound()

            var item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.ItemID == id);
            if (item == null) return NotFound();


            await _audit.LogAsync(
                $"EditItem GET, ItemID={item.ItemID}, ItemName=\"{item.ItemName}\"",
                HttpContext);

            var categories = await _db.CategoryItems
                .AsNoTracking()
                .Where(c => c.ItemID == id)
                .Select(c => c.CategoryName)
                .ToListAsync();

            var keywords = await _db.KeywordsOfItems
                .AsNoTracking()
                .Where(k => k.ItemID == id)
                .Select(k => k.KeywordName)
                .ToListAsync();

            var vm = new ManagerItemEditVm
            {
                ItemID = item.ItemID,
                ItemName = item.ItemName,
                ItemPrice = item.ItemPrice,
                Description = item.Description ?? "",
                IsActive = item.IsActive,
                OnHand = inv.OnHand,
                ExistingPictureLink = item.PictureLink,
                CategoriesCsv = string.Join(", ", categories),
                KeywordsCsv = string.Join(", ", keywords)
            };

            return View("EditItem", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateItem(ManagerItemEditVm vm)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            // normalize
            vm.ItemName = (vm.ItemName ?? "").Trim();
            vm.Description = (vm.Description ?? "").Trim();
            vm.CategoriesCsv = (vm.CategoriesCsv ?? "").Trim();
            vm.KeywordsCsv = (vm.KeywordsCsv ?? "").Trim();

            // server-side required rules
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

            var picUrl = await SaveItemPictureAsync(vm.PictureFile);

            var item = new Item
            {
                ItemName = vm.ItemName,
                ItemPrice = vm.ItemPrice,
                Description = vm.Description,
                CreatedAt = DateTime.UtcNow,
                IsActive = vm.IsActive,
                PictureLink = picUrl
            };

            _db.Items.Add(item);
            await _db.SaveChangesAsync();

            var storeId = _storeAccessor.CurrentStoreId;

            // Always create inventory row (stock is required >= 1)
            _db.InventoryItems.Add(new InventoryItem
            {
                StoreID = storeId,
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

            await _audit.LogAsync(
                $"Item created. ItemID={item.ItemID}, Name={item.ItemName}",
                HttpContext);

            return RedirectToAction(nameof(Items));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditItem(ManagerItemEditVm vm)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            if (!vm.ItemID.HasValue)
                return BadRequest();

            // normalize
            vm.ItemName = (vm.ItemName ?? "").Trim();
            vm.Description = (vm.Description ?? "").Trim();
            vm.CategoriesCsv = (vm.CategoriesCsv ?? "").Trim();
            vm.KeywordsCsv = (vm.KeywordsCsv ?? "").Trim();

            // server-side required rules
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

            var storeId = _storeAccessor.CurrentStoreId;

            var inv = await _db.InventoryItems
                .FirstOrDefaultAsync(x => x.StoreID == storeId && x.ItemID == itemId);

            if (inv == null)
                return Forbid();

            var item = await _db.Items.FirstOrDefaultAsync(i => i.ItemID == itemId);
            if (item == null) return NotFound();

            item.ItemName = vm.ItemName;
            item.ItemPrice = vm.ItemPrice;
            item.Description = vm.Description;
            item.IsActive = vm.IsActive;

            var newPic = await SaveItemPictureAsync(vm.PictureFile);
            if (!string.IsNullOrEmpty(newPic))
            {
                var oldPic = item.PictureLink;
                item.PictureLink = newPic;

                if (!string.IsNullOrWhiteSpace(oldPic))
                {
                    DeletePhysicalFile(oldPic);
                    await _audit.LogAsync(
                        $"Item picture changed. ItemID={item.ItemID}, Old={oldPic}, New={newPic}",
                        HttpContext);
                }
                else
                {
                    await _audit.LogAsync(
                        $"Item picture set. ItemID={item.ItemID}, New={newPic}",
                        HttpContext);
                }
            }

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

            await _audit.LogAsync(
                $"EditItem POST, ItemID={item.ItemID}, ItemName=\"{item.ItemName}\"",
                HttpContext);


            return RedirectToAction(nameof(Items));
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


        private async Task<string?> SaveItemPictureAsync(IFormFile? file)
        {
            if (file == null || file.Length == 0)
                return null;

            var uploadsRoot = Path.Combine(_env.WebRootPath, "img", "products");
            Directory.CreateDirectory(uploadsRoot);

            var ext = Path.GetExtension(file.FileName);
            var fileName = $"{Guid.NewGuid()}{ext}";
            var fullPath = Path.Combine(uploadsRoot, fileName);

            using (var stream = System.IO.File.Create(fullPath))
            {
                await file.CopyToAsync(stream);
            }

            return $"/img/products/{fileName}";
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


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteItem(int id)
    {
        if (!await UserCanManageCurrentStoreAsync())
            return Forbid();

        // (Optional) get item name for audit *before* deleting
        var itemName = await _db.Items
            .AsNoTracking()
            .Where(i => i.ItemID == id)
            .Select(i => i.ItemName)
            .FirstOrDefaultAsync();

        await using var tx = await _db.Database.BeginTransactionAsync();

        // 1) Break FK references from ComplaintActionLogs (since FK is NO ACTION / RESTRICT)
        //    - If you made ItemID/CommentID nullable, these updates will work.
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

        // 2) Delete children first (raw SQL avoids EF OUTPUT + trigger issue)
        await _db.Database.ExecuteSqlInterpolatedAsync($@"DELETE FROM dbo.InventoryItems WHERE ItemID = {id};");
        await _db.Database.ExecuteSqlInterpolatedAsync($@"DELETE FROM dbo.ItemStocks WHERE ItemID = {id};");
        await _db.Database.ExecuteSqlInterpolatedAsync($@"DELETE FROM dbo.CategoryItems WHERE ItemID = {id};");
        await _db.Database.ExecuteSqlInterpolatedAsync($@"DELETE FROM dbo.KeywordsOfItems WHERE ItemID = {id};");
        await _db.Database.ExecuteSqlInterpolatedAsync($@"DELETE FROM dbo.ItemComments WHERE ItemID = {id};");

        // You have this relationship in the model too; include it so it won't block item delete
        await _db.Database.ExecuteSqlInterpolatedAsync($@"DELETE FROM dbo.SpecialOfferItems WHERE ItemID = {id};");

        // 3) Delete the item (raw SQL => no OUTPUT clause => triggers won't error)
        await _db.Database.ExecuteSqlInterpolatedAsync($@"DELETE FROM dbo.Items WHERE ItemID = {id};");

        await tx.CommitAsync();

            HttpContext.Items["AuditDescription"] = $"id={id}, itemName=\"{itemName}\"";

            await _audit.LogAsync(
                $"action=DeleteItem, itemId={id}, itemName={itemName}",
                HttpContext);

            return RedirectToAction(nameof(Items));
    }


    [Authorize]
        public async Task<IActionResult> SpecialOffers(int? id, bool newOffer = false)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var storeId = _storeAccessor.CurrentStoreId;
            var now = DateTime.Now;

            var vm = new SpecialOfferManagementVm();

            // All offers for this store
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

            // Determine which offer is selected in the UI
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
                var end = start.AddDays(7); // default

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

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveSpecialOffer([Bind(Prefix = "SelectedOffer")] SpecialOfferEditVm vm)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var storeId = _storeAccessor.CurrentStoreId;

            if (!vm.Start.HasValue || !vm.End.HasValue || vm.End <= vm.Start)
            {
                TempData["SpecialOfferError"] = "Geçerli bir başlangıç ve bitiş tarihi giriniz.";
                return RedirectToAction(nameof(SpecialOffers), new { id = vm.SpecialOfferID });
            }

            vm.Name = (vm.Name ?? "").Trim();

            if (string.IsNullOrWhiteSpace(vm.Name))
            {
                TempData["SpecialOfferError"] = "Kampanya adı boş olamaz.";
                return RedirectToAction(nameof(SpecialOffers), new { id = vm.SpecialOfferID });
            }

            var includedItems = (vm.Items ?? new List<SpecialOfferItemVm>())
                .Where(x => x.Included)
                .ToList();

            if (!includedItems.Any())
            {
                TempData["SpecialOfferError"] = "Kampanya oluşturmak için en az 1 ürün seçmelisiniz.";
                return RedirectToAction(nameof(SpecialOffers), new { id = vm.SpecialOfferID });
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

                // must be > 0
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
                // NO <br/> here (pre-line in view will handle newlines)
                var msg = "Seçilen ürünlerde fiyat hatası var:\n" + string.Join("\n", priceErrors.Take(10));
                TempData["SpecialOfferError"] = msg;
                return RedirectToAction(nameof(SpecialOffers), new { id = vm.SpecialOfferID });
            }

            SpecialOffer entity;

            if (vm.SpecialOfferID == null)
            {
                entity = new SpecialOffer
                {
                    StoreID = storeId
                };
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

            // Replace item list
            var existingItems = await _db.SpecialOfferItems
                .Where(x => x.SpecialOfferID == entity.SpecialOfferID)
                .ToListAsync();

            _db.SpecialOfferItems.RemoveRange(existingItems);

            var allowedItemIds = await _db.InventoryItems
                .Where(ii => ii.StoreID == storeId && ii.IsActive)
                .Select(ii => ii.ItemID)
                .ToListAsync();

            var allowedSet = allowedItemIds.ToHashSet();

            var toAdd = (vm.Items ?? new List<SpecialOfferItemVm>())
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

            await _audit.LogAsync(
                $"Special offer saved. OfferID={entity.SpecialOfferID}, StoreID={storeId}, Name={entity.SpecialOfferName}",
                HttpContext);

            return RedirectToAction(nameof(SpecialOffers), new { id = entity.SpecialOfferID });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EndSpecialOfferEarly(int id)
        {
            if (!await UserCanManageCurrentStoreAsync())
                return Forbid();

            var storeId = _storeAccessor.CurrentStoreId;
            var now = DateTime.Now;

            var current = await _db.SpecialOffers
                .FirstOrDefaultAsync(o => o.SpecialOfferID == id && o.StoreID == storeId);

            if (current == null)
                return RedirectToAction(nameof(SpecialOffers));

            if (current.IsCancelled || current.SpecialOfferDateEnd <= now)
            {
                TempData["SpecialOfferError"] = "Bu kampanya zaten sona ermiş veya iptal edilmiş.";
                return RedirectToAction(nameof(SpecialOffers), new { id });
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
            {
                next.SpecialOfferDateStart = now;
            }

            await _db.SaveChangesAsync();

            await _audit.LogAsync(
                $"Special offer ended early. OfferID={id}, StoreID={storeId}",
                HttpContext);

            return RedirectToAction(nameof(SpecialOffers), new { id });
        }

        private async Task<bool> UserCanManageCurrentStoreAsync()
        {
            var storeId = _storeAccessor.CurrentStoreId;
            if (storeId == 0)
                return false;

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out var userId))
                return false;

            var isOwner = await _db.StoreOwners
                .AnyAsync(o =>
                    o.UserID == userId &&
                    o.StoreID == storeId &&
                    o.IsActive);

            if (isOwner)
                return true;

            var managerRoleId = await _db.Roles
                .Where(r => r.StoreID == storeId && r.RoleName == "Manager")
                .Select(r => r.RoleID)
                .FirstOrDefaultAsync();

            if (managerRoleId == 0)
                return false;

            var isManager = await _db.Personels
                .AnyAsync(p =>
                    p.UserID == userId &&
                    p.StoreID == storeId &&
                    p.RoleID == managerRoleId);

            return isManager;
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
