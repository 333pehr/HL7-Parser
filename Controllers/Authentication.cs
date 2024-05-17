using Configuration;
using Controllers.Dtos;
using Entities;
using Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Models;
using Repositories;
using Services;

namespace Controllers;

[ApiController]
[Route("[controller]")]
public sealed class AuthenticationController(
    UserRepository userRepository,
    PasswordHasher passwordHasher,
    SessionRepository sessionRepository
) : ControllerBase
{
    private readonly UserRepository _userRepository = userRepository;
    private readonly PasswordHasher _passwordHasher = passwordHasher;
    private readonly SessionRepository _sessionRepository = sessionRepository;

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest registerRequest)
    {
        User? existingUser = await _userRepository.FindByEmailAsync(registerRequest.Email);
        if (existingUser is not null)
        {
            return BadRequest(new { message = "User already exists" });
        }

        User user = new()
        {
            FirstName = registerRequest.FirstName,
            LastName = registerRequest.LastName,
            Email = registerRequest.Email,
            PasswordHash = _passwordHasher.HashPassword(registerRequest.Password),
            Roles = 1 << (int)Role.Unverified,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _userRepository.CreateAsync(user);

        return Ok(new { message = "User registered" });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest loginRequest)
    {
        User? user = await _userRepository.FindByEmailAsync(loginRequest.Email);
        if (user is null)
        {
            return NotFound(new { message = "User not found" });
        }

        if (!_passwordHasher.VerifyPassword(loginRequest.Password, user.PasswordHash))
        {
            return BadRequest(new { message = "Invalid password" });
        }

        // ---- user is authenticated ----

        DateTime sessionExpireAt = DateTime.UtcNow.Add(Configurations.SessionExpiry);

        Session newSession = new()
        {
            UserId = user.Id,
            Roles = user.Roles.ToRoleList(),
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            UserAgent = Request.Headers.UserAgent.ToString(),
            CreatedAt = DateTime.UtcNow,
            ExpireAt = sessionExpireAt,
        };

        Guid sessionId;
        Session? existingSession = await _sessionRepository.GetSessionByUserIdAsync(user.Id);
        if (existingSession is null)
        {
            sessionId = await _sessionRepository.SaveSessionAsync(newSession);
        }
        else
        {
            sessionId = existingSession.Id;
            await _sessionRepository.UpdateSessionByIdAsync(existingSession.Id, newSession);
        }

        Response.Cookies.Append(Configurations.SessionIdCookieKey, sessionId.ToString(), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = true,
            Expires = sessionExpireAt
        });

        return Ok(new { message = "User logged in" });
    }
}
