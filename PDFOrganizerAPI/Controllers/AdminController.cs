using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Microsoft.Data.SqlClient;
using PDFOrganizerAPI.Models;
using Microsoft.EntityFrameworkCore;
using System.Data;
using PDFOrganizerAPI.Filters;
using PDFOrganizerAPI.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Mail;
using System.Net;
using Newtonsoft.Json;



namespace PDFOrganizerAPI.Controllers
{
  [HttpRequestFilter]
  [ApiController]
    public class AdminController : ControllerBase
    {


        private readonly ITokenService _userinfoservice;
        internal readonly IConfiguration _config;
        readonly ESGContext context = new ESGContext();
    public AdminController(ITokenService UserInfoService, IConfiguration configuration)
    {
            _config = configuration;
            _userinfoservice = UserInfoService;
     }

    bool isSuccess = false;
    string message = string.Empty;
    object? jsonData;





    [HttpGet]
    [Route("api/GetAllEmployeeAccess")]
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





    [HttpPost]
    [Route("api/SaveEmployeeAccess")]
    public async Task<IActionResult> PostUser([FromBody] Json input)
    {
      try
      {
        var param = new SqlParameter("@JsonInput", input.JsonInput);
        int? userpsnum = _userinfoservice.GetUserPSNumberFromToken();
        var User = new SqlParameter("@UserId", userpsnum);
        var spoutput = new SqlParameter("@Result", SqlDbType.NVarChar, -1){Direction = ParameterDirection.Output}; 
        var result = await context.Database.ExecuteSqlRawAsync("EXEC [dbo].[sp_Employee_Save] @JsonInput,@UserId, @Result OUTPUT", param,User, spoutput);
        var resultMessage = spoutput.Value?.ToString() ?? "Log saved.";
        return Ok(new { message = resultMessage,status="success" });
      }
      catch (Exception ex)
      {
        return StatusCode(500, ex.Message);
      }
    }




        public class ApproveRejectInput
        {
            public int action { get; set; }
            public List<int> ids { get; set; }
        }



        [HttpPost]
        [Route("api/EmployeeApproveReject")]
        public async Task<IActionResult> EmployeeApproveReject([FromBody] Json input)
        {
            try
            {
                string resultMessage;

                var inputData = JsonConvert.DeserializeObject<ApproveRejectInput>(input.JsonInput);

                if (inputData == null || inputData.ids == null || !inputData.ids.Any())
                    return BadRequest("Invalid input data.");

                var users = new List<string>();

                using var connection = new SqlConnection(context.Database.GetConnectionString());
                await connection.OpenAsync();

         
                using (var cmd = connection.CreateCommand())
                {
                    var paramNames = inputData.ids
                        .Select((id, index) => $"@id{index}")
                        .ToArray();

                    cmd.CommandText = $@"
SELECT EA_Email FROM EmployeeAccess WHERE EA_Id IN ({string.Join(",", paramNames)})";

                    for (int i = 0; i < inputData.ids.Count; i++)
                        cmd.Parameters.Add(new SqlParameter(paramNames[i], inputData.ids[i]));

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var email = reader["EA_Email"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(email))
                            users.Add(email);
                    }
                }


                // CHECK IF NO USERS FOUND
                if (!users.Any())
                {
                    return Ok(new
                    {
                        message = "No  users found for the selected records.",
                        status = "success"
                    });
                }


                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "EXEC [dbo].[sp_Employee_ApproveReject] @JsonInput, @UserId, @Result OUTPUT";
                    cmd.Parameters.Add(new SqlParameter("@JsonInput", input.JsonInput));
                    cmd.Parameters.Add(new SqlParameter("@UserId", _userinfoservice.GetUserPSNumberFromToken()));

                    var output = new SqlParameter("@Result", SqlDbType.NVarChar, -1)
                    {
                        Direction = ParameterDirection.Output
                    };

                    cmd.Parameters.Add(output);
                    await cmd.ExecuteNonQueryAsync();
                    resultMessage = output.Value?.ToString() ?? "Log saved.";
                }


                if (resultMessage == "Selected employees approved successfully.")
                {

                    string toEmail = string.Join(";", users);
                    //  string CC = _config.GetSection("AppSettings").GetSection("UserEmailCC").Value;
                    string CC = "pdforganizer@lntcon.onmicrosoft.com";
                    string BCC = _config.GetSection("AppSettings").GetSection("UserEmailBCC").Value;
                    var mailformat = CreateUserMailFormat("Approved");
                    var subject = "Access Approval Request | PDF Organizer";
                    var sendemail = EmailTrigger(subject, mailformat, CC, BCC, toEmail);
                }

