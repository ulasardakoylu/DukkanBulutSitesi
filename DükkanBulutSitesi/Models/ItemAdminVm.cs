using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DükkanBulutSitesi.Models.Manager
{
    public class ManagerItemListRowVm
    {
        public int ItemID { get; set; }
        public string ItemName { get; set; } = "";
        public decimal ItemPrice { get; set; }
        public int OnHand { get; set; }
        public bool IsActive { get; set; }
        public string? PictureLink { get; set; }
    }

    public class ManagerItemListVm
    {
        public List<ManagerItemListRowVm> Items { get; set; } = new();
    }

    public class ManagerItemEditVm
    {
        public int? ItemID { get; set; }

        [Required(ErrorMessage = "Ürün adı zorunludur.")]
        [StringLength(200, ErrorMessage = "Ürün adı en fazla 200 karakter olabilir.")]
        [Display(Name = "Ürün Adı")]
        public string ItemName { get; set; } = "";

        [Required(ErrorMessage = "Fiyat zorunludur.")]
        [Range(0.01, 9_999_999, ErrorMessage = "Fiyat 0'dan büyük olmalıdır.")]
        [Display(Name = "Fiyat")]
        public decimal ItemPrice { get; set; }

        [Required(ErrorMessage = "Açıklama zorunludur.")]
        [StringLength(5000, ErrorMessage = "Açıklama en fazla 5000 karakter olabilir.")]
        [Display(Name = "Açıklama")]
        public string Description { get; set; } = "";

        [Display(Name = "Aktif mi?")]
        public bool IsActive { get; set; } = true;

        [Required(ErrorMessage = "Stok zorunludur.")]
        [Range(1, 1_000_000, ErrorMessage = "Stok 1 veya daha büyük olmalıdır.")]
        [Display(Name = "Stok (bu mağaza)")]
        public int OnHand { get; set; } = 1;

        // CSV required, ALSO validate "at least 1 token" in controller
        [Required(ErrorMessage = "En az 1 kategori girin.")]
        [StringLength(1000, ErrorMessage = "Kategori alanı çok uzun.")]
        [Display(Name = "Kategoriler (virgülle)")]
        public string CategoriesCsv { get; set; } = "";

        [Required(ErrorMessage = "En az 1 anahtar kelime girin.")]
        [StringLength(1500, ErrorMessage = "Anahtar kelime alanı çok uzun.")]
        [Display(Name = "Anahtar Kelimeler (virgülle)")]
        public string KeywordsCsv { get; set; } = "";

        public string? ExistingPictureLink { get; set; }

        [Display(Name = "Ürün Resmi")]
        public IFormFile? PictureFile { get; set; }
    }
}
