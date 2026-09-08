using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using backend.Data;
using backend.Models;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PostsController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public PostsController(ApplicationDbContext context)
    {
        _context = context;
    }

    private int GetCurrentUserId()
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        int.TryParse(userIdString, out int currentUserId);
        return currentUserId;
    }

    [HttpGet]
    public async Task<IActionResult> GetPosts()
    {
        var currentUserId = GetCurrentUserId();

        var posts = await _context.Posts
            .Include(p => p.Author)
            .Include(p => p.Likes)
                .ThenInclude(l => l.User)
            .Include(p => p.Comments)
                .ThenInclude(c => c.Author)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        var dtos = posts.Select(p =>
        {
            var userLike = currentUserId > 0 ? p.Likes.FirstOrDefault(l => l.UserId == currentUserId) : null;
            var isLiked = userLike != null;
            var userReaction = userLike?.ReactionType;

            return new PostResponseDto
            {
                Id = p.Id,
                Content = p.Content,
                ImageUrl = p.ImageUrl,
                CreatedAt = p.CreatedAt,
                AuthorId = p.AuthorId,
                AuthorName = p.Author != null ? p.Author.FullName : "Unknown",
                AuthorUsername = p.Author != null ? p.Author.Username : "Unknown",
                AuthorAvatar = p.Author != null ? p.Author.AvatarUrl : null,
                LikesCount = p.Likes.Count,
                IsLikedByCurrentUser = isLiked,
                UserReaction = userReaction,
                ReactionCounts = p.Likes.GroupBy(l => l.ReactionType ?? "LIKE")
                                        .ToDictionary(g => g.Key, g => g.Count()),
                Likers = p.Likes.OrderByDescending(l => l.CreatedAt).Select(l => new LikerDto
                {
                    Id = l.User.Id,
                    Username = l.User.Username,
                    FullName = l.User.FullName,
                    AvatarUrl = l.User.AvatarUrl,
                    ReactionType = l.ReactionType ?? "LIKE"
                }).ToList(),
                Comments = p.Comments.Select(c => new CommentResponseDto
                {
                    Id = c.Id,
                    PostId = c.PostId,
                    Content = c.Content,
                    CreatedAt = c.CreatedAt,
                    AuthorId = c.AuthorId,
                    AuthorName = c.Author != null ? c.Author.FullName : "Unknown",
                    AuthorUsername = c.Author != null ? c.Author.Username : "Unknown",
                    AuthorAvatar = c.Author != null ? c.Author.AvatarUrl : null
                }).ToList()
            };
        }).ToList();

        return Ok(dtos);
    }

    // POST: api/posts
    [Authorize]
    [HttpPost]
    public async Task<IActionResult> CreatePost([FromBody] CreatePostDto dto)
    {
        var authorId = GetCurrentUserId();
        if (authorId == 0)
            return Unauthorized(new { message = "Invalid token or user not logged in." });

        var author = await _context.Users.FindAsync(authorId);
        if (author == null)
            return Unauthorized(new { message = "User not found." });

        var post = new Post
        {
            Content = dto.Content,
            ImageUrl = dto.ImageUrl,
            AuthorId = authorId,
            CreatedAt = DateTime.UtcNow
        };

        _context.Posts.Add(post);
        await _context.SaveChangesAsync();

        var response = new PostResponseDto
        {
            Id = post.Id,
            Content = post.Content,
            ImageUrl = post.ImageUrl,
            CreatedAt = post.CreatedAt,
            AuthorId = post.AuthorId,
            AuthorName = author.FullName,
            AuthorUsername = author.Username,
            AuthorAvatar = author.AvatarUrl,
            LikesCount = 0,
            IsLikedByCurrentUser = false,
            UserReaction = null,
            ReactionCounts = new Dictionary<string, int>(),
            Likers = new List<LikerDto>(),
            Comments = new List<CommentResponseDto>()
        };

        return Ok(response);
    }

    // POST: api/posts/{id}/like
    [Authorize]
    [HttpPost("{id}/like")]
    public async Task<IActionResult> ToggleLike(int id, [FromBody] ToggleReactionDto? dto)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == 0)
            return Unauthorized(new { message = "Invalid token or user not logged in." });

        var post = await _context.Posts.Include(p => p.Author).FirstOrDefaultAsync(p => p.Id == id);
        if (post == null)
            return NotFound(new { message = "Post not found." });

        var requestedReaction = dto?.ReactionType ?? "LIKE";
        var existingLike = await _context.PostLikes
            .FirstOrDefaultAsync(l => l.PostId == id && l.UserId == currentUserId);

        bool isLiked;
        string? currentReaction = null;

        if (existingLike != null)
        {
            if (existingLike.ReactionType == requestedReaction)
            {
                // Toggle off
                _context.PostLikes.Remove(existingLike);
                isLiked = false;
            }
            else
            {
                // Switch reaction type
                existingLike.ReactionType = requestedReaction;
                isLiked = true;
                currentReaction = requestedReaction;
            }
        }
        else
        {
            // Create new reaction
            var newLike = new PostLike
            {
                PostId = id,
                UserId = currentUserId,
                ReactionType = requestedReaction,
                CreatedAt = DateTime.UtcNow
            };
            _context.PostLikes.Add(newLike);
            isLiked = true;
            currentReaction = requestedReaction;

            // Trigger notification for post author (if not reacting to own post)
            if (post.AuthorId != currentUserId)
            {
                var actor = await _context.Users.FindAsync(currentUserId);
                _context.Notifications.Add(new Notification
                {
                    UserId = post.AuthorId,
                    ActorId = currentUserId,
                    Type = "LIKE",
                    Content = $"{actor?.FullName ?? "Someone"} reacted to your post.",
                    TargetId = post.Id,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        await _context.SaveChangesAsync();

        var likes = await _context.PostLikes
            .Include(l => l.User)
            .Where(l => l.PostId == id)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();

        var likesCount = likes.Count;
        var reactionCounts = likes.GroupBy(l => l.ReactionType ?? "LIKE")
                                   .ToDictionary(g => g.Key, g => g.Count());
        var likers = likes.Select(l => new LikerDto
        {
            Id = l.User.Id,
            Username = l.User.Username,
            FullName = l.User.FullName,
            AvatarUrl = l.User.AvatarUrl,
            ReactionType = l.ReactionType ?? "LIKE"
        }).ToList();

        return Ok(new
        {
            isLiked,
            userReaction = currentReaction,
            likesCount,
            reactionCounts,
            likers
        });
    }

    // POST: api/posts/{postId}/comments
    [Authorize]
    [HttpPost("{postId}/comments")]
    public async Task<IActionResult> AddComment(int postId, [FromBody] CreateCommentDto dto)
    {
        var authorId = GetCurrentUserId();
        if (authorId == 0)
            return Unauthorized(new { message = "Invalid token or user not logged in." });

        var post = await _context.Posts.FindAsync(postId);
        if (post == null)
            return NotFound(new { message = "Post not found." });

        var author = await _context.Users.FindAsync(authorId);
        if (author == null)
            return Unauthorized(new { message = "User not found." });

        var comment = new Comment
        {
            PostId = postId,
            AuthorId = authorId,
            Content = dto.Content,
            CreatedAt = DateTime.UtcNow
        };

        _context.Comments.Add(comment);

        // Notify post author if different user
        if (post.AuthorId != authorId)
        {
            _context.Notifications.Add(new Notification
            {
                UserId = post.AuthorId,
                ActorId = authorId,
                Type = "COMMENT",
                Content = $"{author.FullName} commented on your post.",
                TargetId = post.Id,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();

        return Ok(new CommentResponseDto
        {
            Id = comment.Id,
            PostId = comment.PostId,
            Content = comment.Content,
            CreatedAt = comment.CreatedAt,
            AuthorId = comment.AuthorId,
            AuthorName = author.FullName,
            AuthorUsername = author.Username,
            AuthorAvatar = author.AvatarUrl
        });
    }

    [Authorize]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePost(int id)
    {
        var currentUserId = GetCurrentUserId();
        var post = await _context.Posts.FindAsync(id);
        if (post == null) return NotFound(new { message = "Post not found." });

        if (post.AuthorId != currentUserId) return Forbid();

        _context.Posts.Remove(post);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Post deleted successfully." });
    }
}

public class ToggleReactionDto
{
    public string? ReactionType { get; set; } = "LIKE";
}

public class PostResponseDto
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public int AuthorId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorUsername { get; set; } = string.Empty;
    public string? AuthorAvatar { get; set; }
    public int LikesCount { get; set; }
    public bool IsLikedByCurrentUser { get; set; }
    public string? UserReaction { get; set; }
    public Dictionary<string, int> ReactionCounts { get; set; } = new();
    public List<LikerDto> Likers { get; set; } = new();
    public List<CommentResponseDto> Comments { get; set; } = new();
}

public class LikerDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string ReactionType { get; set; } = "LIKE";
}

public class CommentResponseDto
{
    public int Id { get; set; }
    public int PostId { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int AuthorId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorUsername { get; set; } = string.Empty;
    public string? AuthorAvatar { get; set; }
}

public class CreatePostDto
{
    public string Content { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
}

public class CreateCommentDto
{
    public string Content { get; set; } = string.Empty;
}