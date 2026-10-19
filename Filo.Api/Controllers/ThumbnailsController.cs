using Amazon.S3;
using Amazon.S3.Model;
using Filo.Api.Data;
using Filo.Api.DTOs;
using Filo.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Filo.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ThumbnailsController : ControllerBase
    {

        private readonly IAmazonS3 _cloudStorage;
        private readonly IConfiguration _configuration;
        private readonly FiloDbContext _context;

        public ThumbnailsController(IAmazonS3 cloudStorage, IConfiguration configuration, FiloDbContext context)
        {
            _cloudStorage = cloudStorage;
            _configuration = configuration;
            _context = context;

        }


            [HttpPost("upload")]
        public async Task<IActionResult> UploadThumbnail([FromForm] IFormFile file, [FromForm] string name)
        {
            if (file == null || file.Length == 0)
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
            string awsPath = $"users/{userId}/thumbnails/{uniqueId}{fileExtension}";

            var bucketName = _configuration["AWS:BucketName"];
            if (string.IsNullOrWhiteSpace(bucketName))
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

            Thumbnail thumbnail = new Thumbnail();
            thumbnail.UserId = userId;
            thumbnail.Name = name;
            thumbnail.S3ObjectKey = awsPath;

            _context.Thumbnails.Add(thumbnail);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Thumbnail created successfully" });
        }

        [HttpGet]
        public async Task<IActionResult> GetThumbnails()
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int userId))
            {
                return Unauthorized();
            }

            var queryThumbnails = await _context.Thumbnails.Where(t => t.UserId == userId).ToListAsync();

            var bucketName = _configuration["AWS:BucketName"];
            if (string.IsNullOrWhiteSpace(bucketName))
            {
                return BadRequest();
            }

            var thumbnailResponses = queryThumbnails.Select(thumbnail =>
            {
                var request = new GetPreSignedUrlRequest
                {
                    BucketName = bucketName,
                    Key = thumbnail.S3ObjectKey,
                    Expires = DateTime.UtcNow.AddMinutes(30)
                };

                var imageUrl = _cloudStorage.GetPreSignedURL(request);

                return new ThumbnailResponse { ThumbnailId = thumbnail.ThumbnailId, Name = thumbnail.Name, ImageUrl = imageUrl };
            }).ToList();


            return Ok(thumbnailResponses);
        }

        [HttpGet("{thumbnailId}")]
        public async Task<IActionResult> GetOneThumbnail(int thumbnailId)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int userId))
            {
                return Unauthorized();
            }

            var queryThumbnail = await _context.Thumbnails.FirstOrDefaultAsync(t => t.UserId == userId && t.ThumbnailId == thumbnailId);
            if(queryThumbnail == null)
            {
                return NotFound();
            }

            var bucketName = _configuration["AWS:BucketName"];
            if (string.IsNullOrWhiteSpace(bucketName))
            {
                return BadRequest();
            }

            var request = new GetPreSignedUrlRequest
            {
                BucketName = bucketName,
                Key = queryThumbnail.S3ObjectKey,
                Expires = DateTime.UtcNow.AddMinutes(30)
            };

            var imageUrl = _cloudStorage.GetPreSignedURL(request);

            var thumbnailResponse = new ThumbnailResponse
            {
                ThumbnailId = queryThumbnail.ThumbnailId, Name = queryThumbnail.Name, ImageUrl = imageUrl
            };

            return Ok(thumbnailResponse);
        }
    }
}
