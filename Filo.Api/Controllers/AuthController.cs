using Filo.Api.Models;
using Filo.Api.Models.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.Extensions.Options;

namespace Filo.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IOptionsMonitor<BearerTokenOptions> _bearerTokenOptions;

        public AuthController(UserManager<ApplicationUser> userManager,SignInManager<ApplicationUser> signInManager,IOptionsMonitor<BearerTokenOptions> bearerTokenOptions)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _bearerTokenOptions = bearerTokenOptions;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterRequest request)
        {
            var user = new ApplicationUser
            {
                UserName = request.Username,
                Email = request.Email
            };

            var result = await _userManager.CreateAsync(user, request.Password);

            if (!result.Succeeded)
            {
                return BadRequest(result.Errors);
            }

            return Ok(new
            {
                message = "User registered successfully.",
                userId = user.Id,
                username = user.UserName,
                email = user.Email
            });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequest request)
        {
            var user = await _userManager.FindByNameAsync(request.Username);

            if (user == null)
            {
                return Unauthorized(new
                {
                    message = "Invalid username or password."
                });
            }

            var passwordValid =
                await _userManager.CheckPasswordAsync(user, request.Password);

            if (!passwordValid)
            {
                return Unauthorized(new
                {
                    message = "Invalid username or password."
                });
            }

            var principal =
    await _signInManager.CreateUserPrincipalAsync(user);

            await HttpContext.SignInAsync(
                IdentityConstants.BearerScheme,
                principal);

            return Empty;
        }

        [Authorize]
        [HttpGet("me")]
        public IActionResult Me()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var username = User.FindFirstValue(ClaimTypes.Name);

            return Ok(new
            {
                userId,
                username
            });
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh(RefreshRequest request)
        {
            var refreshTokenProtector =
                _bearerTokenOptions
                    .Get(IdentityConstants.BearerScheme)
                    .RefreshTokenProtector;

            var refreshTicket =
                refreshTokenProtector.Unprotect(request.RefreshToken);

            if (refreshTicket?.Properties?.ExpiresUtc is not { } expiresUtc)
            {
                return Unauthorized();
            }

            if (DateTimeOffset.UtcNow >= expiresUtc)
            {
                return Unauthorized();
            }

            var user =
                await _signInManager.ValidateSecurityStampAsync(
                    refreshTicket.Principal);

            if (user == null)
            {
                return Unauthorized();
            }

            var newPrincipal =
                await _signInManager.CreateUserPrincipalAsync(user);

            await HttpContext.SignInAsync(
                IdentityConstants.BearerScheme,
                newPrincipal);

            return Empty;
        }

    }
}