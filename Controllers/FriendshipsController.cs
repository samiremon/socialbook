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
public class FriendshipsController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public FriendshipsController(ApplicationDbContext context)
    {
        _context = context;
    }

    private int GetCurrentUserId()
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        int.TryParse(userIdString, out int currentUserId);
        return currentUserId;
    }

    // POST /api/friendships/request/{targetUserId}
    [HttpPost("request/{targetUserId}")]
    public async Task<IActionResult> SendFriendRequest(int targetUserId)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == 0) return Unauthorized();
        if (currentUserId == targetUserId) return BadRequest(new { message = "Cannot add yourself as a friend." });

        var targetUser = await _context.Users.FindAsync(targetUserId);
        if (targetUser == null) return NotFound(new { message = "User not found." });

        var existing = await _context.Friendships.FirstOrDefaultAsync(f =>
            (f.RequesterId == currentUserId && f.AddresseeId == targetUserId) ||
            (f.RequesterId == targetUserId && f.AddresseeId == currentUserId));

        if (existing != null)
        {
            if (existing.Status == "ACCEPTED")
                return BadRequest(new { message = "Already friends." });
            if (existing.Status == "PENDING")
                return BadRequest(new { message = "Friend request already pending." });
        }

        var friendship = new Friendship
        {
            RequesterId = currentUserId,
            AddresseeId = targetUserId,
            Status = "PENDING",
            CreatedAt = DateTime.UtcNow
        };

        _context.Friendships.Add(friendship);

        // Add notification for receiver
        var actor = await _context.Users.FindAsync(currentUserId);
        _context.Notifications.Add(new Notification
        {
            UserId = targetUserId,
            ActorId = currentUserId,
            Type = "FRIEND_REQUEST",
            Content = $"{actor?.FullName ?? "Someone"} sent you a friend request.",
            TargetId = friendship.Id,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        return Ok(new { message = "Friend request sent successfully.", friendshipId = friendship.Id, status = "PENDING" });
    }

    // POST /api/friendships/accept/{requestId}
    [HttpPost("accept/{requestId}")]
    public async Task<IActionResult> AcceptFriendRequest(int requestId)
    {
        var currentUserId = GetCurrentUserId();
        var request = await _context.Friendships.FindAsync(requestId);

        if (request == null) return NotFound(new { message = "Friend request not found." });
        if (request.AddresseeId != currentUserId) return Forbid();

        request.Status = "ACCEPTED";

        // Notify requester that request was accepted
        var actor = await _context.Users.FindAsync(currentUserId);
        _context.Notifications.Add(new Notification
        {
            UserId = request.RequesterId,
            ActorId = currentUserId,
            Type = "FRIEND_ACCEPT",
            Content = $"{actor?.FullName ?? "Someone"} accepted your friend request.",
            TargetId = request.Id,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        return Ok(new { message = "Friend request accepted.", status = "ACCEPTED" });
    }

    // POST /api/friendships/reject/{requestId}
    [HttpPost("reject/{requestId}")]
    public async Task<IActionResult> RejectFriendRequest(int requestId)
    {
        var currentUserId = GetCurrentUserId();
        var request = await _context.Friendships.FindAsync(requestId);

        if (request == null) return NotFound(new { message = "Friend request not found." });
        if (request.AddresseeId != currentUserId && request.RequesterId != currentUserId) return Forbid();

        _context.Friendships.Remove(request);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Friend request removed." });
    }

    // GET /api/friendships/requests
    [HttpGet("requests")]
    public async Task<IActionResult> GetPendingRequests()
    {
        var currentUserId = GetCurrentUserId();
        var requests = await _context.Friendships
            .Include(f => f.Requester)
            .Where(f => f.AddresseeId == currentUserId && f.Status == "PENDING")
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new
            {
                Id = f.Id,
                RequesterId = f.Requester.Id,
                RequesterName = f.Requester.FullName,
                RequesterUsername = f.Requester.Username,
                RequesterAvatar = f.Requester.AvatarUrl,
                CreatedAt = f.CreatedAt
            })
            .ToListAsync();

        return Ok(requests);
    }

    // GET /api/friendships/friends
    [HttpGet("friends")]
    public async Task<IActionResult> GetFriends()
    {
        var currentUserId = GetCurrentUserId();
        var friendships = await _context.Friendships
            .Include(f => f.Requester)
            .Include(f => f.Addressee)
            .Where(f => f.Status == "ACCEPTED" && (f.RequesterId == currentUserId || f.AddresseeId == currentUserId))
            .ToListAsync();

        var friends = friendships.Select(f =>
        {
            var friendUser = f.RequesterId == currentUserId ? f.Addressee : f.Requester;
            return new
            {
                FriendshipId = f.Id,
                Id = friendUser.Id,
                Username = friendUser.Username,
                FullName = friendUser.FullName,
                AvatarUrl = friendUser.AvatarUrl,
                Bio = friendUser.Bio
            };
        }).ToList();

        return Ok(friends);
    }

    // GET /api/friendships/suggestions
    [HttpGet("suggestions")]
    public async Task<IActionResult> GetPeopleYouMayKnow()
    {
        var currentUserId = GetCurrentUserId();

        // Get all user IDs involved in friendships with current user
        var friendshipUserIds = await _context.Friendships
            .Where(f => f.RequesterId == currentUserId || f.AddresseeId == currentUserId)
            .Select(f => f.RequesterId == currentUserId ? f.AddresseeId : f.RequesterId)
            .ToListAsync();

        friendshipUserIds.Add(currentUserId);

        var suggestions = await _context.Users
            .Where(u => !friendshipUserIds.Contains(u.Id))
            .OrderBy(u => Guid.NewGuid()) // Random order
            .Take(10)
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.FullName,
                u.AvatarUrl,
                u.Bio
            })
            .ToListAsync();

        return Ok(suggestions);
    }

    // GET /api/friendships/status/{targetUserId}
    [HttpGet("status/{targetUserId}")]
    public async Task<IActionResult> GetFriendshipStatus(int targetUserId)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == targetUserId)
            return Ok(new { status = "SELF" });

        var friendship = await _context.Friendships.FirstOrDefaultAsync(f =>
            (f.RequesterId == currentUserId && f.AddresseeId == targetUserId) ||
            (f.RequesterId == targetUserId && f.AddresseeId == currentUserId));

        if (friendship == null)
            return Ok(new { status = "NONE" });

        if (friendship.Status == "ACCEPTED")
            return Ok(new { status = "FRIENDS", friendshipId = friendship.Id });

        if (friendship.RequesterId == currentUserId)
            return Ok(new { status = "PENDING_SENT", friendshipId = friendship.Id });

        return Ok(new { status = "PENDING_RECEIVED", friendshipId = friendship.Id });
    }
}
