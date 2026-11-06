using PDFOrganizerAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using PDFOrganizerAPI.Services;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System.Security.Cryptography;
using System.Data;
using System.Net;
using Microsoft.AspNetCore.Mvc.Filters;
using System.IdentityModel.Tokens.Jwt;
using PDFOrganizerAPI.Filters;



namespace PDFOrganizerAPI.Controllers
{
    [HttpRequestFilter]
    public class EnergyDashboardController : Controller
    {

        readonly ESGContext context = new ESGContext();

        bool isSuccess = false;
        string message = string.Empty;
        object? jsonData;

        internal IEnumerable<Userlist> GetAllUser()
        {
            return context.EmployeeAccess.FromSqlRaw("EXEC dbo.[sp_GetUser_List_Grid_List]").AsEnumerable();
        }


        internal string GetTokenByEmail(string email)
        {
            var token = context.Database.SqlQuery<string>($"select PLH_Generated_Token from dbo.PDF_Login_History where PLH_Email_ID ={email}").AsEnumerable().FirstOrDefault();
            return token;
        }

        public string? GetUserEmailFromToken()
        {
            string? token = HttpContext.Request.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrEmpty(token) || token == "null")
            {
                return null; // No token found or it's "null" string
            }
            // Remove the "Bearer " prefix from token
            token = token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? token.Substring(7) : token;

            // Parse the JWT token
            var handler = new JwtSecurityTokenHandler();
            var jwtSecurityToken = handler.ReadJwtToken(token);

            // Extract the email claim ("upn" is often used for user principal name)
            var emailClaim = jwtSecurityToken.Claims.FirstOrDefault(x => x.Type == "upn")?.Value;

            if (string.IsNullOrEmpty(emailClaim))
            {
                return null; // No email claim found
            }
            return emailClaim;
        }




        [HttpPost]
        [Route("api/lock")]
        public async Task<IActionResult> LockPdf([FromForm] IFormFile file, [FromForm] string password)
        {
            if (file == null || file.Length == 0 || string.IsNullOrEmpty(password))
                return BadRequest("File and password required.");

            //Decrypt passwrod
            var decryptPass = EncryptionDecryption.DecryptString(password);


            //Validated File
            var expectedTypes = new List<string> { "pdf" };
            bool isValid = await FileValidateService.ValidateFileTypeAsync(file, expectedTypes);

            if (!isValid)
                return BadRequest("Invalid file type or size.");


            using var inputStream = file.OpenReadStream();
            var tempPath = Path.GetTempFileName();

            using (var fs = new FileStream(tempPath, FileMode.Create))
                await inputStream.CopyToAsync(fs);

            PdfDocument document = PdfReader.Open(tempPath, PdfDocumentOpenMode.Modify);
            //document.SecuritySettings.UserPassword = password;
            document.SecuritySettings.UserPassword = decryptPass;

            document.SecuritySettings.OwnerPassword = GenerateRandomPassword();
            document.SecuritySettings.PermitPrint = false;
            document.SecuritySettings.PermitModifyDocument = false;
            document.SecuritySettings.PermitExtractContent = false;

            using var output = new MemoryStream();
            document.Save(output, false);
            output.Position = 0;

            System.IO.File.Delete(tempPath); // Clean up

            return File(output.ToArray(), "application/pdf", "locked.pdf");
        }



        private string GenerateRandomPassword()
        {
            var bytes = new byte[8];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes);
        }



        [HttpPost]
        [Route("api/PostUserActionLog")]
        public async Task<IActionResult> PostUserActionLog([FromBody] PdfUserActionLog log)
        {

            if (log == null)
            {
                return BadRequest("Request body is null");
            }
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState); // Returns all validation errors

            }

            var emaill = GetUserEmailFromToken();

            //Decrypt passwrod
            //var decryptEmail = EncryptionDecryption.DecryptString(log.PUA_Email_ID

            try

            {
                var p0 = new SqlParameter("@PUA_Email_ID", emaill);
                var p1 = new SqlParameter("@PUA_IP", (Request.Headers["X-Forwarded-For"].FirstOrDefault() ?? HttpContext.Connection.RemoteIpAddress?.ToString()) ?? (object)DBNull.Value);
                //var p2 = new SqlParameter("@PUA_Action", log.PUA_Action ?? (object)DBNull.Value);
                var p2 = new SqlParameter("@PUA_Action", "Downloaded PDF");
                var p3 = new SqlParameter("@PUA_Tool", log.PUA_Tool ?? (object)DBNull.Value);
                //var p4 = new SqlParameter("@PUA_Timestamp", log.PUA_Timestamp);
                var p5 = new SqlParameter("@PUA_File_Name", string.IsNullOrEmpty(log.PUA_File_Name) ? (object)DBNull.Value : log.PUA_File_Name);
                var p6 = new SqlParameter("@PUA_File_Size", log.PUA_File_Size ?? (object)DBNull.Value);
                var p7 = new SqlParameter("@Result", SqlDbType.NVarChar, -1)
                {
                    Direction = ParameterDirection.Output
                };

                await context.Database.ExecuteSqlRawAsync(
                    "EXEC dbo.sp_InsertUserActionLog @PUA_Email_ID, @PUA_IP, @PUA_Action, @PUA_Tool, @PUA_File_Name, @PUA_File_Size, @Result OUTPUT",
                    p0, p1, p2, p3, p5, p6, p7
                );

                var resultMessage = p7.Value?.ToString() ?? "Log saved.";

                return Ok(new { message = resultMessage });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Internal server error", details = ex.Message });
            }
        }







    }

}