                else if (resultMessage == "Selected employees rejected successfully.")
                {
                    string toEmail = string.Join(";", "");
                    string CC = "pdforganizer@lntcon.onmicrosoft.com";
                    string BCC = _config.GetSection("AppSettings").GetSection("UserEmailBCC").Value;
                    var mailformat = CreateUserMailFormat("Rejected");
                    var subject = "Access Approval Request | PDF Organizer";
                    var sendemail = EmailTrigger(subject, mailformat, CC, BCC, toEmail);

                }
                
                return Ok(new { message = resultMessage, status = "success" });

            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }







    [HttpGet]
    [Route("api/GetAllMenu")]
    public async Task<IActionResult> GetAllMenuList()
    {
      try
      {
        var data = await context.Menus.FromSqlRaw("EXEC [dbo].[SP_Menu_GetAll]").ToListAsync();
        return Ok(data);
      }
      catch (Exception ex)
      {
        return StatusCode(500, ex.Message);
      }
    }





    [HttpGet]
    [Route("api/GetMenuByUidUser")]
    public async Task<IActionResult> GetMenusByUid()
    {
      try
      {
        int? userpsnum = _userinfoservice.GetUserPSNumberFromToken();
        var User = new SqlParameter("@UserId", userpsnum);
        var data = await context.MenusByUid.FromSqlRaw("EXEC [dbo].[SP_Menu_GetByUid] @UserId",User).ToListAsync();
        return Ok(data);
      }
      catch (Exception ex)
      {
        return StatusCode(500, ex.Message);
      }
    }






    [HttpGet]
    [Route("api/GetMenuByUidAdmin")]
    public async Task<IActionResult> GetMenusByUidAdmin()
    {
      try
      {
        int? userpsnum = _userinfoservice.GetUserPSNumberFromToken();
        var User = new SqlParameter("@UserId", userpsnum);
        var data = await context.MenusByUid.FromSqlRaw("EXEC [dbo].[SP_Menu_GetByUidAdmin] @UserId", User).ToListAsync();
        return Ok(data);
      }
      catch (Exception ex)
      {
        return StatusCode(500, ex.Message);
      }
    }





    [HttpPost]
    [Route("api/SaveMenu")]
    public async Task<IActionResult> MenuSave([FromBody] Json input)
    {
      try
      {
        
        var param = new SqlParameter("@JsonInput", input.JsonInput);
        int? userpsnum = _userinfoservice.GetUserPSNumberFromToken();
        var User = new SqlParameter("@UserId", userpsnum);
        var spoutput = new SqlParameter("@Result", SqlDbType.NVarChar, -1) { Direction = ParameterDirection.Output };
        var result = await context.Database.ExecuteSqlRawAsync("EXEC [dbo].[SP_Menu_Save] @JsonInput,@UserId, @Result OUTPUT", param, User, spoutput);
        var resultMessage = spoutput.Value?.ToString() ?? "Log saved.";
        return Ok(new { message = resultMessage, status = "success" });
      }
      catch (Exception ex)
      {
        return StatusCode(500, ex.Message);
      }
    }





    [HttpPost]
    [Route("api/DeleteMenu")]
    public async Task<IActionResult> DeleteMenu([FromBody] Json input)
    {
      try
      {
        var param = new SqlParameter("@JsonInput", input.JsonInput);
        int? userpsnum = _userinfoservice.GetUserPSNumberFromToken();
        var User = new SqlParameter("@UserId", userpsnum);
        var spoutput = new SqlParameter("@Result", SqlDbType.NVarChar, -1) { Direction = ParameterDirection.Output };
        var result = await context.Database.ExecuteSqlRawAsync("EXEC [dbo].[SP_MenuMaster_Delete] @JsonInput,@UserId, @Result OUTPUT", param, User, spoutput);
        var resultMessage = spoutput.Value?.ToString() ?? "Log saved.";
        return Ok(new { message = resultMessage, status = "success" });
      }
      catch (Exception ex)
      {
        return StatusCode(500, ex.Message);
      }
    }





    [HttpGet]
    [Route("api/GetAllRoles")]
    public async Task<IActionResult> GetAllRoles()
    {
      try
      {
        var data = await context.Roles
            .FromSqlRaw("EXEC [dbo].[SP_RoleMaster_GetAll]")
            .ToListAsync();

        return Ok(data);
      }
      catch (Exception ex)
      {
        return StatusCode(500, ex.Message);
      }
    }




