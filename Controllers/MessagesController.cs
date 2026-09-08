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

    private int GetCurrentUserId()
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        int.TryParse(userIdString, out int currentUserId);
        return currentUserId;
    }

    // GET /api/messages/conversations
    [HttpGet("conversations")]
    public async Task<IActionResult> GetConversations()
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == 0) return Unauthorized();

        // Get all unique users current user has exchanged messages with
        var allMessages = await _context.Messages
            .Include(m => m.Sender)
            .Include(m => m.Receiver)
            .Where(m => m.SenderId == currentUserId || m.ReceiverId == currentUserId)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync();

        var partnerIds = allMessages
            .Select(m => m.SenderId == currentUserId ? m.ReceiverId : m.SenderId)
            .Distinct()
            .ToList();

        var conversations = new List<object>();

        foreach (var partnerId in partnerIds)
        {
            var partner = await _context.Users.FindAsync(partnerId);
            if (partner == null) continue;

            var lastMessage = allMessages.First(m =>
                (m.SenderId == currentUserId && m.ReceiverId == partnerId) ||
                (m.SenderId == partnerId && m.ReceiverId == currentUserId));

            var unreadCount = allMessages.Count(m =>
                m.SenderId == partnerId && m.ReceiverId == currentUserId && !m.IsRead);

            conversations.Add(new
            {
                PartnerId = partner.Id,
                PartnerName = partner.FullName,
                PartnerUsername = partner.Username,
                PartnerAvatar = partner.AvatarUrl,
                LastMessage = lastMessage.Content,
                LastMessageTime = lastMessage.CreatedAt,
                IsLastMessageFromMe = lastMessage.SenderId == currentUserId,
                UnreadCount = unreadCount
            });
        }

        return Ok(conversations);
    }

    // GET /api/messages?withUserId={id}
    [HttpGet]
    public async Task<IActionResult> GetMessageHistory([FromQuery] int withUserId)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == 0) return Unauthorized();

        // Fetch messages where either user is sender and other is receiver
        var messages = await _context.Messages
            .Where(m => (m.SenderId == currentUserId && m.ReceiverId == withUserId) ||
                        (m.SenderId == withUserId && m.ReceiverId == currentUserId))
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        // Automatically mark unread incoming messages as read
        var unreadIncoming = messages.Where(m => m.ReceiverId == currentUserId && !m.IsRead).ToList();
        if (unreadIncoming.Any())
        {
            foreach (var msg in unreadIncoming)
            {
                msg.IsRead = true;
            }
            await _context.SaveChangesAsync();
        }

        var dtos = messages.Select(m => new MessageResponseDto
        {
            Id = m.Id,
            SenderId = m.SenderId,
            ReceiverId = m.ReceiverId,
            Content = m.Content,
            IsRead = m.IsRead,
            CreatedAt = m.CreatedAt
        }).ToList();

        return Ok(dtos);
    }

    // POST /api/messages
    [HttpPost]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageDto dto)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == 0) return Unauthorized();

        var receiver = await _context.Users.FindAsync(dto.ReceiverId);
        if (receiver == null) return NotFound(new { message = "Receiver not found." });

        var sender = await _context.Users.FindAsync(currentUserId);

        var message = new Message
        {
            SenderId = currentUserId,
            ReceiverId = dto.ReceiverId,
            Content = dto.Content,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.Messages.Add(message);

        // Add notification for receiver
        _context.Notifications.Add(new Notification
        {
            UserId = dto.ReceiverId,
            ActorId = currentUserId,
            Type = "MESSAGE",
            Content = $"{sender?.FullName ?? "Someone"} sent you a message.",
            TargetId = currentUserId,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        return Ok(new MessageResponseDto
        {
            Id = message.Id,
            SenderId = message.SenderId,
            ReceiverId = message.ReceiverId,
            Content = message.Content,
            IsRead = message.IsRead,
            CreatedAt = message.CreatedAt
        });
    }

    // GET /api/messages/recent
    [HttpGet("recent")]
    public async Task<IActionResult> GetRecentUserIds()
    {
        var currentUserId = GetCurrentUserId();
        var userIds = await _context.Messages
            .Where(m => m.SenderId == currentUserId || m.ReceiverId == currentUserId)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => m.SenderId == currentUserId ? m.ReceiverId : m.SenderId)
            .Distinct()
            .ToListAsync();

        return Ok(userIds);
    }
}

public class MessageResponseDto
{
    public int Id { get; set; }
    public int SenderId { get; set; }
    public int ReceiverId { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SendMessageDto
{
    public int ReceiverId { get; set; }
    public string Content { get; set; } = string.Empty;
}