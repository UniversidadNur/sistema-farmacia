using System.Security.Claims;
using FarmaciaSalacor.Web.Data;
using FarmaciaSalacor.Web.Models;
using FarmaciaSalacor.Web.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FarmaciaSalacor.Web.Controllers;

[AllowAnonymous]
public class AccountController : Controller
{
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        // Evita bucles de redirección con ReturnUrl anidado (puede llegar a 414 Request-URI Too Long).
        if (!string.IsNullOrWhiteSpace(returnUrl))
        {
            if (returnUrl.Length > 512
                || returnUrl.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase)
                || !Url.IsLocalUrl(returnUrl))
            {
                returnUrl = null;
            }
        }

        // Si ya está autenticado, no tiene sentido volver al login.
        if (User?.Identity?.IsAuthenticated ?? false)
        {
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Home");
        }

        ViewData["ReturnUrl"] = returnUrl;
        ViewBag.ResetActive = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FARMACIA_RESET_ADMIN_PASSWORD"));
        ViewBag.ResetUsername = Environment.GetEnvironmentVariable("FARMACIA_RESET_ADMIN_USERNAME") ?? "admin";
        return View(new LoginViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null, [FromServices] AppDbContext db = null!)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl)
            && (returnUrl.Length > 512 || returnUrl.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase)))
        {
            returnUrl = null;
        }

        ViewData["ReturnUrl"] = returnUrl;
        ViewBag.ResetActive = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FARMACIA_RESET_ADMIN_PASSWORD"));
        ViewBag.ResetUsername = Environment.GetEnvironmentVariable("FARMACIA_RESET_ADMIN_USERNAME") ?? "admin";

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var inputUsername = (model.Username ?? string.Empty).Trim();
        var inputPassword = model.Password ?? string.Empty;

        // Modo recuperación (producción): si se define FARMACIA_RESET_ADMIN_PASSWORD,
        // permite iniciar sesión con esa clave y asegura que el usuario exista/sea Admin.
        // IMPORTANTE: quitar FARMACIA_RESET_ADMIN_PASSWORD después de recuperar acceso.
        var resetPasswordRaw = Environment.GetEnvironmentVariable("FARMACIA_RESET_ADMIN_PASSWORD");
        if (!string.IsNullOrWhiteSpace(resetPasswordRaw))
        {
            // En Railway es común pegar la variable con espacios sin querer.
            // Solo en modo recuperación normalizamos con Trim para evitar bloqueos.
            var resetPassword = resetPasswordRaw.Trim();

            var resetUsername = Environment.GetEnvironmentVariable("FARMACIA_RESET_ADMIN_USERNAME");
            if (string.IsNullOrWhiteSpace(resetUsername)) resetUsername = "admin";

            resetUsername = resetUsername.Trim();
            var resetUsernameLower = resetUsername.ToLowerInvariant();

            if (string.Equals(inputUsername, resetUsername, StringComparison.OrdinalIgnoreCase)
                && string.Equals(inputPassword.Trim(), resetPassword, StringComparison.Ordinal))
            {
                var adminUser = await db.Usuarios.FirstOrDefaultAsync(x => x.Username.ToLower() == resetUsernameLower);
                if (adminUser is null)
                {
                    adminUser = new Usuario
                    {
                        Username = resetUsername,
                        NombreCompleto = "Administrador",
                        Rol = Roles.Admin,
                        Activo = true
                    };
                    db.Usuarios.Add(adminUser);
                }

                // Normaliza el username (por si venía con distinta capitalización).
                adminUser.Username = resetUsername;

                adminUser.PasswordHash = BCrypt.Net.BCrypt.HashPassword(resetPassword);
                adminUser.Rol = Roles.Admin;
                adminUser.Activo = true;
                await db.SaveChangesAsync();

                var resetClaims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, adminUser.Id.ToString()),
                    new(ClaimTypes.Name, adminUser.Username),
                    new(ClaimTypes.Role, adminUser.Rol)
                };

                if (!string.IsNullOrWhiteSpace(adminUser.NombreCompleto))
                {
                    resetClaims.Add(new Claim("NombreCompleto", adminUser.NombreCompleto));
                }

                var resetIdentity = new ClaimsIdentity(resetClaims, CookieAuthenticationDefaults.AuthenticationScheme);
                var resetPrincipal = new ClaimsPrincipal(resetIdentity);

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    resetPrincipal,
                    new AuthenticationProperties
                    {
                        IsPersistent = model.RememberMe,
                        AllowRefresh = true,
                        ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(7) : DateTimeOffset.UtcNow.AddHours(8)
                    });

                if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }

                return RedirectToAction("Index", "Home");
            }

            if (string.Equals(inputUsername, resetUsername, StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(string.Empty, "Modo recuperación activo: la contraseña no coincide con la variable configurada en Railway.");
                return View(model);
            }
        }

        var inputUsernameLower = inputUsername.ToLowerInvariant();
        var user = await db.Usuarios
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Username.ToLower() == inputUsernameLower);

        if (user is null || !user.Activo)
        {
            ModelState.AddModelError(string.Empty, "Usuario o contraseña inválidos.");
            return View(model);
        }

        if (!BCrypt.Net.BCrypt.Verify(inputPassword, user.PasswordHash))
        {
            ModelState.AddModelError(string.Empty, "Usuario o contraseña inválidos.");
            return View(model);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Rol)
        };

        if (!string.IsNullOrWhiteSpace(user.NombreCompleto))
        {
            claims.Add(new Claim("NombreCompleto", user.NombreCompleto));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = model.RememberMe,
                AllowRefresh = true,
                ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(7) : DateTimeOffset.UtcNow.AddHours(8)
            });

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult Denied()
    {
        return View();
    }
}
