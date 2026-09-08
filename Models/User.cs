namespace backend.Models;

public class User
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsEmailVerified { get; set; } = true;
    public string? EmailOtpCode { get; set; }
    public DateTime? EmailOtpExpiresAt { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<UserEmail> AdditionalEmails { get; set; } = new();
    public List<Post> Posts { get; set; } = new();
    public List<Comment> Comments { get; set; } = new();
    public List<PostLike> Likes { get; set; } = new();
}