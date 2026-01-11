using System.ComponentModel.DataAnnotations;

namespace DükkanBulutSitesi.Models
{
    public sealed class ProductDetailsVm
    {
        public int ItemID { get; set; }
        public string ItemName { get; set; } = "";
        public decimal Price { get; set; }
        public string? PictureLink { get; set; }
        public string? Description { get; set; }

        public double AverageStars { get; set; }
        public int ReviewCount { get; set; }

        public bool IsFavourite { get; set; }

        public decimal OriginalPrice { get; set; }
        public bool HasCampaignPrice { get; set; }

        public List<ReviewVm> Comments { get; set; } = new();
        public List<ReviewVm> Featured { get; set; } = new();
        public int CommentPage { get; set; } = 1;
        public int CommentPageSize { get; set; } = 5;
        public int CommentTotal { get; set; }

        public bool CanManageThisStore { get; set; }

        public List<ProductMiniDto> Related { get; set; } = new();
        public List<ProductMiniDto> ViewedSuggestions { get; set; } = new();

        public List<string> ViewedSuggestionCategories { get; set; } = new();
        public List<string> ViewedSuggestionKeywords { get; set; } = new();

        public NewCommentVm NewComment { get; set; } = new(); 
    }

    public sealed class ReviewVm
    {
        public int ReviewID { get; set; }
        public string OwnerName { get; set; } = "";
        public int Stars { get; set; }
        public string Text { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    public sealed class NewCommentVm
    {
        public int ItemID { get; set; }
        [Range(1, 5)] public int Stars { get; set; }
        [Required, StringLength(1000)]
        public string Text { get; set; } = "";
    }

    public sealed class ProductMiniDto
    {
        public int ItemID { get; set; }
        public string ItemName { get; set; } = "";
        public decimal Price { get; set; }
        public string? PictureLink { get; set; }

        public decimal OriginalPrice { get; set; }

        public List<string>? MatchedCategories { get; set; }
        public List<string>? MatchedKeywords { get; set; }

        public bool HasCampaignPrice { get; set; }
    }

    public sealed class ReviewPreviewDto 
    {
        public string CustomerName { get; set; } = "";
        public int Stars { get; set; }
        public string Excerpt { get; set; } = "";
    }
}
