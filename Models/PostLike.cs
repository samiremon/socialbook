namespace backend.Models;

public class PostLike
{
    public int Id { get; set; }
    public int PostId { get; set; }
    public Post Post { get; set; } = null!;

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public string ReactionType { get; set; } = "LIKE"; // LIKE, LOVE, HAHA, WOW, SAD, ANGRY

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
