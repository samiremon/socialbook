namespace backend.Models;

public class Notification
{
    public int Id { get; set; }

    public int UserId { get; set; } // Recipient
    public User User { get; set; } = null!;

    public int ActorId { get; set; } // Trigger User
    public User Actor { get; set; } = null!;

    public string Type { get; set; } = string.Empty; // LIKE, COMMENT, FRIEND_REQUEST, FRIEND_ACCEPT, MESSAGE
    public string Content { get; set; } = string.Empty;

    public int? TargetId { get; set; } // PostId, MessageId, etc.
    public bool IsRead { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