    [HttpPost]
    [Route("api/SaveRole")]
    public async Task<IActionResult> SaveRole([FromBody] Json input)
    {
      try
      {
        var param = new SqlParameter("@JsonInput", input.JsonInput);
        int? userpsnum = _userinfoservice.GetUserPSNumberFromToken();
        var User = new SqlParameter("@UserId", userpsnum);
        var spoutput = new SqlParameter("@Result", SqlDbType.NVarChar, -1) { Direction = ParameterDirection.Output };
        var result = await context.Database.ExecuteSqlRawAsync("EXEC [dbo].[SP_RoleMaster_Save] @JsonInput,@UserId, @Result OUTPUT", param, User, spoutput);
        var resultMessage = spoutput.Value?.ToString() ?? "Log saved.";
        return Ok(new { message = resultMessage, status = "success" });
      }
      catch (Exception ex)
      {
        return StatusCode(500, ex.Message);
      }
    }





    [HttpPost]
    [Route("api/DeleteRoleMaster")]
    public async Task<IActionResult> DeleteRoleMaster([FromBody] Json input)
    {
      try
      { 
        var param = new SqlParameter("@JsonInput", input.JsonInput);
        int? userpsnum = _userinfoservice.GetUserPSNumberFromToken();
        var User = new SqlParameter("@UserId", userpsnum);
        var spoutput = new SqlParameter("@Result", SqlDbType.NVarChar, -1) { Direction = ParameterDirection.Output };
        var result = await context.Database.ExecuteSqlRawAsync("EXEC [dbo].[SP_RoleMaster_Delete] @JsonInput,@UserId, @Result OUTPUT", param, User, spoutput);
        var resultMessage = spoutput.Value?.ToString() ?? "Log saved.";
        return Ok(new { message = resultMessage, status = "success" });
      }
      catch (Exception ex)
      {
        return StatusCode(500, ex.Message);
      }
    }




    [HttpGet]
    [Route("api/GetAllUsersRole")]
    public async Task<IActionResult> GetAllUsersRole()
    {
      try
      {
        var data = await context.UserRoles
            .FromSqlRaw("EXEC [dbo].[SP_User_Roles_GetAll]")
            .ToListAsync();

        return Ok(data);
      }
      catch (Exception ex)
      {
        return StatusCode(500, ex.Message);
      }
    }




    [HttpGet]
    [Route("api/GetUserRole")]
    public async Task<IActionResult> GetUserRole()
    {
      try
      {
        var data = await context.UserRoles
            .FromSqlRaw("EXEC [dbo].[SP_User_Roles_GetAll]")
            .ToListAsync();

        return Ok(data);
      }
      catch (Exception ex)
      {
        return StatusCode(500, ex.Message);
      }
    }





    [HttpPost]
    [Route("api/GetUserRoleByUid")]
    public async Task<IActionResult> GetUserRoleByUid()
    {
      try
      {
        int? userpsnum = _userinfoservice.GetUserPSNumberFromToken();
        var User = new SqlParameter("@UserId", userpsnum);
        var spoutput = new SqlParameter("@Result", SqlDbType.NVarChar, -1) { Direction = ParameterDirection.Output };
        var result = await context.Database.ExecuteSqlRawAsync("EXEC [dbo].[SP_User_Roles_GetByUid] @UserId, @Result OUTPUT",  User, spoutput);
        var userRoles = spoutput.Value?.ToString() ?? "Log saved.";
        return Ok(new { userRoles });
      }
      catch (Exception ex)
      {
        return StatusCode(500, ex.Message);
      }
    }





    [HttpPost]
    [Route("api/SaveUserRole")]
    public async Task<IActionResult> SaveUserRole([FromBody] Json input)
    {
      try
      {
        var param = new SqlParameter("@JsonInput", input.JsonInput);
        int? userpsnum = _userinfoservice.GetUserPSNumberFromToken();
        var User = new SqlParameter("@UserId", userpsnum);
        var spoutput = new SqlParameter("@Result", SqlDbType.NVarChar, -1) { Direction = ParameterDirection.Output };
        var result = await context.Database.ExecuteSqlRawAsync("EXEC [dbo].[SP_User_Roles_Save] @JsonInput,@UserId, @Result OUTPUT", param, User, spoutput);
        var resultMessage = spoutput.Value?.ToString() ?? "Log saved.";
        return Ok(new { message = resultMessage, status = "success" });
      }
      catch (Exception ex)
      {
        return StatusCode(500, ex.Message);
      }
    }





