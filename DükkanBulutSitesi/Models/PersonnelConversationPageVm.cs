using Entity;

namespace DükkanBulutSitesi.Models
{
    public class PersonnelConversationPageVm
    {
        public int CurrentUserID { get; set; }

        public List<PersonnelThreadSummaryVm> Threads { get; set; } = new();

        public int? SelectedConversationID { get; set; }
        public int? SelectedIssueRequestID { get; set; }
        public PersonnelThreadKind? SelectedKind { get; set; }

        public string? OtherUserName { get; set; }

        public List<ConversationMessage>? SelectedMessages { get; set; }

        // Issue request details
        public string? IssueMessage { get; set; }
        public IssueRequestStatus? IssueStatus { get; set; }
        public DateTime? IssueCreatedAt { get; set; }

        public List<IssueRequest> MyIssueRequests { get; set; } = new();

        public List<IssueRequest> AcceptedIssues { get; set; } = new();
        public List<IssueRequest> RejectedIssues { get; set; } = new();
        public List<IssueRequest> PendingIssues { get; set; } = new();
    }

    public class PersonnelConversationSummaryVm
    {
        public int ConversationID { get; set; }
        public string OtherUserName { get; set; } = "";
        public string LastMessage { get; set; } = "";
        public DateTime? LastSentAt { get; set; }
    }

    public enum PersonnelThreadKind
    {
        Conversation = 0,
        IssueRequest = 1
    }

    public class PersonnelThreadSummaryVm
    {
        public PersonnelThreadKind Kind { get; set; }
        public int ConversationID { get; set; }          // for Kind=Conversation
        public int IssueRequestID { get; set; }          // for Kind=IssueRequest

        public string OtherUserName { get; set; } = "";
        public string LastMessage { get; set; } = "";
        public DateTime? LastSentAt { get; set; }

        public IssueRequestStatus? IssueStatus { get; set; } // for Kind=IssueRequest
    }


}
