using Filo.Api.Migrations;
using Filo.Api.Models;
using Filo.Api.Models.Bookmarks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Reflection.Emit;

namespace Filo.Api.Data
{
    public class FiloDbContext : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>
    {
        public FiloDbContext(DbContextOptions<FiloDbContext> options)
            : base(options)
        {
        }

        public DbSet<MediaFile> MediaFiles { get; set; }
        public DbSet<Thumbnail> Thumbnails { get; set; }
        public DbSet<Playlist> Playlists { get; set; }
        public DbSet<PlaylistMedia> PlaylistMedia { get; set; }
        public DbSet<ProcessingJob> ProcessingJobs { get; set; }
        public DbSet<MediaBookmark> MediaBookmarks { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);


            // -------------------------
            // ProcessingJob
            // -------------------------

            builder.Entity<ProcessingJob>(entity =>
            {
                entity.HasKey(j => j.ProcessingJobId);

                entity.Property(j => j.JobType)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(j => j.Status)
                    .HasConversion<string>()
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(j => j.Progress)
                    .HasDefaultValue(0);

                entity.Property(j => j.ParametersJson)
                    .IsRequired();

                entity.Property(j => j.WorkerId)
                    .HasMaxLength(100);

                entity.HasOne(j => j.OwnerUser)
                    .WithMany()
                    .HasForeignKey(j => j.OwnerUserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(j => j.MediaFile)
                    .WithMany()
                    .HasForeignKey(j => j.MediaFileId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(j => j.OutputMediaFile)
                    .WithMany()
                    .HasForeignKey(j => j.OutputMediaFileId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(j => new
                {
                    j.Status,
                    j.CreatedAt
                });
            });

            //BookMark

            builder.Entity<MediaBookmark>(entity =>
            {
                entity.HasKey(e => e.MediaBookmarkId);

                entity.Property(e => e.Title)
                    .IsRequired()
                    .HasMaxLength(255);

                entity.Property(e => e.Description)
                    .HasMaxLength(2000);

                entity.Property(e => e.Color)
                    .HasMaxLength(20);

                entity.Property(e => e.CreatedAt)
                    .IsRequired();

                entity.Property(e => e.UpdatedAt)
                    .IsRequired();

                entity.HasOne(e => e.MediaFile)
                    .WithMany()
                    .HasForeignKey(e => e.MediaFileId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.OwnerUser)
                    .WithMany()
                    .HasForeignKey(e => e.OwnerUserId)
                    .OnDelete(DeleteBehavior.NoAction);

                entity.HasIndex(e => new
                {
                    e.OwnerUserId,
                    e.MediaFileId,
                    e.StartSeconds
                });
            });


            // -------------------------
            // MediaFile
            // -------------------------

            builder.Entity<MediaFile>(entity =>
            {
                entity.HasKey(m => m.MediaFileId);

                entity.Property(m => m.Title)
                    .HasMaxLength(255)
                    .IsRequired();

                entity.Property(m => m.OriginalFileName)
                    .HasMaxLength(255)
                    .IsRequired();

                entity.Property(m => m.S3ObjectKey)
                    .HasMaxLength(1024)
                    .IsRequired();

                entity.Property(m => m.ContentType)
                    .HasMaxLength(255)
                    .IsRequired();

                entity.HasOne(m => m.User)
                    .WithMany()
                    .HasForeignKey(m => m.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(m => m.Thumbnail)
                    .WithMany()
                    .HasForeignKey(m => m.ThumbnailId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // -------------------------
            // Thumbnail
            // -------------------------

            builder.Entity<Thumbnail>(entity =>
            {
                entity.HasKey(t => t.ThumbnailId);

                entity.Property(t => t.S3ObjectKey)
                    .HasMaxLength(1024)
                    .IsRequired();

                entity.HasOne(t => t.User)
                    .WithMany()
                    .HasForeignKey(t => t.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // -------------------------
            // Playlist
            // -------------------------

            builder.Entity<Playlist>(entity =>
            {
                entity.HasKey(p => p.PlaylistId);

                entity.Property(p => p.Name)
                    .HasMaxLength(255)
                    .IsRequired();

                entity.HasOne(p => p.User)
                    .WithMany()
                    .HasForeignKey(p => p.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(p => p.Thumbnail)
                    .WithMany()
                    .HasForeignKey(p => p.ThumbnailId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // -------------------------
            // PlaylistMedia
            // -------------------------

            builder.Entity<PlaylistMedia>(entity =>
            {
                entity.HasKey(pm => new
                {
                    pm.PlaylistId,
                    pm.MediaFileId
                });

                entity.HasOne(pm => pm.Playlist)
                    .WithMany(p => p.PlaylistMedia)
                    .HasForeignKey(pm => pm.PlaylistId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(pm => pm.MediaFile)
                    .WithMany()
                    .HasForeignKey(pm => pm.MediaFileId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
     }
}