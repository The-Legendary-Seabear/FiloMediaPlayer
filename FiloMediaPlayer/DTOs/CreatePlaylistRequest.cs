using System;
using System.Collections.Generic;
using System.Text;

namespace FiloMediaPlayer.DTOs
{
    public class CreatePlaylistRequest
    {
        public string PlaylistName { get; set; } = "";

        public CreatePlaylistRequest(string playlistName)
        {
            PlaylistName = playlistName;
        }
    }

    
}
