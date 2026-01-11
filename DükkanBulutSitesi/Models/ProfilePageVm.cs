using System;
using System.Collections.Generic;

namespace DükkanBulutSitesi.Models.Profile
{
    public class ProfileItemVm
    {
        public int ItemID { get; set; }
        public string ItemName { get; set; } = "";
        public string? PictureLink { get; set; }
        public decimal OriginalPrice { get; set; }
        public decimal Price { get; set; }  
        public bool HasCampaignPrice { get; set; } 
        public DateTime Date { get; set; }
    }

    public class ProfilePageVm
    {
        public string UserName { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public int BoughtItemCount { get; set; }

        public List<ProfileItemVm> PurchasedItems { get; set; } = new();
        public List<ProfileItemVm> ViewedItems { get; set; } = new();
        public List<ProfileItemVm> FavouriteItems { get; set; } = new();
    }
}
