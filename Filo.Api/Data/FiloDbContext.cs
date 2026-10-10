using Filo.Api.Migrations;
using Filo.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

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

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

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