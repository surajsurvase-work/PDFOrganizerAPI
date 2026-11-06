using PDFOrganizerAPI.Models;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;
namespace PDFOrganizerAPI.Services
{
  
  public interface ITokenService
    {
        string? GetUserEmailFromToken();
    int? GetUserPSNumberFromToken();   

  }

  public class GetEmailService : ITokenService
    {

  

   
    private readonly ESGContext _eSGContext;
    private readonly IHttpContextAccessor _httpContextAccessor;

        public GetEmailService(IHttpContextAccessor httpContextAccessor, ESGContext  eSGContext)
        {
            _httpContextAccessor = httpContextAccessor;
            _eSGContext = eSGContext;
        }

        public string? GetUserEmailFromToken()
        {
            var token = _httpContextAccessor.HttpContext?.Request.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrEmpty(token) || token == "null") return null;

            token = token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? token.Substring(7) : token;
            var handler = new JwtSecurityTokenHandler();
            var jwtSecurityToken = handler.ReadJwtToken(token);
            return jwtSecurityToken.Claims.FirstOrDefault(x => x.Type == "upn")?.Value;
        }

    public int? GetUserPSNumberFromToken()
    {
  
      var token = _httpContextAccessor.HttpContext?.Request.Headers["Authorization"].FirstOrDefault();
      if (string.IsNullOrEmpty(token) || token == "null") return null;

      token = token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? token.Substring(7) : token;

      var handler = new JwtSecurityTokenHandler();
      var jwt = handler.ReadJwtToken(token);
      var email = jwt.Claims.FirstOrDefault(x => x.Type == "upn")?.Value;

      if (email == null)
        return null;
   
      var users = _eSGContext.EmployeeDropdowns
          .FromSqlRaw("EXEC dbo.[sp_GetUser_List_Grid_List]")
          .ToList();

      var user = users.FirstOrDefault(u =>
     string.Equals(u.EA_Email, email, StringComparison.OrdinalIgnoreCase));

      if (user == null || !user.EA_PS_Number.HasValue) return null;

      // Convert float/double to int safely
      return (int?)Convert.ToInt32(user.EA_PS_Number.Value);

    }



  }
}
