using System;
using System.Collections.Generic;

namespace DükkanBulutSitesi.Models
{
    public class PersonnelNotificationVm
    {
        public string Type { get; set; } = "info"; // "info", "warning", "danger"
        public string Title { get; set; } = "";
        public string Text { get; set; } = "";
    }

    public class PersonnelConversationVm
    {
        public int ChatID { get; set; }
        public string WithName { get; set; } = "";
        public string Preview { get; set; } = "";
        public DateTime LastMessageAt { get; set; }
    }

    public class IssueTargetVm
    {
        public int ManagerUserID { get; set; }
        public string DisplayName { get; set; } = "";
    }

    public class PersonnelDashboardVm
    {
        public string DisplayName { get; set; } = "";
        public string RoleName { get; set; } = "";

        public DateTime Now { get; set; }

        public TimeSpan? ShiftStart { get; set; }
        public TimeSpan? ShiftEnd { get; set; }

        public string? ShiftInfoText { get; set; }

        public List<PersonnelNotificationVm> Notifications { get; set; } = new();
        public List<PersonnelConversationVm> RecentConversations { get; set; } = new();
        public List<IssueTargetVm> IssueTargets { get; set; } = new();
        public List<ShiftStatusVM> Shifts { get; set; } = new();
    }

    public class QuickWorkPanelVm
    {
        // Top: campaign
        public string? CampaignName { get; set; }
        public string? CampaignDescription { get; set; }
        public DateTime? CampaignStart { get; set; }
        public DateTime? CampaignEnd { get; set; }
        public List<QuickCampaignItemVm> CampaignItems { get; set; } = new();

        // Notes (main display)
        public int? SelectedNoteId { get; set; }
        public string NoteTitle { get; set; } = "";
        public string NoteBody { get; set; } = "";
        public DateTime? NoteCreatedAt { get; set; }
        public DateTime? NoteUpdatedAt { get; set; }

        public DateTime Day { get; set; } // local date
        public List<QuickChecklistTaskVm> Checklist { get; set; } = new();

        public int? SelectedChartItemId { get; set; }
        public string? SelectedChartItemName { get; set; }
        public List<QuickStockPointVm> StockPoints { get; set; } = new();
    }

    public class QuickCampaignItemVm
    {
        public int ItemID { get; set; }
        public string ItemName { get; set; } = "";
        public string? PictureLink { get; set; }
        public decimal NewPrice { get; set; }
    }

    public class QuickChecklistTaskVm
    {
        public string Key { get; set; } = ""; 
        public string Text { get; set; } = ""; 
        public bool Done { get; set; }
    }

    public class QuickStockPointVm
    {
        public string Label { get; set; } = ""; // e.g. "14.12 19:10"
        public int Value { get; set; }          // StockCount
    }

    public class ShiftStatusVM
{
    public int ShiftID { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }

    public bool IsClockedIn { get; set; }
    public bool IsClockedOut { get; set; }

    public string InColor { get; set; } = "gray";   // gray/yellow/red/green
    public string InTooltip { get; set; } = "";

    public string OutColor { get; set; } = "gray";
    public string OutTooltip { get; set; } = "";
}

public class MesaiBilgileriVM
{
    public List<ShiftStatusVM> Shifts { get; set; } = new();
    public DateTime ServerNow { get; set; }
}


}