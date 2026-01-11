using System.ComponentModel.DataAnnotations;

namespace DükkanBulutSitesi.Models.Owner
{
    public class OwnerStoreSummaryVm
    {
        public int StoreID { get; set; }
        public string StoreName { get; set; } = "";
        public string? StoreDescription { get; set; }
        public string? StoreAddress { get; set; }
        public string? StorePictureLink { get; set; }
        public DateTime StoreOpenedAt { get; set; }

        public bool HasManager { get; set; }
        public string? ManagerName { get; set; }

        public int ManagerCount { get; set; }
        public List<string> ManagerNames { get; set; } = new();
    }

    public class OwnerDashboardVm
    {
        public string OwnerName { get; set; } = "";
        public List<OwnerStoreSummaryVm> Stores { get; set; } = new();
    }

public class OwnerEditStoreVm : IValidatableObject
    {
        public int? StoreID { get; set; }

        [Required, StringLength(200)]
        [Display(Name = "Dükkan Adı")]
        public string StoreName { get; set; } = "";

        [Required, StringLength(500)]
        [Display(Name = "Açıklama")]
        public string StoreDescription { get; set; } = "";

        [Required, StringLength(500)]
        [Display(Name = "Adres")]
        public string StoreAddress { get; set; } = "";

        [Required]
        [Display(Name = "Dükkan Açılış Tarihi")]
        [DataType(DataType.Date)]
        public DateTime? StoreOpenedAt { get; set; }

        [Display(Name = "Aktif mi?")]
        public bool IsActive { get; set; } = true;

        public string? ExistingPictureLink { get; set; }

        [Display(Name = "Dükkan Fotoğrafı")]
        public IFormFile? StorePictureFile { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // CREATE ONLY: cannot create as inactive
            if (StoreID == null && IsActive == false)
            {
                yield return new ValidationResult(
                    "Yeni dükkan pasif olarak oluşturulamaz. Aktif olmalıdır.",
                    new[] { nameof(IsActive) }
                );
            }

            if (string.IsNullOrWhiteSpace(StoreName))
                yield return new ValidationResult("Dükkan adı zorunludur.", new[] { nameof(StoreName) });

            if (string.IsNullOrWhiteSpace(StoreDescription))
                yield return new ValidationResult("Açıklama zorunludur.", new[] { nameof(StoreDescription) });

            if (string.IsNullOrWhiteSpace(StoreAddress))
                yield return new ValidationResult("Adres zorunludur.", new[] { nameof(StoreAddress) });

            if (!StoreOpenedAt.HasValue)
                yield return new ValidationResult("Açılış tarihi zorunludur.", new[] { nameof(StoreOpenedAt) });
        }
    }


    public class OwnerCommentManagementVm
    {
        public int StoreID { get; set; }
        public string StoreName { get; set; } = "";
        public List<Manager.PendingCommentVm> Pending { get; set; } = new();
        public List<OwnerCommentLogVm> Logs { get; set; } = new();
    }

    public class OwnerCommentLogVm
    {
        public string ManagerName { get; set; } = "";
        public string Action { get; set; } = ""; // CommentApproved / CommentRejected
        public DateTime PerformedAt { get; set; }
        public string Notes { get; set; } = "";
    }


    public class OwnerStoreDashboardVm
    {
        public int StoreID { get; set; }
        public string StoreName { get; set; } = "";
        public string? StoreDescription { get; set; }
        public string? StoreAddress { get; set; }

        public List<SalesPointVm> DaySales { get; set; } = new();
        public List<SalesPointVm> WeekSales { get; set; } = new();
        public List<SalesPointVm> MonthSales { get; set; } = new();

        public List<PieSliceVm> CategoryBreakdown { get; set; } = new();
        public List<PieSliceVm> TopItemsBreakdown { get; set; } = new();
    }

    public class SalesPointVm
    {
        public string Label { get; set; } = "";  // example: "10:00", "12.04"
        public decimal Value { get; set; }       // revenue
    }

    public class PieSliceVm
    {
        public string Label { get; set; } = "";  // category or item name
        public decimal Value { get; set; }       // revenue
    }
}
