using Microsoft.AspNetCore.Mvc.Rendering;

namespace DükkanBulutSitesi.Models.Manager
{
    public class ConversationSummaryVm
    {
        public int ConversationID { get; set; }
        public int PersonelID { get; set; }

        public string PersonelName { get; set; } = "";
        public string LastMessage { get; set; } = "";
        public DateTime LastSentAt { get; set; }
    }

    public class ConversationMessageVm
    {
        public int ConversationID { get; set; }
        public int PersonelID { get; set; }

        public bool IsFromManager { get; set; }
        public bool IsSystem { get; set; }
        public string Message { get; set; } = "";
        public DateTime SentAt { get; set; }
    }
    public class ConversationPageVm
    {
        public List<ConversationSummaryVm> Conversations { get; set; } = new();
        public int? SelectedConversationID { get; set; }
        public int? SelectedPersonelID { get; set; }
        public string? SelectedPersonelName { get; set; }

        public List<ConversationMessageVm> Messages { get; set; } = new();

        public string? SelectedPersonelStoreName { get; set; }

        public List<ArchivedConversationVm> ArchivedConversations { get; set; } = new();

        public List<PersonelLookupVm> PersonelChoices { get; set; } = new();
        public List<SelectListItem> OwnerChoices { get; set; } = new();

        public List<IssueRequestSummaryVm> PendingIssueRequests { get; set; } = new();
        public int? SelectedIssueRequestID { get; set; }
        public bool IsIssueRequestSelected { get; set; }
        public string? SelectedIssueMessage { get; set; }
        public DateTime? SelectedIssueCreatedAt { get; set; }
        public List<IssueRequestSummaryVm> RejectedIssueRequests { get; set; } = new();
    }

    public class IssueRequestSummaryVm
    {
        public int IssueRequestID { get; set; }
        public int RequesterUserID { get; set; }
        public int RequesterPersonelID { get; set; }
        public string RequesterName { get; set; } = "";

        public string Message { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    public class ArchivedConversationVm
    {
        public int ConversationID { get; set; }
        public int PersonelID { get; set; }
        public string PersonelName { get; set; } = "";
        public string LastMessage { get; set; } = "";
        public DateTime LastSentAt { get; set; }
        public List<ConversationMessageVm> Messages { get; set; } = new();
    }

    public class PersonelLookupVm
    {
        public int PersonelID { get; set; }
        public string DisplayName { get; set; } = "";
    }
}