    [HttpPost]
    [Route("api/DeleteUserRole")]
    public async Task<IActionResult> DeleteUserRole([FromBody] Json input)
    {
      try
      {
        var param = new SqlParameter("@JsonInput", input.JsonInput);
        int? userpsnum = _userinfoservice.GetUserPSNumberFromToken();
        var User = new SqlParameter("@UserId", userpsnum);
        var spoutput = new SqlParameter("@Result", SqlDbType.NVarChar, -1) { Direction = ParameterDirection.Output };
        var result = await context.Database.ExecuteSqlRawAsync("EXEC [dbo].[SP_User_Roles_Delete] @JsonInput,@UserId, @Result OUTPUT", param, User, spoutput);
        var resultMessage = spoutput.Value?.ToString() ?? "Log saved.";
        return Ok(new { message = resultMessage, status = "success" });
      }
      catch (Exception ex)
      {
        return StatusCode(500, ex.Message);
      }
    }





    [HttpPost]
    [Route("api/GetRoleMenu")]
    public async Task<IActionResult> GetRoleMenu([FromBody] Json input)
    {
      try
      {
        var param = new SqlParameter("@JsonInput", input.JsonInput);
       // int? userpsnum = _userinfoservice.GetUserPSNumberFromToken();
       // var User = new SqlParameter("@UserId", userpsnum);
        var spoutput = new SqlParameter("@Result", SqlDbType.NVarChar, -1) { Direction = ParameterDirection.Output };
        var result = await context.Database.ExecuteSqlRawAsync("EXEC [dbo].[SP_RoleMenu_Get] @JsonInput , @Result OUTPUT", param,  spoutput);
        var userRoles = spoutput.Value?.ToString() ?? "Log saved.";
        return Ok(new { userRoles });
      }
      catch (Exception ex)
      {
        return StatusCode(500, ex.Message);
      }
    }





    [HttpPost]
    [Route("api/SaveRoleMenu")]
    public async Task<IActionResult> SaveRoleMenu([FromBody] Json input)
    {
      try
      {
        var param = new SqlParameter("@JsonInput", input.JsonInput);
        int? userpsnum = _userinfoservice.GetUserPSNumberFromToken();
         var User = new SqlParameter("@UserId", userpsnum);
        var spoutput = new SqlParameter("@Result", SqlDbType.NVarChar, -1) { Direction = ParameterDirection.Output };
        var result = await context.Database.ExecuteSqlRawAsync("EXEC [dbo].[SP_RoleMenu_Save] @JsonInput ,@UserId, @Result OUTPUT", param,User, spoutput);
        var output = spoutput.Value?.ToString() ?? "Log saved.";
        return Ok(new { output });
      }
      catch (Exception ex)
      {
        return StatusCode(500, ex.Message);
      }
    }





    [HttpPost]
    [Route("api/GetUserActionLog")]
    public async Task<IActionResult> GetUserActionLogByDate([FromBody] Json input)
    {
      try
      {
        var param = new SqlParameter("@JsonInput", input.JsonInput);
        var spoutput = new SqlParameter("@Result", SqlDbType.NVarChar, -1) { Direction = ParameterDirection.Output };
        var result = await context.Database.ExecuteSqlRawAsync("EXEC [dbo].[SP_User_Action_Log_GetByDate] @JsonInput, @Result OUTPUT", param, spoutput);
        var data = spoutput.Value?.ToString() ?? "Log saved.";
        return Ok(new { data });
      }
      catch (Exception ex)
      {
        return StatusCode(500, ex.Message);
      }
    }




    [HttpPost]
    [Route("api/GetUserLoginLog")]
    public async Task<IActionResult> GetUserLoginHistory([FromBody] Json input)
    {
      try
      {
        var param = new SqlParameter("@JsonInput", input.JsonInput);
        var spoutput = new SqlParameter("@Result", SqlDbType.NVarChar, -1) { Direction = ParameterDirection.Output };
        var result = await context.Database.ExecuteSqlRawAsync("EXEC [dbo].[SP_Login_History_GetByDate] @JsonInput, @Result OUTPUT", param, spoutput);
        var data = spoutput.Value?.ToString() ?? "Log saved.";
        return Ok(new { data });
      }
      catch (Exception ex)
      {
        return StatusCode(500, ex.Message);
      }
    }





