
using Filo.Api.Data;
using Filo.Api.Models;
using Filo.Api.Models.Processing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Filo.Api.DTOs;
using Amazon.S3;
using Amazon.S3.Model;
using System.Net;
using System.IO;

namespace Filo.Api.Controllers;

[ApiController]
[Route("api/processing-jobs")]
[Authorize]
public class ProcessingJobsController : ControllerBase
{
    private readonly FiloDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IAmazonS3 _s3;

    public ProcessingJobsController(FiloDbContext context, IConfiguration configuration, IAmazonS3 s3)
    {
        _context = context;
        _configuration = configuration;
        _s3 = s3;
    }

    [HttpPost("conversion")]
    public async Task<IActionResult> CreateConversion(CreateConversionJobRequest request)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int userId))
        {
            return Unauthorized();
        }

        var targetFormat = request.TargetFormat?
    .Trim()
    .TrimStart('.')
    .ToLowerInvariant();

        var allowedFormats = new HashSet<string>
{
    "mp3",
    "wav",
    "flac",
    "mp4",
    "webm",
    "mkv"
};

        if (string.IsNullOrWhiteSpace(targetFormat) ||
            !allowedFormats.Contains(targetFormat))
        {
            return BadRequest("Unsupported output format.");
        }

        var videoCodec = request.VideoCodec?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(videoCodec))
        {
            videoCodec = targetFormat switch
            {
                "mp4" => "h264",
                "webm" => "vp9",
                "mkv" => "h264",
                _ => null
            };
        }


        var allowedCodecs = targetFormat switch
        {
            "mp4" => new[] { "h264" },
            "webm" => new[] { "vp8", "vp9" },
            "mkv" => new[] { "h264" },
            _ => Array.Empty<string>()
        };


        if (videoCodec != null && !allowedCodecs.Contains(videoCodec))
        {
            return BadRequest(
                "The selected video codec is not supported for this format.");
        }

        var media = await _context.MediaFiles.FirstOrDefaultAsync(m => m.MediaFileId == request.MediaFileId && m.UserId == userId);
        if(media == null)
        {
            return NotFound("Media Not Found");
        }

        var isAudio = media.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase);
        var isVideo = media.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);

        var targetIsVideo = targetFormat is "mp4" or "mkv" or "webm";
        if(!isAudio && !isVideo)
        {
            return BadRequest();
        }
        if(targetIsVideo && !isVideo)
        {
            return BadRequest();
        }

        var parametersJson = JsonSerializer.Serialize(new
        {
            TargetFormat = targetFormat,
            VideoCodec = videoCodec
        });

        ProcessingJob job = new ProcessingJob {
            OwnerUserId = userId,
            MediaFileId = media.MediaFileId,
            JobType = "Conversion",
            Status = ProcessingJobStatus.Queued,
            Progress = 0,
            ParametersJson = parametersJson,
            CreatedAt = DateTime.UtcNow
        };

        _context.ProcessingJobs.Add(job);
        await _context.SaveChangesAsync();

        return Accepted(new
        {
            ProcessingJobId = job.ProcessingJobId,
            Status = job.Status.ToString(),
            Message = "Conversion job queued successfully."
        });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetProcessingJob(int id)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int userId))
        {
            return Unauthorized();
        }

        var job = await _context.ProcessingJobs.FirstOrDefaultAsync(j => j.ProcessingJobId == id && j.OwnerUserId == userId);
        if(job == null)
        {
            return NotFound();
        }

        var response = new ProcessingJobResponse
        {
            ProcessingJobId = job.ProcessingJobId,
            MediaFileId = job.MediaFileId,
            OutputMediaFileId = job.OutputMediaFileId,
            JobType = job.JobType,
            Status = job.Status,
            Progress = job.Progress,
            ErrorMessage = job.ErrorMessage,
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt
        };


        return Ok(response);
    }

    private bool IsAuthorizedWorker()
    {
        var expectedKey = _configuration["ProcessingWorker:ApiKey"];
        if(string.IsNullOrWhiteSpace(expectedKey))
        {
            return false;
        }

        var providedKey = Request.Headers["X-Worker-Key"].ToString();
        if(string.IsNullOrWhiteSpace(providedKey))
        {
            return false;
        }

        var expectedKeyBytes = Encoding.UTF8.GetBytes(expectedKey);
        var providedKeyBytes = Encoding.UTF8.GetBytes(providedKey);

        if(expectedKeyBytes.Length != providedKeyBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedKeyBytes, providedKeyBytes);
    }

    [HttpPost("claim")]
    [AllowAnonymous]
    public async Task<IActionResult> ClaimJob([FromBody] ClaimProcessingJobRequest request)
    {
        if (!IsAuthorizedWorker())
        {
            return Unauthorized();
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var jobs = await _context.ProcessingJobs
            .FromSqlRaw("""
                SELECT *
                FROM "ProcessingJobs"
                WHERE "Status" = 'Queued'
                ORDER BY "CreatedAt" ASC
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync();

        var oldestQueuedJob = jobs.FirstOrDefault();

        //var oldestQueuedJob = await _context.ProcessingJobs.Where(j => j.Status == ProcessingJobStatus.Queued).OrderBy(j => j.CreatedAt).ThenBy(j => j.ProcessingJobId).FirstOrDefaultAsync();
        if(oldestQueuedJob == null)
        {
            return NoContent();
        }

        oldestQueuedJob.Status = ProcessingJobStatus.Processing;
        oldestQueuedJob.WorkerId = request.WorkerId;
        oldestQueuedJob.StartedAt = DateTime.UtcNow;
        oldestQueuedJob.LastHeartbeatAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return Ok(new
        {
            oldestQueuedJob.ProcessingJobId,
            oldestQueuedJob.MediaFileId,
            oldestQueuedJob.JobType,
            oldestQueuedJob.ParametersJson
        });
    }

    [HttpPost("{id}/heartbeat")]
    [AllowAnonymous]
    public async Task<IActionResult> Heartbeat(int id, [FromBody] HeartbeatRequest request)
    {
        if (!IsAuthorizedWorker())
        {
            return Unauthorized();
        }

        var job = await _context.ProcessingJobs.FirstOrDefaultAsync(j => j.ProcessingJobId == id);
        if(job == null)
        {
            return NotFound();
        }

        if(job.WorkerId != request.WorkerId || job.Status != ProcessingJobStatus.Processing)
        {
            return Conflict();
        }

        job.LastHeartbeatAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return NoContent();
    }


    [HttpPost("{id}/complete")]
    [AllowAnonymous]
    public async Task<IActionResult> CompleteJob(
        int id,
        [FromBody] CompleteProcessingJobRequest request)
    {
        if (!IsAuthorizedWorker())
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.WorkerId))
            return BadRequest("WorkerId is required.");

        var now = DateTime.UtcNow;

        int updated = await _context.ProcessingJobs
            .Where(j =>
                j.ProcessingJobId == id &&
                j.WorkerId == request.WorkerId &&
                j.Status == ProcessingJobStatus.Processing)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    j => j.Status,
                    ProcessingJobStatus.Completed)
                .SetProperty(j => j.Progress, 100)
                .SetProperty(j => j.CompletedAt, now)
                .SetProperty(j => j.LastHeartbeatAt, now)
                .SetProperty(j => j.ErrorMessage, (string?)null));

        if (updated == 0)
        {
            bool exists = await _context.ProcessingJobs
                .AnyAsync(j => j.ProcessingJobId == id);

            return exists
                ? Conflict("Job is not processing or belongs to another worker.")
                : NotFound();
        }

        return NoContent();
    }


    [HttpPost("{id}/fail")]
    [AllowAnonymous]
    public async Task<IActionResult> FailJob(
        int id,
        [FromBody] FailProcessingJobRequest request)
    {
        if (!IsAuthorizedWorker())
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.WorkerId))
            return BadRequest("WorkerId is required.");

        if (string.IsNullOrWhiteSpace(request.ErrorMessage))
            return BadRequest("An error message is required.");

        string errorMessage = request.ErrorMessage.Trim();

        if (errorMessage.Length > 2000)
            errorMessage = errorMessage[..2000];

        var now = DateTime.UtcNow;

        int updated = await _context.ProcessingJobs
            .Where(j =>
                j.ProcessingJobId == id &&
                j.WorkerId == request.WorkerId &&
                j.Status == ProcessingJobStatus.Processing)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    j => j.Status,
                    ProcessingJobStatus.Failed)
                .SetProperty(j => j.ErrorMessage, errorMessage)
                .SetProperty(j => j.CompletedAt, now)
                .SetProperty(j => j.LastHeartbeatAt, now));

        if (updated == 0)
        {
            bool exists = await _context.ProcessingJobs
                .AnyAsync(j => j.ProcessingJobId == id);

            return exists
                ? Conflict("Job is not processing or belongs to another worker.")
                : NotFound();
        }

        return NoContent();
    }


    [HttpPost("{id}/source")]
    [AllowAnonymous]
    public async Task<IActionResult> GetJobSource(int id, [FromBody] HeartbeatRequest request)
    {
        if (!IsAuthorizedWorker())
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.WorkerId))
            return BadRequest("WorkerId is required.");

        var job = await _context.ProcessingJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j =>
                j.ProcessingJobId == id &&
                j.WorkerId == request.WorkerId &&
                j.Status == ProcessingJobStatus.Processing);

        if (job == null)
            return NotFound("No active job found for this worker.");

        if (job.JobType != "Conversion")
            return BadRequest("Unsupported processing job type.");

        var media = await _context.MediaFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(m =>
                m.MediaFileId == job.MediaFileId &&
                m.UserId == job.OwnerUserId);

        if (media == null)
            return NotFound("Source media not found.");

        if (string.IsNullOrWhiteSpace(media.S3ObjectKey))
            return Conflict("Source media has no S3 object key.");

        var bucketName = _configuration["S3:BucketName"];

        if (string.IsNullOrWhiteSpace(bucketName))
            return StatusCode(500, "S3 bucket is not configured.");

        var requestUrl = new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = media.S3ObjectKey,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(15)
        };

        var downloadUrl = _s3.GetPreSignedURL(requestUrl);

        return Ok(new
        {
            DownloadUrl = downloadUrl,
            media.OriginalFileName,
            media.ContentType,
            media.FileSizeBytes
        });
    }


    [HttpPost("{id}/output-upload")]
    [AllowAnonymous]
    public async Task<IActionResult> PrepareOutputUpload(
        int id,
        [FromBody] HeartbeatRequest request)
    {
        if (!IsAuthorizedWorker())
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.WorkerId))
            return BadRequest("WorkerId is required.");

        var job = await _context.ProcessingJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j =>
                j.ProcessingJobId == id &&
                j.WorkerId == request.WorkerId &&
                j.Status == ProcessingJobStatus.Processing);

        if (job == null)
            return NotFound("No active job found for this worker.");

        if (job.JobType != "Conversion")
            return BadRequest("Unsupported job type.");

        if (string.IsNullOrWhiteSpace(job.ParametersJson))
            return Conflict("Job has no conversion parameters.");

        string format;

        try
        {
            using var document = JsonDocument.Parse(job.ParametersJson);

            format = document.RootElement
                .GetProperty("TargetFormat")
                .GetString()?
                .Trim()
                .ToLowerInvariant() ?? "";
        }
        catch (JsonException)
        {
            return Conflict("Invalid conversion parameters.");
        }
        catch (KeyNotFoundException)
        {
            return Conflict("Target format is missing.");
        }

        var contentType = format switch
        {
            "mp3" => "audio/mpeg",
            "flac" => "audio/flac",
            "wav" => "audio/wav",
            "mp4" => "video/mp4",
            "webm" => "video/webm",
            "mkv" => "video/x-matroska",
            _ => null
        };

        if (contentType == null)
            return BadRequest("Unsupported output format.");

        var bucketName = _configuration["S3:BucketName"];

        if (string.IsNullOrWhiteSpace(bucketName))
            return StatusCode(500, "S3 bucket is not configured.");

        var objectKey =
            $"processed/{job.OwnerUserId}/{job.ProcessingJobId}/output.{format}";

        var uploadRequest = new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            Expires = DateTime.UtcNow.AddMinutes(15)
        };

        var uploadUrl = _s3.GetPreSignedURL(uploadRequest);

        return Ok(new
        {
            UploadUrl = uploadUrl,
            OutputFileName = $"output.{format}",
            ContentType = contentType
        });
    }


    [HttpPost("{id}/finalize-output")]
    [AllowAnonymous]
    public async Task<IActionResult> FinalizeOutput(
        int id,
        [FromBody] HeartbeatRequest request)
    {
        if (!IsAuthorizedWorker())
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.WorkerId))
            return BadRequest("WorkerId is required.");

        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        // Lock the job so it cannot be finalized twice concurrently.
        var job = await _context.ProcessingJobs
            .FromSqlInterpolated(
                $"SELECT * FROM \"ProcessingJobs\" WHERE \"ProcessingJobId\" = {id} FOR UPDATE")
            .FirstOrDefaultAsync();

        if (job == null)
            return NotFound();

        if (job.WorkerId != request.WorkerId ||
            job.Status != ProcessingJobStatus.Processing)
            return Conflict("Job is not active for this worker.");

        if (job.JobType != "Conversion")
            return BadRequest("This endpoint only finalizes conversions.");

        if (job.OutputMediaFileId != null)
            return Conflict("Output is already registered.");

        if (string.IsNullOrWhiteSpace(job.ParametersJson))
            return Conflict("Missing conversion parameters.");

        string format;

        try
        {
            using var document =
                JsonDocument.Parse(job.ParametersJson);

            format = document.RootElement
                .GetProperty("TargetFormat")
                .GetString()?
                .Trim()
                .ToLowerInvariant() ?? "";
        }
        catch (Exception ex) when (
            ex is JsonException ||
            ex is KeyNotFoundException ||
            ex is InvalidOperationException)
        {
            return Conflict("Invalid conversion parameters.");
        }

        string? contentType = format switch
        {
            "mp3" => "audio/mpeg",
            "flac" => "audio/flac",
            "wav" => "audio/wav",
            "mp4" => "video/mp4",
            "webm" => "video/webm",
            "mkv" => "video/x-matroska",
            _ => null
        };

        if (contentType == null)
            return BadRequest("Unsupported output format.");

        var source = await _context.MediaFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(m =>
                m.MediaFileId == job.MediaFileId &&
                m.UserId == job.OwnerUserId);

        if (source == null)
            return NotFound("Original media not found.");

        var bucketName = _configuration["S3:BucketName"];

        if (string.IsNullOrWhiteSpace(bucketName))
            return StatusCode(500, "S3 bucket is not configured.");

        var objectKey =
            $"processed/{job.OwnerUserId}/{job.ProcessingJobId}/output.{format}";

        GetObjectMetadataResponse metadata;

        try
        {
            metadata = await _s3.GetObjectMetadataAsync(
                new GetObjectMetadataRequest
                {
                    BucketName = bucketName,
                    Key = objectKey
                });
        }
        catch (AmazonS3Exception ex)
            when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return Conflict("Converted output has not been uploaded.");
        }

        if (metadata.ContentLength <= 0)
            return Conflict("Converted output is empty.");

        var originalBaseName = Path.GetFileNameWithoutExtension(
            source.OriginalFileName);

        var output = new MediaFile
        {
            UserId = job.OwnerUserId,
            Title = $"{source.Title} ({format.ToUpperInvariant()})",
            OriginalFileName = $"{originalBaseName}.{format}",
            S3ObjectKey = objectKey,
            ContentType = contentType,
            FileSizeBytes = metadata.ContentLength,
            DurationSeconds = source.DurationSeconds,
            UploadedAt = DateTime.UtcNow
        };

        _context.MediaFiles.Add(output);

        await _context.SaveChangesAsync();

        job.OutputMediaFileId = output.MediaFileId;
        job.Status = ProcessingJobStatus.Completed;
        job.Progress = 100;
        job.CompletedAt = DateTime.UtcNow;
        job.LastHeartbeatAt = DateTime.UtcNow;
        job.ErrorMessage = null;

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return Ok(new
        {
            job.ProcessingJobId,
            job.OutputMediaFileId,
            Status = job.Status.ToString(),
            output.S3ObjectKey,
            output.FileSizeBytes
        });
    }



}

