using System;
using System.Collections.Generic;
using System.Text;

namespace FiloMediaPlayer.DTOs
{
    public class ValidationErrorResponse
    {
        public Dictionary<string, string[]> Errors { get; set; } = new();
    }
}