        private string CreateUserMailFormat(string data)
        {
         
            return $@"

<body style=""margin:0; padding:0; background-color:#eef2f7; font-family:'Segoe UI', Arial, sans-serif;"">

<table width=""100%"" cellpadding=""0"" cellspacing=""0"" >
  <tr>
    <td align=""center"">

      <!-- Container -->
      <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#ffffff; border-radius:10px; overflow:hidden; border:1px solid #e5e7eb;"">

      
        <!-- Body -->
        <tr>
          <td style=""padding:25px 40px; color:#374151; font-size:15px; line-height:1.7;"">
            <p>Dear Sir/Ma'am,</p>

            <p>
                Your access request for the <strong>PDF Organizer</strong> application has been <strong style=""color:#16a34a;"">{data}</strong>.
            </p>


            <p style=""margin-top:20px;"">
              If you need any assistance, please feel free to reach out.
            </p>
            
            <p>
             <i> This is a system-generated notification. No reply is required.</i>
            </p>    

            <p style=""margin-top:30px;"">
              Regards,<br>
              <strong>ISD WET IC</strong>
            </p>
          </td>
        </tr>

        <!-- Footer -->
        <tr>
          <td style=""background:#f9fafb; padding:18px 40px; text-align:center; font-size:12px; color:#9ca3af;"">
            © ISD Water & Effluent Treatment IC
          </td>
        </tr>

      </table>

    </td>
  </tr>
</table>
</body>

 
";
        }





        private string EmailTrigger(string subject, string mailformat, string ccMails, string bccMails, string toEmail)
        {
            try
            {
                MailMessage message = new MailMessage();
                string fromEmailAddress = "atswetadmin@lntecc.com";
                message.From = new MailAddress(fromEmailAddress, "Admin");
               // message.To.Add(new MailAddress(toEmail, "To whom mail has to go"));

                if (!string.IsNullOrEmpty(toEmail))
                {
                    var toAddresses = toEmail.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var email in toAddresses)
                    {
                        message.To.Add(new MailAddress(email.Trim()));
                    }
                }


                if (!string.IsNullOrEmpty(ccMails))
                {
                    var ccAddresses = ccMails.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var ccMail in ccAddresses)
                    {
                        message.CC.Add(new MailAddress(ccMail.Trim()));
                    }
                }


                if (!string.IsNullOrEmpty(bccMails))
                {
                    var bccAddresses = bccMails.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var bccMail in bccAddresses)
                    {
                        message.Bcc.Add(new MailAddress(bccMail.Trim()));
                    }
                }


                message.Body = mailformat;
                message.IsBodyHtml = true;
                message.Subject = subject;
                var resultObj = SendEmail(message);
                var mailresult = JsonConvert.SerializeObject(resultObj,
                    Formatting.Indented,
                    new JsonSerializerSettings() { ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore });
                var dictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(mailresult);

                if (dictionary["isSuccess"] == "true")
                {
                    return "Mail Sent Successfully";
                }
                else
                {
                    return dictionary["isSuccess"];
                }
            }
            catch (SmtpFailedRecipientsException ex)
            {
                return ex.Message;
            }
            catch (SmtpException smtpex)
            {
                return smtpex.Message;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }


        private async Task<object> SendEmail(MailMessage message)
        {
            object jsonData = null;
            string msg = string.Empty;
            bool isSuccess;
            try
            {
                System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                using (SmtpClient smtp = new SmtpClient())
                {
                    string fromEmailAddress = _config.GetSection("AppSettings").GetSection("Email").Value;
                    string FrmPassword = _config.GetSection("AppSettings").GetSection("EmailPassword").Value;
                    smtp.EnableSsl = false;
                    smtp.DeliveryMethod = SmtpDeliveryMethod.Network;
                    smtp.Host = _config.GetSection("AppSettings").GetSection("MailServer").Value;
                    smtp.Port = Convert.ToInt32(_config.GetSection("AppSettings").GetSection("Port").Value);
                    smtp.UseDefaultCredentials = false;
                    NetworkCredential NetworkCred = new NetworkCredential { UserName = fromEmailAddress, Password = FrmPassword };
                    smtp.Credentials = NetworkCred;
                    await smtp.SendMailAsync(message);
                    jsonData = new { isSuccess = true, msg = "Mail sent Successfully" };

                }
            }
            catch (SmtpFailedRecipientsException ex)
            {
                //_logger.LogError(ex + "error1", "An error occurred SmtpFailedRecipientsException");
                jsonData = new { isSuccess = false, msg = ex.Message };

            }
            catch (SmtpException smtpex)
            {
                //_logger.LogError(smtpex + "error2", "An error occurred SmtpException");
                jsonData = new { isSuccess = false, msg = smtpex.Message };
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex + "error3", "An error occurred Exception");
                jsonData = new { isSuccess = false, msg = ex.Message };
            }
            return jsonData;
        }




    }
}
