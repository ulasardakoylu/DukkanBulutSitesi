using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using static Entity.Personel;

namespace Entity
{
    public class User
    {
        public int UserID { get; set; }
        public int? RoleID { get; set; }
        public Role? Role { get; set; }
        [MaxLength(100)] public string UserName { get; set; } = default!;
        [MaxLength(200)] public string Email { get; set; } = default!;
        public string PasswordHash { get; set; } = default!;
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLogin { get; set; }
        public bool IsActive { get; set; }
        public string? ProfilePictureLink { get; set; }

        public ICollection<Setting> Settings { get; set; } = new List<Setting>();
        public ICollection<FavouriteUserItem> FavouriteItems { get; set; } = new List<FavouriteUserItem>();
        public ICollection<LookedAtItem> LookedAtItems { get; set; } = new List<LookedAtItem>();
        public ICollection<BoughtUserItem> BoughtItems { get; set; } = new List<BoughtUserItem>();

        public ICollection<SavedCard> SavedCards { get; set; } = new List<SavedCard>();
        public ICollection<SavedAddress> SavedAddresses { get; set; } = new List<SavedAddress>();

        public ICollection<Checklist> Checklists { get; set; } = new List<Checklist>();

        public ICollection<ShoppingCart> Carts { get; set; } = new List<ShoppingCart>();
    }

    public class Role
    {
        public int RoleID { get; set; }
        [MaxLength(100)] public string RoleName { get; set; } = default!;

        public int StoreID { get; set; }
        public Store Store { get; set; } = default!;
        public TimeSpan? StartTime { get; set; }
        public TimeSpan? ExitTime { get; set; }
        public bool IsSystem { get; set; } = false;
        public int HierarchyLevel { get; set; } = 50;

        public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
        public ICollection<PersonnelNumberCode> PersonnelNumberCodes { get; set; } = new List<PersonnelNumberCode>();
    }
        
    public class Permission
    {
        public int PermissionID { get; set; }
        [MaxLength(64)]public string PermissionKey { get; set; } = default!;
        [MaxLength(150)] public string DisplayName { get; set; } = default!;
        public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    }

    public class RolePermission
    {
        public int RoleID { get; set; }
        public Role Role { get; set; } = default!;
        public int PermissionID { get; set; }
        public Permission Permission { get; set; } = default!;
    }

    public class Item
    {
        public int ItemID { get; set; }
        [MaxLength(200)] public string ItemName { get; set; } = default!;
        public decimal ItemPrice { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; }
        public string? Description { get; set; }
        public string? PictureLink { get; set; }

        public ICollection<ItemStock> StockChanges { get; set; } = new List<ItemStock>();
        public ICollection<CategoryItem> Categories { get; set; } = new List<CategoryItem>();
        public ICollection<KeywordOfItem> Keywords { get; set; } = new List<KeywordOfItem>();

        public ICollection<ItemComment> Comments { get; set; } = new List<ItemComment>();
        public ICollection<SpecialOfferItem> SpecialOfferItems { get; set; } = new List<SpecialOfferItem>();
    }

    public class ItemStock
    {
        public int ItemID { get; set; }
        public Item Item { get; set; } = default!;
        public int StockCount { get; set; }
        public DateTime ChangedAt { get; set; }
    }

    public class InventoryItem
    {
        public int StoreID { get; set; }
        public Store Store { get; set; } = default!;

        public int ItemID { get; set; }
        public Item Item { get; set; } = default!;

        public int OnHand { get; set; }  
        public bool IsActive { get; set; } = true;
    }

    public class CategoryItem
    {
        public int ItemID { get; set; }
        public Item Item { get; set; } = default!;
        [MaxLength(120)] public string CategoryName { get; set; } = default!;
    }

    public class FavouriteUserItem
    {
        public int UserID { get; set; }
        public User User { get; set; } = default!;
        public int ItemID { get; set; }
        public Item Item { get; set; } = default!;
        public DateTime CreatedAt { get; set; }
    }

    public class BoughtUserItem
    {
        public int BoughtUserItemID { get; set; }

        public int UserID { get; set; }
        public User User { get; set; } = default!;

        public int ItemID { get; set; }
        public Item Item { get; set; } = default!;

        public int StoreID { get; set; }
        public DateTime BoughtAt { get; set; }

        public int Qty { get; set; } 
    }



    public class LookedAtItem
    {
        public int UserID { get; set; }
        public User User { get; set; } = default!;
        public int ItemID { get; set; }
        public Item Item { get; set; } = default!;
        public DateTime ViewedAt { get; set; }
    }

    public class KeywordOfItem
    {
        public int ItemID { get; set; }
        public Item Item { get; set; } = default!;
        [MaxLength(120)] public string KeywordName { get; set; } = default!;
    }

    public class Setting
    {
        public int SettingID { get; set; }
        public int UserID { get; set; }
        public User User { get; set; } = default!;
        [MaxLength(100)] public string SettingName { get; set; } = default!;
        public string SettingValue { get; set; } = default!;
    }

    public class ItemComment
    {
       public int CommentID { get; set; }
       public int ItemID { get; set; }

