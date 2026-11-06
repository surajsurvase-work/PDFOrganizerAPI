using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PDFOrganizerAPI.Filters;
using PDFOrganizerAPI.Models;

namespace PDFOrganizerAPI.Controllers
{
  [HttpRequestFilter]
  [ApiController]
    public class DropDownController : ControllerBase
    {

    readonly ESGContext context = new ESGContext();

    bool isSuccess = false;
    string message = string.Empty;
    object? jsonData;


    [HttpGet]
    [Route("api/GetEmployeeDDN")]
    public async Task<IActionResult> GetEmployeeDropdown()
    {
      try
      {
        // Raw SQL query
        var data = await context.EmployeeDropdowns
            .FromSqlRaw("SELECT EA_Email, EA_PS_Number FROM [dbo].[EmployeeAccess] WHERE EA_IsActive = 1")
            .Select(e => new
            {
              e.EA_Email,
              e.EA_PS_Number
            })
            .ToListAsync();

        return Ok(new { data });
      }
      catch (Exception ex)
      {
        return StatusCode(500, ex.Message);
      }
    }


    [HttpGet]
    [Route("api/GetRoleDDN")]
    public async Task<IActionResult> GetRoleDDN()
    {
      try
      {
        // Raw SQL query
        var data = await context.Roles
            .FromSqlRaw("SELECT PDF_Role_Id, PDF_Role_Name FROM [dbo].[PDF_M_Role] WHERE PDF_Role_Status = 'Y'")
            .Select(e => new
            {
              e.PDF_Role_Id,
              e.PDF_Role_Name
            })
            .ToListAsync();

        return Ok(new { data });
      }
      catch (Exception ex)
      {
        return StatusCode(500, ex.Message);
      }
    }

    [HttpGet]
    [Route("api/getMenuDDN2")]
    public async Task<IActionResult> getMenuDDN()
    {
      try
      {
        // Raw SQL query
        var data = await context.Menus
            .FromSqlRaw("SELECT PDF_Menu_Id, PDF_Menu_Name FROM [dbo].[PDF_M_Menu] WHERE PDF_Menu_Status = 'Y'")
            .Select(e => new
            {
              e.PDF_Menu_Id,
              e.PDF_Menu_Name
            })
            .ToListAsync();

        return Ok(new { data });
      }
      catch (Exception ex)
      {
        return StatusCode(500, ex.Message);
      }
    }


    [HttpGet]
    [Route("api/getMenuDDN")]
    public async Task<IActionResult> getMenusDropdown()
    {
      try
      {
        var data = await context.MenusDDN
            .FromSqlRaw("EXEC [dbo].[SP_Menu_GetAllDDN]")
            .ToListAsync();

        return Ok(data);
      }
      catch (Exception ex)
      {
        return StatusCode(500, ex.Message);
      }
    }



  }
}
