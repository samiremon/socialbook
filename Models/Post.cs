namespace backend.Models;

public class Post
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? ImageUrl { get; set; } //
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int AuthorId { get; set; }
    public User Author { get; set; } = null!;

    public List<Comment> Comments { get; set; } = new();
    public List<PostLike> Likes { get; set; } = new();
}