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

    [HttpGet]
    public async Task<IActionResult> GetPosts()
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        int.TryParse(userIdString, out int currentUserId);

        var posts = await _context.Posts
            .Include(p => p.Author)
            .Include(p => p.Likes)
                .ThenInclude(l => l.User)
            .Include(p => p.Comments)
                .ThenInclude(c => c.Author)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PostResponseDto
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
                IsLikedByCurrentUser = currentUserId > 0 && p.Likes.Any(l => l.UserId == currentUserId),
                Likers = p.Likes.OrderByDescending(l => l.CreatedAt).Select(l => new LikerDto
                {
                    Id = l.User.Id,
                    Username = l.User.Username,
                    FullName = l.User.FullName,
                    AvatarUrl = l.User.AvatarUrl
                }).ToList(),
                Comments = p.Comments.Select(c => new CommentResponseDto
                {
                    Id = c.Id,
                    PostId = c.PostId,
                    Content = c.Content,
                    CreatedAt = c.CreatedAt,
                    AuthorId = c.AuthorId,
                    AuthorName = c.Author != null ? c.Author.FullName : "Unknown",
                    AuthorUsername = c.Author != null ? c.Author.Username : "Unknown"
                }).ToList()
            })
            .ToListAsync();

        return Ok(posts);
    }

    // POST: api/posts
    [Authorize]
    [HttpPost]
    public async Task<IActionResult> CreatePost([FromBody] CreatePostDto dto)
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

        if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int authorId))
        {
            return Unauthorized(new { message = "Invalid token or user not logged in." });
        }

        var author = await _context.Users.FindAsync(authorId);
        if (author == null)
        {
            return Unauthorized(new { message = "User not found." });
        }

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
            Likers = new List<LikerDto>(),
            Comments = new List<CommentResponseDto>()
        };

        return Ok(response);
    }

    // POST: api/posts/{id}/like
    [Authorize]
    [HttpPost("{id}/like")]
    public async Task<IActionResult> ToggleLike(int id)
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int currentUserId))
        {
            return Unauthorized(new { message = "Invalid token or user not logged in." });
        }

        var post = await _context.Posts.FindAsync(id);
        if (post == null)
        {
            return NotFound(new { message = "Post not found." });
        }

        var existingLike = await _context.PostLikes
            .FirstOrDefaultAsync(l => l.PostId == id && l.UserId == currentUserId);

        bool isLiked;
        if (existingLike != null)
        {
            _context.PostLikes.Remove(existingLike);
            isLiked = false;
        }
        else
        {
            _context.PostLikes.Add(new PostLike
            {
                PostId = id,
                UserId = currentUserId,
                CreatedAt = DateTime.UtcNow
            });
            isLiked = true;
        }

        await _context.SaveChangesAsync();

        var likesCount = await _context.PostLikes.CountAsync(l => l.PostId == id);
        var likers = await _context.PostLikes
            .Where(l => l.PostId == id)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new LikerDto
            {
                Id = l.User.Id,
                Username = l.User.Username,
                FullName = l.User.FullName,
                AvatarUrl = l.User.AvatarUrl
            })
            .ToListAsync();

        return Ok(new
        {
            isLiked,
            likesCount,
            likers
        });
    }

    // GET: api/posts/{id}/likes
    [HttpGet("{id}/likes")]
    public async Task<IActionResult> GetLikers(int id)
    {
        var postExists = await _context.Posts.AnyAsync(p => p.Id == id);
        if (!postExists)
        {
            return NotFound(new { message = "Post not found." });
        }

        var likers = await _context.PostLikes
            .Where(l => l.PostId == id)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new LikerDto
            {
                Id = l.User.Id,
                Username = l.User.Username,
                FullName = l.User.FullName,
                AvatarUrl = l.User.AvatarUrl
            })
            .ToListAsync();

        return Ok(likers);
    }

    // POST: api/posts/{postId}/comments
    [Authorize]
    [HttpPost("{postId}/comments")]
    public async Task<IActionResult> AddComment(int postId, [FromBody] CreateCommentDto dto)
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int authorId))
        {
            return Unauthorized(new { message = "Invalid token or user not logged in." });
        }

        var post = await _context.Posts.FindAsync(postId);
        if (post == null)
        {
            return NotFound(new { message = "Post not found." });
        }

        var author = await _context.Users.FindAsync(authorId);
        if (author == null)
        {
            return Unauthorized(new { message = "User not found." });
        }

        var comment = new Comment
        {
            PostId = postId,
            AuthorId = authorId,
            Content = dto.Content,
            CreatedAt = DateTime.UtcNow
        };

        _context.Comments.Add(comment);
        await _context.SaveChangesAsync();

        return Ok(new CommentResponseDto
        {
            Id = comment.Id,
            PostId = comment.PostId,
            Content = comment.Content,
            CreatedAt = comment.CreatedAt,
            AuthorId = comment.AuthorId,
            AuthorName = author.FullName,
            AuthorUsername = author.Username
        });
    }

    [Authorize]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePost(int id)
    {
        var post = await _context.Posts.FindAsync(id);
        if (post == null)
        {
            return NotFound(new { message = "Post not found." });
        }

        _context.Posts.Remove(post);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Post deleted successfully." });
    }
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
    public List<LikerDto> Likers { get; set; } = new();
    public List<CommentResponseDto> Comments { get; set; } = new();
}

public class LikerDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
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