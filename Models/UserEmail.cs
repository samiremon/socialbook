namespace backend.Models;

public class UserEmail
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public string Email { get; set; } = string.Empty;
    public bool IsVerified { get; set; } = false;
    public string? OtpCode { get; set; }
    public DateTime? OtpExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
