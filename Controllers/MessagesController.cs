using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using backend.Data;
using backend.Models;

namespace backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class MessagesController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public MessagesController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET /api/messages/recent
    [HttpGet("recent")]
    public async Task<IActionResult> GetRecentConversationUserIds()
    {
        var currentUserIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!int.TryParse(currentUserIdString, out int currentUserId))
        {
            return Unauthorized(new { message = "Invalid token." });
        }

        var recentUserIds = await _context.Messages
            .Where(m => m.SenderId == currentUserId || m.ReceiverId == currentUserId)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => m.SenderId == currentUserId ? m.ReceiverId : m.SenderId)
            .Distinct()
            .ToListAsync();

        return Ok(recentUserIds);
    }

    // GET /api/messages?withUserId={id}
    [HttpGet]
    public async Task<IActionResult> GetMessageHistory([FromQuery] int withUserId)
    {
        var currentUserIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!int.TryParse(currentUserIdString, out int currentUserId))
        {
            return Unauthorized(new { message = "Invalid token." });
        }

        // Fetch messages where either user is the sender and the other is the receiver
        var messages = await _context.Messages
            .Where(m => (m.SenderId == currentUserId && m.ReceiverId == withUserId) ||
                        (m.SenderId == withUserId && m.ReceiverId == currentUserId))
            .OrderBy(m => m.CreatedAt)
            .Select(m => new MessageResponseDto
            {
                Id = m.Id,
                SenderId = m.SenderId,
                ReceiverId = m.ReceiverId,
                Content = m.Content,
                CreatedAt = m.CreatedAt
            })
            .ToListAsync();

        return Ok(messages);
    }

    // POST /api/messages
    [HttpPost]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageDto dto)
    {
        var currentUserIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!int.TryParse(currentUserIdString, out int currentUserId))
        {
            return Unauthorized(new { message = "Invalid token." });
        }

        var receiverExists = await _context.Users.AnyAsync(u => u.Id == dto.ReceiverId);
        if (!receiverExists)
        {
            return NotFound(new { message = "Receiver not found." });
        }

        var message = new Message
        {
            SenderId = currentUserId,
            ReceiverId = dto.ReceiverId,
            Content = dto.Content,
            CreatedAt = DateTime.UtcNow
        };

        _context.Messages.Add(message);
        await _context.SaveChangesAsync();

        return Ok(new MessageResponseDto
    {
        Id = message.Id,
        SenderId = message.SenderId,
        ReceiverId = message.ReceiverId,
        Content = message.Content,
        CreatedAt = message.CreatedAt
    });
    }
}

public class MessageResponseDto
{
    public int Id { get; set; }
    public int SenderId { get; set; }
    public int ReceiverId { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class SendMessageDto
{
    public int ReceiverId { get; set; }
    public string Content { get; set; } = string.Empty;
}