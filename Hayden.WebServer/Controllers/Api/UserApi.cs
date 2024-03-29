using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Hayden.Consumers.HaydenMysql.DB;
using Hayden.WebServer.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hayden.WebServer.Controllers.Api
{
	public partial class ApiController
	{
		internal static Dictionary<string, ModeratorRole> RegisterCodes = new Dictionary<string, ModeratorRole>();

		[HttpPost("user/login")]
		public async Task<IActionResult> UserLoginAsync([FromServices] IDataProvider dataProvider,
			[FromForm] string username, [FromForm] string password)
		{
			var delayTask = Task.Delay(1000);

			var user = await dataProvider.GetModerator(username);

			if (user != null)
			{
				byte[] hashedPassword = GenerateHash(password, user.PasswordSalt);

				if (CryptographicOperations.FixedTimeEquals(user.PasswordHash, hashedPassword))
				{
					await LoginAsUser(user);

					return Ok();
				}
			}

			await delayTask;

			return Unauthorized();
		}

		[HttpPost("user/logout")]
		public async Task<IActionResult> UserLogoutAsync()
		{
			await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

			return Ok();
		}

		[HttpPost("user/register")]
		public async Task<IActionResult> UserRegisterAsync([FromServices] IDataProvider dataProvider,
			[FromForm] string username, [FromForm] string password, [FromForm] string registerCode)
		{
			await Task.Delay(1000);

			ModeratorRole grantedRole;

			lock (RegisterCodes)
			{
				if (!RegisterCodes.TryGetValue(registerCode, out grantedRole))
					return Unauthorized(new { error = "Unknown register code" });
			}

			var salt = GenerateSalt();
			byte[] hashedPassword = GenerateHash(password, salt);

			var moderator = new DBModerator
			{
				Role = grantedRole,
				Username = username,
				PasswordHash = hashedPassword,
				PasswordSalt = salt
			};

			if (!await dataProvider.RegisterModerator(moderator))
			{
				return BadRequest(new { error = "Username already exists" });
			}

			lock (RegisterCodes)
			{
				RegisterCodes.Remove(registerCode);
			}

			await LoginAsUser(moderator);

			return Ok();
		}

		[HttpPost("user/info")]
		public async Task<IActionResult> GetUserInfoAsync([FromServices] IServiceProvider services)
		{
			var dataProvider = services.GetService<IDataProvider>();
			if (dataProvider == null) // fallback
				return Json(new
				{
					id = (int?)null,
					role = (int?)null
				});

			var authenticateResult = await AuthenticateAsync(HttpContext);

			var moderator = authenticateResult.Principal != null
				? await authenticateResult.Principal.GetModeratorAsync(dataProvider)
				: null;

			return Json(new
			{
				id = moderator?.Id,
				role = moderator?.Role
			});
		}

		#region Helpers

		[NonAction]
		private async Task LoginAsUser(DBModerator user)
		{
			var claims = new List<Claim>
			{
				new Claim(ClaimTypes.Name, user.Username),
				new Claim("ID", user.Id.ToString())
			};

			HttpContext.User = new ClaimsPrincipal(
				new ClaimsIdentity(claims,
					CookieAuthenticationDefaults.AuthenticationScheme));

			await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, HttpContext.User);
		}

		[NonAction]
		private static byte[] GenerateSalt()
		{
			return RandomNumberGenerator.GetBytes(32);
		}

		[NonAction]
		private static byte[] GenerateHash(string password, byte[] salt)
		{
			return KeyDerivation.Pbkdf2(password ?? "", salt, KeyDerivationPrf.HMACSHA512, 5000, 64);
		}

		[NonAction]
		public static Task<AuthenticateResult> AuthenticateAsync(HttpContext context)
		{
			return context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
		}

		#endregion
	}

	internal static class AuthExtensions
	{
		public static ushort? GetUserID(this ClaimsPrincipal principal)
		{
			var claims = principal.FindAll("ID").ToArray();

			if (claims.Length == 0)
				return null;

			return ushort.Parse(claims[0].Value);
		}

		public static bool IsLoggedIn(this ClaimsPrincipal principal)
		{
			return principal?.GetUserID().HasValue ?? false;
		}

		public static async Task<DBModerator> GetModeratorAsync(this ClaimsPrincipal principal, IDataProvider dataProvider)
		{
			var id = principal.GetUserID();

			if (!id.HasValue)
				return null;

			return await dataProvider.GetModerator(id.Value);
		}

		public static Task<DBModerator> GetModeratorAsync(this HttpContext context)
			=> context.User != null
				? GetModeratorAsync(context.User, context.RequestServices.GetRequiredService<IDataProvider>())
				: Task.FromResult<DBModerator>(null);
	}
}