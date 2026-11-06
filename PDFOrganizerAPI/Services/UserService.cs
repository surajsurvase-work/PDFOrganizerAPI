using PDFOrganizerAPI.Models;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;

namespace PDFOrganizerAPI.Services
{
    public class UserService : IUserService
    {
        private readonly ESGContext _energydb;

        public UserService(ESGContext energydb)
        {
            _energydb = energydb;
        }


        public List<Userlist> GetAllUser()
        {
            return _energydb.EmployeeAccess.FromSqlRaw("EXEC dbo.[sp_GetUser_List_Grid_List]").AsEnumerable().ToList();

        }
        internal static string GetSignedInUser(string token)
        {
            token = token.Substring(7);
            var handler = new JwtSecurityTokenHandler();
            var jwtSecurityToken = handler.ReadJwtToken(token);
            var emailClaim = jwtSecurityToken.Claims.First(x => x.Type == "upn").Value;
            return emailClaim;

        }

        internal static string GetSignedInUserName(string token)
        {
            token = token.Substring(7);
            var handler = new JwtSecurityTokenHandler();
            var jwtSecurityToken = handler.ReadJwtToken(token);
            var nameClaim = jwtSecurityToken.Claims.First(x => x.Type == "name").Value;
            return nameClaim;

        }
    }
}
