namespace DükkanBulutSitesi.Models.Store
{
    public class StoreSpecialOffersVm
    {
        public List<ActiveSpecialOfferVm> ActiveOffers { get; set; } = new();
        public int? SelectedOfferID { get; set; }

        public int? PreviousOfferID { get; set; }
        public int? NextOfferID { get; set; }

        public DateTime? NextPlannedOfferStart { get; set; }
    }

    public class ActiveSpecialOfferVm
    {
        public int SpecialOfferID { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }

        public List<ActiveSpecialOfferItemVm> Items { get; set; } = new();
    }

    public class ActiveSpecialOfferItemVm
    {
        public int ItemID { get; set; }
        public string ItemName { get; set; } = "";
        public decimal OriginalPrice { get; set; }
        public decimal NewPrice { get; set; }
        public string? PictureLink { get; set; }
    }

    public sealed class StoreAboutVm
    {
        public int StoreID { get; set; }
        public string StoreName { get; set; } = "";
        public DateTime StoreOpenedAt { get; set; }
        public string? StoreDescription { get; set; }
        public string StoreAddress { get; set; } = "";
        public string? StorePictureLink { get; set; }

        public List<string> OwnerNames { get; set; } = new();
    }
}