       public int? UserID { get; set; } 
       public string OwnerName { get; set; } = "";
       public string CommentDescription { get; set; } = "";
       public int CommentStar { get; set; } 
       public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
       public bool IsActive { get; set; } = true;
       public Item? Item { get; set; }
        public string? PendingDescription { get; set; }
        public bool IsDescriptionApproved { get; set; } = false;
        public CommentModerationStatus ModerationStatus { get; set; } = CommentModerationStatus.Pending;
        public DateTime? ModerationDecidedAt { get; set; }
    }

    public enum CommentModerationStatus
    {
        Pending = 0,
        Approved = 1,
        Rejected = 2
    }

    public enum ComplaintDomain
    {
        Item = 1,
        Comment = 2
    }

    public class SavedCard
    {
        public int CardID { get; set; }
        public int UserID { get; set; }
        public User User { get; set; } = default!;
        [MaxLength(50)] public string Provider { get; set; } = default!;
        [MaxLength(200)] public string Token { get; set; } = default!;
        [MaxLength(4)] public string Last4 { get; set; } = default!;
        [MaxLength(30)] public string Brand { get; set; } = default!;
        public int CardExpMonth { get; set; }
        public int CardExpYear { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class SavedAddress
    {
        public int AddressID { get; set; }
        public int UserID { get; set; }

        [MaxLength(100)]
        public string? AddressName { get; set; }

        public User User { get; set; } = default!;
        public string AddressInformation { get; set; } = default!;
        public bool IsActive { get; set; }
    }

    public class Store
    {
        public int StoreID { get; set; }
        [MaxLength(200)] public string StoreName { get; set; } = default!;
        public DateTime StoreOpenedAt { get; set; }
        public string? StoreDescription { get; set; }
        public string StoreAddress { get; set; } = default!;
        public string? StorePictureLink { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public ICollection<Role> Roles { get; set; } = new List<Role>();
        public ICollection<SpecialOffer> SpecialOffers { get; set; } = new List<SpecialOffer>();
        public ICollection<ShoppingCart> Carts { get; set; } = new List<ShoppingCart>();
    }

    public class StoreOwner
    {
        public int StoreID { get; set; }
        public Store Store { get; set; } = default!;

        public int UserID { get; set; }
        public User User { get; set; } = default!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;

        public string OwnerName { get; set; } = default!;
    }

    public class ComplaintActionLog
    {
        public int LogID { get; set; }

        public int? ComplaintID { get; set; }
        public Personel.Complaint? Complaint { get; set; }

        public int? ItemID { get; set; }
        public Item? Item { get; set; }

        public int? CommentID { get; set; }
        public ItemComment? Comment { get; set; }

        [MaxLength(50)]
        public string Action { get; set; } = default!; // e.g. "CommentApproved", "ItemDeleted"

        public int PerformedByUserID { get; set; }
        public User PerformedByUser { get; set; } = default!;

        public DateTime PerformedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(500)]
        public string? Notes { get; set; }
    }

    public class Personel
    {
        public int PersonelID { get; set; }
        public string PersonelNumber { get; set; } = default!;
        public int UserID { get; set; }
        public int RoleID { get; set; }
        public Role Role { get; set; } = default!;
        public User User { get; set; } = default!;
        [MaxLength(100)] public string PersonelName { get; set; } = default!;
        [MaxLength(100)] public string PersonelSurname { get; set; } = default!;
        [MaxLength(200)] public string PersonelEmail { get; set; } = default!;
        [MaxLength(50)] public string PersonelTC { get; set; } = default!;
        public DateTime PersonelBirthDate { get; set; }
        public DateTime? PersonelCreatedAt { get; set; }
        public DateTime? PersonelExitAt { get; set; }
        public DateTime? LastLogin { get; set; }

        public bool IsActive { get; set; } = true;

        public decimal? Salary { get; set; }

        public int StoreID { get; set; }
        public Store Store { get; set; } = default!;
        public string? ProfilePictureLink { get; set; }
        public ICollection<PersonelTimeOff> TimeOffs { get; set; } = new List<PersonelTimeOff>();

        public class Complaint
        {
            public int ComplaintID { get; set; }

            public int? ItemID { get; set; }
            public Item? Item { get; set; }

            public int? CommentID { get; set; }
            public ItemComment? Comment { get; set; }

            public int ReporterUserID { get; set; }
            public User ReporterUser { get; set; } = default!;

            public int? TargetUserID { get; set; }
            public User? TargetUser { get; set; }

            public int ComplaintTypeID { get; set; }
            public ComplaintType ComplaintType { get; set; } = default!;

            [MaxLength(200)]
            public string Title { get; set; } = default!;

            [MaxLength(2000)]
            public string Description { get; set; } = default!;

            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

            [MaxLength(30)]
            public string Status { get; set; } = "New";

            public string? ManagerNotes { get; set; }
            public ComplaintDomain Domain { get; set; } = ComplaintDomain.Item;
        }

        public class ComplaintType
        {
            public int ComplaintTypeID { get; set; }

            [MaxLength(100)]
            public string Name { get; set; } = default!;

            public bool IsActive { get; set; } = true;
            public ComplaintDomain Domain { get; set; } = ComplaintDomain.Item;
        }

        public ICollection<PersonelWorkHistory> Histories { get; set; } = new List<PersonelWorkHistory>();
        public ICollection<PersonelEducation> Educations { get; set; } = new List<PersonelEducation>();
        public ICollection<PersonelCertificate> Certificates { get; set; } = new List<PersonelCertificate>();
        public ICollection<PersonelShift> Shifts { get; set; } = new List<PersonelShift>();
        public ICollection<PersonelWarning> Warnings { get; set; } = new List<PersonelWarning>();
        public ICollection<PersonelNote> Notes { get; set; } = new List<PersonelNote>();
    }

    public class ManagerAuditLog
    {
        public int ManagerAuditLogID { get; set; }

        public int StoreID { get; set; }
        public Store? Store { get; set; }

        public int ManagerUserID { get; set; }
        public User? ManagerUser { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public string Controller { get; set; } = "";
        public string Action { get; set; } = "";
        public string HttpMethod { get; set; } = "";

        public string? Path { get; set; }
        public string? QueryString { get; set; }
        public string? Description { get; set; }
        public string? IpAddress { get; set; }
    }

    public class PersonnelNumberCode
    {
        public int PersonnelNumberCodeID { get; set; }

        public int RoleID { get; set; }
        public Role Role { get; set; } = default!;

        [MaxLength(20)]public string Code { get; set; } = default!;

        public bool IsUsed { get; set; } = false;

        public DateTime CreatedAt { get; set; }
        public DateTime? UsedAt { get; set; }
        public int StoreID { get; set; }
        public Store Store { get; set; } = default!;
    }

    public class PersonnelQuickNote
    {
        public int PersonnelQuickNoteID { get; set; }

        public int PersonelID { get; set; }
        public Personel Personel { get; set; } = default!;

        [MaxLength(200)]
        public string Title { get; set; } = "";

        [MaxLength(4000)]
        public string Body { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public bool IsActive { get; set; } = true;
    }

    public class PersonnelDailyChecklistState
    {
        public int PersonnelDailyChecklistStateID { get; set; }

        public int PersonelID { get; set; }
        public Personel Personel { get; set; } = default!;

        public DateTime Day { get; set; }

        [MaxLength(120)]
        public string TaskKey { get; set; } = ""; // stable key like "shelves", "cashiers"

        public bool Done { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }


    public class PersonelWorkHistory
    {
        public int PersonelWorkHistoryID { get; set; }
        public int PersonelID { get; set; }
        public Personel Personel { get; set; } = default!;
        public string PersonelWorkHistoryName { get; set; } = default!;
        public DateTime PersonelWorkHistoryDateStart { get; set; }
        public DateTime? PersonelWorkHistoryDateEnd { get; set; }
        public string? PersonelWorkHistoryDescription { get; set; }
    }

    public class PersonelCertificate
    {
        public int PersonelCertificateID { get; set; }
        public int PersonelID { get; set; }
        public Personel Personel { get; set; } = default!;
        public string PersonelCertificateName { get; set; } = default!;
        public string? PersonelCertificateObtainedAtPlace { get; set; }
        public DateTime PersonelCertificateObtainedDateStart { get; set; }
        public DateTime? PersonelCertificateObtainedDateEnd { get; set; }
        public string? PersonelCertificateDescription { get; set; }
    }

    public class PersonelEducation
    {
        public int PersonelEducationID { get; set; }
        public int PersonelID { get; set; }
        public Personel Personel { get; set; } = default!;
        public string PersonelEducationName { get; set; } = default!;
        public DateTime PersonelEducationDateStart { get; set; }
        public DateTime? PersonelEducationDateEnd { get; set; }
        public bool PersonelEducationFinished { get; set; }
        public string? PersonelEducationDescription { get; set; }
    }

    public class PersonelNote
    {
        public int NoteID { get; set; }
        public int PersonelID { get; set; }
        public Personel Personel { get; set; } = default!;
        public string NoteName { get; set; } = default!;
        public string NoteDescription { get; set; } = default!;
        public DateTime NoteCreatedAt { get; set; }
    }

    public class PersonelWarning
    {
        public int PersonelWarningID { get; set; }
        public int PersonelID { get; set; }
        public Personel Personel { get; set; } = default!;
        public string GivenBy { get; set; } = default!;
        public DateTime CreatedAt { get; set; }
        [MaxLength(30)] public string WarningLevel { get; set; } = default!;
        public string WarningDescription { get; set; } = default!;
    }

    public class SpecialOffer
    {
        public int SpecialOfferID { get; set; }
        public int StoreID { get; set; }

        public string SpecialOfferName { get; set; } = null!;
        public string? SpecialOfferDescription { get; set; }

        public DateTime SpecialOfferDateStart { get; set; }
        public DateTime SpecialOfferDateEnd { get; set; }

        public bool IsCancelled { get; set; }

        public Store Store { get; set; } = null!;
        public ICollection<SpecialOfferItem> Items { get; set; } = new List<SpecialOfferItem>();
    }

    public class SpecialOfferItem
    {
        public int SpecialOfferID { get; set; }
        public int ItemID { get; set; }

        public decimal NewPrice { get; set; }

        public SpecialOffer SpecialOffer { get; set; } = null!;
        public Item Item { get; set; } = null!;
    }

    public class MessageDirect
    {
        public int MessageDirectID { get; set; }
        public int PersonelID { get; set; }
        public Personel Personel { get; set; } = default!;
        public DateTime CreatedAt { get; set; }
        public string MessageDescription { get; set; } = default!;
        public bool MessageReceived { get; set; }
        public bool MessageDone { get; set; }
        public bool IsActive { get; set; }
    }

    public class Conversation
    {
        public int ConversationID { get; set; }
        public int StarterUserID { get; set; }
        public int TargetUserID { get; set; }
        public DateTime CreatedAt { get; set; }

        public bool IsDeletedByStarter { get; set; }
        public bool IsDeletedByTarget { get; set; }

        public User StarterUser { get; set; } = default!;
        public User TargetUser { get; set; } = default!;
        public ICollection<ConversationMessage> Messages { get; set; } = new List<ConversationMessage>();
    }

    public enum IssueRequestStatus
    {
        Pending = 0,
        Accepted = 1,
        Rejected = 2
    }

    public class IssueRequest
    {
        public int IssueRequestID { get; set; }
        public int StoreID { get; set; }

        public int RequesterUserID { get; set; }
        public int TargetManagerUserID { get; set; }

        public string Message { get; set; } = "";
        public IssueRequestStatus Status { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? DecidedAt { get; set; }

        public int? DecidedByUserID { get; set; }
        public int? CreatedConversationID { get; set; }
    }
    public class ConversationMessage
    {
        public int ConversationMessageID { get; set; }
        public int ConversationID { get; set; }
        public int SenderUserID { get; set; }
        public string Text { get; set; } = "";
        public DateTime SentAt { get; set; }

        public Conversation Conversation { get; set; } = default!;
        public User SenderUser { get; set; } = default!;
    }

    public class Checklist
    {
        public int ChecklistID { get; set; }
        public int UserID { get; set; }
        public User User { get; set; } = default!;
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; }

        public ICollection<ChecklistContent> Content { get; set; } = new List<ChecklistContent>();
    }

    public class ChecklistContent
    {
        public int ChecklistContentID { get; set; } 
        public int ChecklistID { get; set; }
        public Checklist Checklist { get; set; } = default!;
        public string Task { get; set; } = default!;
    }

    public class PersonelShift
    {
        public int ShiftID { get; set; }
        public int PersonelID { get; set; }
        public Personel Personel { get; set; } = default!;

        [MaxLength(20)]
        public string ShiftDay { get; set; } = default!;

        public DateTime ShiftDateStart { get; set; }
        public DateTime ShiftDateEnd { get; set; }
        public bool IsActive { get; set; }

        [MaxLength(20)]
        public string? ShiftColor { get; set; }

        public DateTime? ClockInAt { get; set; }
        public DateTime? ClockOutAt { get; set; }
        public bool IsClockedIn { get; set; } = false;
        public bool IsClockedOut { get; set; } = false;
    }

    public class PersonelTimeOff
    {
        public int TimeOffID { get; set; }
        public int PersonelID { get; set; }
        public Personel Personel { get; set; } = default!;

        public DateTime DateStart { get; set; }
        public DateTime DateEnd { get; set; }

        [MaxLength(40)]
        public string TimeOffType { get; set; } = "PaidLeave"; // Annual, Sick, Unpaid, Off, etc.

        public bool IsPaid { get; set; } = true;
        public bool IsApproved { get; set; } = true;

        [MaxLength(500)]
        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class SuspiciousActivity
    {
        public int SActivityID { get; set; }
        public int PersonelID { get; set; }
        public Personel Personel { get; set; } = default!;
        public bool MoreThanOnePerson { get; set; }
        public string LocationOfReport { get; set; } = default!;
        public bool NearbyInnocentPeople { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Description { get; set; } = default!;
        public bool IsActive { get; set; }
    }

    public class SuspiciousPerson
    {
        public int SPeopleID { get; set; }
        public int PersonelID { get; set; }
        public Personel Personel { get; set; } = default!;
        public bool Temporary { get; set; }
        public string? PictureLink { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; }
        public string Description { get; set; } = default!;
        public string Reason { get; set; } = default!;
    }

    public class Report
    {
        public int ReportID { get; set; }
        public int PersonelID { get; set; }
        public Personel Personel { get; set; } = default!;
        public string ReportedPlace { get; set; } = default!;
        public DateTime ActivityDateStart { get; set; }
        public DateTime? ActivityDateEnd { get; set; }
        public int InjuredCount { get; set; }
        public string Description { get; set; } = default!;
        public string? PictureLink { get; set; }

        public ICollection<ReportProblem> Problems { get; set; } = new List<ReportProblem>();
    }

    public class ReportProblem
    {
        public int ReportProblemID { get; set; }
        public int ReportID { get; set; }
        public Report Report { get; set; } = default!;
        public string ProblemName { get; set; } = default!;
    }

    public class ShoppingCart
    {
        public int CartID { get; set; }
        public int UserID { get; set; }
        public User User { get; set; } = default!;
        public DateTime CreatedAt { get; set; }
        public int StoreID { get; set; }
        public Store Store { get; set; } = default!; 
        public bool IsActive { get; set; } = true;

        public ICollection<ShoppingCartItem> Items { get; set; } = new List<ShoppingCartItem>();
    }

    public class ShoppingCartItem
    {
        public int CartItemID { get; set; }
        public int CartID { get; set; }
        public ShoppingCart Cart { get; set; } = default!;
        public int ItemID { get; set; }
        public Item Item { get; set; } = default!;
        public int Qty { get; set; } = 1;
        public DateTime AddedToCartAt { get; set; }
    }

    // -------------------- DB CONTEXT --------------------

    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void ConfigureConventions(ModelConfigurationBuilder builder)
        {
            builder.Properties<decimal>().HavePrecision(18, 2);
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<PersonnelNumberCode> PersonnelNumberCodes => Set<PersonnelNumberCode>();

        public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();

        public DbSet<Item> Items => Set<Item>();
        public DbSet<ItemStock> ItemStocks => Set<ItemStock>();
        public DbSet<CategoryItem> CategoryItems => Set<CategoryItem>();
        public DbSet<FavouriteUserItem> FavouriteUserItems => Set<FavouriteUserItem>();
        public DbSet<BoughtUserItem> BoughtUserItems => Set<BoughtUserItem>();
        public DbSet<LookedAtItem> LookedAtItems => Set<LookedAtItem>();
        public DbSet<KeywordOfItem> KeywordsOfItems => Set<KeywordOfItem>();
        public DbSet<Setting> Settings => Set<Setting>();
        public DbSet<ItemComment> ItemComments => Set<ItemComment>();
        public DbSet<ComplaintActionLog> ComplaintActionLogs => Set<ComplaintActionLog>();

        public DbSet<Complaint> Complaints { get; set; } = default!;
        public DbSet<ComplaintType> ComplaintTypes { get; set; } = default!;
        public DbSet<SavedCard> SavedCards => Set<SavedCard>();
        public DbSet<SavedAddress> SavedAddresses => Set<SavedAddress>();

        public DbSet<Store> Stores => Set<Store>();
        public DbSet<StoreOwner> StoreOwners => Set<StoreOwner>();

        public DbSet<Personel> Personels => Set<Personel>();
        public DbSet<PersonelWorkHistory> PersonelHistories => Set<PersonelWorkHistory>();
        public DbSet<PersonelCertificate> PersonelCertificates => Set<PersonelCertificate>();
        public DbSet<PersonelEducation> PersonelEducations => Set<PersonelEducation>();
        public DbSet<PersonelNote> PersonelNotes => Set<PersonelNote>();
        public DbSet<PersonelWarning> PersonelWarnings => Set<PersonelWarning>();
        public DbSet<PersonnelQuickNote> PersonnelQuickNotes => Set<PersonnelQuickNote>();
        public DbSet<PersonnelDailyChecklistState> PersonnelDailyChecklistStates => Set<PersonnelDailyChecklistState>();
        public DbSet<SpecialOffer> SpecialOffers { get; set; } = null!;
        public DbSet<SpecialOfferItem> SpecialOfferItems { get; set; } = null!;
        public DbSet<ManagerAuditLog> ManagerAuditLogs => Set<ManagerAuditLog>();

        public DbSet<MessageDirect> MessagesDirectly => Set<MessageDirect>();
        public DbSet<Conversation> Conversations { get; set; } = default!;
        public DbSet<ConversationMessage> ConversationMessages { get; set; } = default!;

        public DbSet<IssueRequest> IssueRequests => Set<IssueRequest>();

        public DbSet<Checklist> Checklists => Set<Checklist>();
        public DbSet<ChecklistContent> ChecklistContents => Set<ChecklistContent>();

        public DbSet<PersonelShift> PersonelShifts => Set<PersonelShift>();
        public DbSet<PersonelTimeOff> PersonelTimeOffs => Set<PersonelTimeOff>();

        public DbSet<SuspiciousActivity> SuspiciousActivityDatabase => Set<SuspiciousActivity>();
        public DbSet<SuspiciousPerson> SuspiciousPeopleDatabase => Set<SuspiciousPerson>();

        public DbSet<Report> ReportCreation => Set<Report>();
        public DbSet<ReportProblem> ReportCreationProblems => Set<ReportProblem>();

        public DbSet<ShoppingCart> ShoppingCarts => Set<ShoppingCart>();
        public DbSet<ShoppingCartItem> ShoppingCartItems => Set<ShoppingCartItem>();

        protected override void OnModelCreating(ModelBuilder b)
        {

            base.OnModelCreating(b);
            b.Entity<User>().HasKey(x => x.UserID);
            b.Entity<User>().HasIndex(x => x.Email).IsUnique();

            b.Entity<Role>(e =>
            {
                e.HasKey(r => r.RoleID);

                e.Property(r => r.RoleName)
                    .IsRequired()
                    .HasMaxLength(100);

                e.HasOne(r => r.Store)
                    .WithMany(s => s.Roles)
                    .HasForeignKey(r => r.StoreID)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasIndex(r => new { r.StoreID, r.RoleName })
                    .IsUnique();
            });

            b.Entity<Permission>()
                .HasKey(p => p.PermissionID);

            b.Entity<Permission>()
                .Property(p => p.PermissionKey)
                .IsRequired()
                .HasMaxLength(100);

            b.Entity<Permission>()
                .HasIndex(p => p.PermissionKey)
                .IsUnique();

            b.Entity<RolePermission>()
                .HasKey(rp => new { rp.RoleID, rp.PermissionID });

            b.Entity<RolePermission>()
                .HasOne(rp => rp.Role)
                .WithMany(r => r.RolePermissions)
                .HasForeignKey(rp => rp.RoleID);

            b.Entity<RolePermission>()
                .HasOne(rp => rp.Permission)
                .WithMany(p => p.RolePermissions)
                .HasForeignKey(rp => rp.PermissionID);

            b.Entity<PersonnelNumberCode>(e =>
            {
                e.HasKey(x => x.PersonnelNumberCodeID);

                e.Property(x => x.Code)
                    .HasMaxLength(20)
                    .IsRequired();

                e.HasIndex(x => x.Code).IsUnique();

                e.HasOne(x => x.Role)
                    .WithMany(r => r.PersonnelNumberCodes)
                    .HasForeignKey(x => x.RoleID);
            });

            b.Entity<PersonnelNumberCode>()
                .HasOne(c => c.Store)
                .WithMany()
                .HasForeignKey(c => c.StoreID)
                .OnDelete(DeleteBehavior.Cascade);

            b.Entity<PersonnelQuickNote>(e =>
            {
                e.HasKey(x => x.PersonnelQuickNoteID);
                e.HasOne(x => x.Personel)
                 .WithMany()
                 .HasForeignKey(x => x.PersonelID)
                 .OnDelete(DeleteBehavior.Cascade);

                e.Property(x => x.Title).HasMaxLength(200).IsRequired();
                e.Property(x => x.Body).HasMaxLength(4000).IsRequired();
                e.HasIndex(x => new { x.PersonelID, x.IsActive, x.UpdatedAt });
            });

            b.Entity<PersonnelDailyChecklistState>(e =>
            {
                e.HasKey(x => x.PersonnelDailyChecklistStateID);
                e.HasOne(x => x.Personel)
                 .WithMany()
                 .HasForeignKey(x => x.PersonelID)
                 .OnDelete(DeleteBehavior.Cascade);

                e.Property(x => x.TaskKey).HasMaxLength(120).IsRequired();
                e.HasIndex(x => new { x.PersonelID, x.Day, x.TaskKey }).IsUnique();
            });

            b.Entity<Conversation>()
                .HasOne(c => c.StarterUser)
                .WithMany()  
                .HasForeignKey(c => c.StarterUserID)
                .OnDelete(DeleteBehavior.Restrict);

            b.Entity<Conversation>()
                .HasOne(c => c.TargetUser)
                .WithMany()
                .HasForeignKey(c => c.TargetUserID)
                .OnDelete(DeleteBehavior.Restrict);

            b.Entity<ConversationMessage>()
                .HasOne(m => m.Conversation)
                .WithMany(c => c.Messages)
                .HasForeignKey(m => m.ConversationID);

            b.Entity<ConversationMessage>()
                .HasOne(m => m.SenderUser)
                .WithMany() 
                .HasForeignKey(m => m.SenderUserID)
                .OnDelete(DeleteBehavior.Restrict);

            b.Entity<IssueRequest>(e =>
            {
                e.ToTable("IssueRequests");
                e.HasKey(x => x.IssueRequestID);

                e.Property(x => x.Message).HasMaxLength(2000).IsRequired();
                e.Property(x => x.Status).IsRequired();

                e.HasIndex(x => new { x.StoreID, x.Status, x.CreatedAt });
                e.HasIndex(x => new { x.TargetManagerUserID, x.Status, x.CreatedAt });
                e.HasIndex(x => new { x.RequesterUserID, x.Status, x.CreatedAt });
            });



            b.Entity<ComplaintType>().HasData(
                new ComplaintType { ComplaintTypeID = 1, Name = "Ürünle ilgili" },
                new ComplaintType { ComplaintTypeID = 2, Name = "Yorum / Davranış" },
                new ComplaintType { ComplaintTypeID = 3, Name = "Diğer" }
            );

            b.Entity<Item>().HasKey(x => x.ItemID);
            b.Entity<ItemStock>().HasKey(x => new { x.ItemID, x.ChangedAt });
            b.Entity<ItemStock>()
                .HasOne(x => x.Item).WithMany(i => i.StockChanges).HasForeignKey(x => x.ItemID);

            b.Entity<CategoryItem>().HasKey(x => new { x.ItemID, x.CategoryName });
            b.Entity<CategoryItem>()
                .HasOne(x => x.Item).WithMany(i => i.Categories).HasForeignKey(x => x.ItemID);

            b.Entity<KeywordOfItem>().HasKey(x => new { x.ItemID, x.KeywordName });
            b.Entity<KeywordOfItem>()
                .HasOne(x => x.Item).WithMany(i => i.Keywords).HasForeignKey(x => x.ItemID);

            b.Entity<FavouriteUserItem>().HasKey(x => new { x.UserID, x.ItemID });
            b.Entity<FavouriteUserItem>()
                .HasOne(x => x.User).WithMany(u => u.FavouriteItems).HasForeignKey(x => x.UserID);
            b.Entity<FavouriteUserItem>()
                .HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemID);

            b.Entity<BoughtUserItem>(e =>
            {
                e.ToTable("BoughtUserItems");

                e.HasKey(x => x.BoughtUserItemID);

                e.Property(x => x.Qty).IsRequired();
                e.Property(x => x.BoughtAt).IsRequired();

                e.HasOne(x => x.User)
                    .WithMany(u => u.BoughtItems)
                    .HasForeignKey(x => x.UserID)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.Item)
                    .WithMany()
                    .HasForeignKey(x => x.ItemID)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasIndex(x => new { x.StoreID, x.ItemID, x.BoughtAt });
                e.HasIndex(x => new { x.StoreID, x.BoughtAt });
                e.HasIndex(x => new { x.UserID, x.StoreID, x.BoughtAt });
            });


            b.Entity<LookedAtItem>().HasKey(x => new { x.UserID, x.ItemID, x.ViewedAt });
            b.Entity<LookedAtItem>()
                .HasOne(x => x.User).WithMany(u => u.LookedAtItems).HasForeignKey(x => x.UserID);
            b.Entity<LookedAtItem>()
                .HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemID);

            b.Entity<ItemComment>(e =>
            {
                e.HasKey(x => x.CommentID);

                e.Property(x => x.OwnerName).HasMaxLength(120).IsRequired();
                e.Property(x => x.CommentDescription).HasMaxLength(1000).IsRequired();
                e.Property(x => x.CommentStar).IsRequired();

                e.HasOne(x => x.Item)
                    .WithMany(i => i.Comments)
                    .HasForeignKey(x => x.ItemID);

                e.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(x => x.UserID)
                    .OnDelete(DeleteBehavior.SetNull);

                e.Property(x => x.PendingDescription).HasMaxLength(1000);
                e.Property(x => x.ModerationStatus).IsRequired();
                e.Property(x => x.ModerationDecidedAt);
            });

            b.Entity<ComplaintActionLog>(e =>
            {
                e.HasKey(x => x.LogID);

                e.Property(x => x.Action).HasMaxLength(50).IsRequired();
                e.Property(x => x.Notes).HasMaxLength(500);

                e.HasOne(x => x.PerformedByUser)
                 .WithMany()
                 .HasForeignKey(x => x.PerformedByUserID)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.Complaint)
                 .WithMany()
                 .HasForeignKey(x => x.ComplaintID)
                 .OnDelete(DeleteBehavior.SetNull);

                e.HasOne(x => x.Item)
                 .WithMany()
                 .HasForeignKey(x => x.ItemID)
                 .OnDelete(DeleteBehavior.SetNull);

                e.HasOne(x => x.Comment)
                 .WithMany()
                 .HasForeignKey(x => x.CommentID)
                 .OnDelete(DeleteBehavior.SetNull);
            });

            b.Entity<InventoryItem>().HasKey(x => new { x.StoreID, x.ItemID });
            b.Entity<InventoryItem>()
                .HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreID);
            b.Entity<InventoryItem>()
                .HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemID);

            b.Entity<SavedCard>().HasKey(x => x.CardID);
            b.Entity<SavedCard>()
                .HasOne(x => x.User).WithMany(u => u.SavedCards).HasForeignKey(x => x.UserID);
            b.Entity<SavedAddress>().HasKey(x => x.AddressID);
            b.Entity<SavedAddress>()
                .HasOne(x => x.User).WithMany(u => u.SavedAddresses).HasForeignKey(x => x.UserID);

            b.Entity<SavedAddress>()
                .Property(a => a.AddressName)
                .HasMaxLength(100);

            b.Entity<StoreOwner>().HasKey(x => new { x.StoreID, x.UserID });
            b.Entity<StoreOwner>()
                .HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreID);

            b.Entity<StoreOwner>()
                .HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserID)
                .OnDelete(DeleteBehavior.Cascade);

