using Microsoft.AspNetCore.Identity;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
namespace SecureLab.Api.Scaffolding;

public static class Lab03Identity
{
    public static void AddLab03Identity(this WebApplicationBuilder builder)
    {
        builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 10;
        }).AddEntityFrameworkStores<SecureLabDbContext>().AddDefaultTokenProviders();
        builder.Services.AddAuthorization();
        builder.AddLoginProtection();
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
    }

    public static void MapLab03Identity(this WebApplication app)
    {
        app.MapPost("/api/auth/register", async (
            RegistrationInput input,
            UserManager<ApplicationUser> users) =>
        {
            var errors = new Dictionary<string, string[]>();

            var userName = input.UserName?.Trim() ?? "";
            var email = input.Email?.Trim() ?? "";
            var displayName = input.DisplayName?.Trim() ?? "";

            const string allowedUserNameCharacters =
                "abcdefghijklmnopqrstuvwxyz" +
                "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
                "0123456789-._@+";

            if (string.IsNullOrWhiteSpace(input.UserName))
            {
                errors["userName"] = ["Ім'я користувача обов'язкове."];
            }
            else if (input.UserName.Length > 256)
            {
                errors["userName"] = ["Не довше 256 символів."];
            }
            else if (userName.Any(character =>
                        !allowedUserNameCharacters.Contains(character)))
            {
                errors["userName"] =
                    ["Дозволені латинські літери, цифри та символи -._@+."];
            }

            if (string.IsNullOrWhiteSpace(input.Email))
            {
                errors["email"] = ["Email обов'язковий."];
            }
            else if (input.Email.Length > 256)
            {
                errors["email"] = ["Не довше 256 символів."];
            }
            else if (!new EmailAddressAttribute().IsValid(email))
            {
                errors["email"] = ["Некоректний формат email."];
            }

            if (string.IsNullOrWhiteSpace(input.DisplayName))
            {
                errors["displayName"] = ["Відображуване ім'я обов'язкове."];
            }
            else if (input.DisplayName.Length > 120)
            {
                errors["displayName"] = ["Не довше 120 символів."];
            }

            if (string.IsNullOrEmpty(input.Password))
            {
                errors["password"] = ["Пароль обов'язковий."];
            }

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = userName,
                Email = email,
                DisplayName = displayName
            };

            var result = await users.CreateAsync(user, input.Password!);

            if (!result.Succeeded)
            {
                return Results.BadRequest(new
                {
                    message = "Registration failed"
                });
            }

            var roleResult = await users.AddToRoleAsync(
                user,
                DbSeeder.ReporterRole);

            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Default role assignment failed.");
            }

            return Results.Created(
                "/api/me",
                new RegisteredUserResponse(
                    user.Id,
                    user.UserName!,
                    user.DisplayName));
        });
        app.MapPost("/api/auth/login", async (
            LoginInput input,
            SignInManager<ApplicationUser> signIn,
            HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(input.UserName) ||
                string.IsNullOrEmpty(input.Password))
            {
                LoginProtection.Record(context, "login", "failure");
                return Results.Unauthorized();
            }

            var result = await signIn.PasswordSignInAsync(
                input.UserName,
                input.Password,
                isPersistent: false,
                lockoutOnFailure: false);

            LoginProtection.Record(
                context,
                "login",
                result.Succeeded ? "success" : "failure");

            return result.Succeeded
                ? Results.NoContent()
                : Results.Unauthorized();
        })
        .RequireRateLimiting(LoginProtection.PolicyName);

        app.MapPost("/api/auth/logout", async (
            SignInManager<ApplicationUser> signIn,
            HttpContext context) =>
        {
            await signIn.SignOutAsync();

            LoginProtection.Record(context, "logout", "success");

            return Results.NoContent();
        })
        .RequireAuthorization();
        app.MapGet("/api/me", (HttpContext context) =>
        {
            var user = context.User;

            if (!Guid.TryParse(
                    user.FindFirstValue(ClaimTypes.NameIdentifier),
                    out var userId))
            {
                return Results.Unauthorized();
            }

            return Results.Ok(new CurrentUserResponse(
                userId,
                user.FindFirstValue(ClaimTypes.Name)));
        })
        .RequireAuthorization();
        // Приклад лише mechanics: не повертає identity і не реалізує оцінювану operation.
        app.MapGet("/api/auth/session-check", () => Results.NoContent()).RequireAuthorization();
    }
}

public sealed record RegistrationInput(
    string? UserName,
    string? Email,
    string? DisplayName,
    string? Password);
public sealed record LoginInput(string? UserName, string? Password);
public sealed record CurrentUserResponse(
    Guid Id,
    string? UserName);
public sealed record RegisteredUserResponse(
    Guid Id,
    string UserName,
    string DisplayName);