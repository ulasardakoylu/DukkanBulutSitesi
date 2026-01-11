using System.Collections.Generic;

namespace DükkanBulutSitesi.Models.Search
{
    public sealed class SearchResultsVm
    {
        public string Query { get; set; } = "";
        public List<SearchResultItemVm> Results { get; set; } = new();
    }

    public sealed class SearchResultItemVm
    {
        public int ItemID { get; set; }
        public string ItemName { get; set; } = "";
        public string? PictureLink { get; set; }

        public decimal OriginalPrice { get; set; }
        public decimal Price { get; set; }
        public bool HasCampaignPrice { get; set; }

        public double AverageStars { get; set; }
        public int ReviewCount { get; set; }
    }
}
