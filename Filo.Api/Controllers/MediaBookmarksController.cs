using Filo.Api.Data;
using Filo.Api.Models.Bookmarks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.RegularExpressions;

[ApiController]
[Authorize]
[Route("api/media-bookmarks")]
public class MediaBookmarksController : ControllerBase
{
    private readonly FiloDbContext _context;

    public MediaBookmarksController(FiloDbContext context)
    {
        _context = context;
    }

    [HttpPost]
    public async Task<IActionResult> CreateBookmark(
    [FromBody] CreateBookmarkRequest request)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int userId))
        {
            return Unauthorized();
        }

        var media = await _context.MediaFiles.FirstOrDefaultAsync(m => m.MediaFileId == request.MediaFileId && m.UserId == userId);
        if(media == null)
        {
            return NotFound();
        }

        if(string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest("Bookmark title cannot be empty.");
        }

        if (!double.IsFinite(request.StartSeconds) || request.StartSeconds < 0)
        {
            return BadRequest("Invalid bookmark start time.");
        }

        if(request.EndSeconds.HasValue)
        {
            if(!double.IsFinite(request.EndSeconds.Value) || request.EndSeconds.Value <= request.StartSeconds)
            {
                return BadRequest("Invalid bookmark end time.");
            }
            if (media.DurationSeconds is > 0)
            {
                if (request.EndSeconds.Value > media.DurationSeconds)
                {
                    return BadRequest("Invalid bookmark end time.");
                }
            }
        }

        if (media.DurationSeconds is > 0)
        {
            if (request.StartSeconds > media.DurationSeconds)
            {
                return BadRequest("Invalid bookmark start time.");
            }
        }

        if (request.Color != null && !Regex.IsMatch(request.Color, @"^#[0-9A-Fa-f]{6}$"))
        {
            return BadRequest("Color must use the #RRGGBB format.");
        }

        MediaBookmark mediaBookmark = new MediaBookmark
        {
            OwnerUserId = userId,
            MediaFileId = request.MediaFileId,
            Title = request.Title,
            Description = request.Description,
            StartSeconds = request.StartSeconds,
            EndSeconds = request.EndSeconds,
            Color = request.Color
        };

        _context.MediaBookmarks.Add(mediaBookmark);
        await _context.SaveChangesAsync();

        var response = new BookmarkResponse
        {
            MediaBookmarkId = mediaBookmark.MediaBookmarkId,
            MediaFileId = mediaBookmark.MediaFileId,
            Title = mediaBookmark.Title,
            Description = mediaBookmark.Description,
            StartSeconds = mediaBookmark.StartSeconds,
            EndSeconds = mediaBookmark.EndSeconds,
            Color = mediaBookmark.Color,
            CreatedAt = mediaBookmark.CreatedAt,
            UpdatedAt = mediaBookmark.UpdatedAt
        };

        return Created($"/api/media-bookmarks/{mediaBookmark.MediaBookmarkId}", response);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetBookmark(int id)
    {
        if (!int.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out int userId))
        {
            return Unauthorized();
        }

        var bookmark = await _context.MediaBookmarks
            .AsNoTracking()
            .FirstOrDefaultAsync(b =>
                b.MediaBookmarkId == id &&
                b.OwnerUserId == userId);

        if (bookmark == null)
        {
            return NotFound();
        }

        var response = new BookmarkResponse
        {
            MediaBookmarkId = bookmark.MediaBookmarkId,
            MediaFileId = bookmark.MediaFileId,
            Title = bookmark.Title,
            Description = bookmark.Description,
            StartSeconds = bookmark.StartSeconds,
            EndSeconds = bookmark.EndSeconds,
            Color = bookmark.Color,
            CreatedAt = bookmark.CreatedAt,
            UpdatedAt = bookmark.UpdatedAt
        };

        return Ok(response);
    }

    [HttpGet("media/{mediaId}")]
    public async Task<IActionResult> GetMediaBookmarks(
    int mediaId,
    [FromQuery] string? search,
    [FromQuery] string? sort = "time")
    {
        if (!int.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out int userId))
        {
            return Unauthorized();
        }

        bool ownsMedia = await _context.MediaFiles
            .AnyAsync(m =>
                m.MediaFileId == mediaId &&
                m.UserId == userId);

        if (!ownsMedia)
        {
            return NotFound();
        }

        var query = _context.MediaBookmarks
            .AsNoTracking()
            .Where(b =>
                b.MediaFileId == mediaId &&
                b.OwnerUserId == userId);

        // Filter by bookmark title
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(b =>
                b.Title.ToLower().Contains(search.Trim().ToLower()));
        }

        // Sort bookmarks
        query = sort?.ToLowerInvariant() switch
        {
            "title" => query
                .OrderBy(b => b.Title)
                .ThenBy(b => b.StartSeconds),

            "title-desc" => query
                .OrderByDescending(b => b.Title)
                .ThenBy(b => b.StartSeconds),

            "time-desc" => query
                .OrderByDescending(b => b.StartSeconds)
                .ThenBy(b => b.MediaBookmarkId),

            _ => query
                .OrderBy(b => b.StartSeconds)
                .ThenBy(b => b.MediaBookmarkId)
        };

        var bookmarks = await query
            .Select(b => new BookmarkResponse
            {
                MediaBookmarkId = b.MediaBookmarkId,
                MediaFileId = b.MediaFileId,
                Title = b.Title,
                Description = b.Description,
                StartSeconds = b.StartSeconds,
                EndSeconds = b.EndSeconds,
                Color = b.Color,
                CreatedAt = b.CreatedAt,
                UpdatedAt = b.UpdatedAt
            })
            .ToListAsync();

        return Ok(bookmarks);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateBookmark(
    int id,
    [FromBody] UpdateBookmarkRequest request)
    {
        if (!int.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out int userId))
        {
            return Unauthorized();
        }

        var bookmark = await _context.MediaBookmarks
            .FirstOrDefaultAsync(b =>
                b.MediaBookmarkId == id &&
                b.OwnerUserId == userId);

        if (bookmark == null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest("Bookmark title cannot be empty.");
        }

        if (!double.IsFinite(request.StartSeconds) ||
            request.StartSeconds < 0)
        {
            return BadRequest("Invalid bookmark start time.");
        }

        if (request.EndSeconds.HasValue &&
            (!double.IsFinite(request.EndSeconds.Value) ||
             request.EndSeconds.Value <= request.StartSeconds))
        {
            return BadRequest("Invalid bookmark end time.");
        }

        var media = await _context.MediaFiles
            .FirstOrDefaultAsync(m =>
                m.MediaFileId == bookmark.MediaFileId &&
                m.UserId == userId);

        if (media == null)
        {
            return NotFound();
        }

        if (media.DurationSeconds is > 0)
        {
            if (request.StartSeconds > media.DurationSeconds.Value)
            {
                return BadRequest("Bookmark start time exceeds media duration.");
            }

            if (request.EndSeconds.HasValue &&
                request.EndSeconds.Value > media.DurationSeconds.Value)
            {
                return BadRequest("Bookmark end time exceeds media duration.");
            }
        }

        if (request.Color != null &&
            !Regex.IsMatch(request.Color, @"^#[0-9A-Fa-f]{6}$"))
        {
            return BadRequest("Color must use the #RRGGBB format.");
        }

        bookmark.Title = request.Title.Trim();
        bookmark.Description = request.Description;
        bookmark.StartSeconds = request.StartSeconds;
        bookmark.EndSeconds = request.EndSeconds;
        bookmark.Color = request.Color;
        bookmark.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new BookmarkResponse
        {
            MediaBookmarkId = bookmark.MediaBookmarkId,
            MediaFileId = bookmark.MediaFileId,
            Title = bookmark.Title,
            Description = bookmark.Description,
            StartSeconds = bookmark.StartSeconds,
            EndSeconds = bookmark.EndSeconds,
            Color = bookmark.Color,
            CreatedAt = bookmark.CreatedAt,
            UpdatedAt = bookmark.UpdatedAt
        });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBookmark(int id)
    {
        if (!int.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out int userId))
        {
            return Unauthorized();
        }

        var bookmark = await _context.MediaBookmarks
            .FirstOrDefaultAsync(b =>
                b.MediaBookmarkId == id &&
                b.OwnerUserId == userId);

        if (bookmark == null)
        {
            return NotFound();
        }

        _context.MediaBookmarks.Remove(bookmark);

        await _context.SaveChangesAsync();

        return NoContent();
    }
}