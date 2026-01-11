using DükkanBulutSitesi.Models;
using DükkanBulutSitesi.Models.Personnel;
using Entity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace DükkanBulutSitesi.Controllers
{
    [Authorize]
    [Authorize(Policy = "ActivePersonnel")]
    public class PersonnelController : Controller
    {
        private readonly AppDbContext _db;
        private readonly Microsoft.AspNetCore.Identity.IPasswordHasher<User> _hasher;

        public PersonnelController(AppDbContext db, Microsoft.AspNetCore.Identity.IPasswordHasher<User> hasher)
        {
            _db = db;
            _hasher = hasher;
        }

        [Authorize(Policy = "ActivePersonnel")]
        public async Task<IActionResult> Dashboard()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
                return RedirectToAction("Index", "Home");

            var personel = await _db.Personels
                .AsNoTracking()
                .Include(p => p.Role)
                .FirstOrDefaultAsync(p => p.UserID == userId);

            if (personel == null)
                return Forbid();

            var storeId = personel.StoreID;

            var managerRoleId = await _db.Roles
                .Where(r => r.StoreID == storeId && r.RoleName == "Manager")
                .Select(r => r.RoleID)
                .FirstOrDefaultAsync();

            var vm = new PersonnelDashboardVm
            {
                DisplayName = $"{personel.PersonelName} {personel.PersonelSurname}".Trim(),
                RoleName = personel.Role?.RoleName ?? "Personel",
                Now = DateTime.Now
            };

            var now = DateTime.Now;
            var dayStart = now.Date;
            var dayEnd = dayStart.AddDays(1);

            // Overlap filter (handles overnight shifts too)
            var shifts = await _db.PersonelShifts
                .AsNoTracking()
                .Where(s => s.PersonelID == personel.PersonelID
                         && s.IsActive
                         && s.ShiftDateStart < dayEnd
                         && s.ShiftDateEnd >= dayStart)
                .OrderBy(s => s.ShiftDateStart)
                .ToListAsync();

            const int EARLY_MIN = 3;

            foreach (var sh in shifts)
            {
                var fixedEnd = FixEnd(sh.ShiftDateEnd);

                var vmShift = new ShiftStatusVM
                {
                    ShiftID = sh.ShiftID,
                    Start = sh.ShiftDateStart,
                    End = fixedEnd,
                    IsClockedIn = sh.IsClockedIn,
                    IsClockedOut = sh.IsClockedOut
                };

                // ---- IN status ----
                var inOpen = sh.ShiftDateStart.AddMinutes(-EARLY_MIN);

                if (sh.IsClockedIn)
                {
                    vmShift.InColor = "green";
                    vmShift.InTooltip = "İşe giriş yaptınız.";
                }
                else if (now < inOpen)
                {
                    vmShift.InColor = "gray";
                    var mins = Math.Max(0, (int)Math.Ceiling((inOpen - now).TotalMinutes));
                    vmShift.InTooltip = $"İşe girişe {mins} dk var.";
                }
                else if (now >= inOpen && now < sh.ShiftDateStart)
                {
                    vmShift.InColor = "yellow";
                    var mins = Math.Max(0, (int)Math.Ceiling((sh.ShiftDateStart - now).TotalMinutes));
                    vmShift.InTooltip = $"İşe giriş zamanı yaklaşıyor: {mins} dk kaldı.";
                }
                else if (now >= sh.ShiftDateStart && now <= fixedEnd)
                {
                    vmShift.InColor = "red";
                    vmShift.InTooltip = "İşe giriş yapmadınız!";
                }
                else
                {
                    vmShift.InColor = "red";
                    vmShift.InTooltip = "Vardiya geçti, işe giriş yapılmadı.";
                }

                // ---- OUT status ----
                var outOpen = fixedEnd.AddMinutes(-EARLY_MIN);
                var outClose = fixedEnd.AddMinutes(EARLY_MIN);

                if (sh.IsClockedOut)
                {
                    vmShift.OutColor = "green";
                    vmShift.OutTooltip = "İşten çıkış yaptınız.";
                }
                else if (!sh.IsClockedIn)
                {
                    vmShift.OutColor = "gray";
                    vmShift.OutTooltip = "Önce işe giriş yapmalısınız.";
                }
                else if (now < outOpen)
                {
                    vmShift.OutColor = "gray";
                    vmShift.OutTooltip = "Çıkış zamanı henüz gelmedi.";
                }
                else if (now >= outOpen && now < fixedEnd)
                {
                    vmShift.OutColor = "yellow";
                    var mins = Math.Max(0, (int)Math.Ceiling((fixedEnd - now).TotalMinutes));
                    vmShift.OutTooltip = $"Çıkışa {mins} dk kaldı.";
                }
                else if (now >= fixedEnd && now <= outClose)
                {
                    vmShift.OutColor = "red";
                    vmShift.OutTooltip = "Çıkış yapmadınız!";
                }
                else
                {
                    vmShift.OutColor = "red";
                    vmShift.OutTooltip = "Çıkış süresi geçti, çıkış yapılmadı.";
                }

                vm.Shifts.Add(vmShift);
            }

            // Dropdown targets
            if (managerRoleId != 0)
            {
                vm.IssueTargets = await _db.Personels
                    .AsNoTracking()
                    .Include(p => p.User)
                    .Where(p => p.StoreID == storeId
                                && p.RoleID == managerRoleId
                                && p.IsActive
                                && p.User.IsActive)
                    .OrderBy(p => p.PersonelName)
                    .Select(p => new IssueTargetVm
                    {
                        ManagerUserID = p.UserID,
                        DisplayName = (p.PersonelName + " " + p.PersonelSurname).Trim()
                    })
                    .ToListAsync();
            }

            // last 3 Accepted/Rejected decisions as notifications
            var decidedRequests = await _db.IssueRequests
                .AsNoTracking()
                .Where(x => x.StoreID == storeId
                            && x.RequesterUserID == userId
                            && (x.Status == IssueRequestStatus.Accepted || x.Status == IssueRequestStatus.Rejected))
                .OrderByDescending(x => x.DecidedAt ?? x.CreatedAt)
                .Take(3)
                .ToListAsync();

            if (decidedRequests.Any())
            {
                var mgrIds = decidedRequests.Select(x => x.TargetManagerUserID).Distinct().ToList();

                var mgrNames = await _db.Personels
                    .AsNoTracking()
                    .Where(p => p.StoreID == storeId && mgrIds.Contains(p.UserID))
                    .Select(p => new
                    {
                        p.UserID,
                        Name = (p.PersonelName + " " + p.PersonelSurname).Trim()
                    })
                    .ToListAsync();

                var mgrNameById = mgrNames
                    .GroupBy(x => x.UserID)
                    .ToDictionary(g => g.Key, g => g.First().Name);

                foreach (var r in decidedRequests)
                {
                    mgrNameById.TryGetValue(r.TargetManagerUserID, out var mgrName);
                    mgrName ??= $"Yönetici ({r.TargetManagerUserID})";

                    if (r.Status == IssueRequestStatus.Accepted)
                    {
                        vm.Notifications.Add(new PersonnelNotificationVm
                        {
                            Type = "success",
                            Title = "Sorun mesajınız kabul edildi",
                            Text = $"Yönetici: {mgrName}. Sohbet açıldı. Konuşmalar sayfasından devam edebilirsiniz."
                        });
                    }
                    else
                    {
                        vm.Notifications.Add(new PersonnelNotificationVm
                        {
                            Type = "danger",
                            Title = "Sorun mesajınız reddedildi",
                            Text = $"Yönetici: {mgrName}. İsterseniz yeni bir sorun mesajı gönderebilirsiniz."
                        });
                    }
                }
            }

            return View(vm);
        }

        [Authorize(Policy = "Perm:QuickWorkPanel")]
        public async Task<IActionResult> QuickWorkPanel(int? noteId, int? chartItemId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
                return RedirectToAction("Index", "Home");

            var personel = await _db.Personels
                .AsNoTracking()
                .Include(p => p.Store)
                .FirstOrDefaultAsync(p => p.UserID == userId);

            if (personel == null) return Forbid();

            var storeId = personel.StoreID;
            var personelId = personel.PersonelID;

            // “Local day” for daily checklist
            var day = DateTime.Now.Date;

            var vm = new QuickWorkPanelVm
            {
                Day = day
            };

            // Campaign (active SpecialOffer)
            var now = DateTime.Now;
            var kampanya = await _db.SpecialOffers
                .AsNoTracking()
                .Where(x => x.StoreID == storeId && !x.IsCancelled && x.SpecialOfferDateStart <= now && x.SpecialOfferDateEnd >= now)
                .OrderByDescending(x => x.SpecialOfferDateStart)
                .FirstOrDefaultAsync();

            if (kampanya != null)
            {
                vm.CampaignName = kampanya.SpecialOfferName;
                vm.CampaignDescription = kampanya.SpecialOfferDescription;
                vm.CampaignStart = kampanya.SpecialOfferDateStart;
                vm.CampaignEnd = kampanya.SpecialOfferDateEnd;

                vm.CampaignItems = await (
                    from si in _db.SpecialOfferItems.AsNoTracking()
                    join it in _db.Items.AsNoTracking() on si.ItemID equals it.ItemID
                    where si.SpecialOfferID == kampanya.SpecialOfferID
                    select new QuickCampaignItemVm
                    {
                        ItemID = it.ItemID,
                        ItemName = it.ItemName,
                        PictureLink = it.PictureLink,
                        NewPrice = si.NewPrice
                    }
                ).ToListAsync();
            }

            // Notes (selected note or latest)
            PersonnelQuickNote? selectedNote = null;

            if (noteId == 0)
            {
                vm.SelectedNoteId = null;
                vm.NoteTitle = "";
                vm.NoteBody = "";
                vm.NoteCreatedAt = null;
                vm.NoteUpdatedAt = null;
            }
            else
            {
                if (noteId.HasValue)
                {
                    selectedNote = await _db.PersonnelQuickNotes
                        .AsNoTracking()
                        .Where(x =>
                            x.PersonelID == personelId &&
                            x.IsActive &&
                            x.PersonnelQuickNoteID == noteId.Value)
                        .FirstOrDefaultAsync();
                }

                if (selectedNote == null)
                {
                    selectedNote = await _db.PersonnelQuickNotes
                        .AsNoTracking()
                        .Where(x => x.PersonelID == personelId && x.IsActive)
                        .OrderByDescending(x => x.UpdatedAt)
                        .FirstOrDefaultAsync();
                }

                if (selectedNote != null)
                {
                    vm.SelectedNoteId = selectedNote.PersonnelQuickNoteID;
                    vm.NoteTitle = selectedNote.Title;
                    vm.NoteBody = selectedNote.Body;
                    vm.NoteCreatedAt = selectedNote.CreatedAt;
                    vm.NoteUpdatedAt = selectedNote.UpdatedAt;
                }
            }

            // Checklist template
            var template = new List<(string key, string text)>
    {
        ("shelves", "Raflardaki ürünler konuldu"),
        ("cashiers", "Kasalar çalışıyor"),
        ("labels", "Fiyat değişikliği olan ürünlerin etiketleri yenilendi mi?"),
        ("lights", "Işıklar çalışıyor mu?")
    };

            var states = await _db.PersonnelDailyChecklistStates
                .AsNoTracking()
                .Where(x => x.PersonelID == personelId && x.Day == day)
                .ToListAsync();

            var stateByKey = states.ToDictionary(x => x.TaskKey, x => x.Done);

            vm.Checklist = template.Select(t => new QuickChecklistTaskVm
            {
                Key = t.key,
                Text = t.text,
                Done = stateByKey.TryGetValue(t.key, out var done) && done
            }).ToList();

            // Stock chart item selection (default: first campaign item or nothing)
            int? effectiveItemId = chartItemId;

            if (!effectiveItemId.HasValue && vm.CampaignItems.Any())
                effectiveItemId = vm.CampaignItems.First().ItemID;

            vm.SelectedChartItemId = effectiveItemId;

            if (effectiveItemId.HasValue)
            {
                var item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(x => x.ItemID == effectiveItemId.Value);
                vm.SelectedChartItemName = item?.ItemName;

                // last 20 stock changes for the item
                var points = await _db.ItemStocks.AsNoTracking()
                    .Where(x => x.ItemID == effectiveItemId.Value)
                    .OrderByDescending(x => x.ChangedAt)
                    .Take(20)
                    .ToListAsync();

                vm.StockPoints = points
                    .OrderBy(x => x.ChangedAt)
                    .Select(x => new QuickStockPointVm
                    {
                        Label = x.ChangedAt.ToLocalTime().ToString("dd.MM HH:mm"),
                        Value = x.StockCount
                    })
                    .ToList();
            }

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveQuickNote(int? noteId, string title, string body)
        {
            if (noteId == 0) noteId = null;

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
                return RedirectToAction("Index", "Home");

            var personel = await _db.Personels.FirstOrDefaultAsync(p => p.UserID == userId);
            if (personel == null) return Forbid();

            title = (title ?? "").Trim();
            body = (body ?? "").Trim();

            if (string.IsNullOrWhiteSpace(title)) title = "Not";
            if (string.IsNullOrWhiteSpace(body)) body = "";

            if (noteId.HasValue)
            {
                var existing = await _db.PersonnelQuickNotes
                    .FirstOrDefaultAsync(x => x.PersonelID == personel.PersonelID && x.IsActive && x.PersonnelQuickNoteID == noteId.Value);

                if (existing != null)
                {
                    existing.Title = title;
                    existing.Body = body;
                    existing.UpdatedAt = DateTime.UtcNow;

                    await _db.SaveChangesAsync();
                    return RedirectToAction(nameof(QuickWorkPanel), new { noteId = existing.PersonnelQuickNoteID });
                }
            }

            var created = new PersonnelQuickNote
            {
                PersonelID = personel.PersonelID,
                Title = title,
                Body = body,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsActive = true
            };

            _db.PersonnelQuickNotes.Add(created);
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(QuickWorkPanel), new { noteId = created.PersonnelQuickNoteID });
        }

        [HttpGet]
        public async Task<IActionResult> GetQuickNoteHistory()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
                return Unauthorized();

            var personel = await _db.Personels.AsNoTracking().FirstOrDefaultAsync(p => p.UserID == userId);
            if (personel == null) return Forbid();

            var notes = await _db.PersonnelQuickNotes.AsNoTracking()
                .Where(x => x.PersonelID == personel.PersonelID && x.IsActive)
                .OrderByDescending(x => x.UpdatedAt)
                .Take(50)
                .Select(x => new
                {
                    id = x.PersonnelQuickNoteID,
                    title = x.Title,
                    createdAt = x.CreatedAt,
                    updatedAt = x.UpdatedAt
                })
                .ToListAsync();

            return Json(notes);
        }

        [Authorize(Policy = "Perm:SecurityWorkPanel")]
        public async Task<IActionResult> SecurityWorkPanel()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
                return RedirectToAction("Index", "Home");

            var personel = await _db.Personels
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserID == userId);

            if (personel == null) return Forbid();

            var storeId = personel.StoreID;
            var personelId = personel.PersonelID;
            var day = DateTime.Now.Date;

            // Checklist template for security
            var template = new List<(string key, string text)>
    {
        ("sec_doors", "Kapılar kontrol edildi"),
        ("sec_cameras", "Kameralar kontrol edildi"),
        ("sec_alarm", "Alarm sistemi aktif mi?"),
        ("sec_patrol", "Devriye / tur atıldı"),
        ("sec_incidents", "Olay defteri kontrol edildi")
    };

            var states = await _db.PersonnelDailyChecklistStates
                .AsNoTracking()
                .Where(x => x.PersonelID == personelId && x.Day == day)
                .ToListAsync();

            var stateByKey = states.ToDictionary(x => x.TaskKey, x => x.Done);

            // Banned / suspicious people list (same store only)
            var bannedPeople = await (
                from sp in _db.SuspiciousPeopleDatabase.AsNoTracking()
                join p in _db.Personels.AsNoTracking() on sp.PersonelID equals p.PersonelID
                where sp.IsActive && p.StoreID == storeId
                orderby sp.CreatedAt descending
                select new BannedPersonCardVm
                {
                    SPeopleID = sp.SPeopleID,
                    Description = sp.Description,
                    Reason = sp.Reason,
                    Temporary = sp.Temporary,
                    PictureLink = sp.PictureLink,
                    CreatedAt = sp.CreatedAt
                }
            ).Take(50).ToListAsync();

            // Recent reports created by this security person
            var recentReports = await _db.ReportCreation
                .AsNoTracking()
                .AsSplitQuery()
                .Include(r => r.Problems)
                .Where(r => r.PersonelID == personelId)
                .OrderByDescending(r => r.ReportID)
                .Take(10)
                .Select(r => new SecurityReportCardVm
                {
                    ReportID = r.ReportID,
                    CreatedAt = r.ActivityDateStart,
                    ReportedPlace = r.ReportedPlace,
                    ActivityDateStart = r.ActivityDateStart,
                    ActivityDateEnd = r.ActivityDateEnd,
                    InjuredCount = r.InjuredCount,
                    Description = r.Description,
                    Problems = r.Problems.Select(x => x.ProblemName).ToList()
                })
                .ToListAsync();

            var vm = new SecurityWorkPanelVm
            {
                Day = day,
                Checklist = template.Select(t => new SecurityChecklistTaskVm
                {
                    Key = t.key,
                    Text = t.text,
                    Done = stateByKey.TryGetValue(t.key, out var done) && done
                }).ToList(),

                ActivityDateStart = DateTime.Now,
                ActivityDateEnd = null,

                BannedPeople = bannedPeople,
                RecentReports = recentReports
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "Perm:SecurityWorkPanel")]
        public async Task<IActionResult> AddBannedPerson(string reason, string description, bool temporary, string? pictureLink)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
                return RedirectToAction("Index", "Home");

            var personel = await _db.Personels.FirstOrDefaultAsync(p => p.UserID == userId);
            if (personel == null) return Forbid();

            reason = (reason ?? "").Trim();
            description = (description ?? "").Trim();
            pictureLink = string.IsNullOrWhiteSpace(pictureLink) ? null : pictureLink.Trim();

            if (string.IsNullOrWhiteSpace(reason) || string.IsNullOrWhiteSpace(description))
            {
                TempData["SecError"] = "Yasaklı kişi eklemek için 'Sebep' ve 'Açıklama' doldurulmalı.";
                return RedirectToAction(nameof(SecurityWorkPanel));
            }

            // Basic safety: avoid extreme lengths even if DB allows longer
            if (reason.Length > 200) reason = reason.Substring(0, 200);
            if (description.Length > 1000) description = description.Substring(0, 1000);

            _db.SuspiciousPeopleDatabase.Add(new SuspiciousPerson
            {
                PersonelID = personel.PersonelID,
                Reason = reason,
                Description = description,
                Temporary = temporary,
                PictureLink = pictureLink,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });

            await _db.SaveChangesAsync();

            TempData["SecSaved"] = "Yasaklı kişi kaydı eklendi.";
            return RedirectToAction(nameof(SecurityWorkPanel));
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleSecurityChecklist(string taskKey, bool done)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
                return RedirectToAction("Index", "Home");

            var personel = await _db.Personels.FirstOrDefaultAsync(p => p.UserID == userId);
            if (personel == null) return Forbid();

            var day = DateTime.Now.Date;
            taskKey = (taskKey ?? "").Trim();
            if (string.IsNullOrWhiteSpace(taskKey))
                return RedirectToAction(nameof(SecurityWorkPanel));

            var row = await _db.PersonnelDailyChecklistStates
                .FirstOrDefaultAsync(x => x.PersonelID == personel.PersonelID && x.Day == day && x.TaskKey == taskKey);

            if (row == null)
            {
                _db.PersonnelDailyChecklistStates.Add(new PersonnelDailyChecklistState
                {
                    PersonelID = personel.PersonelID,
                    Day = day,
                    TaskKey = taskKey,
                    Done = done,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                row.Done = done;
                row.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(SecurityWorkPanel));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateSecurityReport(SecurityWorkPanelVm vm)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
                return RedirectToAction("Index", "Home");

            var personel = await _db.Personels.FirstOrDefaultAsync(p => p.UserID == userId);
            if (personel == null) return Forbid();

            vm.ReportedPlace = (vm.ReportedPlace ?? "").Trim();
            vm.Description = (vm.Description ?? "").Trim();
            vm.PictureLink = string.IsNullOrWhiteSpace(vm.PictureLink) ? null : vm.PictureLink.Trim();

            if (string.IsNullOrWhiteSpace(vm.ReportedPlace) || string.IsNullOrWhiteSpace(vm.Description))
            {
                TempData["SecError"] = "Rapor oluşturmak için 'Yer' ve 'Açıklama' doldurulmalı.";
                return RedirectToAction(nameof(SecurityWorkPanel));
            }

            var report = new Report
            {
                PersonelID = personel.PersonelID,
                ReportedPlace = vm.ReportedPlace,
                ActivityDateStart = vm.ActivityDateStart,
                ActivityDateEnd = vm.ActivityDateEnd,
                InjuredCount = vm.InjuredCount < 0 ? 0 : vm.InjuredCount,
                Description = vm.Description,
                PictureLink = vm.PictureLink
            };

            _db.ReportCreation.Add(report);
            await _db.SaveChangesAsync();

            // optional problems (each line = one problem)
            var problems = (vm.ProblemsText ?? "")
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .Distinct()
                .Take(10)
                .ToList();

            if (problems.Any())
            {
                foreach (var pr in problems)
                {
                    _db.ReportCreationProblems.Add(new ReportProblem
                    {
                        ReportID = report.ReportID,
                        ProblemName = pr
                    });
                }
                await _db.SaveChangesAsync();
            }

            TempData["SecSaved"] = "Rapor oluşturuldu.";
            return RedirectToAction(nameof(SecurityWorkPanel));
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleDailyChecklist(string taskKey, bool done)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
                return RedirectToAction("Index", "Home");

            var personel = await _db.Personels.FirstOrDefaultAsync(p => p.UserID == userId);
            if (personel == null) return Forbid();

            var day = DateTime.Now.Date;

            taskKey = (taskKey ?? "").Trim();
            if (string.IsNullOrWhiteSpace(taskKey))
                return RedirectToAction(nameof(QuickWorkPanel));

            var row = await _db.PersonnelDailyChecklistStates
                .FirstOrDefaultAsync(x => x.PersonelID == personel.PersonelID && x.Day == day && x.TaskKey == taskKey);

            if (row == null)
            {
                _db.PersonnelDailyChecklistStates.Add(new PersonnelDailyChecklistState
                {
                    PersonelID = personel.PersonelID,
                    Day = day,
                    TaskKey = taskKey,
                    Done = done,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                row.Done = done;
                row.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(QuickWorkPanel));
        }

        [HttpGet]
        public async Task<IActionResult> SearchQuickStockItems(string q)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
                return Unauthorized();

            var personel = await _db.Personels.AsNoTracking().FirstOrDefaultAsync(p => p.UserID == userId);
            if (personel == null) return Forbid();

            var storeId = personel.StoreID;
            q = (q ?? "").Trim();

            var results = await _db.InventoryItems.AsNoTracking()
                .Include(ii => ii.Item)
                .Where(ii => ii.StoreID == storeId
                             && ii.IsActive
                             && (q == "" || ii.Item!.ItemName.Contains(q)))
                .OrderBy(ii => ii.Item!.ItemName)
                .Take(60)
                .Select(ii => new
                {
                    itemId = ii.ItemID,
                    name = ii.Item!.ItemName,
                    picture = ii.Item.PictureLink,
                    onHand = ii.OnHand
                })
                .ToListAsync();

            return Json(results);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SetQuickChartItem(int itemId)
        {
            return RedirectToAction(nameof(QuickWorkPanel), new { chartItemId = itemId });
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendIssueRequest(int managerUserId, string message)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
                return RedirectToAction("Index", "Home");

            if (managerUserId == 0 || string.IsNullOrWhiteSpace(message))
            {
                TempData["IssueError"] = "Lütfen yönetici seçin ve mesaj yazın.";
                return RedirectToAction(nameof(Dashboard));
            }

            var personel = await _db.Personels
                .FirstOrDefaultAsync(p => p.UserID == userId);

            if (personel == null) return Forbid();

            var storeId = personel.StoreID;

            // validate manager belongs to same store and is manager
            var managerRoleId = await _db.Roles
                .Where(r => r.StoreID == storeId && r.RoleName == "Manager")
                .Select(r => r.RoleID)
                .FirstOrDefaultAsync();

            var isValidManager = await _db.Personels
                .Include(p => p.User)
                .AnyAsync(p =>
                    p.StoreID == storeId &&
                    p.RoleID == managerRoleId &&
                    p.UserID == managerUserId &&
                    p.IsActive &&
                    p.User.IsActive);

            if (!isValidManager)
            {
                TempData["IssueError"] = "Seçilen yönetici bulunamadı.";
                return RedirectToAction(nameof(Dashboard));
            }

            // prevent multiple pending requests to same manager
            var hasPending = await _db.IssueRequests.AnyAsync(x =>
                x.StoreID == storeId &&
                x.RequesterUserID == userId &&
                x.TargetManagerUserID == managerUserId &&
                x.Status == IssueRequestStatus.Pending);

            if (hasPending)
            {
                TempData["IssueError"] = "Bu yöneticiye zaten bekleyen bir sorun mesajınız var.";
                return RedirectToAction(nameof(Dashboard));
            }

            _db.IssueRequests.Add(new IssueRequest
            {
                StoreID = storeId,
                RequesterUserID = userId,
                TargetManagerUserID = managerUserId,
                Message = message.Trim(),
                Status = IssueRequestStatus.Pending,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Conversations));
        }


        // --- PERSONEL KONUSMALARI SAYFASI ------------------------------------
        public async Task<IActionResult> Conversations(int? id, int? issueRequestId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
                return RedirectToAction("Index", "Home");

            var personnel = await _db.Personels
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.UserID == userId);

            if (personnel == null)
                return Forbid();

            int myUserId = personnel.UserID;
            int storeId = personnel.StoreID;

            // Normal conversations
            var convos = await _db.Conversations
                .AsNoTracking()
                .AsSplitQuery()
                .Include(c => c.StarterUser)
                .Include(c => c.TargetUser)
                .Include(c => c.Messages)
                .Where(c => c.StarterUserID == myUserId || c.TargetUserID == myUserId)
                .ToListAsync();

            // Issue requests (only for this personnel + store)
            var myRequests = await _db.IssueRequests
                .AsNoTracking()
                .Where(x => x.RequesterUserID == myUserId && x.StoreID == storeId)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();

            // Manager display names for issue request header
            var managerUserIds = myRequests.Select(x => x.TargetManagerUserID).Distinct().ToList();
            var managerNames = await _db.Personels
                .AsNoTracking()
                .Where(p => managerUserIds.Contains(p.UserID))
                .Select(p => new
                {
                    p.UserID,
                    Name = (p.PersonelName + " " + p.PersonelSurname).Trim()
                })
                .ToListAsync();

            var managerNameByUserId = managerNames
                .GroupBy(x => x.UserID)
                .ToDictionary(g => g.Key, g => g.First().Name);

            var vm = new PersonnelConversationPageVm
            {
                CurrentUserID = myUserId
            };

            // Modal lists: Accepted / Rejected (store + requester)
            var allMine = await _db.IssueRequests
                .AsNoTracking()
                .Where(x => x.StoreID == storeId && x.RequesterUserID == myUserId)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();

            vm.MyIssueRequests = allMine;
            vm.PendingIssues = allMine.Where(x => x.Status == IssueRequestStatus.Pending).ToList();
            vm.AcceptedIssues = allMine.Where(x => x.Status == IssueRequestStatus.Accepted).ToList();
            vm.RejectedIssues = allMine.Where(x => x.Status == IssueRequestStatus.Rejected).ToList();

            // Build LEFT list threads => ONLY conversations
            var threads = new List<PersonnelThreadSummaryVm>();

            foreach (var c in convos)
            {
                var lastMsg = c.Messages
                    .OrderByDescending(m => m.SentAt)
                    .FirstOrDefault();

                var otherUser = c.StarterUserID == myUserId ? c.TargetUser : c.StarterUser;

                threads.Add(new PersonnelThreadSummaryVm
                {
                    Kind = PersonnelThreadKind.Conversation,
                    ConversationID = c.ConversationID,
                    IssueRequestID = 0,
                    OtherUserName = otherUser.UserName ?? otherUser.Email,
                    LastMessage = lastMsg?.Text ?? "",
                    LastSentAt = lastMsg?.SentAt ?? c.CreatedAt
                });
            }

            vm.Threads = threads
                .OrderByDescending(t => t.LastSentAt ?? DateTime.MinValue)
                .ToList();

            // Selection logic:
            // - If issueRequestId is present => show issue details on right (does NOT appear in left list)
            // - Else select conversation by id or first conversation
            if (issueRequestId.HasValue)
            {
                vm.SelectedKind = PersonnelThreadKind.IssueRequest;
                vm.SelectedIssueRequestID = issueRequestId.Value;

                var req = myRequests.FirstOrDefault(x => x.IssueRequestID == issueRequestId.Value);
                if (req != null)
                {
                    vm.IssueMessage = req.Message;
                    vm.IssueStatus = req.Status;
                    vm.IssueCreatedAt = req.CreatedAt;

                    // If accepted and conversation exists -> jump to real conversation
                    if (req.Status == IssueRequestStatus.Accepted && req.CreatedConversationID.HasValue)
                    {
                        return RedirectToAction(nameof(Conversations), new { id = req.CreatedConversationID.Value });
                    }

                    managerNameByUserId.TryGetValue(req.TargetManagerUserID, out var mgrName);
                    vm.OtherUserName = !string.IsNullOrWhiteSpace(mgrName) ? mgrName : "Yönetici";
                }

                return View(vm);
            }

            // Normal conversation selection
            vm.SelectedConversationID = id ?? vm.Threads.FirstOrDefault()?.ConversationID;

            if (vm.SelectedConversationID.HasValue)
            {
                var selected = convos.FirstOrDefault(c => c.ConversationID == vm.SelectedConversationID.Value);
                if (selected != null)
                {
                    vm.SelectedKind = PersonnelThreadKind.Conversation;

                    vm.SelectedMessages = selected.Messages
                        .OrderBy(m => m.SentAt)
                        .ToList();

                    var otherUser = selected.StarterUserID == myUserId ? selected.TargetUser : selected.StarterUser;
                    vm.OtherUserName = otherUser.UserName ?? otherUser.Email;
                }
            }

            return View(vm);
        }

        // --- MESAJ GÖNDERME (VAR OLAN SOHBETE) -------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendConversationMessage(int conversationId, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return RedirectToAction("Conversations", new { id = conversationId });

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
                return RedirectToAction("Index", "Home");

            var user = await _db.Users.FindAsync(userId);
            if (user == null)
                return Unauthorized();

            bool isParticipant = await _db.Conversations.AnyAsync(c =>
                c.ConversationID == conversationId &&
                (c.StarterUserID == userId || c.TargetUserID == userId));

            if (!isParticipant)
                return Forbid();

            var msg = new ConversationMessage
            {
                ConversationID = conversationId,
                SenderUserID = user.UserID,
                Text = message,
                SentAt = DateTime.UtcNow
            };

            _db.ConversationMessages.Add(msg);
            await _db.SaveChangesAsync();

            return RedirectToAction("Conversations", new { id = conversationId });
        }

        // --- PERSONEL: STOK KONTROL ------------------------------------------

        [Authorize(Policy = "Perm:StockControl")]
        public async Task<IActionResult> StockControl(int? selectedItemId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
                return RedirectToAction("Index", "Home");

            var personnel = await _db.Personels
                .Include(p => p.Role)
                .Include(p => p.Store)
                .FirstOrDefaultAsync(p => p.UserID == userId);

            if (personnel == null)
                return Forbid();

            var storeId = personnel.StoreID;

            var store = await _db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.StoreID == storeId);
            if (store == null)
                return NotFound();

            var nowUtc = DateTime.UtcNow;
            var since30 = nowUtc.AddDays(-30);
            var since7 = nowUtc.AddDays(-7);

            // -------------------------------------------------
            // In-stock breakdown (OnHand) by Category (store-specific)
            // -------------------------------------------------
            var inStockByCategory = await (
                from ii in _db.InventoryItems.AsNoTracking()
                join ci in _db.CategoryItems.AsNoTracking() on ii.ItemID equals ci.ItemID
                where ii.StoreID == storeId && ii.IsActive
                group ii by ci.CategoryName into g
                select new StockSliceVm
                {
                    Label = g.Key,
                    Value = g.Sum(x => (decimal)x.OnHand)
                }
            )
            .OrderByDescending(x => x.Value)
            .Take(12)
            .ToListAsync();

            if (!inStockByCategory.Any())
            {
                inStockByCategory = await _db.InventoryItems.AsNoTracking()
                    .Where(ii => ii.StoreID == storeId && ii.IsActive)
                    .GroupBy(ii => "Kategori Yok")
                    .Select(g => new StockSliceVm { Label = g.Key, Value = g.Sum(x => (decimal)x.OnHand) })
                    .ToListAsync();
            }

            // -------------------------------------------------
            // Sold breakdown (Qty) by Category - last 30 days (store-specific)
            // -------------------------------------------------
            var soldByCategory = await (
                from b in _db.BoughtUserItems.AsNoTracking()
                where b.StoreID == storeId && b.BoughtAt >= since30
                join ii in _db.InventoryItems.AsNoTracking()
                    on new { StoreID = b.StoreID, ItemID = b.ItemID }
                    equals new { ii.StoreID, ii.ItemID }
                where ii.IsActive
                join ci in _db.CategoryItems.AsNoTracking()
                    on b.ItemID equals ci.ItemID
                group b by ci.CategoryName into g
                select new StockSliceVm
                {
                    Label = g.Key,
                    Value = g.Sum(x => (decimal)x.Qty)
                }
            )
            .OrderByDescending(x => x.Value)
            .Take(12)
            .ToListAsync();

            // -------------------------------------------------
            // Auto-select: most sold item in last 30 days (store-specific)
            // -------------------------------------------------
            int? autoTopItemId = await (
                from b in _db.BoughtUserItems.AsNoTracking()
                where b.StoreID == storeId && b.BoughtAt >= since30
                join ii in _db.InventoryItems.AsNoTracking()
                    on new { StoreID = b.StoreID, ItemID = b.ItemID }
                    equals new { ii.StoreID, ii.ItemID }
                where ii.IsActive
                group b by b.ItemID into g
                orderby g.Sum(x => x.Qty) descending
                select (int?)g.Key
            ).FirstOrDefaultAsync();

            var effectiveSelectedItemId = selectedItemId ?? autoTopItemId;

            // -------------------------------------------------
            // Selected item details
            // -------------------------------------------------
            StockSelectedItemVm? selected = null;

            if (effectiveSelectedItemId.HasValue)
            {
                var selId = effectiveSelectedItemId.Value;

                var inv = await _db.InventoryItems.AsNoTracking()
                    .Include(ii => ii.Item)
                    .FirstOrDefaultAsync(ii => ii.StoreID == storeId && ii.ItemID == selId && ii.IsActive);

                if (inv != null)
                {
                    var sold30Qty = await _db.BoughtUserItems.AsNoTracking()
                        .Where(b => b.StoreID == storeId && b.BoughtAt >= since30 && b.ItemID == selId)
                        .SumAsync(b => (int?)b.Qty) ?? 0;

                    selected = new StockSelectedItemVm
                    {
                        ItemID = inv.ItemID,
                        ItemName = inv.Item!.ItemName,
                        PictureLink = inv.Item.PictureLink,
                        OnHand = inv.OnHand,
                        SoldLast30 = sold30Qty
                    };
                }
            }

            // -------------------------------------------------
            // 7-day sales trend (Qty) for selected item
            // -------------------------------------------------
            var trend = new List<StockLinePointVm>();
            var todayUtc = nowUtc.Date;

            if (selected != null)
            {
                var selId = selected.ItemID;

                var daily = await _db.BoughtUserItems.AsNoTracking()
                    .Where(b => b.StoreID == storeId && b.ItemID == selId && b.BoughtAt >= since7)
                    .GroupBy(b => b.BoughtAt.Date)
                    .Select(g => new { Day = g.Key, Qty = g.Sum(x => x.Qty) })
                    .ToListAsync();

                for (int i = 6; i >= 0; i--)
                {
                    var day = todayUtc.AddDays(-i);
                    var qty = daily.Where(x => x.Day == day).Select(x => x.Qty).FirstOrDefault();

                    trend.Add(new StockLinePointVm
                    {
                        Label = day.ToString("dd.MM.yyyy"),
                        Value = qty
                    });
                }
            }
            else
            {
                for (int i = 6; i >= 0; i--)
                {
                    var day = todayUtc.AddDays(-i);
                    trend.Add(new StockLinePointVm { Label = day.ToString("dd.MM.yyyy"), Value = 0 });
                }
            }

            // -------------------------------------------------
            // Initial modal items (top onhand)
            // -------------------------------------------------
            var initialItems = await _db.InventoryItems.AsNoTracking()
                .Include(ii => ii.Item)
                .Where(ii => ii.StoreID == storeId && ii.IsActive)
                .OrderByDescending(ii => ii.OnHand)
                .Take(30)
                .Select(ii => new StockItemCardVm
                {
                    ItemID = ii.ItemID,
                    ItemName = ii.Item!.ItemName,
                    PictureLink = ii.Item.PictureLink,
                    OnHand = ii.OnHand
                })
                .ToListAsync();

            var vm = new StockControlVm
            {
                StoreID = storeId,
                StoreName = store.StoreName,
                InStockBreakdown = inStockByCategory,
                SoldBreakdown = soldByCategory,
                StockTrend = trend,
                InitialItems = initialItems,
                SelectedItem = selected
            };

            return View(vm);
        }


        [HttpGet]
        public async Task<IActionResult> SearchStockItems(string q)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
                return Unauthorized();

            var personnel = await _db.Personels.AsNoTracking().FirstOrDefaultAsync(p => p.UserID == userId);
            if (personnel == null) return Forbid();

            var storeId = personnel.StoreID;
            q = (q ?? "").Trim();

            var results = await _db.InventoryItems.AsNoTracking()
                .Include(ii => ii.Item)
                .Where(ii => ii.StoreID == storeId
                             && ii.IsActive
                             && (q == "" || ii.Item!.ItemName.Contains(q)))
                .OrderBy(ii => ii.Item!.ItemName)
                .Take(60)
                .Select(ii => new StockSearchResultVm
                {
                    ItemID = ii.ItemID,
                    ItemName = ii.Item!.ItemName,
                    PictureLink = ii.Item.PictureLink,
                    OnHand = ii.OnHand
                })
                .ToListAsync();

            return Json(results);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SetCurrentStockItem(int itemId)
        {
            return RedirectToAction(nameof(StockControl), new { selectedItemId = itemId });
        }

        public async Task<IActionResult> About()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
                return RedirectToAction("Index", "Home");

            var p = await _db.Personels
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Role)
                .Include(x => x.Histories)
                .Include(x => x.Educations)
                .Include(x => x.Certificates)
                .Include(x => x.Warnings)
                .FirstOrDefaultAsync(x => x.UserID == userId);

            if (p == null) return Forbid();

            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserID == userId);

            var vm = new PersonnelAboutVm
            {
                PersonelID = p.PersonelID,
                DisplayName = (p.PersonelName + " " + p.PersonelSurname).Trim(),
                RoleName = p.Role?.RoleName ?? "Personel",
                PersonelNumber = p.PersonelNumber,
                TcKimlikNo = p.PersonelTC,
                BirthDate = p.PersonelBirthDate,
                HireDate = p.PersonelCreatedAt,
                Salary = p.Salary,

                UserEmail = user?.Email ?? "",
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

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClockIn(int shiftId)
        {
            var now = DateTime.Now;
            const int EARLY_MIN = 3;

            var personel = await GetCurrentPersonelAsync();
            if (personel == null) return RedirectToAction("Dashboard");

            var sh = await _db.PersonelShifts
                .FirstOrDefaultAsync(x => x.ShiftID == shiftId
                                      && x.PersonelID == personel.PersonelID
                                      && x.IsActive);
            if (sh == null)
            {
                TempData["Err"] = "Vardiya bulunamadı.";
                return RedirectToAction("Dashboard");
            }

            var fixedEnd = FixEnd(sh.ShiftDateEnd);

            var inOpen = sh.ShiftDateStart.AddMinutes(-EARLY_MIN);

            if (sh.IsClockedIn)
            {
                TempData["Err"] = "Bu vardiya için zaten işe giriş yaptınız.";
                return RedirectToAction("Dashboard");
            }

            if (now < inOpen)
            {
                var mins = Math.Max(0, (int)Math.Ceiling((inOpen - now).TotalMinutes));
                TempData["Err"] = $"Şu an işe giriş yapamazsınız. Giriş için {mins} dk daha beklemelisiniz.";
                return RedirectToAction("Dashboard");
            }

            if (now > fixedEnd)
            {
                TempData["Err"] = "Bu vardiya bittiği için işe giriş yapamazsınız.";
                return RedirectToAction("Dashboard");
            }

            sh.IsClockedIn = true;
            sh.ClockInAt = now;

            await _db.SaveChangesAsync();
            TempData["Ok"] = "İşe giriş kaydedildi.";
            return RedirectToAction("Dashboard");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClockOut(int shiftId)
        {
            var now = DateTime.Now;
            const int EARLY_MIN = 3;

            var personel = await GetCurrentPersonelAsync();
            if (personel == null) return RedirectToAction("Dashboard");

            var sh = await _db.PersonelShifts
                .FirstOrDefaultAsync(x => x.ShiftID == shiftId
                                      && x.PersonelID == personel.PersonelID
                                      && x.IsActive);
            if (sh == null)
            {
                TempData["Err"] = "Vardiya bulunamadı.";
                return RedirectToAction("Dashboard");
            }

            if (!sh.IsClockedIn)
            {
                TempData["Err"] = "Önce işe giriş yapmalısınız.";
                return RedirectToAction("Dashboard");
            }

            if (sh.IsClockedOut)
            {
                TempData["Err"] = "Bu vardiya için zaten çıkış yaptınız.";
                return RedirectToAction("Dashboard");
            }

            var fixedEnd = FixEnd(sh.ShiftDateEnd);

            var outOpen = fixedEnd.AddMinutes(-EARLY_MIN);
            var outClose = fixedEnd.AddMinutes(EARLY_MIN);

            if (now < outOpen)
            {
                var mins = Math.Max(0, (int)Math.Ceiling((outOpen - now).TotalMinutes));
                TempData["Err"] = $"Çıkış zamanı henüz gelmedi. Çıkış için {mins} dk kaldı.";
                return RedirectToAction("Dashboard");
            }

            if (now > outClose)
            {
                TempData["Err"] = "Çıkış süresi geçti. Yönetici ile iletişime geçin.";
                return RedirectToAction("Dashboard");
            }

            sh.IsClockedOut = true;
            sh.ClockOutAt = now;

            await _db.SaveChangesAsync();
            TempData["Ok"] = "Çıkış kaydedildi.";
            return RedirectToAction("Dashboard");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(PersonnelAboutVm vm)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
                return RedirectToAction("Index", "Home");

            var user = await _db.Users.FirstOrDefaultAsync(u => u.UserID == userId);
            if (user == null) return Unauthorized();

            var pass = (vm.NewPassword ?? "").Trim();
            var pass2 = (vm.NewPasswordAgain ?? "").Trim();

            if (string.IsNullOrWhiteSpace(pass))
            {
                TempData["AboutError"] = "Yeni şifre boş olamaz.";
                return RedirectToAction(nameof(About));
            }

            if (pass != pass2)
            {
                TempData["AboutError"] = "Şifreler eşleşmiyor.";
                return RedirectToAction(nameof(About));
            }

            user.PasswordHash = _hasher.HashPassword(user, pass);
            await _db.SaveChangesAsync();

            TempData["AboutSaved"] = "Şifreniz güncellendi.";
            return RedirectToAction(nameof(About));
        }

        public async Task<IActionResult> MesaiBilgileri()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var personel = await _db.Personels.FirstOrDefaultAsync(p => p.UserID == userId);
            if (personel == null) return Forbid();

            var now = DateTime.Now;
            var startOfDay = now.Date;
            var endOfDay = now.Date.AddDays(1);

            // Overlap filter (handles overnight shifts too)
            var shifts = await _db.PersonelShifts
                .Where(s => s.PersonelID == personel.PersonelID
                         && s.IsActive
                         && s.ShiftDateStart < endOfDay
                         && s.ShiftDateEnd >= startOfDay)
                .OrderBy(s => s.ShiftDateStart)
                .ToListAsync();

            const int EARLY_MIN = 3;

            var vm = new MesaiBilgileriVM { ServerNow = now };

            foreach (var s in shifts)
            {
                var fixedEnd = FixEnd(s.ShiftDateEnd);

                var inOpenFrom = s.ShiftDateStart.AddMinutes(-EARLY_MIN);
                var inCloseAt = fixedEnd;

                var outOpen = fixedEnd.AddMinutes(-EARLY_MIN);
                var outClose = fixedEnd.AddMinutes(EARLY_MIN);

                var row = new ShiftStatusVM
                {
                    ShiftID = s.ShiftID,
                    Start = s.ShiftDateStart,
                    End = fixedEnd,
                    IsClockedIn = s.IsClockedIn,
                    IsClockedOut = s.IsClockedOut
                };

                // CLOCK-IN CIRCLE
                if (s.IsClockedIn)
                {
                    row.InColor = "green";
                    row.InTooltip = "İşe giriş yaptınız.";
                }
                else if (now < inOpenFrom)
                {
                    row.InColor = "gray";
                    var mins = (int)Math.Ceiling((inOpenFrom - now).TotalMinutes);
                    row.InTooltip = $"İşe girişe {mins} dk var.";
                }
                else if (now >= inOpenFrom && now < s.ShiftDateStart)
                {
                    row.InColor = "yellow";
                    var mins = (int)Math.Ceiling((s.ShiftDateStart - now).TotalMinutes);
                    row.InTooltip = $"İşe giriş zamanı yaklaşıyor: {mins} dk kaldı.";
                }
                else if (now >= s.ShiftDateStart && now <= inCloseAt)
                {
                    row.InColor = "red";
                    row.InTooltip = "İşe giriş yapmadınız!";
                }
                else
                {
                    row.InColor = "red";
                    row.InTooltip = "Vardiya geçti, işe giriş yapılmadı.";
                }

                // CLOCK-OUT CIRCLE
                if (s.IsClockedOut)
                {
                    row.OutColor = "green";
                    row.OutTooltip = "İşten çıkış yaptınız.";
                }
                else if (!s.IsClockedIn)
                {
                    row.OutColor = "gray";
                    row.OutTooltip = "Önce işe giriş yapmalısınız.";
                }
                else if (now < outOpen)
                {
                    row.OutColor = "gray";
                    row.OutTooltip = "Çıkış zamanı henüz gelmedi.";
                }
                else if (now >= outOpen && now < fixedEnd)
                {
                    row.OutColor = "yellow";
                    var mins = (int)Math.Ceiling((fixedEnd - now).TotalMinutes);
                    row.OutTooltip = $"Çıkışa {mins} dk kaldı.";
                }
                else if (now >= fixedEnd && now <= outClose)
                {
                    row.OutColor = "red";
                    row.OutTooltip = "Çıkış yapmadınız!";
                }
                else
                {
                    row.OutColor = "red";
                    row.OutTooltip = "Çıkış süresi geçti, çıkış yapılmadı.";
                }

                vm.Shifts.Add(row);
            }

            return View(vm);
        }


        private async Task<Personel?> GetCurrentPersonelAsync()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
                return null;

            return await _db.Personels.FirstOrDefaultAsync(p => p.UserID == userId && p.IsActive);
        }

        private static DateTime FixEnd(DateTime endFromDb)
        {
            // DB is +1h, so show/compute with -1h
            return endFromDb.AddHours(-1);
        }

    }
}
