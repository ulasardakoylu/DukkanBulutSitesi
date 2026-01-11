using Microsoft.AspNetCore.Mvc.Rendering;

namespace DükkanBulutSitesi.Models.Owner
{
    public class ManagerActivityLogVm
    {
        public int StoreIdContext { get; set; } // the storeId passed for layout/sidebar

        public int? FilterStoreId { get; set; }
        public int? FilterManagerUserId { get; set; }
        public string? FilterAction { get; set; }

        public List<SelectListItem> StoreOptions { get; set; } = new();
        public List<SelectListItem> ManagerOptions { get; set; } = new();
        public List<SelectListItem> ActionOptions { get; set; } = new();

        public List<ManagerActivityLogRowVm> Rows { get; set; } = new();

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public int TotalCount { get; set; }
        public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    }

    public class ManagerActivityLogRowVm
    {
        public int LogId { get; set; }
        public DateTime CreatedAtUtc { get; set; }

        public int StoreId { get; set; }
        public string StoreName { get; set; } = "";

        public int ManagerUserId { get; set; }
        public string ManagerName { get; set; } = "";

        public string Action { get; set; } = "";
        public string Controller { get; set; } = "";
        public string Method { get; set; } = "";
        public string? Path { get; set; }
        public string? Description { get; set; }

        public string FriendlyAction { get; set; } = "";
        public string FriendlyDetail { get; set; } = "";
        public string FriendlyTime { get; set; } = ""; 
    }
}
