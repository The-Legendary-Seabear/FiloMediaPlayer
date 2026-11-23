using Amazon.S3;
using Filo.Api.Data;
using Filo.Api.Infrastructure;
using Filo.Api.Models;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// The bucket is read as "AWS:BucketName" by some controllers and
// "S3:BucketName" by others; expose one configured value under both.
var bucketName = builder.Configuration["AWS:BucketName"];
if (string.IsNullOrWhiteSpace(bucketName))
    bucketName = builder.Configuration["S3:BucketName"];
builder.Configuration["AWS:BucketName"] = bucketName;
builder.Configuration["S3:BucketName"] = bucketName;

// Outside Development, refuse to start with missing settings rather than
// failing on the first request.
if (!builder.Environment.IsDevelopment())
{
    var missing = new List<string>();

    if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("DefaultConnection")))
        missing.Add("ConnectionStrings__DefaultConnection");

    if (string.IsNullOrWhiteSpace(bucketName))
        missing.Add("AWS__BucketName");

    if (missing.Count > 0)
    {
        throw new InvalidOperationException(
            "Missing required configuration: " + string.Join(", ", missing));
    }
}

// Access and refresh tokens are encrypted with Data Protection keys. By
// default the keys live inside the container and are lost on redeploy,
// which signs every user out. Point this at a persistent volume in
// production.
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("Filo.Api")
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: new[] { "ready" });


builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 6_000_000_000L;
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 6_000_000_000L;
});


builder.Services.AddDefaultAWSOptions(
    builder.Configuration.GetAWSOptions());

builder.Services.AddAWSService<IAmazonS3>();

builder.Services.AddDbContext<FiloDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services
    .AddIdentityCore<ApplicationUser>()
    .AddRoles<IdentityRole<int>>()
    .AddEntityFrameworkStores<FiloDbContext>()
    .AddSignInManager();

builder.Services
    .AddAuthentication(IdentityConstants.BearerScheme)
    .AddBearerToken(IdentityConstants.BearerScheme);

builder.Services.AddAuthorization();

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHsts();
}

if (string.IsNullOrWhiteSpace(app.Configuration["ProcessingWorker:ApiKey"]))
{
    app.Logger.LogWarning(
        "ProcessingWorker:ApiKey is not set; the processing worker will be rejected.");
}

app.UseHttpsRedirection();

// Liveness: the process is up. Readiness: it can also reach the database.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
