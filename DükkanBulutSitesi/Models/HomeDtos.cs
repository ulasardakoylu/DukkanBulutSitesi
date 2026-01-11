namespace DükkanBulutSitesi.Models
{
    public class StoreDto
    {
        public int StoreID { get; set; }
        public string StoreName { get; set; } = "";
    }

    public class ProductCardDto
    {
        public int ItemID { get; set; }
        public string ItemName { get; set; } = "";

        public decimal Price { get; set; } 
        public decimal OriginalPrice { get; set; }  
        public bool HasCampaignPrice { get; set; }  

        public string? PictureLink { get; set; }
    }

    public class HomeIndexVm
    {
        public List<StoreDto> Stores { get; set; } = new();
        public int SelectedStoreId { get; set; }
        public bool ShouldShowStoreModal { get; set; }
        public List<ProductCardDto> PagedProducts { get; set; } = new();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }

        public List<string> Categories { get; set; } = new();
        public string? SelectedCategory { get; set; }
    }
}
