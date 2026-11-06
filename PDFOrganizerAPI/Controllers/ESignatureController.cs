using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using PDFOrganizerAPI.Models;
using PDFOrganizerAPI.Services;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using PdfiumDoc = PdfiumViewer.PdfDocument;
using ITextPdfDocument = iText.Kernel.Pdf.PdfDocument;
using ITextPageSize = iText.Kernel.Geom.PageSize;
using DrawingImage = System.Drawing.Image;
using DrawingColor = System.Drawing.Color;
using PdfiumViewer;
using iText.IO.Image;
using iText.Kernel.Pdf;
using iText.Signatures;
using iText.Commons.Bouncycastle.Cert;
using iText.Bouncycastle.Crypto;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Crypto;
using iText.Bouncycastle.X509;
using System.Net.Mail;
using System.Net;


namespace PDFOrganizerAPI.Controllers
{
    
    [ApiController]
    public class ESignatureController : ControllerBase
    {


        private readonly ITokenService _userinfoservice;
        internal readonly IConfiguration _config;
        private readonly ESGContext context;

        public ESignatureController(
            ITokenService userInfoService,
            IConfiguration configuration,
            ESGContext contextDB)
        {
            _config = configuration;
            _userinfoservice = userInfoService;
            context = contextDB;
        }
        public class ESignRequestDto
        {
            public string? requestType { get; set; }
            public int totalPages { get; set; }
            public decimal zoomScale { get; set; }
            public List<SignatureDto>? signatures { get; set; }
        }


        public class SignatureDto
        {
            public long id { get; set; }
            public int level { get; set; }
            public string name { get; set; }
            public int page { get; set; }
            public decimal x { get; set; }
            public decimal y { get; set; }
            public decimal width { get; set; }
            public decimal height { get; set; }
        }

        public class PositionDto
        {
            public int X { get; set; }
            public int Y { get; set; }
        }

        public class SizeDto
        {
            public int Width { get; set; }
            public int Height { get; set; }
        }



        [HttpPost]
        [Route("api/ESign/SaveRequest")]
        public async Task<IActionResult> SaveRequest(
    [FromForm] IFormFile file,
    [FromForm] string requestData)
        {
            var mailTrace = new List<string>();
            string? tempFilePath = null;

            try
            {
                if (file == null || file.Length == 0)
                    return BadRequest("File not received");

                int? userpsnum = _userinfoservice.GetUserPSNumberFromToken();
                if (!userpsnum.HasValue || userpsnum.Value <= 0)
                    return Unauthorized("Invalid user token.");

                string rootFolder = _config.GetSection("AppSettings:ESignRootFolder").Value ?? @"D:\EsignatureAPP";
                if (!Directory.Exists(rootFolder))
                    Directory.CreateDirectory(rootFolder);

                string tempFolder = Path.Combine(rootFolder, "_temp");
                if (!Directory.Exists(tempFolder))
                    Directory.CreateDirectory(tempFolder);

                string tempExtension = GetSafeExtension(file.FileName);
                tempFilePath = Path.Combine(tempFolder, $"{Guid.NewGuid():N}{tempExtension}");

                using (var stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await file.CopyToAsync(stream);
                }

                var param = new SqlParameter("@JsonInput", requestData);
                var userParam = new SqlParameter("@UserId", userpsnum.Value);
                var fileParam = new SqlParameter("@RequestFile", tempFilePath);

                var spoutput = new SqlParameter("@Result", SqlDbType.NVarChar, -1)
                {
                    Direction = ParameterDirection.Output
                };

                await context.Database.ExecuteSqlRawAsync(
                    "EXEC SP_ESign_SaveRequest @JsonInput, @UserId, @RequestFile, @Result OUTPUT",
                    param, userParam, fileParam, spoutput
                );

                string resultMessage = spoutput.Value?.ToString() ?? "Request processed";
                string requestCode = ExtractRequestCode(resultMessage);

                string finalOriginalFilePath;
                string requestFolder = Path.Combine(rootFolder, GetRequestFolderName(requestCode));
                if (!Directory.Exists(requestFolder))
                    Directory.CreateDirectory(requestFolder);

                string originalFileName = Path.GetFileName(file.FileName);
                finalOriginalFilePath = Path.Combine(requestFolder, originalFileName);

                if (System.IO.File.Exists(finalOriginalFilePath))
                    System.IO.File.Delete(finalOriginalFilePath);

                System.IO.File.Move(tempFilePath, finalOriginalFilePath);
                tempFilePath = null;

                await UpdateRequestPathsAsync(requestCode, finalOriginalFilePath, null);

                if (resultMessage.Contains("saved", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        dynamic requestJson = JsonConvert.DeserializeObject(requestData);

                        string requestSubject = requestJson?.requestType?.ToString() ?? "eSignature Request";
                        string documentName = Path.GetFileName(finalOriginalFilePath);
                        string actionUrlBase = _config.GetSection("AppSettings:ESignActionUrl").Value ?? "";

                        string requesterName = "Requester";
                        string requesterEmail = "";

                        var requester = await context.EmployeeGridAccess
                            .AsNoTracking()
                            .FirstOrDefaultAsync(x => x.EA_PS_Number == userpsnum.Value);

                        if (requester != null)
                        {
                            requesterName = requester.EA_Name ?? "Requester";
                            requesterEmail = requester.EA_Email ?? "";
                        }

                        var participants = new List<EsignParticipantModel>();
                        var signatures = requestJson?.signatures;

                        if (signatures != null)
                        {
                            foreach (var signer in signatures)
                            {
                                string signerEmail = signer?.name?.ToString() ?? "";
                                int signerLevel = signer?.level != null ? (int)signer.level : 0;

                                if (string.IsNullOrWhiteSpace(signerEmail))
                                    continue;

                                string signerName = signerEmail;

                                var signerMaster = await context.EmployeeGridAccess
                                    .AsNoTracking()
                                    .FirstOrDefaultAsync(x => x.EA_Email == signerEmail);

                                if (signerMaster != null && !string.IsNullOrWhiteSpace(signerMaster.EA_Name))
                                    signerName = signerMaster.EA_Name;

                                participants.Add(new EsignParticipantModel
                                {
                                    Level = signerLevel,
                                    Name = signerName,
                                    Email = signerEmail,
                                    Status = "Pending"
                                });
                            }
                        }

                        string level1Emails = string.Join(";",
                            participants
                                .Where(x => x.Level == 1)
                                .Select(x => x.Email)
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .Distinct(StringComparer.OrdinalIgnoreCase));

                        if (!string.IsNullOrWhiteSpace(level1Emails))
                        {
                            var signerMailModel = new EsignEmailModel
                            {
                                RequestNumber = requestCode,
                                RequestSubject = requestSubject,
                                RequesterName = requesterName,
                                RequesterEmail = requesterEmail,
                                DocumentName = documentName,
                                RequestedOn = DateTime.Now,
                                ActionUrl = $"{actionUrlBase}?requestNo={Uri.EscapeDataString(requestCode)}",
                                Participants = participants
                                    .OrderBy(x => x.Level)
                                    .ThenBy(x => x.Name)
                                    .ToList()
                            };

                            string signerSubject = $"Signature Required | {requestCode} | {requestSubject}";
                            string signerBody = CreateLevelWiseSignerNotificationMailFormat(signerMailModel);

                            string cc = requesterEmail;
                            string bcc = _config.GetSection("AppSettings:UserEmailBCC").Value ?? "";

                            var signerMailResult = await EmailTriggerMultipleTo(
                                signerSubject,
                                signerBody,
                                cc,
                                bcc,
                                level1Emails
                            );

                            if (signerMailResult.StartsWith("SUCCESS", StringComparison.OrdinalIgnoreCase))
                            {
                                await UpdateRequestStatusAsync(requestCode, "InProcess");
                            }

                            await InsertAuditLog(requestCode, null, "Request Created", requesterName, requesterEmail, userpsnum, "Pending", "Request created successfully");
                            await InsertAuditLog(requestCode, 1, "Email Sent", "System", level1Emails, null, "Pending", "Mail sent to Level 1 signer(s)");

                            mailTrace.Add($"SaveRequest mail => TO:{level1Emails} CC:{cc} => {signerMailResult}");
                        }
                        else
                        {
                            mailTrace.Add("No level 1 signer found. Mail not sent.");
                        }
                    }
                    catch (Exception ex)
                    {
                        mailTrace.Add($"SaveRequest mail block exception => {ex.Message}");
                    }
                }

                return Ok(new
                {
                    message = resultMessage,
                    requestCode = requestCode,
                    filePath = finalOriginalFilePath,
                    status = "success",
                    mailTrace = mailTrace
                });
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(tempFilePath) && System.IO.File.Exists(tempFilePath))
                {
                    try { System.IO.File.Delete(tempFilePath); } catch { }
                }

                return StatusCode(500, ex.Message);
            }
        }
        public class EsignParticipantModel
        {
            public int Level { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Status { get; set; } = "Pending";
        }

        public class EsignEmailModel
        {
            public string RequestNumber { get; set; } = string.Empty;
            public string RequestSubject { get; set; } = string.Empty;
            public string RequesterName { get; set; } = string.Empty;
            public string RequesterEmail { get; set; } = string.Empty;
            public string DocumentName { get; set; } = string.Empty;
            public string ActionUrl { get; set; } = string.Empty;
            public string SignedPdfUrl { get; set; } = string.Empty;
            public DateTime RequestedOn { get; set; }
            public List<EsignParticipantModel> Participants { get; set; } = new();
        }

        private async Task<string> EmailTriggerMultipleTo(
    string subject,
    string mailformat,
    string ccMails,
    string bccMails,
    string toEmails,
    List<string>? attachmentPaths = null)
        {
            try
            {
                using MailMessage message = new MailMessage();

                string fromEmailAddress = _config.GetSection("AppSettings:Email").Value ?? "";
                string fromDisplayName = _config.GetSection("AppSettings:EmailDisplayName").Value ?? "eSignature Workflow";

                if (string.IsNullOrWhiteSpace(fromEmailAddress))
                    return "FAILED: AppSettings:Email is blank";

                var toList = GetValidEmailList(toEmails);
                var ccList = GetValidEmailList(ccMails);
                var bccList = GetValidEmailList(bccMails);

                if (!toList.Any())
                    return "FAILED: No valid To emails found";

                message.From = new MailAddress(fromEmailAddress, fromDisplayName);

                foreach (var to in toList)
                    message.To.Add(new MailAddress(to));

                foreach (var cc in ccList)
                    message.CC.Add(new MailAddress(cc));

                foreach (var bcc in bccList)
                    message.Bcc.Add(new MailAddress(bcc));

                if (attachmentPaths != null)
                {
                    foreach (var filePath in attachmentPaths
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (System.IO.File.Exists(filePath))
                            message.Attachments.Add(new Attachment(filePath));
                    }
                }

                message.Subject = subject;
                message.Body = mailformat;
                message.IsBodyHtml = true;

                var resultObj = await SendEmail(message);
                var mailresult = JsonConvert.SerializeObject(
                    resultObj,
                    Formatting.Indented,
                    new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore });

                var dictionary = JsonConvert.DeserializeObject<Dictionary<string, object>>(mailresult);

                if (dictionary != null &&
                    dictionary.ContainsKey("isSuccess") &&
                    dictionary["isSuccess"]?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return "SUCCESS: Mail Sent Successfully";
                }

                return dictionary != null && dictionary.ContainsKey("msg")
                    ? $"FAILED: {dictionary["msg"]?.ToString()}"
                    : "FAILED: Mail sending failed";
            }
            catch (Exception ex)
            {
                return $"FAILED: {ex.Message}";
            }
        }



