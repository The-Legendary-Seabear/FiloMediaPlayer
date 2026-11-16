
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


    [HttpPost("grafting")]
    public async Task<IActionResult> CreateMediaGrafting(
        [FromBody] CreateMediaGraftingJobRequest request)
    {
        if (!int.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out int userId))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Title) ||
            request.Title.Trim().Length > 255)
        {
            return BadRequest("A valid output title is required.");
        }

        if (request.MediaFileIds == null ||
            request.MediaFileIds.Count < 2)
        {
            return BadRequest(
                "Select at least two media files.");
        }

        if (request.MediaFileIds.Count > 20)
        {
            return BadRequest(
                "A maximum of 20 media files is supported.");
        }

        if (request.MediaFileIds.Distinct().Count() !=
            request.MediaFileIds.Count)
        {
            return BadRequest(
                "Duplicate media files are not supported yet.");
        }

        var mediaType = request.MediaType?
            .Trim()
            .ToLowerInvariant();

        if (mediaType != "audio" && mediaType != "video")
        {
            return BadRequest("Invalid media type.");
        }

        var mediaFiles = await _context.MediaFiles
            .AsNoTracking()
            .Where(m =>
                m.UserId == userId &&
                request.MediaFileIds.Contains(m.MediaFileId))
            .ToListAsync();

        if (mediaFiles.Count != request.MediaFileIds.Count)
        {
            return BadRequest(
                "One or more media files are unavailable.");
        }

        bool allCompatible = mediaFiles.All(m =>
            m.ContentType != null &&
            m.ContentType.StartsWith(
                mediaType + "/",
                StringComparison.OrdinalIgnoreCase));

        if (!allCompatible)
        {
            return BadRequest(
                "All selected files must match the grafting type.");
        }

        var parametersJson = JsonSerializer.Serialize(new
        {
            Title = request.Title.Trim(),
            MediaType = mediaType,
            MediaFileIds = request.MediaFileIds
        });

        var job = new ProcessingJob
        {
            OwnerUserId = userId,
            MediaFileId = null,
            JobType = "MediaGrafting",
            Status = ProcessingJobStatus.Queued,
            Progress = 0,
            ParametersJson = parametersJson,
            CreatedAt = DateTime.UtcNow
        };

        _context.ProcessingJobs.Add(job);
        await _context.SaveChangesAsync();

        return Accepted(new
        {
            job.ProcessingJobId,
            Status = job.Status.ToString(),
            Message = "Media grafting job queued."
        });
    }



    [HttpPost("loudness-analysis/{mediaFileId:int}")]
    public async Task<IActionResult> CreateLoudnessAnalysis(int mediaFileId)
    {
        if (!int.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out int userId))
        {
            return Unauthorized();
        }

        var media = await _context.MediaFiles
            .FirstOrDefaultAsync(m =>
                m.MediaFileId == mediaFileId &&
                m.UserId == userId);

        if (media == null)
            return NotFound("Media not found.");

        bool supported =
            media.ContentType.StartsWith(
                "audio/", StringComparison.OrdinalIgnoreCase) ||
            media.ContentType.StartsWith(
                "video/", StringComparison.OrdinalIgnoreCase);

        if (!supported)
            return BadRequest("Media must contain audio.");

        if (media.LoudnessAnalyzedAt.HasValue)
            return Conflict("This media has already been analyzed.");

        bool alreadyQueued = await _context.ProcessingJobs
            .AnyAsync(j =>
                j.MediaFileId == mediaFileId &&
                j.OwnerUserId == userId &&
                j.JobType == "LoudnessAnalysis" &&
                (j.Status == ProcessingJobStatus.Queued ||
                 j.Status == ProcessingJobStatus.Processing));

        if (alreadyQueued)
            return Conflict("Loudness analysis is already queued.");

        var job = new ProcessingJob
        {
            OwnerUserId = userId,
            MediaFileId = mediaFileId,
            JobType = "LoudnessAnalysis",
            Status = ProcessingJobStatus.Queued,
            Progress = 0,
            ParametersJson = "{}",
            CreatedAt = DateTime.UtcNow
        };

        _context.ProcessingJobs.Add(job);
        await _context.SaveChangesAsync();

        return Accepted(new
        {
            job.ProcessingJobId,
            Status = job.Status.ToString(),
            Message = "Loudness analysis queued."
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


        if (job.JobType != "Conversion" &&
            job.JobType != "LoudnessAnalysis")
        {
            return BadRequest("Unsupported processing job type.");
        }


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


    [HttpPost("{id:int}/loudness-result")]
    [AllowAnonymous]
    public async Task<IActionResult> SaveLoudnessAnalysis(
        int id,
        [FromBody] LoudnessAnalysisResultRequest request)
    {
        if (!IsAuthorizedWorker())
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.WorkerId))
            return BadRequest("WorkerId is required.");

        double[] values =
        {
        request.IntegratedLufs,
        request.TruePeakDbfs,
        request.LoudnessRangeLu,
        request.LoudnessThresholdLufs,
        request.NormalizationGainDb
    };

        if (values.Any(v => !double.IsFinite(v)))
            return BadRequest("Invalid loudness measurements.");


        if (request.LoudnessRangeLu < 0 ||
            request.NormalizationGainDb +
                request.TruePeakDbfs > -1.5 + 0.000001)
        {
            return BadRequest("Invalid or unsafe loudness values.");
        }


        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        var job = await _context.ProcessingJobs
            .FromSqlInterpolated(
                $"SELECT * FROM \"ProcessingJobs\" WHERE \"ProcessingJobId\" = {id} FOR UPDATE")
            .FirstOrDefaultAsync();

        if (job == null)
            return NotFound();

        if (job.WorkerId != request.WorkerId ||
            job.Status != ProcessingJobStatus.Processing ||
            job.JobType != "LoudnessAnalysis")
        {
            return Conflict("Job is not active for this worker.");
        }

        var media = await _context.MediaFiles
            .FirstOrDefaultAsync(m =>
                m.MediaFileId == job.MediaFileId &&
                m.UserId == job.OwnerUserId);

        if (media == null)
            return NotFound("Source media not found.");

        var now = DateTime.UtcNow;

        media.IntegratedLufs = request.IntegratedLufs;
        media.TruePeakDbfs = request.TruePeakDbfs;
        media.LoudnessRangeLu = request.LoudnessRangeLu;
        media.LoudnessThresholdLufs = request.LoudnessThresholdLufs;
        media.NormalizationGainDb = request.NormalizationGainDb;
        media.LoudnessAnalyzedAt = now;

        job.Status = ProcessingJobStatus.Completed;
        job.Progress = 100;
        job.CompletedAt = now;
        job.LastHeartbeatAt = now;
        job.ErrorMessage = null;

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return Ok(new
        {
            job.ProcessingJobId,
            media.MediaFileId,
            media.NormalizationGainDb
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


    [HttpPost("{id:int}/grafting-sources")]
    [AllowAnonymous]
    public async Task<IActionResult> GetGraftingSources(
        int id,
        [FromBody] HeartbeatRequest request)
    {
        if (!IsAuthorizedWorker())
            return Unauthorized();

        var job = await _context.ProcessingJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j =>
                j.ProcessingJobId == id &&
                j.WorkerId == request.WorkerId &&
                j.Status == ProcessingJobStatus.Processing &&
                j.JobType == "MediaGrafting");

        if (job == null)
            return NotFound("Active grafting job not found.");

        using var document = JsonDocument.Parse(job.ParametersJson);

        var mediaIds = document.RootElement
            .GetProperty("MediaFileIds")
            .EnumerateArray()
            .Select(x => x.GetInt32())
            .ToList();

        var mediaFiles = await _context.MediaFiles
            .AsNoTracking()
            .Where(m =>
                m.UserId == job.OwnerUserId &&
                mediaIds.Contains(m.MediaFileId))
            .ToDictionaryAsync(m => m.MediaFileId);

        if (mediaFiles.Count != mediaIds.Count)
            return Conflict("One or more source files are missing.");

        var bucketName = _configuration["S3:BucketName"];

        if (string.IsNullOrWhiteSpace(bucketName))
            return StatusCode(500, "S3 bucket is not configured.");

        var sources = mediaIds.Select(mediaId =>
        {
            var media = mediaFiles[mediaId];

            var url = _s3.GetPreSignedURL(
                new GetPreSignedUrlRequest
                {
                    BucketName = bucketName,
                    Key = media.S3ObjectKey,
                    Verb = HttpVerb.GET,
                    Expires = DateTime.UtcNow.AddMinutes(30)
                });

            return new
            {
                media.MediaFileId,
                media.OriginalFileName,
                media.ContentType,
                media.FileSizeBytes,
                DownloadUrl = url
            };
        }).ToList();

        return Ok(sources);
    }


    [HttpPost("{id:int}/grafting-output-upload")]
    [AllowAnonymous]
    public async Task<IActionResult> PrepareGraftingOutput(
        int id,
        [FromBody] HeartbeatRequest request)
    {
        if (!IsAuthorizedWorker())
            return Unauthorized();

        var job = await _context.ProcessingJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j =>
                j.ProcessingJobId == id &&
                j.WorkerId == request.WorkerId &&
                j.Status == ProcessingJobStatus.Processing &&
                j.JobType == "MediaGrafting");

        if (job == null)
            return NotFound("Active grafting job not found.");

        using var document = JsonDocument.Parse(job.ParametersJson);

        string? mediaType = document.RootElement
            .GetProperty("MediaType").GetString();

        string extension;
        string contentType;

        if (mediaType == "audio")
        {
            extension = "mp3";
            contentType = "audio/mpeg";
        }
        else if (mediaType == "video")
        {
            extension = "mp4";
            contentType = "video/mp4";
        }
        else
        {
            return BadRequest("Invalid grafting media type.");
        }

        var bucketName = _configuration["S3:BucketName"];

        if (string.IsNullOrWhiteSpace(bucketName))
            return StatusCode(500, "S3 bucket is not configured.");

        var objectKey =
            $"processed/{job.OwnerUserId}/{job.ProcessingJobId}/grafted.{extension}";

        var uploadUrl = _s3.GetPreSignedURL(
            new GetPreSignedUrlRequest
            {
                BucketName = bucketName,
                Key = objectKey,
                Verb = HttpVerb.PUT,
                Expires = DateTime.UtcNow.AddMinutes(30)
            });

        return Ok(new
        {
            UploadUrl = uploadUrl,
            ContentType = contentType,
            OutputFileName = $"grafted.{extension}"
        });
    }


    [HttpPost("{id:int}/grafting-finalize")]
    [AllowAnonymous]
    public async Task<IActionResult> FinalizeGrafting(
        int id,
        [FromBody] HeartbeatRequest request)
    {
        if (!IsAuthorizedWorker())
            return Unauthorized();

        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        var job = await _context.ProcessingJobs
            .FromSqlInterpolated(
                $"SELECT * FROM \"ProcessingJobs\" WHERE \"ProcessingJobId\" = {id} FOR UPDATE")
            .FirstOrDefaultAsync();

        if (job == null)
            return NotFound();

        if (job.WorkerId != request.WorkerId ||
            job.Status != ProcessingJobStatus.Processing ||
            job.JobType != "MediaGrafting")
        {
            return Conflict("Job is not active for this worker.");
        }

        if (job.OutputMediaFileId != null)
            return Conflict("Output already registered.");

        using var document = JsonDocument.Parse(job.ParametersJson);

        string? title = document.RootElement
            .GetProperty("Title").GetString();

        string? mediaType = document.RootElement
            .GetProperty("MediaType").GetString();

        string extension;
        string contentType;

        if (mediaType == "audio")
        {
            extension = "mp3";
            contentType = "audio/mpeg";
        }
        else if (mediaType == "video")
        {
            extension = "mp4";
            contentType = "video/mp4";
        }
        else
        {
            return BadRequest("Invalid media type.");
        }

        if (string.IsNullOrWhiteSpace(title))
            return BadRequest("Invalid output title.");

        var bucketName = _configuration["S3:BucketName"];

        if (string.IsNullOrWhiteSpace(bucketName))
            return StatusCode(500, "S3 bucket is not configured.");

        var objectKey =
            $"processed/{job.OwnerUserId}/{job.ProcessingJobId}/grafted.{extension}";

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
            return Conflict("Grafted output has not been uploaded.");
        }

        if (metadata.ContentLength <= 0)
            return Conflict("Grafted output is empty.");

        var now = DateTime.UtcNow;

        var output = new MediaFile
        {
            UserId = job.OwnerUserId,
            Title = title,
            OriginalFileName = $"grafted-{job.ProcessingJobId}.{extension}",
            S3ObjectKey = objectKey,
            ContentType = contentType,
            FileSizeBytes = metadata.ContentLength,
            UploadedAt = now
        };

        _context.MediaFiles.Add(output);
        await _context.SaveChangesAsync();

        job.OutputMediaFileId = output.MediaFileId;
        job.Status = ProcessingJobStatus.Completed;
        job.Progress = 100;
        job.CompletedAt = now;
        job.LastHeartbeatAt = now;
        job.ErrorMessage = null;

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return Ok(new
        {
            job.ProcessingJobId,
            job.OutputMediaFileId,
            Status = job.Status.ToString()
        });
    }


}

