using Entity;

namespace DükkanBulutSitesi.Models
{
    public class RoleSummaryVm
    {
        public int RoleID { get; set; }
        public string RoleName { get; set; } = "";
        public bool IsSystem { get; set; }
    }
    public class PersonnelNumberCodeVm
    {
        public int PersonnelNumberCodeID { get; set; }
        public string Code { get; set; } = default!;
        public bool IsUsed { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UsedAt { get; set; }
    }
    public class RoleEditVm
    {
        public int? RoleID { get; set; }
        public string RoleName { get; set; } = "";
        public TimeSpan? StartTime { get; set; }
        public TimeSpan? ExitTime { get; set; }
        public bool IsSystem { get; set; }

        public List<PermissionCheckboxVm> Permissions { get; set; } = new();
        public List<PersonnelNumberCodeVm> PersonnelCodes { get; set; } = new();
    }

    public class PermissionCheckboxVm
    {
        public int PermissionID { get; set; }
        public string PermissionName { get; set; } = "";
        public bool Granted { get; set; }
    }

    public class RoleManagementVm
    {
        public List<RoleSummaryVm> Roles { get; set; } = new();
        public RoleEditVm SelectedRole { get; set; } = new();
    }
}
