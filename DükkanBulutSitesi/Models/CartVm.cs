using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DükkanBulutSitesi.Models.Cart
{
    public class CartItemVm
    {
        public int CartItemID { get; set; }
        public int ItemID { get; set; }
        public string ItemName { get; set; } = "";
        public string? PictureLink { get; set; }

        public decimal OriginalUnitPrice { get; set; }

        public decimal UnitPrice { get; set; }

        public int Qty { get; set; }
        public DateTime AddedAt { get; set; }

        public bool HasDiscount => UnitPrice < OriginalUnitPrice;
    }

    public class SavedCardVm
    {
        public int CardID { get; set; }
        public string Brand { get; set; } = "";
        public string Last4 { get; set; } = "";
        public int ExpMonth { get; set; }
        public int ExpYear { get; set; }
    }

    public class SavedAddressVm
    {
        public int AddressID { get; set; }
        public string AddressInformation { get; set; } = "";
        public string? AddressName { get; set; }
    }

    public class SuggestedProductVm
    {
        public int ItemID { get; set; }
        public string ItemName { get; set; } = "";
        public string? PictureLink { get; set; }

        public decimal Price { get; set; }
        public decimal OriginalPrice { get; set; }
        public bool HasCampaignPrice { get; set; }
    }

    public class CartPageVm
    {
        public List<CartItemVm> Items { get; set; } = new();
        public List<SavedCardVm> SavedCards { get; set; } = new();
        public List<SavedAddressVm> SavedAddresses { get; set; } = new();
        public List<SuggestedProductVm> Suggestions { get; set; } = new();

        public decimal Total => Items.Sum(i => i.UnitPrice * i.Qty);
    }

    public class SavedCardFormVm
    {
        public int? CardID { get; set; }

        [Required, StringLength(30)]
        public string Brand { get; set; } = "";

        [Required, StringLength(4, MinimumLength = 4)]
        public string Last4 { get; set; } = "";

        [Range(1, 12)]
        public int ExpMonth { get; set; }

        [Range(2024, 2100)]
        public int ExpYear { get; set; }
    }

    public class SavedAddressFormVm
    {
        public int? AddressID { get; set; }

        [Required, StringLength(100)]
        public string? AddressName { get; set; }

        [Required]
        public string AddressInformation { get; set; } = "";
    }
}