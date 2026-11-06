using DocumentFormat.OpenXml.InkML;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json.Linq;
using PDFOrganizerAPI.Filters;
using PDFOrganizerAPI.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.IdentityModel.Tokens.Jwt;

namespace PDFOrganizerAPI.Controllers
{

    [HttpRequestFilter]
    [ApiController]
    public class Login : Controller
    {
    readonly ESGContext context = new ESGContext();

    [HttpGet]
        [Route("api/checkLogin")]
        public IActionResult checkLogin()
        {
            return Ok(0);

        }


    [HttpGet]
    [Route("api/GetUserRoles")]
    public async Task<IActionResult> GetAllUserGridList()
    {
      try
      {
        var data = await context.EmployeeGridAccess.FromSqlRaw("EXEC dbo.[SP_EmployeeAccess_GetAll]").ToListAsync();
        return Ok(data);
      }
      catch (Exception ex)
      {
        return StatusCode(500, ex.Message);
      }
    }

 

  }

}
