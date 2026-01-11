using Microsoft.AspNetCore.Mvc.Rendering;

namespace DükkanBulutSitesi.Models.Manager
{
    public class ShiftLandingVm
    {
        public DateTime SelectedDate { get; set; } = DateTime.Today;

        public int? SelectedPersonelId { get; set; }
        public List<SelectListItem> PersonelOptions { get; set; } = new();
    }

    public class ShiftResourceVm
    {
        public int PersonelID { get; set; }
        public string DisplayName { get; set; } = default!;
        public string RoleName { get; set; } = default!;
    }

    public class ShiftEventVm
    {
        public int ShiftID { get; set; }
        public int PersonelID { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public string Text { get; set; } = default!;
        public string Color { get; set; } = "#ff0000";
    }

    public class ShiftDayVm
    {
        public DateTime SelectedDate { get; set; }

        public List<ShiftResourceVm> Personnel { get; set; } = new();
        public List<ShiftEventVm> Shifts { get; set; } = new();
    }

    public class PersonelWeekShiftVm
    {
        public int PersonelID { get; set; }
        public string PersonelName { get; set; } = "";
        public DateTime WeekStart { get; set; }   // Monday
        public DateTime WeekEnd { get; set; }     // Sunday

        public List<PersonelWeekDayVm> Days { get; set; } = new();

        public bool HasAnyShiftThisWeek => Days.Any(d => d.Shifts.Any());
        public bool EntireWeekIsTimeOff => Days.All(d => !d.Shifts.Any() && d.TimeOffs.Any());
    }

    public class PersonelWeekDayVm
    {
        public DateTime Date { get; set; }
        public List<ShiftSummaryVm> Shifts { get; set; } = new();
        public List<TimeOffSummaryVm> TimeOffs { get; set; } = new();

        public bool NeedsAttention => !Shifts.Any() && !TimeOffs.Any();
    }

    public class ShiftSummaryVm
    {
        public int ShiftID { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public string? Color { get; set; }

        public string TimeRange => $"{Start:HH\\:mm} - {End:HH\\:mm}";
    }

    public class TimeOffSummaryVm
    {
        public int TimeOffID { get; set; }
        public DateTime DateStart { get; set; }
        public DateTime DateEnd { get; set; }
        public string Type { get; set; } = "";
        public bool IsPaid { get; set; }
        public bool IsApproved { get; set; }
        public string? Description { get; set; }
    }

    public class PersonelTimeOffCreateVm
    {
        public int PersonelID { get; set; }
        public DateTime DateStart { get; set; }
        public DateTime DateEnd { get; set; }
        public string TimeOffType { get; set; } = "PaidLeave";
        public bool IsPaid { get; set; } = true;
        public string? Description { get; set; }

        public DateTime AnchorDate { get; set; }
    }

}