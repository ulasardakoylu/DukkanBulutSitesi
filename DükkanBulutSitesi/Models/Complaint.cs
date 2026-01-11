using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace DükkanBulutSitesi.Models
{
    public class ComplaintCreateVm
    {
        public int? ItemID { get; set; }
        public int? CommentID { get; set; }

        [Required]
        [Display(Name = "Şikayet Türü")]
        public int ComplaintTypeID { get; set; }

        public IEnumerable<SelectListItem>? TypeOptions { get; set; }

        [Required]
        [StringLength(2000)]
        [Display(Name = "Açıklama")]
        public string Description { get; set; } = "";

        public string? ItemName { get; set; }
        public string? ReturnUrl { get; set; }
    }
}