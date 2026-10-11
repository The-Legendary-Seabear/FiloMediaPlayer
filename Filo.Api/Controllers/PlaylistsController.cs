using Microsoft.AspNetCore.Mvc;
using Filo.Api.Data;
using Filo.Api.DTOs;
using Filo.Api.Models;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;


namespace Filo.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PlaylistsController : ControllerBase
    {
        private readonly FiloDbContext _context;

        public PlaylistsController(FiloDbContext context)
        {
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

            var query = _context.Playlists.Where( playlist => playlist.UserId  == userId)
                .Select(playlist => new PlaylistResponse { PlaylistId = playlist.PlaylistId, Name = playlist.Name, ThumbnailId = playlist.ThumbnailId});
            var playlists = await query.ToListAsync();
            return Ok(playlists);
        }
    }
}
