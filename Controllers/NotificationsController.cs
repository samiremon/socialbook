using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using backend.Data;

namespace backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class NotificationsController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public NotificationsController(ApplicationDbContext context)
    {
        _context = context;
    }

    private int GetCurrentUserId()
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        int.TryParse(userIdString, out int currentUserId);
        return currentUserId;
    }

    // GET /api/notifications
    [HttpGet]
    public async Task<IActionResult> GetNotifications()
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == 0) return Unauthorized();

        var notifications = await _context.Notifications
            .Include(n => n.Actor)
            .Where(n => n.UserId == currentUserId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(30)
            .Select(n => new
            {
                n.Id,
                n.UserId,
                n.ActorId,
                ActorName = n.Actor.FullName,
                ActorUsername = n.Actor.Username,
                ActorAvatar = n.Actor.AvatarUrl,
                n.Type,
                n.Content,
                n.TargetId,
                n.IsRead,
                n.CreatedAt
            })
            .ToListAsync();

        var unreadCount = await _context.Notifications
            .CountAsync(n => n.UserId == currentUserId && !n.IsRead);

        return Ok(new { unreadCount, notifications });
    }

    // PUT /api/notifications/read/{id}
    [HttpPut("read/{id}")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        var currentUserId = GetCurrentUserId();
        var notification = await _context.Notifications.FindAsync(id);

        if (notification == null) return NotFound();
        if (notification.UserId != currentUserId) return Forbid();

        notification.IsRead = true;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Marked as read." });
    }

    // PUT /api/notifications/read-all
    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var currentUserId = GetCurrentUserId();
        var unreadNotifications = await _context.Notifications
            .Where(n => n.UserId == currentUserId && !n.IsRead)
            .ToListAsync();

        foreach (var n in unreadNotifications)
        {
            n.IsRead = true;
        }

        await _context.SaveChangesAsync();
        return Ok(new { message = "All marked as read." });
    }
}