            b.Entity<Personel>().HasKey(x => x.PersonelID);
            b.Entity<Personel>()
                .HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleID);

            b.Entity<Personel>(e =>
            {
                e.HasKey(x => x.PersonelID);

                e.Property(x => x.PersonelNumber)
                    .HasMaxLength(40) 
                    .IsRequired();

                e.HasIndex(x => x.PersonelNumber)
                    .IsUnique();
            });

            b.Entity<Personel>()
                .HasOne(p => p.Store)
                .WithMany()
                .HasForeignKey(p => p.StoreID)
                .OnDelete(DeleteBehavior.Restrict);

            b.Entity<Personel>()
                .HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserID)
                .OnDelete(DeleteBehavior.Restrict);

            b.Entity<PersonelWorkHistory>().HasKey(x => x.PersonelWorkHistoryID);
            b.Entity<PersonelWorkHistory>()
                .HasOne(x => x.Personel)
                .WithMany(p => p.Histories)
                .HasForeignKey(x => x.PersonelID);

            b.Entity<PersonelCertificate>().HasKey(x => x.PersonelCertificateID);
            b.Entity<PersonelCertificate>()
                .HasOne(x => x.Personel)
                .WithMany(p => p.Certificates)
                .HasForeignKey(x => x.PersonelID);

            b.Entity<PersonelEducation>().HasKey(x => x.PersonelEducationID);
            b.Entity<PersonelEducation>()
                .HasOne(x => x.Personel)
                .WithMany(p => p.Educations)
                .HasForeignKey(x => x.PersonelID);

            b.Entity<PersonelWarning>().HasKey(x => x.PersonelWarningID);
            b.Entity<PersonelWarning>()
                .HasOne(x => x.Personel)
                .WithMany(p => p.Warnings)
                .HasForeignKey(x => x.PersonelID);

            b.Entity<PersonelNote>().HasKey(x => x.NoteID);
            b.Entity<PersonelNote>()
                .HasOne(x => x.Personel).WithMany(p => p.Notes).HasForeignKey(x => x.PersonelID);

            b.Entity<SpecialOffer>().HasKey(x => x.SpecialOfferID);

            b.Entity<SpecialOffer>().HasOne(x => x.Store)
              .WithMany(s => s.SpecialOffers)
              .HasForeignKey(x => x.StoreID)
              .OnDelete(DeleteBehavior.Cascade);

            b.Entity<SpecialOffer>().Property(x => x.SpecialOfferName)
              .IsRequired()
              .HasMaxLength(200);

            b.Entity<SpecialOfferItem>().HasKey(x => new { x.SpecialOfferID, x.ItemID });

            b.Entity<SpecialOfferItem>().HasOne(x => x.SpecialOffer)
               .WithMany(o => o.Items)
               .HasForeignKey(x => x.SpecialOfferID)
               .OnDelete(DeleteBehavior.Cascade);

            b.Entity<SpecialOfferItem>().HasOne(x => x.Item)
               .WithMany(i => i.SpecialOfferItems)
               .HasForeignKey(x => x.ItemID)
               .OnDelete(DeleteBehavior.Cascade);

            b.Entity<MessageDirect>().HasKey(x => x.MessageDirectID);
            b.Entity<MessageDirect>()
                .HasOne(x => x.Personel).WithMany().HasForeignKey(x => x.PersonelID);

            b.Entity<Checklist>().HasKey(x => x.ChecklistID);
            b.Entity<Checklist>()
                .HasOne(x => x.User).WithMany(u => u.Checklists).HasForeignKey(x => x.UserID);
            b.Entity<ChecklistContent>().HasKey(x => x.ChecklistContentID);
            b.Entity<ChecklistContent>()
                .HasOne(x => x.Checklist).WithMany(c => c.Content).HasForeignKey(x => x.ChecklistID);

            b.Entity<PersonelShift>().HasKey(x => x.ShiftID);
            b.Entity<PersonelShift>()
                .HasOne(x => x.Personel)
                .WithMany(p => p.Shifts)
                .HasForeignKey(x => x.PersonelID);

            b.Entity<PersonelShift>()
                .Property(x => x.ShiftColor)
                .HasMaxLength(20);

            b.Entity<PersonelTimeOff>(e =>
            {
                e.ToTable("PersonelTimeOff");

                e.HasKey(x => x.TimeOffID);

                e.HasOne(x => x.Personel)
                    .WithMany(p => p.TimeOffs)
                    .HasForeignKey(x => x.PersonelID)
                    .OnDelete(DeleteBehavior.Cascade);

                e.Property(x => x.TimeOffType)
                    .HasMaxLength(40)
                    .IsRequired();

                e.Property(x => x.Description)
                    .HasMaxLength(500);
            });

            b.Entity<SuspiciousActivity>().HasKey(x => x.SActivityID);
            b.Entity<SuspiciousActivity>()
                .HasOne(x => x.Personel).WithMany().HasForeignKey(x => x.PersonelID);
            b.Entity<SuspiciousPerson>().HasKey(x => x.SPeopleID);
            b.Entity<SuspiciousPerson>()
                .HasOne(x => x.Personel).WithMany().HasForeignKey(x => x.PersonelID);

            b.Entity<Report>().HasKey(x => x.ReportID);
            b.Entity<Report>()
                .HasOne(x => x.Personel).WithMany().HasForeignKey(x => x.PersonelID);
            b.Entity<ReportProblem>().HasKey(x => x.ReportProblemID);
            b.Entity<ReportProblem>()
                .HasOne(x => x.Report).WithMany(r => r.Problems).HasForeignKey(x => x.ReportID);

            b.Entity<ShoppingCart>().HasKey(x => x.CartID);
            b.Entity<ShoppingCart>()
                .HasOne(x => x.User).WithMany(u => u.Carts).HasForeignKey(x => x.UserID);

            b.Entity<ShoppingCart>()   
                .HasOne(x => x.Store)    
                .WithMany(s => s.Carts) 
                .HasForeignKey(x => x.StoreID);

            b.Entity<ShoppingCartItem>().HasKey(x => x.CartItemID);
            b.Entity<ShoppingCartItem>()
                .HasOne(x => x.Cart).WithMany(c => c.Items).HasForeignKey(x => x.CartID);
            b.Entity<ShoppingCartItem>()
                .HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemID);
        }
    }
}
