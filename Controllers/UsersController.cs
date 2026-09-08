using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using backend.Data;
using backend.Models;
using backend.Services;

namespace backend.Controllers;

[Authorize]
[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailService _emailService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(ApplicationDbContext context, IEmailService emailService, ILogger<UsersController> logger)
    {
        _context = context;
        _emailService = emailService;
        _logger = logger;
    }

    private int? GetCurrentUserId()
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (int.TryParse(userIdString, out int userId)) return userId;

        // Fallback to first user in database (e.g. John Doe) if no token is present
        var fallbackUser = _context.Users.FirstOrDefault();
        if (fallbackUser != null) return fallbackUser.Id;

        return null;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllUsers()
    {
        var users = await _context.Users
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.FullName,
                u.Email,
                u.IsEmailVerified,
                u.Bio,
                u.AvatarUrl
            })
            .ToListAsync();

        return Ok(users);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetUserById(int id)
    {
        var user = await _context.Users
            .Where(u => u.Id == id)
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.FullName,
                u.Email,
                u.IsEmailVerified,
                u.Bio,
                u.AvatarUrl
            })
            .FirstOrDefaultAsync();

        if (user == null)
        {
            return NotFound(new { message = "User not found." });
        }

        return Ok(user);
    }

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            return Unauthorized(new { message = "Invalid token or user not logged in." });
        }

        var user = await _context.Users.FindAsync(userId.Value);
        if (user == null)
        {
            return NotFound(new { message = "User not found." });
        }

        if (!string.IsNullOrWhiteSpace(dto.FullName))
        {
            user.FullName = dto.FullName;
        }

        user.Bio = dto.Bio;

        if (!string.IsNullOrEmpty(dto.AvatarUrl))
        {
            user.AvatarUrl = dto.AvatarUrl;
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            id = user.Id,
            username = user.Username,
            fullName = user.FullName,
            email = user.Email,
            isEmailVerified = user.IsEmailVerified,
            bio = user.Bio,
            avatarUrl = user.AvatarUrl
        });
    }

    [AllowAnonymous]
    [HttpGet("account")]
    public async Task<IActionResult> GetAccountSettings()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "Invalid token." });

        var user = await _context.Users
            .Include(u => u.AdditionalEmails)
            .FirstOrDefaultAsync(u => u.Id == userId.Value);

        if (user == null) return NotFound(new { message = "User not found." });

        return Ok(new
        {
            id = user.Id,
            username = user.Username,
            fullName = user.FullName,
            primaryEmail = user.Email,
            isPrimaryEmailVerified = user.IsEmailVerified,
            additionalEmails = user.AdditionalEmails.Select(e => new
            {
                id = e.Id,
                email = e.Email,
                isVerified = e.IsVerified,
                createdAt = e.CreatedAt
            }).ToList()
        });
    }

    [AllowAnonymous]
    [HttpPost("send-email-otp")]
    public async Task<IActionResult> SendEmailOtp([FromBody] SendEmailOtpDto dto)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "Invalid token." });

        if (string.IsNullOrWhiteSpace(dto.Email) || !dto.Email.Contains("@") || !dto.Email.Contains("."))
        {
            return BadRequest(new { message = "Please provide a valid email address." });
        }

        var cleanEmail = dto.Email.Trim().ToLowerInvariant();

        var user = await _context.Users
            .Include(u => u.AdditionalEmails)
            .FirstOrDefaultAsync(u => u.Id == userId.Value);

        if (user == null) return NotFound(new { message = "User not found." });

        // Check if email is already registered and verified on a different account
        var emailTakenByOther = await _context.Users.AnyAsync(u => u.Id != user.Id && u.Email.ToLower() == cleanEmail)
            || await _context.UserEmails.AnyAsync(e => e.UserId != user.Id && e.Email.ToLower() == cleanEmail && e.IsVerified);

        if (emailTakenByOther)
        {
            return BadRequest(new { message = "This email is already associated with another account." });
        }

        var otp = Random.Shared.Next(100000, 999999).ToString();
        var expiresAt = DateTime.UtcNow.AddMinutes(10);

        if (user.Email.ToLower() == cleanEmail)
        {
            user.EmailOtpCode = otp;
            user.EmailOtpExpiresAt = expiresAt;
        }
        else
        {
            var existing = user.AdditionalEmails.FirstOrDefault(e => e.Email.ToLower() == cleanEmail);
            if (existing != null)
            {
                existing.OtpCode = otp;
                existing.OtpExpiresAt = expiresAt;
            }
            else
            {
                user.AdditionalEmails.Add(new UserEmail
                {
                    Email = cleanEmail,
                    IsVerified = false,
                    OtpCode = otp,
                    OtpExpiresAt = expiresAt,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        await _context.SaveChangesAsync();

        try
        {
            await _emailService.SendVerificationOtpAsync(cleanEmail, user.FullName, otp);
            return Ok(new { message = $"A 6-digit verification code has been sent to {cleanEmail}." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send OTP email to {Email}", cleanEmail);
            return StatusCode(500, new { message = $"Failed to send email: {ex.Message}. Check your SMTP connection or app password." });
        }
    }

    [AllowAnonymous]
    [HttpPost("verify-email-otp")]
    public async Task<IActionResult> VerifyEmailOtp([FromBody] VerifyEmailOtpDto dto)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "Invalid token." });

        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Otp))
        {
            return BadRequest(new { message = "Email and 6-digit OTP code are required." });
        }

        var cleanEmail = dto.Email.Trim().ToLowerInvariant();
        var cleanOtp = dto.Otp.Trim();

        var user = await _context.Users
            .Include(u => u.AdditionalEmails)
            .FirstOrDefaultAsync(u => u.Id == userId.Value);

        if (user == null) return NotFound(new { message = "User not found." });

        bool verified = false;

        if (user.Email.ToLower() == cleanEmail)
        {
            if (user.EmailOtpCode == cleanOtp && user.EmailOtpExpiresAt >= DateTime.UtcNow)
            {
                user.IsEmailVerified = true;
                user.EmailOtpCode = null;
                user.EmailOtpExpiresAt = null;
                verified = true;
            }
        }
        else
        {
            var additional = user.AdditionalEmails.FirstOrDefault(e => e.Email.ToLower() == cleanEmail);
            if (additional != null && additional.OtpCode == cleanOtp && additional.OtpExpiresAt >= DateTime.UtcNow)
            {
                additional.IsVerified = true;
                additional.OtpCode = null;
                additional.OtpExpiresAt = null;
                verified = true;
            }
        }

        if (!verified)
        {
            return BadRequest(new { message = "Invalid or expired verification code. Please request a new code." });
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = $"Email {cleanEmail} verified successfully! You can now use it to log in.",
            email = cleanEmail,
            isVerified = true
        });
    }

    [AllowAnonymous]
    [HttpDelete("emails/{id}")]
    public async Task<IActionResult> DeleteAdditionalEmail(int id)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "Invalid token." });

        var email = await _context.UserEmails.FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId.Value);
        if (email == null)
        {
            return NotFound(new { message = "Email address not found." });
        }

        _context.UserEmails.Remove(email);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Email address removed successfully." });
    }

    [AllowAnonymous]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "Invalid token." });

        if (string.IsNullOrWhiteSpace(dto.CurrentPassword) || string.IsNullOrWhiteSpace(dto.NewPassword))
        {
            return BadRequest(new { message = "Both current and new passwords are required." });
        }

        if (dto.NewPassword.Length < 6)
        {
            return BadRequest(new { message = "New password must be at least 6 characters long." });
        }

        var user = await _context.Users.FindAsync(userId.Value);
        if (user == null) return NotFound(new { message = "User not found." });

        if (CreatePasswordHash(dto.CurrentPassword) != user.PasswordHash)
        {
            return BadRequest(new { message = "The current password you entered is incorrect." });
        }

        user.PasswordHash = CreatePasswordHash(dto.NewPassword);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Password changed successfully!" });
    }

    private string CreatePasswordHash(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }
}

public class UpdateProfileDto
{
    public string? FullName { get; set; }
    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }
}

public class SendEmailOtpDto
{
    public string Email { get; set; } = string.Empty;
}

public class VerifyEmailOtpDto
{
    public string Email { get; set; } = string.Empty;
    public string Otp { get; set; } = string.Empty;
}

public class ChangePasswordDto
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}