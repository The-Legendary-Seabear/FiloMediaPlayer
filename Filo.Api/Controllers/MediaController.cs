using Amazon.S3;
using Amazon.S3.Model;
using Filo.Api.Data;
using Filo.Api.DTOs;
using Filo.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace Filo.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class MediaController : ControllerBase
    {
        private readonly IAmazonS3 _cloudStorage;
        private readonly IConfiguration _configuration;
        private readonly FiloDbContext _context;

        public MediaController(IAmazonS3 cloudStorage, IConfiguration configuration, FiloDbContext context)
        {
            _cloudStorage = cloudStorage;
            _configuration = configuration;
            _context = context;

        }

        [RequestSizeLimit(500_000_000)]
        [HttpPost("upload")]
        public async Task<IActionResult> UploadMedia([FromForm]IFormFile file, [FromForm]string name)
        {
            if(file == null || file.Length == 0)
            {
                return BadRequest();
            }
            if(string.IsNullOrWhiteSpace(name))
            {
                return BadRequest();
            }

            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int userId)) 
            {
                return Unauthorized();
            }

            var fileExtension = Path.GetExtension(file.FileName);
            var uniqueId = Guid.NewGuid();
            string awsPath = $"users/{userId}/media/{uniqueId}{fileExtension}";

            var bucketName = _configuration["AWS:BucketName"];
            if (string.IsNullOrEmpty(bucketName))
            {
                return BadRequest();
            }

            PutObjectRequest objectRequest = new PutObjectRequest();
            objectRequest.BucketName = bucketName;
            objectRequest.Key = awsPath;
            objectRequest.InputStream = file.OpenReadStream();

            var response = await _cloudStorage.PutObjectAsync(objectRequest);
            if(response.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                return BadRequest();
            }

            MediaFile media = new MediaFile();
            media.Title = name;
            media.UserId = userId;
            media.OriginalFileName = file.FileName;
            media.S3ObjectKey = awsPath;

            _context.MediaFiles.Add(media);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Media created successfully" });
        }

        [HttpGet("{mediaFileId}/playback")]
        public async Task<IActionResult> GetPlaybackUrl(int mediaFileId)
        {
            if(!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int userId)) 
            {
                return Unauthorized();
            }

            var media = await _context.MediaFiles.Include(mf => mf.Thumbnail).FirstOrDefaultAsync(mf => mf.UserId == userId && mf.MediaFileId == mediaFileId);
            if(media == null)
            {
                return NotFound();
            }

            var bucketName = _configuration["AWS:BucketName"];
            if(string.IsNullOrEmpty(bucketName))
            {
                return BadRequest();
            }

            var getPreSignedUrlRequest = new GetPreSignedUrlRequest
            {
                BucketName = bucketName,
                Key = media.S3ObjectKey,
                Expires = DateTime.UtcNow.AddHours(2)
            };

            var mediaUrl = _cloudStorage.GetPreSignedURL(getPreSignedUrlRequest);

            if(media.Thumbnail != null)
            {
                var thumbnailPreSignedURL = new GetPreSignedUrlRequest
                {
                    BucketName = bucketName,
                    Key = media.Thumbnail.S3ObjectKey,
                    Expires = DateTime.UtcNow.AddHours(2)
                };

                var thumbnailurl = _cloudStorage.GetPreSignedURL(thumbnailPreSignedURL);

                var playbackMediaResponseWithThumbnail = new PlaybackMediaResponse { Title = media.Title, MediaFileId = media.MediaFileId, PlaybackUrl = mediaUrl , ContentType = media.ContentType, ThumbnailUrl = thumbnailurl};

                return Ok(playbackMediaResponseWithThumbnail);
            }

            var playbackMediaResponse = new PlaybackMediaResponse { Title = media.Title, MediaFileId = media.MediaFileId, PlaybackUrl = mediaUrl, ContentType = media.ContentType };

            return Ok(playbackMediaResponse);
        }

        [HttpGet("{mediaFileId}")]
        public async Task<IActionResult> GetMedia(int mediaFileId)
        {
            // Retrieve authenticated user ID
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int userId))
            {
                return Unauthorized();
            }

            // Retrieve media belonging to user
            var media = await _context.MediaFiles.Include(mf => mf.Thumbnail).FirstOrDefaultAsync(mf => mf.UserId == userId && mf.MediaFileId == mediaFileId);
            if (media == null)
            {
                return NotFound();
            }


            if (media.Thumbnail != null)
            {
                var bucketName = _configuration["AWS:BucketName"];
                if (string.IsNullOrEmpty(bucketName))
                {
                    return BadRequest();
                }

                var request = new GetPreSignedUrlRequest
                {
                    BucketName = bucketName,
                    Key = media.Thumbnail.S3ObjectKey,
                    Expires = DateTime.UtcNow.AddMinutes(30)
                };

                var imageUrl = _cloudStorage.GetPreSignedURL(request);
                var mediaResponseWithThumbnail = new MediaResponse { MediaFileId = media.MediaFileId, Title = media.Title, ThumbnailId = media.ThumbnailId, ThumbnailUrl = imageUrl, OriginalFileName = media.OriginalFileName, };
                return Ok(mediaResponseWithThumbnail);
                }

            var mediaResponseWithoutThumbnail = new MediaResponse { MediaFileId = media.MediaFileId, Title = media.Title, ThumbnailId = media.ThumbnailId, OriginalFileName = media.OriginalFileName, };

            return Ok(mediaResponseWithoutThumbnail);
        }

        [HttpPut("{mediaFileId}")]
        public async Task<IActionResult> UpdateMedia( int mediaFileId, UpdateMediaRequest request)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int userId))
            {
                return Unauthorized();
            }

            var media = await _context.MediaFiles.Include(mf => mf.Thumbnail).FirstOrDefaultAsync(mf => mf.UserId == userId && mf.MediaFileId == mediaFileId);
            if (media == null)
            {
                return NotFound();
            }

            if(request == null || string.IsNullOrWhiteSpace(request.Title))
            {
                return BadRequest();
            }

            if(request.ThumbnailId != media.ThumbnailId)
            {
                if (request.ThumbnailId.HasValue)
                {
                    var thumbnail = await _context.Thumbnails.FirstOrDefaultAsync(tm => tm.ThumbnailId == request.ThumbnailId && tm.UserId == userId);
                    if (thumbnail == null)
                    {
                        return NotFound();
                    }

                    media.ThumbnailId = request.ThumbnailId;

                }

                else
                {
                    media.ThumbnailId = null;
                }
            }

            if (request.Title != media.Title)
            {
                media.Title = request.Title;
            }

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpGet("{mediaFileId}/playlists")]
        public async Task<IActionResult> GetMediaPlaylists(int mediaFileId)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int userId))
            {
                return Unauthorized();
            }

            var media = await _context.MediaFiles.FirstOrDefaultAsync(mf => mf.MediaFileId == mediaFileId && mf.UserId == userId);

            if (media == null)
            {
                return NotFound();
            }

            var playlistIds = await _context.PlaylistMedia.Where(pm => pm.MediaFileId == mediaFileId).Select(pm => pm.PlaylistId).ToListAsync();
            var playlists = await _context.Playlists.Where(p => p.UserId == userId && playlistIds.Contains(p.PlaylistId)).Select(p => new PlaylistResponse{ PlaylistId = p.PlaylistId, Name = p.Name}).ToListAsync();

            return Ok(playlists);
        }

        [HttpGet("{mediaFileId}/available-playlists")]
        public async Task<IActionResult> GetAvailableMediaPlaylists(int mediaFileId)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int userId))
            {
                return Unauthorized();
            }

            var media = await _context.MediaFiles.FirstOrDefaultAsync(mf => mf.MediaFileId == mediaFileId && mf.UserId == userId);

            if (media == null)
            {
                return NotFound();
            }

            var playlistIds = await _context.PlaylistMedia.Where(pm => pm.MediaFileId == mediaFileId).Select(pm => pm.PlaylistId).ToListAsync();

            var playlists = await _context.Playlists.Where(p => p.UserId == userId && !playlistIds.Contains(p.PlaylistId)).Select(p => new PlaylistResponse{ PlaylistId = p.PlaylistId, Name = p.Name}).ToListAsync();

            return Ok(playlists);
        }



    }
    
}
