using System;
using System.Collections.Generic;
using System.Text;

namespace FiloMediaPlayer.DTOs
{
    public class LoginInformation
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";

        public LoginInformation(string username, string password)
        {
            Username = username;
            Password = password;
        }
    }
}
