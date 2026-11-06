using PDFOrganizerAPI.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using PDFOrganizerAPI.Filters;

using Microsoft.Data.SqlClient; 


namespace PDFOrganizerAPI.Controllers
{
    [HttpRequestFilter]
    public class ReportsController : Controller
    {
        readonly ESGContext context = new ESGContext();
        string message = string.Empty;

        [HttpGet]
        [Route("api/GetCountUserLogData")]
        public async Task<IActionResult> GetCountUserLogData()
        {
            try
            {
                var p0 = new SqlParameter("@JSONResult", System.Data.SqlDbType.NVarChar, -1)
                {
                    Direction = System.Data.ParameterDirection.Output
                };

                await context.Database.ExecuteSqlRawAsync("EXEC dbo.sp_GetDataCounts @JSONResult OUTPUT", p0);

                var jsonResult = p0.Value?.ToString();

                return Ok(jsonResult);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

    

        [HttpGet]
        [Route("api/GetToolUsageCounts")]
        public async Task<IActionResult> GetToolUsageCounts()
        {
            try
            {
                var p0 = new SqlParameter("@JSONResult", System.Data.SqlDbType.NVarChar, -1)
                {
                    Direction = System.Data.ParameterDirection.Output
                };

               await context.Database.ExecuteSqlRawAsync("EXEC dbo.[sp_GetToolUsageCount] @JSONResult OUTPUT", p0);

                var jsonResult = p0.Value;

                return Ok(jsonResult);
    }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message
});
            }
        }


        [HttpGet]
        [Route("api/GetUserActLog7days")]
        public async Task<JsonResult> GetUserActLog7days()
        {
            try
            {
                var p0 = new SqlParameter("@JSONResult", System.Data.SqlDbType.NVarChar, -1) { Direction = System.Data.ParameterDirection.Output};
                await context.Database.ExecuteSqlRawAsync($"EXE [sp_Get_UserActLog_7days] @JSONResult OUTPUT", p0);
                return Json(p0.Value.ToString());
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Json(message);

            }

        }



}
}
