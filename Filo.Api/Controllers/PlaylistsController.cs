using Amazon.S3;
using Amazon.S3.Model;
using Filo.Api.Data;
using Filo.Api.DTOs;
using Filo.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;


namespace Filo.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PlaylistsController : ControllerBase
    {
        private readonly IAmazonS3 _cloudStorage;
        private readonly IConfiguration _configuration;
        private readonly FiloDbContext _context;

        public PlaylistsController(IAmazonS3 cloudStorage, IConfiguration configuration, FiloDbContext context)
        {
            _cloudStorage = cloudStorage;
            _configuration = configuration;
            _context = context;

        }

        [HttpPost("create")]
        public async Task<IActionResult> Create(CreatePlaylistRequest request)
        {
            if(string.IsNullOrWhiteSpace(request.PlaylistName))
            {
                return BadRequest();
            }

            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int userId))
            {
                return Unauthorized();
            }
            Playlist playlist = new Playlist();
            playlist.Name = request.PlaylistName;
            playlist.UserId = userId;

            _context.Playlists.Add(playlist);
            await _context.SaveChangesAsync();

            return Ok(new {message="Playlist created successfully"});
        }

        [HttpGet]
        public async Task<IActionResult> GetPlaylists()
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int userId))
            {
                return Unauthorized();
            }

            var query = await _context.Playlists.Where(playlist => playlist.UserId == userId).Include(playlist => playlist.Thumbnail).ToListAsync();

            var bucketName = _configuration["AWS:BucketName"];
            if (string.IsNullOrWhiteSpace(bucketName))
            {
                return BadRequest();
            }

            
            var playlists = query.Select(playlist =>
            {
                if(playlist.Thumbnail != null)
                {
                var request = new GetPreSignedUrlRequest
                {
                    BucketName = bucketName,
                    Key = playlist.Thumbnail.S3ObjectKey,
                    Expires = DateTime.UtcNow.AddMinutes(30)
                };

                var imageUrl = _cloudStorage.GetPreSignedURL(request);

                return new PlaylistResponse { PlaylistId = playlist.PlaylistId, Name = playlist.Name, ThumbnailId = playlist.ThumbnailId, ThumbnailUrl = imageUrl};

                }

                return new PlaylistResponse { PlaylistId = playlist.PlaylistId, Name = playlist.Name };
            }).ToList();
            return Ok(playlists);
        }

        [HttpGet("{playlistId}/media")]
        public async Task<IActionResult> GetPlaylistMedia(int playlistId)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int userId))
            {
                return Unauthorized();
            }

            var query = await _context.Playlists.FirstOrDefaultAsync(p => p.PlaylistId == playlistId && p.UserId == userId);
            if(query == null)
            {
                return NotFound();
            }

            var mediaQuery = await _context.PlaylistMedia.Where(pm => pm.PlaylistId == playlistId).Include(pm => pm.MediaFile).ThenInclude(pm => pm.Thumbnail).ToListAsync();//Where(playlist => playlist.PlaylistId == playlistId).SelectMany(playlist => playlist.PlaylistMedia).Include();
            /*
            .Select(playlistMedia => new MediaResponse
            {   
                MediaFileId = playlistMedia.MediaFile.MediaFileId,
                Title = playlistMedia.MediaFile.Title,
                OriginalFileName = playlistMedia.MediaFile.OriginalFileName
            })
            .ToListAsync();
            */

            var bucketName = _configuration["AWS:BucketName"];
            if (string.IsNullOrWhiteSpace(bucketName))
            {
                return BadRequest();
            }

            var media = mediaQuery.Select(pm =>
            {
                if(pm.MediaFile.Thumbnail != null)
                {
                    var request = new GetPreSignedUrlRequest
                    {
                        BucketName = bucketName,
                        Key = pm.MediaFile.Thumbnail.S3ObjectKey,
                        Expires = DateTime.UtcNow.AddMinutes(30)
                    };
                    var imageUrl = _cloudStorage.GetPreSignedURL(request);
                    return new MediaResponse { MediaFileId = pm.MediaFile.MediaFileId, Title = pm.MediaFile.Title, OriginalFileName = pm.MediaFile.OriginalFileName, ThumbnailId = pm.MediaFile.ThumbnailId, ThumbnailUrl = imageUrl };
                }
                return new MediaResponse { MediaFileId = pm.MediaFile.MediaFileId, Title = pm.MediaFile.Title, OriginalFileName = pm.MediaFile.OriginalFileName };
            }).ToList();

            return Ok(media);
        }

        [HttpPost("{playlistId}/media/{mediaFileId}")]
        public async Task<IActionResult> AddMediaToPlaylist(int playlistId,int mediaFileId)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int userId))
            {
                return Unauthorized();
            }
            var playlist = await _context.Playlists.FirstOrDefaultAsync(p => p.PlaylistId == playlistId && p.UserId == userId);
            if(playlist == null)
            {
                return NotFound();
            }

            var query = await _context.MediaFiles.FirstOrDefaultAsync(p => p.MediaFileId == mediaFileId && p.UserId == userId);
            if(query == null)
            {
                return NotFound();
            }

            var findRelationship = await _context.PlaylistMedia.AnyAsync(p => p.PlaylistId == playlistId && p.MediaFileId == mediaFileId);
            if(findRelationship)
            {
                return Conflict();
            }

            PlaylistMedia playlistMedia = new PlaylistMedia();
            playlistMedia.PlaylistId = playlistId;
            playlistMedia.MediaFileId = mediaFileId;

            await _context.PlaylistMedia.AddAsync(playlistMedia);
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpGet("{playlistId}/available-media")]
        public async Task<IActionResult> GetAvailableMedia(int playlistId)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int userId))
            {
                return Unauthorized();
            }

            var playlist = await _context.Playlists.FirstOrDefaultAsync(p => p.PlaylistId == playlistId && p.UserId == userId);
            if (playlist == null)
            {
                return NotFound();
            }

            var existingMediaIds = await _context.PlaylistMedia.Where(pm => pm.PlaylistId == playlistId).Select(pm => pm.MediaFileId).ToListAsync();
            var availableMedia = _context.MediaFiles.Where(p => p.UserId == userId && !existingMediaIds.Contains(p.MediaFileId));

            var query = await availableMedia.Select(p => new MediaResponse {
                MediaFileId = p.MediaFileId,
                Title = p.Title,
                OriginalFileName = p.OriginalFileName
            }).ToListAsync();
            return Ok(query);
        }

        [HttpGet("{playlistId}")]
        public async Task<IActionResult> GetPlaylist(int playlistId)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int userId))
            {
                return Unauthorized();
            }

            var query = await _context.Playlists.Where(p => p.UserId == userId && p.PlaylistId == playlistId).Include(p => p.Thumbnail).FirstOrDefaultAsync();

            if(query == null)
            {
                return NotFound();
            }

            var bucketName = _configuration["AWS:BucketName"];
            if (string.IsNullOrEmpty(bucketName))
            {
                return BadRequest();
            }

            if(query.Thumbnail != null)
            {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = bucketName,
                Key = query.Thumbnail.S3ObjectKey,
                Expires = DateTime.UtcNow.AddMinutes(30)
            };

            var imageUrl = _cloudStorage.GetPreSignedURL(request);
            var playlistResponseWithThumbnail =  new PlaylistResponse { Name = query.Name, PlaylistId = query.PlaylistId, ThumbnailId = query.ThumbnailId, ThumbnailUrl = imageUrl };
                return Ok(playlistResponseWithThumbnail);
            }
            var playlistResponse = new PlaylistResponse { Name = query.Name, PlaylistId = query.PlaylistId, ThumbnailId = query.ThumbnailId };
            return Ok(playlistResponse);
        }

        [HttpPut("{playlistId}")]
        public async Task<IActionResult> UpdatePlaylist(int playlistId, UpdatePlaylistRequest request)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int userId))
            {
                return Unauthorized();
            }

            var playlist = await _context.Playlists.FirstOrDefaultAsync(p => p.PlaylistId == playlistId && p.UserId == userId);
            if(string.IsNullOrWhiteSpace(request.Name) || request == null)
            {
                return BadRequest();
            }
            if(playlist == null)
            {
                return NotFound();
            }

            if(request.ThumbnailId != playlist.ThumbnailId)
            {
                var thumbnail = await _context.Thumbnails.FirstOrDefaultAsync(tm => tm.ThumbnailId == request.ThumbnailId && tm.UserId == userId);
                    if(thumbnail == null)
                    {
                        return NotFound();
                    }

                playlist.ThumbnailId = request.ThumbnailId;
            }

            if(request.Name != playlist.Name)
            {
                playlist.Name = request.Name;
            }


            return Ok();
        }
    }
}
