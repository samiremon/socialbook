using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using backend.Data;
using backend.Models;

namespace backend.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;

    public AuthController(ApplicationDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        if (await _context.Users.AnyAsync(u => u.Username == dto.Username || u.Email == dto.Email))
        {
            return BadRequest(new { message = "Username or email already exists." });
        }

        string passwordHash = CreatePasswordHash(dto.Password);

        var user = new User
        {
            FullName = dto.FullName,
            Username = dto.Username,
            Email = dto.Email,
            PasswordHash = passwordHash
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var token = CreateToken(user);

        return Ok(new
        {
            message = "Registration successful!",
            token = token,
            user = new
            {
                id = user.Id,
                username = user.Username,
                fullName = user.FullName,
                email = user.Email,
                isEmailVerified = user.IsEmailVerified,
                avatarUrl = user.AvatarUrl,
                bio = user.Bio
            }
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var input = dto.EmailOrUsername?.Trim() ?? string.Empty;
        var user = await _context.Users
            .Include(u => u.AdditionalEmails)
            .FirstOrDefaultAsync(u => u.Username.ToLower() == input.ToLower() 
                                   || u.Email.ToLower() == input.ToLower() 
                                   || u.AdditionalEmails.Any(e => e.Email.ToLower() == input.ToLower() && e.IsVerified));

        if (user == null || !VerifyPasswordHash(dto.Password, user.PasswordHash))
        {
            return Unauthorized(new { message = "Invalid credentials." });
        }

        var token = CreateToken(user);

        return Ok(new
        {
            message = "Login successful!",
            token = token,
            user = new
            {
                id = user.Id,
                username = user.Username,
                fullName = user.FullName,
                email = user.Email,
                isEmailVerified = user.IsEmailVerified,
                avatarUrl = user.AvatarUrl,
                bio = user.Bio
            }
        });
    }

    private string CreateToken(User user)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username)
        };

        var tokenKey = _configuration["Jwt:Key"] ?? "super_secret_key_that_is_long_enough_123456";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(tokenKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddDays(7),
            SigningCredentials = creds
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);

        return tokenHandler.WriteToken(token);
    }

    private string CreatePasswordHash(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }

    private bool VerifyPasswordHash(string password, string storedHash)
    {
        var computedHash = CreatePasswordHash(password);
        return computedHash == storedHash;
    }
}

public class RegisterDto
{
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginDto
{
    public string EmailOrUsername { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}