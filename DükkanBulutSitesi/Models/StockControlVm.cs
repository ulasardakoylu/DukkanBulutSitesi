using System;
using System.Collections.Generic;

namespace DükkanBulutSitesi.Models.Personnel
{
    public class StockControlVm
    {
        public int StoreID { get; set; }
        public string StoreName { get; set; } = "";

        public List<StockSliceVm> InStockBreakdown { get; set; } = new();
        public List<StockSliceVm> SoldBreakdown { get; set; } = new();
        public List<StockLinePointVm> StockTrend { get; set; } = new();

        public List<StockItemCardVm> InitialItems { get; set; } = new();

        public StockSelectedItemVm? SelectedItem { get; set; }
    }

    public class StockSelectedItemVm
    {
        public int ItemID { get; set; }
        public string ItemName { get; set; } = "";
        public string? PictureLink { get; set; }
        public int OnHand { get; set; }
        public int SoldLast30 { get; set; }
    }

    public class StockSliceVm
    {
        public string Label { get; set; } = "";
        public decimal Value { get; set; }
    }

    public class StockLinePointVm
    {
        public string Label { get; set; } = "";
        public decimal Value { get; set; }
    }

    public class StockItemCardVm
    {
        public int ItemID { get; set; }
        public string ItemName { get; set; } = "";
        public string? PictureLink { get; set; }
        public int OnHand { get; set; }
    }

    public class StockSearchResultVm
    {
        public int ItemID { get; set; }
        public string ItemName { get; set; } = "";
        public string? PictureLink { get; set; }
        public int OnHand { get; set; }
    }

}
