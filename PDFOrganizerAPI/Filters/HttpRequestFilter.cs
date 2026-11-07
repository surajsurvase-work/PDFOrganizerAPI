using PDFOrganizerAPI.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.IdentityModel.Tokens.Jwt;

namespace PDFOrganizerAPI.Filters
{
   
    public class HttpRequestFilterAttribute : Attribute, IAsyncActionFilter
    {

    var emailClaim = jwtSecurityToken.Claims
    .FirstOrDefault(x => x.Type == "email")?.Value
    ?? jwtSecurityToken.Claims
    .FirstOrDefault(x => x.Type == "upn")?.Value;

if (string.IsNullOrWhiteSpace(emailClaim))
{
    context.Result = new UnauthorizedObjectResult(
        new { Message = "Email claim not found in token" });
    return;
}
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            try
            {
                string? token = context.HttpContext.Request.Headers["Authorization"];

                if (token != null && token != "null")
                {
                    token = token.Substring(7);
                    var handler = new JwtSecurityTokenHandler();
                    var jwtSecurityToken = handler.ReadJwtToken(token);

                   var emailClaim = jwtSecurityToken.Claims
    .FirstOrDefault(x => x.Type == "email")?.Value
    ?? jwtSecurityToken.Claims
    .FirstOrDefault(x => x.Type == "upn")?.Value;

if (string.IsNullOrWhiteSpace(emailClaim))
{
    context.Result = new UnauthorizedObjectResult(
        new { Message = "Email claim not found in token" });
    return;
}

                    using (var _energydb = new EnergyDashboardController())
                    {
                        var user2 = _energydb.GetAllUser().AsEnumerable().FirstOrDefault(x => x.EA_Email.ToLower() == emailClaim.ToLower());

                        var user = _energydb.GetAllUser()
                    .AsEnumerable()
                    .FirstOrDefault(x => x.EA_Email.Equals(emailClaim, StringComparison.OrdinalIgnoreCase));


                        bool isActive = user != null && user.EA_IsActive != 0; // EA_IsActive is an int, check for non-zero value


                        var dbtoken = _energydb.GetTokenByEmail(emailClaim);
                        //user != null &&
                        if (isActive)
                        {


                            if (dbtoken != null && dbtoken != token)
                            {
                                context.Result = new ObjectResult(new { Message = "Active Session Found" }) { StatusCode = 403 };
                            }
                            else

                             if (jwtSecurityToken.ValidTo < DateTime.UtcNow)
                            {
                                context.Result = new UnauthorizedObjectResult(new { Message = "Token Expired" });
                            }
                            else


                            {
                                await next();
                            }

                        }
                        else
                        {
                            context.Result = new UnauthorizedObjectResult(new { Message = "User not Found" });
                        }
                    }

                }
                else
                {
                    context.Result = new UnauthorizedObjectResult(new { Message = "Token not Found" });
                }

            }
            catch (Exception ex)
            {
                context.Result = new UnauthorizedObjectResult(new { Message = ex.Message });
            }
        }



    }
}
