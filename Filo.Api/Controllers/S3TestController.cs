using Amazon.S3;
using Microsoft.AspNetCore.Mvc;

namespace Filo.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class S3TestController : ControllerBase
    {
        private readonly IAmazonS3 _s3;
        private readonly IConfiguration _configuration;

        public S3TestController(
            IAmazonS3 s3,
            IConfiguration configuration)
        {
            _s3 = s3;
            _configuration = configuration;
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var bucketName =
                _configuration["S3:BucketName"];

            var response = await _s3.ListObjectsV2Async(
                new Amazon.S3.Model.ListObjectsV2Request
                {
                    BucketName = bucketName
                });

            return Ok(response.S3Objects.Select(x => new
            {
                x.Key,
                x.Size
            }));
        }
    }
}