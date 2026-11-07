
using PDFOrganizerAPI.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.IdentityModel.Tokens.Jwt;

namespace PDFOrganizerAPI.Filters
{
    public class HttpRequestFilterAttribute : Attribute, IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {
            try
            {
                string? authorization =
                    context.HttpContext.Request.Headers["Authorization"].ToString();

                if (string.IsNullOrWhiteSpace(authorization) ||
                    !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    context.Result = new UnauthorizedObjectResult(
                        new { Message = "Token not Found" });
                    return;
                }

                string token = authorization.Substring(7).Trim();

                var handler = new JwtSecurityTokenHandler();
                var jwtSecurityToken = handler.ReadJwtToken(token);

                // Support both Google and Microsoft email claims
                var emailClaim =
                    jwtSecurityToken.Claims.FirstOrDefault(x => x.Type == "email")?.Value
                    ?? jwtSecurityToken.Claims.FirstOrDefault(x => x.Type == "upn")?.Value
                    ?? jwtSecurityToken.Claims.FirstOrDefault(x => x.Type == "preferred_username")?.Value;

                if (string.IsNullOrWhiteSpace(emailClaim))
                {
                    context.Result = new UnauthorizedObjectResult(
                        new { Message = "Email claim not found in token" });
                    return;
                }

                using (var _energydb = new EnergyDashboardController())
                {
                    var user = _energydb.GetAllUser()
                        .AsEnumerable()
                        .FirstOrDefault(x =>
                            x.EA_Email != null &&
                            x.EA_Email.Equals(
                                emailClaim,
                                StringComparison.OrdinalIgnoreCase));

                    bool isActive = user != null && user.EA_IsActive != 0;

                    if (!isActive)
                    {
                        context.Result = new UnauthorizedObjectResult(
                            new { Message = "User not Found" });
                        return;
                    }

                    var dbtoken = _energydb.GetTokenByEmail(emailClaim);

                    if (dbtoken != null && dbtoken != token)
                    {
                        context.Result = new ObjectResult(
                            new { Message = "Active Session Found" })
                        {
                            StatusCode = 403
                        };
                        return;
                    }

                    if (jwtSecurityToken.ValidTo < DateTime.UtcNow)
                    {
                        context.Result = new UnauthorizedObjectResult(
                            new { Message = "Token Expired" });
                        return;
                    }

                    await next();
                }
            }
            catch (Exception)
            {
                context.Result = new UnauthorizedObjectResult(
                    new { Message = "Invalid token" });
            }
        }
    }
}
