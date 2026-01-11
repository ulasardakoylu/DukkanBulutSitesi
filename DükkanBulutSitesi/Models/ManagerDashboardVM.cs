using DükkanBulutSitesi.Controllers;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;

namespace DükkanBulutSitesi.Models.Manager
{
    public class ManagerDashboardVm
    {
        public string ManagerName { get; set; } = "Yönetici";

        public List<DashboardComplaintVm> Complaints { get; set; } = new();

        public List<DashboardIncompletePersonnelVm> IncompletePersonnel { get; set; } = new();

        public List<DashboardConversationVm> RecentConversations { get; set; } = new();
    }

    public class DashboardTaskVm
    {
        public string PersonName { get; set; } = "";
        public string TaskType { get; set; } = "";
    }

    public class DashboardComplaintVm
    {
        public int ComplaintID { get; set; }
        public string Title { get; set; } = "";
    }

    public class DashboardIncompletePersonnelVm
    {
        public int PersonelID { get; set; }
        public string DisplayName { get; set; } = "";
        public string MissingSummary { get; set; } = "";
    }


    public class DashboardConversationVm
    {
        public int ConversationID { get; set; }
        public string From { get; set; } = "";
        public string Preview { get; set; } = "";

        public DateTime LastSentAt { get; set; }
    }

    public class PersonnelListItemVm
    {
        public int PersonelID { get; set; }
        public string DisplayName { get; set; } = default!; 
        public string RoleName { get; set; } = default!;
        public bool IsActive { get; set; }
    }

    public class PersonnelHistoryVm
    {
        public int PersonelHistoryID { get; set; }
        public int PersonelID { get; set; }

        public string Title { get; set; } = default!;

        public DateTime Start { get; set; }
        public DateTime? End { get; set; }

        public string? Description { get; set; }
    }

    public class PersonnelEducationVm
    {
        public int PersonelEducationID { get; set; }
        public int PersonelID { get; set; }

        public string School { get; set; } = default!;
        public string? Degree { get; set; }

        public DateTime Start { get; set; }
        public DateTime? End { get; set; }

        public string? Description { get; set; }
    }

    public class PersonnelCertificateVm
    {
        public int PersonelCertificateID { get; set; }
        public int PersonelID { get; set; }

        public string CertificateName { get; set; } = default!;
        public string? Place { get; set; }

        public DateTime Start { get; set; }
        public DateTime? End { get; set; }

        public string? Description { get; set; }
    }

    public class PersonnelWarningVm
    {
        public int PersonelWarningID { get; set; }
        public int PersonelID { get; set; }
        public DateTime Date { get; set; }
        public string WarningText { get; set; } = default!;
        public string Level { get; set; } = "warning"; // or "danger"
        public string GivenBy { get; set; } = string.Empty;
    }

    public class PersonnelEditVm
    {
        public int PersonelID { get; set; }
        public string PersonelNumber { get; set; } = default!;

        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? TcKimlikNo { get; set; }

        public DateTime? BirthDate { get; set; }
        public DateTime? HireDate { get; set; }
        public DateTime? ExitDate { get; set; }
        public int UserID { get; set; }
        public int RoleID { get; set; }
        public string RoleName { get; set; } = "";

        public string? UserEmail { get; set; }
        public string? NewPassword { get; set; }
        public string? ProfilePictureLink { get; set; }
        public IFormFile? ProfilePictureFile { get; set; }

        public int? PreviousPersonelID { get; set; }
        public int? NextPersonelID { get; set; }

        public bool IsActive { get; set; } 
        public decimal? Salary { get; set; } 

        public List<PersonnelHistoryVm> Histories { get; set; } = new();
        public List<PersonnelEducationVm> Educations { get; set; } = new();
        public List<PersonnelCertificateVm> Certificates { get; set; } = new();
        public List<PersonnelWarningVm> Warnings { get; set; } = new();
        public List<ActiveStoreVm> ActiveStores { get; set; } = new();
        public List<OwnerStoreOptionVm> OwnerStores { get; set; } = new();

        public class ActiveStoreVm
        {
            public int StoreID { get; set; }
            public string StoreName { get; set; } = "";
            public string RoleName { get; set; } = "";
            public string PersonelNumber { get; set; } = "";
        }

        public class OwnerStoreOptionVm
        {
            public int StoreID { get; set; }
            public string StoreName { get; set; } = "";
        }

    }

    public class PersonnelManagementVm
    {
        public List<PersonnelListItemVm> Personnel { get; set; } = new();
        public PersonnelEditVm? Selected { get; set; }
        public string StatusFilter { get; set; } = "all"; // all or active or inactive
    }

    public class ComplaintListItemVm
    {
        public int ComplaintID { get; set; }
        public string Title { get; set; } = default!;
        public string Summary { get; set; } = default!;
        public DateTime CreatedAt { get; set; }
        public string Status { get; set; } = default!;
    }

    public class PendingCommentVm
    {
        public int CommentID { get; set; }
        public int ItemID { get; set; }

        public string ItemName { get; set; } = "";

        public string PendingText { get; set; } = "";

        public DateTime CreatedAt { get; set; }
    }



    public class ComplaintDetailVm
    {
        public int ComplaintID { get; set; }
        public string Title { get; set; } = default!;
        public string Area { get; set; } = default!;
        public string ReporterName { get; set; } = default!;
        public string? TargetName { get; set; }
        public DateTime CreatedAt { get; set; }
        public string ComplaintTypeName { get; set; } = default!;
        public string Description { get; set; } = default!;
        public string Status { get; set; } = default!;
        public string? ManagerNotes { get; set; }

        public int? ItemID { get; set; }
        public int? CommentID { get; set; }
    }

    public class ComplaintManagementVm
    {
        public List<ComplaintListItemVm> Complaints { get; set; } = new();
        public ComplaintDetailVm? Selected { get; set; }
    }

    public class SpecialOfferItemVm
    {
        public int ItemID { get; set; }
        public string ItemName { get; set; } = "";
        public decimal OriginalPrice { get; set; }

        public bool Included { get; set; }
        public decimal? NewPrice { get; set; }
    }

    public class SpecialOfferEditVm
    {
        public int? SpecialOfferID { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }

        public DateTime? Start { get; set; }
        public DateTime? End { get; set; }

        public bool IsCancelled { get; set; }

        public List<SpecialOfferItemVm> Items { get; set; } = new();
    }

    public class SpecialOfferManagementVm
    {
        public List<SpecialOfferSummaryVm> Offers { get; set; } = new();
        public SpecialOfferEditVm? SelectedOffer { get; set; }
        public int? SelectedOfferID { get; set; }
        public DateTime? NextCampaignStart { get; set; }
    }

    public class SpecialOfferSummaryVm
    {
        public int SpecialOfferID { get; set; }
        public string Name { get; set; } = "";
        public DateTime DateStart { get; set; }
        public DateTime DateEnd { get; set; }
        public bool IsCurrent { get; set; }
        public bool IsCancelled { get; set; }
        public bool IsEnded { get; set; }
    }

}