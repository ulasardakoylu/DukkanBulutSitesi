using System.ComponentModel.DataAnnotations;

namespace DükkanBulutSitesi.Models
{
    public class LoginVm
    {
        [Required, EmailAddress, Display(Name = "E-Mail Adresi")]
        public string Email { get; set; } = "";

        [Required, DataType(DataType.Password), Display(Name = "Şifre")]
        public string Password { get; set; } = "";

        public string? ReturnUrl { get; set; }
    }

    public class RegisterVm
    {
        [Required, EmailAddress, Display(Name = "E-Mail Adresi")]
        public string Email { get; set; } = "";

        [Required, StringLength(40), Display(Name = "Kullanıcı Adı")]
        public string UserName { get; set; } = "";

        [Required, DataType(DataType.Password), MinLength(6), Display(Name = "Yeni Şifre")]
        public string Password { get; set; } = "";

        [Required, DataType(DataType.Password), Compare(nameof(Password), ErrorMessage = "Şifreler eşleşmiyor")]
        [Display(Name = "Yeni Şifre Tekrarı")]
        public string ConfirmPassword { get; set; } = "";

        public string? ReturnUrl { get; set; }
    }

    public class EmployeeRegisterVm
    {
        [Required]
        [Display(Name = "Personel Numarası")]
        public string EmployeeNumber { get; set; } = default!;

        [Required, MaxLength(100)]
        [Display(Name = "Kullanıcı Adı")]
        public string UserName { get; set; } = default!;

        [Required, EmailAddress]
        [Display(Name = "E-posta")]
        public string Email { get; set; } = default!;

        [Required, MinLength(6)]
        [DataType(DataType.Password)]
        [Display(Name = "Şifre")]
        public string Password { get; set; } = default!;

        [Required, DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Şifreler uyuşmuyor.")]
        [Display(Name = "Şifre (Tekrar)")]
        public string ConfirmPassword { get; set; } = default!;
    }
}
