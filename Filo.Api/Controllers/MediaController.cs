using Amazon.S3;
using Amazon.S3.Model;
using Filo.Api.Data;
using Filo.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

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
    }
}
