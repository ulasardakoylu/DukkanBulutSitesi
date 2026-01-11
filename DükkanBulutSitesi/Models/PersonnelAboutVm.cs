using System;
using System.Collections.Generic;

namespace DükkanBulutSitesi.Models.Personnel
{
    public class PersonnelAboutVm
    {
        // Basic
        public int PersonelID { get; set; }
        public string DisplayName { get; set; } = "";
        public string RoleName { get; set; } = "";
        public string PersonelNumber { get; set; } = "";
        public string TcKimlikNo { get; set; } = "";
        public DateTime? BirthDate { get; set; }
        public DateTime? HireDate { get; set; }
        public decimal? Salary { get; set; }

        // User
        public string UserEmail { get; set; } = "";
        public string? ProfilePictureLink { get; set; }

        // Password change
        public string NewPassword { get; set; } = "";
        public string NewPasswordAgain { get; set; } = "";

        // Lists
        public List<PersonnelHistoryVm> Histories { get; set; } = new();
        public List<PersonnelEducationVm> Educations { get; set; } = new();
        public List<PersonnelCertificateVm> Certificates { get; set; } = new();
        public List<PersonnelWarningVm> Warnings { get; set; } = new();
    }


    public class PersonnelHistoryVm
    {
        public int PersonelHistoryID { get; set; }
        public int PersonelID { get; set; }
        public string Title { get; set; } = "";
        public DateTime Start { get; set; }
        public DateTime? End { get; set; }
        public string? Description { get; set; }
    }

    public class PersonnelEducationVm
    {
        public int PersonelEducationID { get; set; }
        public int PersonelID { get; set; }
        public string School { get; set; } = "";
        public DateTime Start { get; set; }
        public DateTime? End { get; set; }
        public string? Description { get; set; }
    }

    public class PersonnelCertificateVm
    {
        public int PersonelCertificateID { get; set; }
        public int PersonelID { get; set; }
        public string CertificateName { get; set; } = "";
        public string? Place { get; set; }
        public DateTime Start { get; set; }
        public DateTime? End { get; set; }
        public string? Description { get; set; }
    }

    public class PersonnelWarningVm
    {
        public int PersonelWarningID { get; set; }
        public int PersonelID { get; set; }
        public DateTime Date { get; set; }
        public string WarningText { get; set; } = "";
        public string Level { get; set; } = "warning"; // warning/danger
        public string GivenBy { get; set; } = "";
    }
}