        private bool CanAttachFile(string filePath, long maxSizeMb = 8)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
                return false;

            var fileInfo = new FileInfo(filePath);
            long maxBytes = maxSizeMb * 1024 * 1024;

            return fileInfo.Length > 0 && fileInfo.Length <= maxBytes;
        }

        private string CreateRequesterConfirmationMailFormat(EsignEmailModel model)
        {
            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8' />
    <title>eSignature Request Submitted</title>
</head>
<body style='margin:0; padding:0; background-color:#f4f6f8; font-family:Segoe UI, Arial, sans-serif; color:#1f2937;'>
    <table width='100%' cellpadding='0' cellspacing='0' border='0' style='background-color:#f4f6f8; padding:24px 0;'>
        <tr>
            <td align='center'>
                <table width='680' cellpadding='0' cellspacing='0' border='0' style='background:#ffffff; border:1px solid #e5e7eb; border-radius:10px; overflow:hidden;'>
                    <tr>
                        <td style='background:#1E458A; padding:22px 28px;'>
                            <div style='font-size:22px; font-weight:600; color:#ffffff;'>eSignature Request Submitted</div>
                            <div style='font-size:13px; color:#cbd5e1; margin-top:6px;'>Submission confirmation</div>
                        </td>
                    </tr>

                    <tr>
                        <td style='padding:30px 28px 18px 28px;'>
                            <p style='margin:0 0 14px 0; font-size:15px;'>Dear <strong>{System.Net.WebUtility.HtmlEncode(model.RequesterName)}</strong>,</p>

                            <p style='margin:0 0 18px 0; font-size:14px; line-height:1.7; color:#374151;'>
                                Your eSignature request has been submitted successfully and routed to the current signer level.
                            </p>

                            <table width='100%' cellpadding='0' cellspacing='0' border='0' style='border-collapse:collapse; margin:18px 0 22px 0;'>
                                <tr>
                                    <td colspan='2' style='font-size:15px; font-weight:600; color:#111827; padding-bottom:12px;'>Request Summary</td>
                                </tr>
                                <tr>
                                    <td style='width:180px; padding:10px 12px; border:1px solid #e5e7eb; background:#f9fafb; font-size:13px; font-weight:600;'>Reference</td>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>{System.Net.WebUtility.HtmlEncode(model.RequestNumber)}</td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; background:#f9fafb; font-size:13px; font-weight:600;'>Request Subject</td>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>{System.Net.WebUtility.HtmlEncode(model.RequestSubject)}</td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; background:#f9fafb; font-size:13px; font-weight:600;'>Document Name</td>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>{System.Net.WebUtility.HtmlEncode(model.DocumentName)}</td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; background:#f9fafb; font-size:13px; font-weight:600;'>Submitted On</td>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>{model.RequestedOn:dd-MMM-yyyy hh:mm tt}</td>
                                </tr>
                            </table>

                            <p style='margin:0; font-size:13px; line-height:1.7; color:#374151;'>
                                Regards,<br />
                                <strong>eSignature Workflow System</strong>
                            </p>
                        </td>
                    </tr>

                    <tr>
                        <td style='padding:16px 28px; background:#f9fafb; border-top:1px solid #e5e7eb;'>
                            <p style='margin:0; font-size:12px; color:#6b7280; line-height:1.6;'>
                                This is an automated email from the eSignature Workflow System. Please do not reply directly to this message.
                            </p>
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>";
        }


        private string CreateLevelWiseSignerNotificationMailFormat(EsignEmailModel model)
        {
            var participantRows = "";

            foreach (var p in model.Participants.OrderBy(x => x.Level).ThenBy(x => x.Name))
            {
                participantRows += $@"
<tr>
    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>Level {p.Level}</td>
    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>{System.Net.WebUtility.HtmlEncode(p.Name)}</td>
    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>{System.Net.WebUtility.HtmlEncode(p.Email)}</td>
    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>{System.Net.WebUtility.HtmlEncode(p.Status)}</td>
</tr>";
            }

            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8' />
    <title>eSignature Request</title>
</head>
<body style='margin:0; padding:0; background-color:#f4f6f8; font-family:Segoe UI, Arial, sans-serif; color:#1f2937;'>
    <table width='100%' cellpadding='0' cellspacing='0' border='0' style='background-color:#f4f6f8; padding:24px 0;'>
        <tr>
            <td align='center'>
                <table width='760' cellpadding='0' cellspacing='0' border='0' style='background:#ffffff; border:1px solid #e5e7eb; border-radius:10px; overflow:hidden;'>
                    <tr>
                        <td style='background:#1E458A; padding:22px 28px;'>
                            <div style='font-size:22px; font-weight:600; color:#ffffff;'>eSignature Request</div>
                            <div style='font-size:13px; color:#cbd5e1; margin-top:6px;'>Document workflow notification</div>
                        </td>
                    </tr>

                    <tr>
                        <td style='padding:30px 28px 18px 28px;'>
                            <p style='margin:0 0 14px 0; font-size:15px;'>Dear Sir/Ma'am,</p>

                            <p style='margin:0 0 14px 0; font-size:14px; line-height:1.7; color:#374151;'>
                                Please find the eSignature request details and workflow status below.
                            </p>

                            <table width='100%' cellpadding='0' cellspacing='0' border='0' style='border-collapse:collapse; margin:18px 0 22px 0;'>
                                <tr>
                                    <td colspan='2' style='font-size:15px; font-weight:600; color:#111827; padding-bottom:12px;'>Request Details</td>
                                </tr>
                                <tr>
                                    <td style='width:180px; padding:10px 12px; border:1px solid #e5e7eb; background:#f9fafb; font-size:13px; font-weight:600;'>Reference</td>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>{System.Net.WebUtility.HtmlEncode(model.RequestNumber)}</td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; background:#f9fafb; font-size:13px; font-weight:600;'>Request Subject</td>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>{System.Net.WebUtility.HtmlEncode(model.RequestSubject)}</td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; background:#f9fafb; font-size:13px; font-weight:600;'>Document Name</td>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>{System.Net.WebUtility.HtmlEncode(model.DocumentName)}</td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; background:#f9fafb; font-size:13px; font-weight:600;'>Requested By</td>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>{System.Net.WebUtility.HtmlEncode(model.RequesterName)} ({System.Net.WebUtility.HtmlEncode(model.RequesterEmail)})</td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; background:#f9fafb; font-size:13px; font-weight:600;'>Requested On</td>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>{model.RequestedOn:dd-MMM-yyyy hh:mm tt}</td>
                                </tr>
                            </table>

                            <div style='margin:0 0 20px 0;'>
                                <a href='{model.ActionUrl}'
                                   style='display:inline-block; background:#2563eb; color:#ffffff; text-decoration:none; padding:12px 22px; border-radius:6px; font-size:14px; font-weight:600;'>
                                    Open eSignature Request
                                </a>
                            </div>

                            <table width='100%' cellpadding='0' cellspacing='0' border='0' style='border-collapse:collapse; margin:18px 0 22px 0;'>
                                <tr>
                                    <td colspan='4' style='font-size:15px; font-weight:600; color:#111827; padding-bottom:12px;'>Signing Workflow</td>
                                </tr>
                                <tr>
                                    <th style='padding:10px 12px; border:1px solid #e5e7eb; background:#f3f4f6; font-size:12px; text-align:left;'>Level</th>
                                    <th style='padding:10px 12px; border:1px solid #e5e7eb; background:#f3f4f6; font-size:12px; text-align:left;'>Signer Name</th>
                                    <th style='padding:10px 12px; border:1px solid #e5e7eb; background:#f3f4f6; font-size:12px; text-align:left;'>Email</th>
                                    <th style='padding:10px 12px; border:1px solid #e5e7eb; background:#f3f4f6; font-size:12px; text-align:left;'>Status</th>
                                </tr>
                                {participantRows}
                            </table>

                            <p style='margin:0; font-size:13px; line-height:1.7; color:#374151;'>
                                Regards,<br />
                                <strong>eSignature Workflow System</strong>
                            </p>
                        </td>
                    </tr>

                    <tr>
                        <td style='padding:16px 28px; background:#f9fafb; border-top:1px solid #e5e7eb;'>
                            <p style='margin:0; font-size:12px; color:#6b7280; line-height:1.6;'>
                                This is an automated email from the eSignature Workflow System. Please do not reply directly to this message.
                            </p>
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>";
        }


        private async Task<string> EmailTrigger(
     string subject,
     string mailformat,
     string ccMails,
     string bccMails,
     string toEmail,
     string toDisplayName = "")
        {
            try
            {
                using MailMessage message = new MailMessage();

                string fromEmailAddress = _config.GetSection("AppSettings:Email").Value ?? "";
                string fromDisplayName = _config.GetSection("AppSettings:EmailDisplayName").Value ?? "eSignature Workflow";

                if (string.IsNullOrWhiteSpace(fromEmailAddress))
                    return "FAILED: AppSettings:Email is blank";

                if (string.IsNullOrWhiteSpace(toEmail))
                    return "FAILED: To email is blank";

                message.From = new MailAddress(fromEmailAddress, fromDisplayName);

                if (!string.IsNullOrWhiteSpace(toDisplayName))
                    message.To.Add(new MailAddress(toEmail, toDisplayName));
                else
                    message.To.Add(new MailAddress(toEmail));

                if (!string.IsNullOrWhiteSpace(ccMails))
                {
                    foreach (var ccMail in ccMails.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        message.CC.Add(new MailAddress(ccMail.Trim()));
                    }
                }

                if (!string.IsNullOrWhiteSpace(bccMails))
                {
                    foreach (var bccMail in bccMails.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        message.Bcc.Add(new MailAddress(bccMail.Trim()));
                    }
                }

                message.Subject = subject;
                message.Body = mailformat;
                message.IsBodyHtml = true;

                var resultObj = await SendEmail(message);
                var mailresult = JsonConvert.SerializeObject(
                    resultObj,
                    Formatting.Indented,
                    new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore });

                var dictionary = JsonConvert.DeserializeObject<Dictionary<string, object>>(mailresult);

                if (dictionary != null &&
                    dictionary.ContainsKey("isSuccess") &&
                    dictionary["isSuccess"]?.ToString()?.ToLower() == "true")
                {
                    return "SUCCESS: Mail Sent Successfully";
                }

                return dictionary != null && dictionary.ContainsKey("msg")
                    ? $"FAILED: {dictionary["msg"]?.ToString()}"
                    : "FAILED: Mail sending failed";
            }
            catch (Exception ex)
            {
                return $"FAILED: {ex.Message}";
            }
        }


        private async Task<object> SendEmail(MailMessage message)
        {
            object jsonData = null;

            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                using (SmtpClient smtp = new SmtpClient())
                {
                    string fromEmailAddress = _config.GetSection("AppSettings:Email").Value ?? "";
                    string frmPassword = _config.GetSection("AppSettings:EmailPassword").Value ?? "";
                    string host = _config.GetSection("AppSettings:MailServer").Value ?? "";
                    string portText = _config.GetSection("AppSettings:Port").Value ?? "";

                    if (string.IsNullOrWhiteSpace(fromEmailAddress))
                        return new { isSuccess = false, msg = "Config missing: AppSettings:Email" };

                    if (string.IsNullOrWhiteSpace(frmPassword))
                        return new { isSuccess = false, msg = "Config missing: AppSettings:EmailPassword" };

                    if (string.IsNullOrWhiteSpace(host))
                        return new { isSuccess = false, msg = "Config missing: AppSettings:MailServer" };

                    if (string.IsNullOrWhiteSpace(portText))
                        return new { isSuccess = false, msg = "Config missing: AppSettings:Port" };

                    if (!int.TryParse(portText, out int port))
                        return new { isSuccess = false, msg = $"Invalid port value: {portText}" };

                    smtp.EnableSsl = false;
                    smtp.DeliveryMethod = SmtpDeliveryMethod.Network;
                    smtp.Host = host;
                    smtp.Port = port;
                    smtp.UseDefaultCredentials = false;
                    smtp.Credentials = new NetworkCredential
                    {
                        UserName = fromEmailAddress,
                        Password = frmPassword
                    };

                    await smtp.SendMailAsync(message);

                    jsonData = new
                    {
                        isSuccess = true,
                        msg = $"Mail sent Successfully via host={host}, port={port}, to={string.Join(";", message.To.Select(x => x.Address))}"
                    };
                }
            }
            catch (SmtpFailedRecipientsException ex)
            {
                jsonData = new { isSuccess = false, msg = $"SmtpFailedRecipientsException: {ex.Message}" };
            }
            catch (SmtpException ex)
            {
                jsonData = new { isSuccess = false, msg = $"SmtpException: {ex.Message}" };
            }
            catch (Exception ex)
            {
                jsonData = new { isSuccess = false, msg = $"Exception: {ex.Message}" };
            }

            return jsonData;
        }

        [HttpPost]
        [Route("api/ESign/SaveRequest2")]
        public async Task<IActionResult> SaveReques2t(
    [FromForm] IFormFile file,
    [FromForm] string requestData)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return BadRequest("File not received");

                // ✅ 1️⃣ Set D Drive Path
                string uploadFolder = @"D:\EsignatureAPP";

                // Create folder if not exists
                if (!Directory.Exists(uploadFolder))
                    Directory.CreateDirectory(uploadFolder);

                // Unique file name
                var uniqueFileName = Guid.NewGuid().ToString() + "_" + file.FileName;
                var filePath = System.IO.Path.Combine(uploadFolder, uniqueFileName);

                //  Save File to D Drive
                using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                {
                    await file.CopyToAsync(stream);
                }

                // 3️⃣ Deserialize JSON
                //var input = JsonConvert.DeserializeObject<ESignRequestDto>(requestData);
                //var jsonString = JsonConvert.SerializeObject(input);

                //var param = new SqlParameter("@JsonInput", jsonString);

                var param = new SqlParameter("@JsonInput", requestData);
                int? userpsnum = _userinfoservice.GetUserPSNumberFromToken();
                var userParam = new SqlParameter("@UserId", userpsnum ?? 0);

                var fileParam = new SqlParameter("@RequestFile", filePath);

                var spoutput = new SqlParameter("@Result", SqlDbType.NVarChar, -1)
                {
                    Direction = ParameterDirection.Output
                };

                await context.Database.ExecuteSqlRawAsync(
                    "EXEC SP_ESign_SaveRequest @JsonInput, @UserId, @RequestFile, @Result OUTPUT",
                    param, userParam, fileParam, spoutput
                );

                return Ok(new
                {
                    message = spoutput.Value?.ToString(),
                    filePath = filePath,
                    status = "success"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }


        public class ESignatureResult
        {
            public int RequestId { get; set; }
            public string RequestCode { get; set; }

            public string RequestType { get; set; }
            public int TotalPages { get; set; }
            public decimal ZoomScale { get; set; }
            public int CreatedBy { get; set; }
            public DateTime CreatedOn { get; set; }

            public int SignatureId { get; set; }
            public int LevelNo { get; set; }
            public string SignerName { get; set; }
            public int PageNumber { get; set; }
            public decimal PositionX { get; set; }
            public decimal PositionY { get; set; }
            public decimal BoxWidth { get; set; }
            public decimal BoxHeight { get; set; }
        }

        public class GetBySignerRequestDto
        {
            public string? SignerName { get; set; }
            public int RequestId { get; set; }
        }

        [HttpPost]
        [Route("api/ESign/GetBySignerName")]
        public async Task<IActionResult> GetBySignerName([FromBody] GetBySignerRequestDto input)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(input?.SignerName))
                    return BadRequest("SignerName is required.");

                if (input.RequestId <= 0)
                    return BadRequest("Valid RequestId is required.");

                var signerParam = new SqlParameter("@SignerName", input.SignerName);
                var requestParam = new SqlParameter("@RequestId", input.RequestId);

                var result = await context
                    .ESignatureResults
                    .FromSqlRaw(
                        "EXEC SP_ESign_GetBySignerName @SignerName, @RequestId",
                        signerParam, requestParam)
                    .ToListAsync();

                return Ok(new
                {
                    status = "success",
                    count = result.Count,
                    data = result
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }




        public class SubmittedRequestDto
        {
            public string RequestCode { get; set; } = string.Empty;
            public string RequestType { get; set; } = string.Empty;
            public int? TotalPages { get; set; }
            public decimal? ZoomScale { get; set; }
            public string FilePath { get; set; } = string.Empty;
            public string? SignedFilePath { get; set; }
            public string CreatedBy { get; set; } = string.Empty;
            public DateTime? CreatedOn { get; set; }
            public int ApprovedCount { get; set; }
            public int TotalSignerCount { get; set; }
            public string Siganture_StatusCount { get; set; } = string.Empty;
            public string Siganture_Status { get; set; } = string.Empty;

            public string? RequestStatus { get; set; }
            public int? CancelledBy { get; set; }
            public DateTime? CancelledOn { get; set; }
            public string? CancelReason { get; set; }
        }

        public class RequestDetailsDto
        {
            public int RequestId { get; set; }
            public string RequestCode { get; set; }  
            public string? RequestType { get; set; }
            public int? TotalPages { get; set; }
            public decimal? ZoomScale { get; set; }
            public int CreatedBy { get; set; }
            public DateTime CreatedOn { get; set; }
            public int? SignatureId { get; set; }
            public int? LevelNo { get; set; }
            public string? SignerName { get; set; }
            public string? SignerFullName { get; set; }
            public int? PageNumber { get; set; }
            public decimal? PositionX { get; set; }
            public decimal? PositionY { get; set; }
            public decimal? BoxWidth { get; set; }
            public decimal? BoxHeight { get; set; }
            public string? ES_Signer_Siganture { get; set; }
            public string? SignedFilePath { get; set; }
            public DateTime? ES_Siganture_On { get; set; }
            public string ES_Siganture_Status { get; set; }


        }

        [HttpGet]
        [Route("api/ESign/GetSubmittedRequests")]
        public async Task<IActionResult> GetSubmittedRequests()
        {
            int? userpsnum = _userinfoservice.GetUserPSNumberFromToken();

            var result = await context.SubmittedRequests
                .FromSqlRaw("EXEC SP_ESign_GetSubmittedRequests @p0", userpsnum)
                .ToListAsync();

            return Ok(result);
        }


        [HttpGet]
        [Route("api/ESign/GetPendingWorkflowRequests")]
        public async Task<IActionResult> GetPendingWorkflowRequests()
        {
            try
            {
                int? userpsnum = _userinfoservice.GetUserPSNumberFromToken();

                var result = await context.SubmittedRequests
                    .FromSqlRaw("EXEC SP_ESign_GetPendingWorkflowRequests @p0", userpsnum)
                    .ToListAsync();

                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet]
        [Route("api/ESign/GetApprovedWorkflowRequests")]
        public async Task<IActionResult> GetApprovedWorkflowRequests()
        {
            try
            {
                int? userpsnum = _userinfoservice.GetUserPSNumberFromToken();

                var result = await context.SubmittedRequests
                    .FromSqlRaw("EXEC SP_ESign_GetApprovedWorkflowRequests @p0", userpsnum)
                    .ToListAsync();

                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }



        [HttpGet]
        [Route("api/ESign/GetRequestDetails/{requestCode}")]
        public async Task<IActionResult> GetRequestDetails(string requestCode)
        {
            requestCode = Uri.UnescapeDataString(requestCode);

            var param = new SqlParameter("@RequestCode", requestCode);

            var result = await context.RequestDetails
                .FromSqlRaw("EXEC SP_ESign_GetRequestDetails @RequestCode", param)
                .ToListAsync();

            return Ok(result);
        }





        [HttpGet]
        [Route("api/ESign/ViewFile/{requestCode}")]
        public async Task<IActionResult> ViewFile(string requestCode)
        {
            try
            {
                requestCode = Uri.UnescapeDataString(requestCode);

                var request = await context.FilePathDtos
                    .FromSqlRaw(@"
                SELECT 
                    RequestCode,
                    FilePath
                FROM ESign_Request
                WHERE RequestCode = {0}
            ", requestCode)
                    .FirstOrDefaultAsync();

                if (request == null || string.IsNullOrEmpty(request.FilePath))
                    return NotFound("Request not found");

                if (!System.IO.File.Exists(request.FilePath))
                    return NotFound("File not found on server");

                var stream = new FileStream(
                    request.FilePath,
                    FileMode.Open,
                    FileAccess.Read
                );

                return File(stream, "application/pdf");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        public class SignatureModel
        {
            public int UserId { get; set; }
            public string SignatureType { get; set; }
            public string SignatureImage { get; set; }
        }

        public class UserSignature
        {
            public int SignatureId { get; set; }

            public int UserId { get; set; }

            public string SignatureType { get; set; }

            public string SignatureImage { get; set; }

            public DateTime CreatedDate { get; set; }

            public DateTime? UpdatedDate { get; set; }

            public bool IsActive { get; set; }
        }

        //[HttpGet]
        //[Route("api/ESign/GetUserSignature/{userId}")]
        //public async Task<IActionResult> GetUserSignature(int userId)
        //{
        //    var param = new SqlParameter("@UserId", userId);

        //    var result = await context.UserSignature
        //        .FromSqlRaw("EXEC SP_ESign_GetUserSignature @UserId", param)
        //        .ToListAsync();

        //    return Ok(result);
        //}

        [HttpGet]
        [Route("api/ESign/GetUserSignature")]
        public async Task<IActionResult> GetUserSignature(string signatureType)
        {
            try
            {
                int? userpsnum = _userinfoservice.GetUserPSNumberFromToken();

                if (!userpsnum.HasValue || userpsnum.Value <= 0)
                    return Unauthorized("Invalid user token.");

                if (string.IsNullOrWhiteSpace(signatureType))
                    return BadRequest("Signature type is required.");

                var parameters = new[]
                {
            new SqlParameter("@UserId", userpsnum.Value),
            new SqlParameter("@SignatureType", signatureType)
        };

                var data = await context.UserSignature
                    .FromSqlRaw("EXEC SP_ESign_GetUserSignature @UserId, @SignatureType", parameters)
                    .ToListAsync();

                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }


        public class SaveUserSignatureDto
        {
            public string SignatureType { get; set; }
            public string SignatureImage { get; set; }
        }

        [HttpPost]
        [Route("api/ESign/SaveUserSignature")]
        public async Task<IActionResult> SaveUserSignature([FromBody] SaveUserSignatureDto model)
        {
            try
            {
                int? userpsnum = _userinfoservice.GetUserPSNumberFromToken();

                if (!userpsnum.HasValue || userpsnum.Value <= 0)
                    return Unauthorized("Invalid user token.");

                if (model == null)
                    return BadRequest("Invalid request.");

                if (string.IsNullOrWhiteSpace(model.SignatureType))
                    return BadRequest("Signature type is required.");

                if (string.IsNullOrWhiteSpace(model.SignatureImage))
                    return BadRequest("Signature image is required.");

                var parameters = new[]
                {
            new SqlParameter("@UserId", userpsnum.Value),
            new SqlParameter("@SignatureType", model.SignatureType),
            new SqlParameter("@SignatureImage", model.SignatureImage)
        };

                await context.Database.ExecuteSqlRawAsync(
                    "EXEC SP_ESign_SaveUserSignature @UserId, @SignatureType, @SignatureImage",
                    parameters
                );

                return Ok(new
                {
                    message = "Signature saved successfully",
                    userId = userpsnum.Value,
                    status = "success"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }


        public class SignedSignature
        {
            public string RequestCode { get; set; }

            public int LevelNo { get; set; }

            public string ES_Signer_Siganture { get; set; }
        }


        [Keyless]
        public class ESignMailRequestDto
        {
            public string RequestCode { get; set; } = string.Empty;
            public string RequestSubject { get; set; } = string.Empty;
            public string RequesterName { get; set; } = string.Empty;
            public string RequesterEmail { get; set; } = string.Empty;
            public string DocumentName { get; set; } = string.Empty;

            public int LevelNo { get; set; }
            public string SignerName { get; set; } = string.Empty;
            public string SignerEmail { get; set; } = string.Empty;
            public string SignerStatus { get; set; } = string.Empty;
        }


        [HttpPost]
        [Route("api/ESign/SaveSignedDetails")]
        public async Task<IActionResult> SaveSignedDetails([FromBody] List<SignedSignature> model)
        {
            var mailTrace = new List<string>();

            try
            {
                int? userpsnum = _userinfoservice.GetUserPSNumberFromToken();

                if (!userpsnum.HasValue || userpsnum.Value <= 0)
                    return Unauthorized("Invalid user token.");

                if (model == null || !model.Any())
                    return BadRequest("No signature data received.");

                foreach (var item in model)
                {

                    var requestStatusInfo = await GetRequestStatusInfoAsync(item.RequestCode);

                    if (requestStatusInfo == null)
                    {
                        return NotFound($"Request not found: {item.RequestCode}");
                    }

                    if (requestStatusInfo.RequestStatus.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
                    {
                        return BadRequest($"Request {item.RequestCode} has been cancelled and can no longer be signed.");
                    }

                    if (requestStatusInfo.RequestStatus.Equals("Completed", StringComparison.OrdinalIgnoreCase))
                    {
                        return BadRequest($"Request {item.RequestCode} is already completed.");
                    }


                    if (string.IsNullOrWhiteSpace(item.RequestCode))
                        continue;

                    var parameters = new[]
                    {
                new SqlParameter("@RequestCode", item.RequestCode),
                new SqlParameter("@LevelNo", item.LevelNo),
                new SqlParameter("@SignatureImage", item.ES_Signer_Siganture ?? string.Empty),
                new SqlParameter("@UserId", userpsnum.Value)
            };

                    await context.Database.ExecuteSqlRawAsync(
                        "EXEC SP_ESign_SubmitSignatureUser @RequestCode,@LevelNo,@SignatureImage,@UserId",
                        parameters
                    );

                    try
                    {
                        var requestInfo = await context.ESignMailRequestDtos
                            .FromSqlRaw(
                                "EXEC dbo.SP_ESign_GetRequestMailDetails @RequestCode",
                                new SqlParameter("@RequestCode", item.RequestCode))
                            .ToListAsync();

                        if (!requestInfo.Any())
                        {
                            mailTrace.Add($"No mail data found for request {item.RequestCode}");
                            continue;
                        }

                        var firstRow = requestInfo.First();

                        var currentSigner = requestInfo
                            .FirstOrDefault(x => x.LevelNo == item.LevelNo);

                        string currentSignerName = currentSigner?.SignerName ?? "";
                        string currentSignerEmail = currentSigner?.SignerEmail ?? "";

                        await InsertAuditLog(
                            item.RequestCode,
                            item.LevelNo,
                            "Signed",
                            currentSignerName,
                            currentSignerEmail,
                            userpsnum,
                            "Approved",
                            $"Document signed at Level {item.LevelNo}"
                        );

                        string requestNumber = firstRow.RequestCode;
                        string requestSubject = firstRow.RequestSubject;
                        string requesterName = firstRow.RequesterName;
                        string requesterEmail = firstRow.RequesterEmail;
                        string documentName = firstRow.DocumentName;
                        string actionUrlBase = _config.GetSection("AppSettings:ESignActionUrl").Value ?? "";

                        var participants = requestInfo
                            .Select(x => new EsignParticipantModel
                            {
                                Level = x.LevelNo,
                                Name = x.SignerName,
                                Email = x.SignerEmail,
                                Status = x.SignerStatus
                            })
                            .OrderBy(x => x.Level)
                            .ThenBy(x => x.Name)
                            .ToList();

                        var pendingLevels = participants
                            .Where(x => x.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
                            .Select(x => x.Level)
                            .Distinct()
                            .OrderBy(x => x)
                            .ToList();

                        int nextPendingLevel = pendingLevels.Any() ? pendingLevels.First() : 0;

                        if (nextPendingLevel > 0)
                        {
                            string nextLevelEmails = string.Join(";",
                                participants
                                    .Where(x => x.Level == nextPendingLevel &&
                                                x.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
                                    .Select(x => x.Email)
                                    .Where(x => !string.IsNullOrWhiteSpace(x))
                                    .Distinct(StringComparer.OrdinalIgnoreCase));

                            if (!string.IsNullOrWhiteSpace(nextLevelEmails))
                            {
                                var signerMailModel = new EsignEmailModel
                                {
                                    RequestNumber = requestNumber,
                                    RequestSubject = requestSubject,
                                    RequesterName = requesterName,
                                    RequesterEmail = requesterEmail,
                                    DocumentName = documentName,
                                    RequestedOn = DateTime.Now,
                                    ActionUrl = $"{actionUrlBase}?requestNo={Uri.EscapeDataString(requestNumber)}",
                                    Participants = participants
                                };

                                string signerSubject = $"Signature Required | {requestNumber} | {requestSubject}";
                                string signerBody = CreateLevelWiseSignerNotificationMailFormat(signerMailModel);

                                string cc = requesterEmail;
                                string bcc = _config.GetSection("AppSettings:UserEmailBCC").Value ?? "";

                                var nextMailResult = await EmailTriggerMultipleTo(
                                    signerSubject,
                                    signerBody,
                                    cc,
                                    bcc,
                                    nextLevelEmails
                                );

                                await InsertAuditLog(
                                    item.RequestCode,
                                    nextPendingLevel,
                                    "Email Sent",
                                    "System",
                                    nextLevelEmails,
                                    null,
                                    "Pending",
                                    $"Email sent to Level {nextPendingLevel} signer(s)"
                                );

                                mailTrace.Add($"Next level mail => TO:{nextLevelEmails} CC:{cc} => {nextMailResult}");
                            }
                            else
                            {
                                mailTrace.Add($"No email found for next pending level {nextPendingLevel} in request {item.RequestCode}");
                            }
                        }
                        else
                        {
                            try
                            {
                                // Final signer completed workflow => generate and save final signed PDF now
                                string savedSignedFilePath = await GenerateAndSaveSignedPdfAsync(item.RequestCode);
                                await UpdateRequestStatusAsync(item.RequestCode, "Completed");
                                string signedPdfUrl = BuildSignedPdfDownloadUrl(item.RequestCode);

                                var completedParticipants = participants
                                    .Select(x => new EsignParticipantModel
                                    {
                                        Level = x.Level,
                                        Name = x.Name,
                                        Email = x.Email,
                                        Status = "Approved"
                                    })
                                    .OrderBy(x => x.Level)
                                    .ThenBy(x => x.Name)
                                    .ToList();

                                var completedMailModel = new EsignEmailModel
                                {
                                    RequestNumber = requestNumber,
                                    RequestSubject = requestSubject,
                                    RequesterName = requesterName,
                                    RequesterEmail = requesterEmail,
                                    DocumentName = documentName,
                                    RequestedOn = DateTime.Now,
                                    ActionUrl = $"{actionUrlBase}?requestNo={Uri.EscapeDataString(requestNumber)}",
                                    SignedPdfUrl = signedPdfUrl,
                                    Participants = completedParticipants
                                };

                                string completedSubject = $"eSignature Completed | {requestNumber} | {requestSubject}";
                                string completedBody = CreateCompletionMailFormat(completedMailModel);

                                string finalTo = requesterEmail;

                                string finalCc = string.Join(";",
                                    completedParticipants
                                        .Select(x => x.Email)
                                        .Where(x => !string.IsNullOrWhiteSpace(x))
                                        .Distinct(StringComparer.OrdinalIgnoreCase));

                                string finalBcc = _config.GetSection("AppSettings:UserEmailBCC").Value ?? "";

                                var attachments = new List<string>();

                                if (CanAttachFile(savedSignedFilePath, 8))
                                {
                                    attachments.Add(savedSignedFilePath);
                                    mailTrace.Add($"Signed PDF attached in mail => {savedSignedFilePath}");
                                }
                                else
                                {
                                    mailTrace.Add($"Signed PDF not attached due to size restriction or missing file => {savedSignedFilePath}");
                                }

                                var finalMailResult = await EmailTriggerMultipleTo(
                                    completedSubject,
                                    completedBody,
                                    finalCc,
                                    finalBcc,
                                    finalTo,
                                    attachments
                                );

                                await InsertAuditLog(
                                    item.RequestCode,
                                    null,
                                    "Workflow Completed",
                                    requesterName,
                                    requesterEmail,
                                    null,
                                    "Completed",
                                    $"All signer levels approved. Signed PDF saved at: {savedSignedFilePath}"
                                );

                                mailTrace.Add($"Final completion mail => TO:{finalTo} CC:{finalCc} => {finalMailResult}");
                                mailTrace.Add($"Signed PDF saved => {savedSignedFilePath}");
                            }
                            catch (Exception ex)
                            {
                                mailTrace.Add($"Final completion mail/sign generation exception => {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        mailTrace.Add($"Mail block error for request {item.RequestCode} => {ex.Message}");
                    }
                }

                return Ok(new
                {
                    message = "Signatures updated successfully",
                    status = "success",
                    mailTrace = mailTrace
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }



        private string CreateCompletionMailFormat(EsignEmailModel model)
        {
            var participantRows = "";

            foreach (var p in model.Participants.OrderBy(x => x.Level).ThenBy(x => x.Name))
            {
                participantRows += $@"
<tr>
    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>Level {p.Level}</td>
    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>{System.Net.WebUtility.HtmlEncode(p.Name)}</td>
    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>{System.Net.WebUtility.HtmlEncode(p.Email)}</td>
    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>Approved</td>
</tr>";
            }

            string signedPdfSection = string.IsNullOrWhiteSpace(model.SignedPdfUrl)
                ? ""
                : $@"
<div style='margin:0 0 20px 0;'>
    <a href='{System.Net.WebUtility.HtmlEncode(model.SignedPdfUrl)}'
       style='display:inline-block; background:#1d4ed8; color:#ffffff; text-decoration:none; padding:12px 22px; border-radius:6px; font-size:14px; font-weight:600;'>
        Download Signed PDF
    </a>
</div>";

            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8' />
    <title>eSignature Completed</title>
</head>
<body style='margin:0; padding:0; background-color:#f4f6f8; font-family:Segoe UI, Arial, sans-serif; color:#1f2937;'>
    <table width='100%' cellpadding='0' cellspacing='0' border='0' style='background-color:#f4f6f8; padding:24px 0;'>
        <tr>
            <td align='center'>
                <table width='760' cellpadding='0' cellspacing='0' border='0' style='background:#ffffff; border:1px solid #e5e7eb; border-radius:10px; overflow:hidden;'>

                    <tr>
                        <td style='background:#065f46; padding:22px 28px;'>
                            <div style='font-size:22px; font-weight:600; color:#ffffff;'>eSignature Completed</div>
                            <div style='font-size:13px; color:#d1fae5; margin-top:6px;'>Document signing workflow completed successfully</div>
                        </td>
                    </tr>

                    <tr>
                        <td style='padding:30px 28px 18px 28px;'>
                            <p style='margin:0 0 14px 0; font-size:15px;'>
                                Dear <strong>{System.Net.WebUtility.HtmlEncode(model.RequesterName)}</strong>,
                            </p>

                            <p style='margin:0 0 14px 0; font-size:14px; line-height:1.7; color:#374151;'>
                                Your eSignature request has been completed successfully. All required signers have approved the document.
                            </p>

                                <p style='margin:0 0 14px 0; font-size:14px; line-height:1.7; color:#374151;'>
                                  The signed PDF is attached with this email when permitted by attachment size limits. A download link is also provided below.
                                 </p>

                            <table width='100%' cellpadding='0' cellspacing='0' border='0' style='border-collapse:collapse; margin:18px 0 22px 0;'>
                                <tr>
                                    <td colspan='2' style='font-size:15px; font-weight:600; color:#111827; padding-bottom:12px;'>Request Details</td>
                                </tr>
                                <tr>
                                    <td style='width:180px; padding:10px 12px; border:1px solid #e5e7eb; background:#f9fafb; font-size:13px; font-weight:600;'>Reference</td>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>{System.Net.WebUtility.HtmlEncode(model.RequestNumber)}</td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; background:#f9fafb; font-size:13px; font-weight:600;'>Request Subject</td>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>{System.Net.WebUtility.HtmlEncode(model.RequestSubject)}</td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; background:#f9fafb; font-size:13px; font-weight:600;'>Document Name</td>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>{System.Net.WebUtility.HtmlEncode(model.DocumentName)}</td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; background:#f9fafb; font-size:13px; font-weight:600;'>Completed On</td>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>{DateTime.Now:dd-MMM-yyyy hh:mm tt}</td>
                                </tr>
                            </table>

                            <div style='margin:0 0 20px 0;'>
                                <a href='{model.ActionUrl}'
                                   style='display:inline-block; background:#065f46; color:#ffffff; text-decoration:none; padding:12px 22px; border-radius:6px; font-size:14px; font-weight:600;'>
                                    View Completed Request
                                </a>
                            </div>

                            {signedPdfSection}

                            <table width='100%' cellpadding='0' cellspacing='0' border='0' style='border-collapse:collapse; margin:18px 0 22px 0;'>
                                <tr>
                                    <td colspan='4' style='font-size:15px; font-weight:600; color:#111827; padding-bottom:12px;'>Completed Workflow</td>
                                </tr>
                                <tr>
                                    <th style='padding:10px 12px; border:1px solid #e5e7eb; background:#f3f4f6; font-size:12px; text-align:left;'>Level</th>
                                    <th style='padding:10px 12px; border:1px solid #e5e7eb; background:#f3f4f6; font-size:12px; text-align:left;'>Signer Name</th>
                                    <th style='padding:10px 12px; border:1px solid #e5e7eb; background:#f3f4f6; font-size:12px; text-align:left;'>Email</th>
                                    <th style='padding:10px 12px; border:1px solid #e5e7eb; background:#f3f4f6; font-size:12px; text-align:left;'>Status</th>
                                </tr>
                                {participantRows}
                            </table>

                            <p style='margin:0; font-size:13px; line-height:1.7; color:#374151;'>
                                Regards,<br />
                                <strong>eSignature Workflow System</strong>
                            </p>
                        </td>
                    </tr>

                    <tr>
                        <td style='padding:16px 28px; background:#f9fafb; border-top:1px solid #e5e7eb;'>
                            <p style='margin:0; font-size:12px; color:#6b7280; line-height:1.6;'>
                                This is an automated email from the eSignature Workflow System. Please do not reply directly to this message.
                            </p>
                        </td>
                    </tr>

                </table>
            </td>
        </tr>
    </table>
</body>
</html>";
        }



        [HttpGet]
        [Route("api/ESign/GetRequestDetailsByUser/{requestCode}")]
        public async Task<IActionResult> GetRequestDetailsByUser(string requestCode)
        {
            requestCode = Uri.UnescapeDataString(requestCode);

            try
            {
                int? userpsnum = _userinfoservice.GetUserPSNumberFromToken();

                var parameters = new[]
                {
            new SqlParameter("@RequestCode", requestCode),
            new SqlParameter("@UserId", userpsnum)
        };

                var result = await context.RequestDetails
                    .FromSqlRaw("EXEC SP_ESign_GetRequestDetailsByUserId @RequestCode,@UserId", parameters)
                    .ToListAsync();

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }



        [Keyless]
        public class EmployeeSignerDto
        {
            public string Name { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
        }

        [HttpGet]
        [Route("api/ESign/GetEmployeeSignerList")]
        public async Task<IActionResult> GetEmployeeSignerList()
        {
            try
            {
                var result = await context.EmployeeSignerDtos
                    .FromSqlRaw("EXEC dbo.[SP_ESign_Employee_DDN]")
                    .ToListAsync();

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }




        [HttpGet]
        [Route("api/ESign/GetSignedPdf2/{requestCode}")]
        public async Task<IActionResult> GetSignedPdf2(string requestCode)
        {
            try
            {
                requestCode = Uri.UnescapeDataString(requestCode);

                var pdfBytes = await GenerateSignedPdf(requestCode);

                return File(pdfBytes, "application/pdf", "SignedDocument.pdf");
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }


        [HttpGet]
        [Route("api/ESign/GetSignedPdf/{requestCode}")]
        public async Task<IActionResult> GetSignedPdf(string requestCode)
        {
            try
            {
                requestCode = Uri.UnescapeDataString(requestCode);

                var requestData = await context.SignedPdfRequestDtos
                    .FromSqlRaw(@"
                SELECT
                    RequestCode,
                    FilePath,
                    SignedFilePath,
                    RequestStatus,

                    CASE
                        WHEN ISNULL(RequestStatus, '') = 'Cancelled' THEN 'Cancelled'
                        WHEN ISNULL(RequestStatus, '') = 'Completed' THEN 'Approved'
                        WHEN ISNULL(RequestStatus, '') = 'InProcess' THEN 'InProcess'
                        ELSE 'Pending'
                    END AS Siganture_Status

                FROM ESign_Request
                WHERE RequestCode = {0}
            ", requestCode)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                if (requestData == null)
                    return NotFound("Request not found.");

                string requestStatus =
                    requestData.Siganture_Status
                    ?? requestData.RequestStatus
                    ?? "";

                if (requestStatus.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
                    return BadRequest("Signed PDF is not available because request was cancelled.");

                if (!string.IsNullOrWhiteSpace(requestData.SignedFilePath)
                    && System.IO.File.Exists(requestData.SignedFilePath))
                {
                    var bytes = await System.IO.File.ReadAllBytesAsync(
                        requestData.SignedFilePath
                    );

                    return File(
                        bytes,
                        "application/pdf",
                        Path.GetFileName(requestData.SignedFilePath)
                    );
                }

                // Generate if missing
                string savedSignedFilePath =
                    await GenerateAndSaveSignedPdfAsync(requestCode);

                var generatedBytes =
                    await System.IO.File.ReadAllBytesAsync(savedSignedFilePath);

                return File(
                    generatedBytes,
                    "application/pdf",
                    Path.GetFileName(savedSignedFilePath)
                );
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }




        private async Task<byte[]> GenerateSignedPdf(string requestCode)
        {
            if (string.IsNullOrWhiteSpace(requestCode))
                throw new ArgumentException("Request code is required.", nameof(requestCode));

            // Fetch only required request details if possible.
            // If you still only have SP_ESign_GetSubmittedRequests, this will work,
            // but for better performance create a SP that fetches by RequestCode directly.
            var requestData = await context.FilePathDtos
   .FromSqlRaw(@"
        SELECT 
            RequestCode,
            FilePath
        FROM ESign_Request
        WHERE RequestCode = {0}
    ", requestCode)
   .AsNoTracking()
   .FirstOrDefaultAsync();
            if (requestData == null)
                throw new Exception($"Request not found for RequestCode: {requestCode}");

            string filePath = requestData.FilePath?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(filePath))
                throw new Exception("Source file path is empty.");

            if (!System.IO.File.Exists(filePath))
                throw new Exception($"File not found: {filePath}");

            var signatures = await context.RequestDetails
                .FromSqlRaw(
                    "EXEC SP_ESign_GetRequestDetails @RequestCode",
                    new SqlParameter("@RequestCode", requestCode))
                .AsNoTracking()
                .ToListAsync();

            var signedUsersByPage = signatures
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.ES_Siganture_Status) &&
                    x.ES_Siganture_Status.Trim().Equals("Approved", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(x.ES_Signer_Siganture) &&
                    x.PageNumber.HasValue &&
                    x.PageNumber.Value > 0 &&
                    x.PositionX.HasValue &&
                    x.PositionY.HasValue &&
                    x.BoxWidth.HasValue &&
                    x.BoxHeight.HasValue)
                .GroupBy(x => x.PageNumber!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            using var output = new MemoryStream();

            using var pdfiumDoc = PdfiumDoc.Load(filePath);
            using var writer = new PdfWriter(output);
            using var outPdf = new ITextPdfDocument(writer);

            int totalPages = pdfiumDoc.PageCount;

            // 2.5f is good balance between quality and size.
            const float renderScale = 3f;

            for (int pageIndex = 0; pageIndex < totalPages; pageIndex++)
            {
                int pageNumber = pageIndex + 1;
                var pageSizePt = pdfiumDoc.PageSizes[pageIndex];

                int renderWidth = Math.Max(1, (int)Math.Round(pageSizePt.Width * renderScale));
                int renderHeight = Math.Max(1, (int)Math.Round(pageSizePt.Height * renderScale));

                //using var bitmap = new Bitmap(renderWidth, renderHeight, PixelFormat.Format32bppArgb);
                using var bitmap = new Bitmap(renderWidth, renderHeight, PixelFormat.Format24bppRgb);

                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(DrawingColor.White);
                    ApplyHighQualityGraphicsSettings(g);
                }

                using (var rendered = pdfiumDoc.Render(
                    pageIndex,
                    renderWidth,
                    renderHeight,
                    96,
                    96,
                    PdfRenderFlags.Annotations))
                {
                    using var g = Graphics.FromImage(bitmap);
                    ApplyHighQualityGraphicsSettings(g);
                    g.DrawImage(rendered, 0, 0, renderWidth, renderHeight);
                }

                if (signedUsersByPage.TryGetValue(pageNumber, out var pageUsers) && pageUsers.Count > 0)
                {
                    using var g = Graphics.FromImage(bitmap);
                    ApplyHighQualityGraphicsSettings(g);

                    foreach (var sig in pageUsers)
                    {
                        try
                        {
                            float x = (float)sig.PositionX!.Value * renderWidth;
                            float y = (float)sig.PositionY!.Value * renderHeight;
                            float w = (float)sig.BoxWidth!.Value * renderWidth;
                            float h = (float)sig.BoxHeight!.Value * renderHeight;

                            if (w <= 1 || h <= 1)
                                continue;

                            if (x >= renderWidth || y >= renderHeight)
                                continue;

                            if (x < 0) x = 0;
                            if (y < 0) y = 0;

                            if (x + w > renderWidth) w = renderWidth - x;
                            if (y + h > renderHeight) h = renderHeight - y;

                            if (w <= 1 || h <= 1)
                                continue;

                            float signHeight = h * 0.70f;
                            float nameHeight = h * 0.15f;
                            float timeHeight = h * 0.15f;

                            float nameY = y + signHeight;
                            float timeY = y + signHeight + nameHeight;

                            float padding = Math.Min(w, h) * 0.04f;

                            float sigX = x + padding;
                            float sigY = y + padding;
                            float sigW = Math.Max(1, w - (padding * 2f));
                            float sigH = Math.Max(1, signHeight - (padding * 2f));

                            float realSigX = sigX;
                            float realSigW = sigW;

                            byte[]? imgBytes = null;

                            try
                            {
                                imgBytes = ExtractBase64ImageBytes(sig.ES_Signer_Siganture);
                            }
                            catch
                            {
                                imgBytes = null;
                            }

                            if (imgBytes != null && imgBytes.Length > 0)
                            {
                                using var msSig = new MemoryStream(imgBytes);
                                using var signImage = DrawingImage.FromStream(msSig);

                                float ratio = (float)signImage.Width / signImage.Height;

                                float drawW = sigW;
                                float drawH = sigW / ratio;

                                if (drawH > sigH)
                                {
                                    drawH = sigH;
                                    drawW = sigH * ratio;
                                }

                                float drawX = sigX + ((sigW - drawW) / 2f);
                                float drawY = sigY + ((sigH - drawH) / 2f);

                                g.DrawImage(signImage, drawX, drawY, drawW, drawH);

                                realSigX = drawX;
                                realSigW = drawW;
                            }

                            using (var pen = new Pen(DrawingColor.FromArgb(26, 35, 126), Math.Max(1f, h * 0.01f)))
                            {
                                g.DrawLine(
                                    pen,
                                    realSigX,
                                    nameY,
                                    realSigX + realSigW,
                                    nameY
                                );
                            }

                            string signerName = !string.IsNullOrWhiteSpace(sig.SignerFullName)
                                ? sig.SignerFullName
                                : sig.SignerName ?? string.Empty;

                            signerName = signerName.Trim();

                            if (signerName.Length > 25)
                                signerName = signerName.Substring(0, 25);

                            float nameFontSize = Math.Max(nameHeight * 0.8f, 6f);

                            using var nameFont = CreateBestFitFont(
                                signerName,
                                "Arial",
                                FontStyle.Bold,
                                nameFontSize,
                                6f,
                                realSigW * 0.95f,
                                g
                            );

                            using var nameBrush = new SolidBrush(DrawingColor.FromArgb(26, 35, 126));
                            using var centerFormat = new StringFormat
                            {
                                Alignment = StringAlignment.Center,
                                LineAlignment = StringAlignment.Center,
                                FormatFlags = StringFormatFlags.NoWrap
                            };

                            g.DrawString(
                                signerName,
                                nameFont,
                                nameBrush,
                                new RectangleF(realSigX, nameY, realSigW, nameHeight),
                                centerFormat
                            );

                            string signedOnText = FormatSignatureDate(sig.ES_Siganture_On);

                            float timeFontSize = Math.Max(timeHeight * 0.8f, 6f);

                            using var timeFont = CreateBestFitFont(
                                signedOnText,
                                "Arial",
                                FontStyle.Regular,
                                timeFontSize,
                                6f,
                                realSigW * 0.95f,
                                g
                            );

                            g.DrawString(
                                signedOnText,
                                timeFont,
                                nameBrush,
                                new RectangleF(realSigX, timeY, realSigW, timeHeight),
                                centerFormat
                            );
                        }
                        catch
                        {
                            // Skip only this signature if there is any issue
                            // so the whole PDF generation does not fail.
                            continue;
                        }
                    }
                }

                //using var msImg = new MemoryStream();
                //bitmap.Save(msImg, ImageFormat.Png);
                //byte[] finalImgBytes = msImg.ToArray();

                using var msImg = new MemoryStream();

                var jpegCodec = ImageCodecInfo.GetImageDecoders()
                    .First(c => c.FormatID == ImageFormat.Jpeg.Guid);

                using var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);

                bitmap.Save(msImg, jpegCodec, encoderParams);

                byte[] finalImgBytes = msImg.ToArray();


                var imageData = ImageDataFactory.Create(finalImgBytes);

                var outPage = outPdf.AddNewPage(new ITextPageSize(pageSizePt.Width, pageSizePt.Height));

                var pdfCanvas = new iText.Kernel.Pdf.Canvas.PdfCanvas(outPage);
                pdfCanvas.AddImageFittedIntoRectangle(
                    imageData,
                    new iText.Kernel.Geom.Rectangle(0, 0, pageSizePt.Width, pageSizePt.Height),
                    false
                );
            }

            outPdf.Close();
            return output.ToArray();
        }

        private static void ApplyHighQualityGraphicsSettings(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        }

        private static Font CreateBestFitFont(
    string text,
    string fontFamily,
    FontStyle fontStyle,
    float startSize,
    float minSize,
    float maxWidth,
    Graphics g)
        {
            float currentSize = startSize;

            while (currentSize >= minSize)
            {
                var testFont = new Font(fontFamily, currentSize, fontStyle, GraphicsUnit.Pixel);
                var measured = g.MeasureString(text ?? string.Empty, testFont);

                if (measured.Width <= maxWidth)
                    return testFont;

                testFont.Dispose();
                currentSize -= 1f;
            }

            return new Font(fontFamily, minSize, fontStyle, GraphicsUnit.Pixel);
        }


        private static byte[] ExtractBase64ImageBytes(string base64OrDataUrl)
        {
            if (string.IsNullOrWhiteSpace(base64OrDataUrl))
                return Array.Empty<byte>();

            string base64 = base64OrDataUrl.Trim();

            int commaIndex = base64.IndexOf(',');
            if (commaIndex >= 0)
                base64 = base64.Substring(commaIndex + 1);

            return Convert.FromBase64String(base64);
        }

        private static string FormatSignatureDate(DateTime? signedOn)
        {
            if (!signedOn.HasValue)
                return string.Empty;

            return signedOn.Value.ToString("dd-MMM-yyyy HH:mm", CultureInfo.InvariantCulture);
        }






        private byte[] ApplyDigitalSignature(byte[] pdfBytes)
        {
            if (pdfBytes == null || pdfBytes.Length == 0)
                throw new Exception("Generated PDF is empty.");

            string pfxPath = System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "Certificates",
                "esign-cert.pfx"
            );

            string pfxPassword = "Test@123";

            if (!System.IO.File.Exists(pfxPath))
                throw new Exception("Signing certificate not found.");

            Pkcs12Store pk12 = new Pkcs12StoreBuilder().Build();

            using (var fs = new FileStream(pfxPath, FileMode.Open, FileAccess.Read))
            {
                pk12.Load(fs, pfxPassword.ToCharArray());
            }

            string alias = null;
            foreach (var a in pk12.Aliases)
            {
                alias = (string)a;
                if (pk12.IsKeyEntry(alias))
                    break;
            }

            if (string.IsNullOrWhiteSpace(alias))
                throw new Exception("Private key entry not found in certificate.");

            ICipherParameters pk = pk12.GetKey(alias).Key;

            Org.BouncyCastle.Pkcs.X509CertificateEntry[] ce = pk12.GetCertificateChain(alias);
            Org.BouncyCastle.X509.X509Certificate[] chain =
                new Org.BouncyCastle.X509.X509Certificate[ce.Length];

            for (int i = 0; i < ce.Length; i++)
            {
                chain[i] = ce[i].Certificate;
            }

            IX509Certificate[] certificateWrappers = new IX509Certificate[chain.Length];
            for (int i = 0; i < chain.Length; i++)
            {
                certificateWrappers[i] = new X509CertificateBC(chain[i]);
            }

            using var inputStream = new MemoryStream(pdfBytes);
            using var outputStream = new MemoryStream();

            PdfReader reader = new PdfReader(inputStream);

            PdfSigner signer = new PdfSigner(
                reader,
                outputStream,
                new StampingProperties().UseAppendMode()
            );

            var signerProperties = new SignerProperties()
                .SetFieldName("WETICeSign")
                .SetCertificationLevel(AccessPermissions.NO_CHANGES_PERMITTED)
                .SetReason("Final approved document")
                .SetLocation("LTC-WETIC");
                //.SetPageNumber(1)
                //.SetPageRect(new iText.Kernel.Geom.Rectangle(36, 36, 200, 80));

            signer.SetSignerProperties(signerProperties);

            PrivateKeySignature pks = new PrivateKeySignature(
                new PrivateKeyBC(pk),
                "SHA-256"
            );

            signer.SignDetached(
                pks,
                certificateWrappers,
                null,
                null,
                null,
                0,
                PdfSigner.CryptoStandard.CMS
            );

            return outputStream.ToArray();
        }





        //Audit Report

        [Keyless]
        public class AuditReportHeaderDto
        {
            public string RequestCode { get; set; } = string.Empty;
            public string RequestType { get; set; } = string.Empty;
            public int TotalPages { get; set; }
            public DateTime CreatedOn { get; set; }
            public string FilePath { get; set; } = string.Empty;
            public string CreatedByName { get; set; } = string.Empty;
            public string CreatedByEmail { get; set; } = string.Empty;
            public string RequestStatus { get; set; } = string.Empty;
        }

        [Keyless]
        public class AuditReportSignerDto
        {
            public int LevelNo { get; set; }
            public string SignerName { get; set; } = string.Empty;
            public string SignerEmail { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public DateTime? SignedOn { get; set; }
        }

        [Keyless]
        public class AuditReportEventDto
        {
            public DateTime EventDateTime { get; set; }
            public string UserName { get; set; } = string.Empty;
            public string UserEmail { get; set; } = string.Empty;
            public string EventType { get; set; } = string.Empty;
            public int? LevelNo { get; set; }
            public string ActionStatus { get; set; } = string.Empty;
            public string IPAddress { get; set; } = string.Empty;
            public string DeviceInfo { get; set; } = string.Empty;
            public string Remarks { get; set; } = string.Empty;
        }



        private async Task InsertAuditLog(
    string requestCode,
    int? levelNo,
    string eventType,
    string userName,
    string userEmail,
    int? userPSNo,
    string actionStatus,
    string remarks = "")
        {
            var ipAddress = HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "";
            // var deviceInfo = Request?.Headers["User-Agent"].ToString() ?? "";
            var userAgent = Request?.Headers["User-Agent"].ToString() ?? "";
            string hostName = "";
            try
            {
                if (!string.IsNullOrWhiteSpace(ipAddress))
                {
                    hostName = System.Net.Dns.GetHostEntry(ipAddress).HostName;
                }
            }
            catch
            {
                hostName = "";
            }

            var deviceInfo = string.IsNullOrWhiteSpace(hostName)
                ? userAgent
                : $"HostName: {hostName} | UserAgent: {userAgent}";

            var parameters = new[]
            {
        new SqlParameter("@RequestCode", requestCode),
        new SqlParameter("@LevelNo", (object?)levelNo ?? DBNull.Value),
        new SqlParameter("@EventType", eventType),
        new SqlParameter("@UserName", (object?)userName ?? DBNull.Value),
        new SqlParameter("@UserEmail", (object?)userEmail ?? DBNull.Value),
        new SqlParameter("@UserPSNo", (object?)userPSNo ?? DBNull.Value),
        new SqlParameter("@ActionStatus", (object?)actionStatus ?? DBNull.Value),
        new SqlParameter("@IPAddress", (object?)ipAddress ?? DBNull.Value),
        new SqlParameter("@DeviceInfo", (object?)deviceInfo ?? DBNull.Value),
        new SqlParameter("@Remarks", (object?)remarks ?? DBNull.Value)
    };

            await context.Database.ExecuteSqlRawAsync(
                "EXEC dbo.SP_ESign_InsertAuditLog @RequestCode,@LevelNo,@EventType,@UserName,@UserEmail,@UserPSNo,@ActionStatus,@IPAddress,@DeviceInfo,@Remarks",
                parameters
            );
        }




        private static string ExtractRequestCode(string resultMessage)
        {
            if (string.IsNullOrWhiteSpace(resultMessage))
                throw new Exception("Stored procedure result is empty.");

            const string token = "RequestNo:";
            int idx = resultMessage.IndexOf(token, StringComparison.OrdinalIgnoreCase);

            if (idx < 0)
                throw new Exception("Request number not found in stored procedure result.");

            string requestCode = resultMessage.Substring(idx + token.Length).Trim();

            if (string.IsNullOrWhiteSpace(requestCode))
                throw new Exception("Parsed request number is blank.");

            return requestCode;
        }


        private static string GetRequestFolderName(string requestCode)
        {
            if (string.IsNullOrWhiteSpace(requestCode))
                throw new ArgumentException("Request code is required.", nameof(requestCode));

            return requestCode
                .Replace("/", "-")
                .Replace("\\", "-")
                .Trim();
        }

        private static string GetSafeExtension(string fileName)
        {
            var ext = Path.GetExtension(fileName);

            if (string.IsNullOrWhiteSpace(ext))
                return ".pdf";

            return ext;
        }


        private async Task<string> SaveOriginalFileInRequestFolderAsync(IFormFile file, string requestCode)
        {
            string rootFolder = _config.GetSection("AppSettings:ESignRootFolder").Value ?? @"D:\EsignatureAPP";

            if (!Directory.Exists(rootFolder))
                Directory.CreateDirectory(rootFolder);

            string folderName = GetRequestFolderName(requestCode);
            string requestFolder = Path.Combine(rootFolder, folderName);

            if (!Directory.Exists(requestFolder))
                Directory.CreateDirectory(requestFolder);

            string originalFileName = Path.GetFileName(file.FileName);
            string originalFilePath = Path.Combine(requestFolder, originalFileName);

            using (var stream = new FileStream(originalFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await file.CopyToAsync(stream);
            }

            return originalFilePath;
        }

        private static string GetSignedFilePath(string originalFilePath)
        {
            if (string.IsNullOrWhiteSpace(originalFilePath))
                throw new ArgumentException("Original file path is required.", nameof(originalFilePath));

            string directory = Path.GetDirectoryName(originalFilePath) ?? "";
            string fileName = Path.GetFileName(originalFilePath);

            return Path.Combine(directory, $"Signed_{fileName}");
        }


        private async Task UpdateRequestPathsAsync(string requestCode, string? filePath = null, string? signedFilePath = null)
        {
            var parameters = new[]
            {
        new SqlParameter("@RequestCode", requestCode),
        new SqlParameter("@FilePath", (object?)filePath ?? DBNull.Value),
        new SqlParameter("@SignedFilePath", (object?)signedFilePath ?? DBNull.Value)
    };

            await context.Database.ExecuteSqlRawAsync(
                "EXEC dbo.SP_ESign_UpdateRequestPaths @RequestCode, @FilePath, @SignedFilePath",
                parameters
            );
        }

        private string BuildSignedPdfDownloadUrl(string requestCode)
        {
            string apiBaseUrl = _config.GetSection("AppSettings:ESignApiBaseUrl").Value ?? "";

            if (string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                var request = HttpContext?.Request;
                if (request != null)
                    apiBaseUrl = $"{request.Scheme}://{request.Host}";
            }

            return $"{apiBaseUrl}/api/ESign/GetSignedPdf/{Uri.EscapeDataString(requestCode)}";
        }

        private async Task<string> GenerateAndSaveSignedPdfAsync(string requestCode)
        {
            if (string.IsNullOrWhiteSpace(requestCode))
                throw new ArgumentException("Request code is required.", nameof(requestCode));

            var requestData = await context.FilePathDtos
                .FromSqlRaw(@"
            SELECT 
                RequestCode,
                FilePath
            FROM ESign_Request
            WHERE RequestCode = {0}
        ", requestCode)
                .AsNoTracking()
                .FirstOrDefaultAsync();

            if (requestData == null)
                throw new Exception($"Request not found for RequestCode: {requestCode}");

            string originalFilePath = requestData.FilePath?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(originalFilePath))
                throw new Exception("Original file path is empty.");

            if (!System.IO.File.Exists(originalFilePath))
                throw new Exception($"Original file not found: {originalFilePath}");

            string signedFilePath = GetSignedFilePath(originalFilePath);

            var visualPdfBytes = await GenerateSignedPdf(requestCode);

            var finalSignedPdfBytes = ApplyDigitalSignature(visualPdfBytes);

            await System.IO.File.WriteAllBytesAsync(
                signedFilePath,
                finalSignedPdfBytes
            );

            await UpdateRequestPathsAsync(
                requestCode,
                null,
                signedFilePath
            );

            return signedFilePath;
        }

        public class CancelRequestDto
        {
            public string RequestCode { get; set; } = string.Empty;
            public string CancelReason { get; set; } = string.Empty;
        }

      

[Keyless]
    public class RequestStatusInfoDto
    {
        public string RequestCode { get; set; } = string.Empty;
        public int CreatedBy { get; set; }
        public string RequestStatus { get; set; } = string.Empty;
        public string? CancelReason { get; set; }
        public DateTime? CancelledOn { get; set; }
        public int? CancelledBy { get; set; }
}


        private async Task<RequestStatusInfoDto?> GetRequestStatusInfoAsync(string requestCode)
        {
            if (string.IsNullOrWhiteSpace(requestCode))
                return null;

            var request = await context.RequestStatusInfos
                .FromSqlRaw(@"
            SELECT TOP 1
                RequestCode,
                CreatedBy,
                ISNULL(RequestStatus, 'Pending') AS RequestStatus,
                CancelReason,
                CancelledOn,
                CancelledBy
            FROM dbo.ESign_Request
            WHERE RequestCode = @RequestCode",
                    new SqlParameter("@RequestCode", requestCode))
                .AsNoTracking()
                .FirstOrDefaultAsync();

            return request;
        }

        private async Task UpdateRequestStatusAsync(string requestCode, string status)
        {
            await context.Database.ExecuteSqlRawAsync(@"
        UPDATE dbo.ESign_Request
        SET RequestStatus = @Status
        WHERE RequestCode = @RequestCode",
                new SqlParameter("@Status", status),
                new SqlParameter("@RequestCode", requestCode));
        }

        private static List<string> GetValidEmailList(string emails)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(emails))
                return result;

            foreach (var value in emails.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var email = value.Trim();
                if (string.IsNullOrWhiteSpace(email))
                    continue;

                try
                {
                    _ = new MailAddress(email);
                    if (!result.Contains(email, StringComparer.OrdinalIgnoreCase))
                        result.Add(email);
                }
                catch
                {
                    // ignore invalid email
                }
            }

            return result;
        }

        private static string JoinEmails(IEnumerable<string?> emails)
        {
            return string.Join(";",
                emails
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase));
        }



        private string CreateCancellationMailFormat(EsignEmailModel model, string cancelReason)
        {
            var participantRows = "";

            foreach (var p in model.Participants.OrderBy(x => x.Level).ThenBy(x => x.Name))
            {
                participantRows += $@"
<tr>
    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>Level {p.Level}</td>
    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>{System.Net.WebUtility.HtmlEncode(p.Name)}</td>
    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>{System.Net.WebUtility.HtmlEncode(p.Email)}</td>
    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>Approved</td>
</tr>";
            }

            var reasonRow = string.IsNullOrWhiteSpace(cancelReason)
                ? string.Empty
                : $@"
<tr>
    <td style='padding:10px 12px; border:1px solid #e5e7eb; background:#f9fafb; font-size:13px; font-weight:600;'>Cancellation Reason</td>
    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>{System.Net.WebUtility.HtmlEncode(cancelReason)}</td>
</tr>";

            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8' />
    <title>eSignature Request Cancelled</title>
</head>
<body style='margin:0; padding:0; background-color:#f4f6f8; font-family:Segoe UI, Arial, sans-serif; color:#1f2937;'>
    <table width='100%' cellpadding='0' cellspacing='0' border='0' style='background-color:#f4f6f8; padding:24px 0;'>
        <tr>
            <td align='center'>
                <table width='760' cellpadding='0' cellspacing='0' border='0' style='background:#ffffff; border:1px solid #e5e7eb; border-radius:10px; overflow:hidden;'>
                    <tr>
                        <td style='background:#b91c1c; padding:22px 28px;'>
                            <div style='font-size:22px; font-weight:600; color:#ffffff;'>eSignature Request Cancelled</div>
                            <div style='font-size:13px; color:#fecaca; margin-top:6px;'>Request cancelled by creator</div>
                        </td>
                    </tr>

                    <tr>
                        <td style='padding:30px 28px 18px 28px;'>
                            <p style='margin:0 0 14px 0; font-size:15px;'>Dear Sir/Ma'am,</p>

                            <p style='margin:0 0 14px 0; font-size:14px; line-height:1.7; color:#374151;'>
                                The below eSignature request has been cancelled by the creator. Since you had already approved this request, this notification is being shared for your reference.
                            </p>

                            <table width='100%' cellpadding='0' cellspacing='0' border='0' style='border-collapse:collapse; margin:18px 0 22px 0;'>
                                <tr>
                                    <td colspan='2' style='font-size:15px; font-weight:600; color:#111827; padding-bottom:12px;'>Request Details</td>
                                </tr>
                                <tr>
                                    <td style='width:180px; padding:10px 12px; border:1px solid #e5e7eb; background:#f9fafb; font-size:13px; font-weight:600;'>Reference</td>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>{System.Net.WebUtility.HtmlEncode(model.RequestNumber)}</td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; background:#f9fafb; font-size:13px; font-weight:600;'>Request Subject</td>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>{System.Net.WebUtility.HtmlEncode(model.RequestSubject)}</td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; background:#f9fafb; font-size:13px; font-weight:600;'>Document Name</td>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>{System.Net.WebUtility.HtmlEncode(model.DocumentName)}</td>
                                </tr>
                                <tr>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; background:#f9fafb; font-size:13px; font-weight:600;'>Requested By</td>
                                    <td style='padding:10px 12px; border:1px solid #e5e7eb; font-size:13px;'>{System.Net.WebUtility.HtmlEncode(model.RequesterName)} ({System.Net.WebUtility.HtmlEncode(model.RequesterEmail)})</td>
                                </tr>
                                {reasonRow}
                            </table>

                            <table width='100%' cellpadding='0' cellspacing='0' border='0' style='border-collapse:collapse; margin:18px 0 22px 0;'>
                                <tr>
                                    <td colspan='4' style='font-size:15px; font-weight:600; color:#111827; padding-bottom:12px;'>Already Approved Signers Notified</td>
                                </tr>
                                <tr>
                                    <th style='padding:10px 12px; border:1px solid #e5e7eb; background:#f3f4f6; font-size:12px; text-align:left;'>Level</th>
                                    <th style='padding:10px 12px; border:1px solid #e5e7eb; background:#f3f4f6; font-size:12px; text-align:left;'>Signer Name</th>
                                    <th style='padding:10px 12px; border:1px solid #e5e7eb; background:#f3f4f6; font-size:12px; text-align:left;'>Email</th>
                                    <th style='padding:10px 12px; border:1px solid #e5e7eb; background:#f3f4f6; font-size:12px; text-align:left;'>Status</th>
                                </tr>
                                {participantRows}
                            </table>

                            <p style='margin:0; font-size:13px; line-height:1.7; color:#374151;'>
                                Regards,<br />
                                <strong>eSignature Workflow System</strong>
                            </p>
                        </td>
                    </tr>

                    <tr>
                        <td style='padding:16px 28px; background:#f9fafb; border-top:1px solid #e5e7eb;'>
                            <p style='margin:0; font-size:12px; color:#6b7280; line-height:1.6;'>
                                This is an automated email from the eSignature Workflow System. Please do not reply directly to this message.
                            </p>
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>";
        }




        [HttpPost]
        [Route("api/ESign/CancelRequest")]
        public async Task<IActionResult> CancelRequest([FromBody] CancelRequestDto model)
        {
            var mailTrace = new List<string>();

            try
            {
                int? userpsnum = _userinfoservice.GetUserPSNumberFromToken();

                if (!userpsnum.HasValue || userpsnum.Value <= 0)
                    return Unauthorized("Invalid user token.");

                if (model == null || string.IsNullOrWhiteSpace(model.RequestCode))
                    return BadRequest("Request code is required.");

                string requestCode = Uri.UnescapeDataString(model.RequestCode.Trim());

                var requestStatusInfo = await GetRequestStatusInfoAsync(requestCode);
                if (requestStatusInfo == null)
                    return NotFound("Request not found.");

                if (!string.Equals(requestStatusInfo.RequestStatus, "Completed", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(requestStatusInfo.RequestStatus, "Cancelled", StringComparison.OrdinalIgnoreCase) &&
                    requestStatusInfo.CreatedBy != userpsnum.Value)
                {
                    return BadRequest("Only creator can cancel this request.");
                }

                var resultParam = new SqlParameter("@Result", SqlDbType.NVarChar, -1)
                {
                    Direction = ParameterDirection.Output
                };

                await context.Database.ExecuteSqlRawAsync(
                    "EXEC dbo.SP_ESign_CancelRequest @RequestCode, @UserId, @CancelReason, @Result OUTPUT",
                    new SqlParameter("@RequestCode", requestCode),
                    new SqlParameter("@UserId", userpsnum.Value),
                    new SqlParameter("@CancelReason", (object?)model.CancelReason ?? DBNull.Value),
                    resultParam
                );

                string resultMessage = resultParam.Value?.ToString() ?? "Request processed";

                if (!resultMessage.Contains("cancelled", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new
                    {
                        status = "failed",
                        message = resultMessage
                    });
                }

                var requestInfo = await context.ESignMailRequestDtos
                    .FromSqlRaw(
                        "EXEC dbo.SP_ESign_GetRequestMailDetails @RequestCode",
                        new SqlParameter("@RequestCode", requestCode))
                    .AsNoTracking()
                    .ToListAsync();

                if (requestInfo.Any())
                {
                    var firstRow = requestInfo.First();

                    string requestNumber = firstRow.RequestCode;
                    string requestSubject = firstRow.RequestSubject;
                    string requesterName = firstRow.RequesterName;
                    string requesterEmail = firstRow.RequesterEmail;
                    string documentName = firstRow.DocumentName;

                    var approvedParticipants = requestInfo
                        .Where(x => x.SignerStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase))
                        .Select(x => new EsignParticipantModel
                        {
                            Level = x.LevelNo,
                            Name = x.SignerName,
                            Email = x.SignerEmail,
                            Status = "Approved"
                        })
                        .OrderBy(x => x.Level)
                        .ThenBy(x => x.Name)
                        .ToList();

                    string approvedEmails = JoinEmails(approvedParticipants.Select(x => x.Email));

                    var cancelMailModel = new EsignEmailModel
                    {
                        RequestNumber = requestNumber,
                        RequestSubject = requestSubject,
                        RequesterName = requesterName,
                        RequesterEmail = requesterEmail,
                        DocumentName = documentName,
                        RequestedOn = DateTime.Now,
                        Participants = approvedParticipants
                    };

                    if (!string.IsNullOrWhiteSpace(approvedEmails))
                    {
                        string cancelSubject = $"eSignature Cancelled | {requestNumber} | {requestSubject}";
                        string cancelBody = CreateCancellationMailFormat(cancelMailModel, model.CancelReason);

                        string finalCc = requesterEmail;
                        string finalBcc = _config.GetSection("AppSettings:UserEmailBCC").Value ?? "";

                        var cancelMailResult = await EmailTriggerMultipleTo(
                            cancelSubject,
                            cancelBody,
                            finalCc,
                            finalBcc,
                            approvedEmails
                        );

                        mailTrace.Add($"Cancellation mail => TO:{approvedEmails} CC:{finalCc} => {cancelMailResult}");
                    }
                    else
                    {
                        mailTrace.Add("No approved signer found. No cancellation email sent.");
                    }

                    await InsertAuditLog(
                        requestCode,
                        null,
                        "Request Cancelled",
                        requesterName,
                        requesterEmail,
                        userpsnum,
                        "Cancelled",
                        $"Request cancelled by creator. Reason: {model.CancelReason}"
                    );
                }
                else
                {
                    await InsertAuditLog(
                        requestCode,
                        null,
                        "Request Cancelled",
                        "System",
                        "",
                        userpsnum,
                        "Cancelled",
                        $"Request cancelled by creator. Reason: {model.CancelReason}"
                    );
                }

                return Ok(new
                {
                    status = "success",
                    message = resultMessage,
                    mailTrace = mailTrace
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }





    }
}
