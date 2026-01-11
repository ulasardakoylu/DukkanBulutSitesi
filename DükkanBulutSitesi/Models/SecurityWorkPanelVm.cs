using System;
using System.Collections.Generic;

namespace DükkanBulutSitesi.Models.Personnel
{
    public class SecurityWorkPanelVm
    {
        public DateTime Day { get; set; }

        // Checklist
        public List<SecurityChecklistTaskVm> Checklist { get; set; } = new();

        // Report creation form
        public string ReportedPlace { get; set; } = "";
        public DateTime ActivityDateStart { get; set; } = DateTime.Now;
        public DateTime? ActivityDateEnd { get; set; }
        public int InjuredCount { get; set; } = 0;
        public string Description { get; set; } = "";
        public string? PictureLink { get; set; }

        // Optional: problems as lines
        public string ProblemsText { get; set; } = "";

        // Recent reports (optional list on the page)
        public List<SecurityReportCardVm> RecentReports { get; set; } = new();

        // Banned / suspicious people list (read-only)
        public List<BannedPersonCardVm> BannedPeople { get; set; } = new();
    }

    public class SecurityChecklistTaskVm
    {
        public string Key { get; set; } = "";
        public string Text { get; set; } = "";
        public bool Done { get; set; }
    }

    public class SecurityReportCardVm
    {
        public int ReportID { get; set; }
        public DateTime CreatedAt { get; set; }
        public string ReportedPlace { get; set; } = "";
        public DateTime ActivityDateStart { get; set; }
        public DateTime? ActivityDateEnd { get; set; }
        public int InjuredCount { get; set; }
        public string Description { get; set; } = "";
        public List<string> Problems { get; set; } = new();
    }

    public class BannedPersonCardVm
    {
        public int SPeopleID { get; set; }
        public string Description { get; set; } = "";
        public string Reason { get; set; } = "";
        public bool Temporary { get; set; }
        public string? PictureLink { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
