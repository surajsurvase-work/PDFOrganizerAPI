using PDFOrganizerAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Data;

using Newtonsoft.Json;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;



namespace PDFOrganizerAPI.Controllers
{
    public class CommonController : Controller
    {
        readonly ESGContext context = new ESGContext();
        private readonly IHttpContextAccessor _httpContextAccessor;
        internal readonly IConfiguration _config;
        public CommonController(IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
        {
            _httpContextAccessor = httpContextAccessor;
            _config = configuration;

        }


        [HttpGet]
        [Route("api/LoginEntry")]
        public async Task<JsonResult> validateAccessToken()
        {
            try
            {


                string? Token = HttpContext.Request.Headers["Authorization"];

                if (Token != null && Token != "null")
                {
                    Token = Token.Substring(7);
                    var handler = new JwtSecurityTokenHandler();
                    var jwtSecurityToken = handler.ReadJwtToken(Token);
                    var EmailId = jwtSecurityToken.Claims.First(x => x.Type == "upn").Value;
                    DateTime iat = jwtSecurityToken.IssuedAt;
                    DateTime exp = jwtSecurityToken.ValidTo;
                    var p0 = new SqlParameter("@EmailId", EmailId);
                    var p1 = new SqlParameter("@Token", Token);
                    var p2 = new SqlParameter("@TokenGeneratedTime", TimeZoneInfo.ConvertTimeFromUtc(iat, TimeZoneInfo.Local));
                    var p3 = new SqlParameter("@LoginTime", TimeZoneInfo.ConvertTimeFromUtc(iat, TimeZoneInfo.Local));
                    var p4 = new SqlParameter("@LogoutTime", TimeZoneInfo.ConvertTimeFromUtc(exp, TimeZoneInfo.Local));
                    var p5 = new SqlParameter("@Result", System.Data.SqlDbType.NVarChar, -1) { Direction = System.Data.ParameterDirection.Output };
                    //await context.Database.ExecuteSqlRawAsync($"EXEC dbo.[Login_History_Entry]  @EmailId,@Token,@TokenGeneratedTime,@LoginTime,@LogoutTime,@Result OUTPUT", p0, p1, p2, p3, p4, p5);

                    await context.Database.ExecuteSqlRawAsync(
                     "EXEC dbo.[Login_History_Entry] @EmailId,@Token,@TokenGeneratedTime,@LoginTime,@LogoutTime,@Result OUTPUT",
                     p0, p1, p2, p3, p4, p5);


                    if (p5.Value.ToString() == "User Already Logged In")
                    {
                        return Json(p5.Value);
                    }
                    else
                    {
                        return Json(p5.Value.ToString());
                    }
                }
                else
                {
                    return Json("Empty Token");
                }
            }
            catch (Exception ex)
            {
                var Message = ex.Message;
                return Json(Message);
            }

        }


        [HttpPost]
        [Route("api/SaveEmployeeAccessSignUp")]
        public async Task<IActionResult> PostUser([FromBody] Json input)
        {
            try
            {
                if (IsTokenValid())
                {
                var param = new SqlParameter("@JsonInput", input.JsonInput);
                var spoutput = new SqlParameter("@Result", SqlDbType.NVarChar, -1) { Direction = ParameterDirection.Output };
                var result = await context.Database.ExecuteSqlRawAsync("EXEC [dbo].[sp_Employee_Save_SignUp] @JsonInput, @Result OUTPUT", param, spoutput);
                var resultMessage = spoutput.Value?.ToString() ?? "Log saved.";
                    
                    if(resultMessage == "Request already exist")
                    {
                        return Ok(new { message = resultMessage, status = "error" });

                    }
                    else if(resultMessage == "Request created Successfully")
                        {
                        var data = input.JsonInput;
                        dynamic dataEmail = JsonConvert.DeserializeObject(input.JsonInput);
                        string Email = dataEmail.eA_Email;
                        //string CC = _config.GetSection("AppSettings").GetSection("UserEmailCC").Value;
                        string CC = "pdforganizer@lntcon.onmicrosoft.com";
                        string BCC = _config.GetSection("AppSettings").GetSection("UserEmailBCC").Value;
                        var mailformat = CreateUserMailFormat(data);
                        var subject = " Access Approval Request | PDF Organizer";
                        var sendemail = EmailTrigger(subject, mailformat, CC, BCC, Email);
                        return Ok(new { message = resultMessage, status = "success" });

                    }
                    else
                    {
                        return Ok(new { message = resultMessage, status = "success" });

                    }
                }
                else
                {
                    return Ok(new { message = "Something went wrong", status = "error" });

                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }


        private string CreateUserMailFormat(string data)
        {
            var json = JObject.Parse(data);
            return $@"

<body style=""margin:0; padding:0;font-family:'Segoe UI', Arial, sans-serif;"">

<table width=""100%"" cellpadding=""0"" cellspacing=""0"" >
  <tr>
    <td align=""center"">

      <!-- Container -->
      <table width=""100%"" cellpadding=""0"" cellspacing=""0""
             style=""background:#ffffff; border-radius:10px; overflow:hidden;
                    box-shadow:0 12px 30px rgb(255, 241, 241);"">


        <!-- Content -->
        <tr>
          <td style=""padding:25px 40px; color:#374151; font-size:15px; line-height:1.7;"">
            <p>Dear Sir/Ma'am,</p>

            <p>
Your access request for the <strong>PDF Organizer</strong> application has been successfully submitted. You will receive an email confirmation once the request has been approved.               
            </p>

            <!-- Horizontal Grid -->
           <!-- Horizontal Grid -->
<table width=""100%"" cellpadding=""0"" cellspacing=""0""
       style=""margin:26px 0;
              border-collapse:collapse;
              border:1px solid #e5e7eb;
              border-radius:8px;
              overflow:hidden;
              font-family:'Segoe UI', Arial, sans-serif;"">

  <!-- Header Row -->
  <tr style=""background:#f3f4f6;"">
  <th style=""padding:12px;
               font-size:12px;
               font-weight:600;
               color:#6b7280;
               text-align:left;
               border-right:1px solid #e5e7eb;"">
      Employee IC
    </th>
    <th style=""padding:12px;
               font-size:12px;
               font-weight:600;
               color:#6b7280;
               text-align:left;
               border-right:1px solid #e5e7eb;"">
      Employee Name
    </th>
    <th style=""padding:12px;
               font-size:12px;
               font-weight:600;
               color:#6b7280;
               text-align:left;
               border-right:1px solid #e5e7eb;"">
      PS Number
    </th>
    <th style=""padding:12px;
               font-size:12px;
               font-weight:600;
               color:#6b7280;
               text-align:left;
               border-right:1px solid #e5e7eb;"">
      Email
    </th>
    <th style=""padding:12px;
               font-size:12px;
               font-weight:600;
               color:#6b7280;
               text-align:left;
               border-right:1px solid #e5e7eb;"">
      Job Code
    </th>
    <th style=""padding:12px;
               font-size:12px;
               font-weight:600;
               color:#6b7280;
               text-align:left;"">
      Job Description
    </th>
  </tr>

  <!-- Data Row -->
  <tr style=""background:#ffffff;"">
   <td style=""padding:14px;
               font-size:12px;
               font-weight:500;
               color:#111827;
               border-top:1px solid #e5e7eb;
               border-right:1px solid #e5e7eb;"">
      {json[""""eA_Ic""""]?.ToString()}
    </td>

    <td style=""padding:14px;
               font-size:12px;
               font-weight:500;
               color:#111827;
               border-top:1px solid #e5e7eb;
               border-right:1px solid #e5e7eb;"">
      {json[""""eA_Name""""]?.ToString()}
    </td>

    <td style=""padding:14px;
               font-size:12px;
               font-weight:500;
               color:#111827;
               border-top:1px solid #e5e7eb;
               border-right:1px solid #e5e7eb;"">
      {json[""""eA_PS_Number""""]?.ToString()}
    </td>

    <td style=""padding:14px;
               font-size:12px;
               font-weight:500;
               color:#111827;
               border-top:1px solid #e5e7eb;
               border-right:1px solid #e5e7eb;"">
      {json[""""eA_Email""""]?.ToString()}
    </td>

    <td style=""padding:14px;
               font-size:12px;
               font-weight:500;
               color:#111827;
               border-top:1px solid #e5e7eb;
               border-right:1px solid #e5e7eb;"">
     {json[""""eA_Job_Code""""]?.ToString()}
    </td>

    <td style=""padding:14px;
               font-size:12px;
               font-weight:500;
               color:#111827;
               border-top:1px solid #e5e7eb;"">
      {json[""""eA_Job_Desc""""]?.ToString()}
    </td>
  </tr>

</table>



            <p>
             <i> This is a system-generated notification. No reply is required.</i>
            </p>

            <p style=""margin-bottom:0;"">
              Regards,<br>
              <strong>ISD WET IC</strong>
            </p>
          </td>
        </tr>

        <!-- Footer -->
        <tr>
          <td style=""background:#f9fafb; padding:18px 40px; text-align:center;"">
            <p style=""margin:0; font-size:12px; color:#9ca3af;"">
            © ISD Water & Effluent Treatment IC
            </p>
          </td>
        </tr>

      </table>

    </td>
  </tr>
</table>
</body>
 
";
        }


        private string EmailTrigger(string subject, string mailformat, string ccMails, string bccMails, string ToEmail)
        {
            try
            {
                MailMessage message = new MailMessage();
                string fromEmailAddress = "atswetadmin@lntecc.com";
                message.From = new MailAddress(fromEmailAddress, "Admin");

                message.To.Add(new MailAddress(ToEmail, "To whom mail has to go"));

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
                    string FrmPassword =  _config.GetSection("AppSettings").GetSection("EmailPassword").Value;
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





        //-----------------------------------------------------
        // token validator time 

        public bool IsTokenValid()
        {
            var token = _httpContextAccessor.HttpContext?
                .Request.Headers["Authorization"]
                .FirstOrDefault();

            if (string.IsNullOrEmpty(token) || token == "null")
                return false;

            token = token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? token.Substring(7)
                : token;

            var handler = new JwtSecurityTokenHandler();

            if (!handler.CanReadToken(token))
                return false;

            var jwt = handler.ReadJwtToken(token);

            // Token expiration check
            return jwt.ValidTo > DateTime.UtcNow;
        }



    }
}
