using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Security.Cryptography;
using PDFOrganizerAPI.Models;
using PDFOrganizerAPI.Services;
using static PDFOrganizerAPI.Controllers.ESignatureController;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace PDFOrganizerAPI.Controllers
{

    [ApiController]
    public class ESignatureReportController : ControllerBase
    {

        private readonly ITokenService _userinfoservice;
        internal readonly IConfiguration _config;
        readonly ESGContext context = new ESGContext();

        public ESignatureReportController(ITokenService UserInfoService, IConfiguration configuration)
        {
            _config = configuration;
            _userinfoservice = UserInfoService;
        }






        [HttpGet]
        [Route("api/ESign/DownloadAuditReport")]
        public async Task<IActionResult> DownloadAuditReport([FromQuery] string requestCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(requestCode))
                    return BadRequest("requestCode is required");
                QuestPDF.Settings.License = LicenseType.Community;

                var requestCodeParam = new SqlParameter("@RequestCode", requestCode);

                var headerList = await context.AuditReportHeaderDtos
                    .FromSqlRaw("EXEC dbo.SP_ESign_GetAuditReportHeader @RequestCode", requestCodeParam)
                .AsNoTracking()
                    .ToListAsync();

                var header = headerList.FirstOrDefault();

                var signers = await context.AuditReportSignerDtos
                    .FromSqlRaw("EXEC dbo.SP_ESign_GetAuditReportSigners @RequestCode", requestCodeParam)
                    .AsNoTracking()
                    .ToListAsync();

                var eventsData = await context.AuditReportEventDtos
                    .FromSqlRaw("EXEC dbo.SP_ESign_GetAuditReportEvents @RequestCode", requestCodeParam)
                    .AsNoTracking()
                    .ToListAsync();

                if (header == null)
                    return NotFound("Audit report data not found");

                string fileName = System.IO.Path.GetFileName(header.FilePath ?? "");
                string fileHash = string.Empty;

                if (!string.IsNullOrWhiteSpace(header.FilePath) && System.IO.File.Exists(header.FilePath))
                {
                    using var sha256 = SHA256.Create();
                    using var stream = System.IO.File.OpenRead(header.FilePath);
                    var hashBytes = sha256.ComputeHash(stream);
                    fileHash = BitConverter.ToString(hashBytes).Replace("-", "");
                }

                //var pdfBytes = GenerateAuditReportPdf(header, signers, eventsData, fileName, fileHash);

                //return File(
                //    pdfBytes,
                //    "application/pdf",
                //    $"AuditReport_{requestCode.Replace("/", "_")}.pdf"
                //);

                var auditPdfBytes = GenerateAuditReportPdf(header, signers, eventsData, fileName, fileHash);
                var finalPdfBytes = MergeAuditWithOriginalPdf(auditPdfBytes, header.FilePath, header.RequestCode);

                return File(
                    finalPdfBytes,
                    "application/pdf",
                    $"AuditReport_{requestCode.Replace("/", "_")}.pdf"
                );


            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }



        private byte[] MergeAuditWithOriginalPdf(byte[] auditPdfBytes, string originalFilePath, string requestCode)
        {
            using var outputDocument = new PdfDocument();

            // 1) Add audit report pages
            using (var auditStream = new MemoryStream(auditPdfBytes))
            {
                using var auditDocument = PdfReader.Open(auditStream, PdfDocumentOpenMode.Import);

                for (int i = 0; i < auditDocument.PageCount; i++)
                {
                    outputDocument.AddPage(auditDocument.Pages[i]);
                }
            }

            // 2) Add original PDF pages (NO watermark)
            if (!string.IsNullOrWhiteSpace(originalFilePath) && System.IO.File.Exists(originalFilePath))
            {
                using var originalDocument = PdfReader.Open(originalFilePath, PdfDocumentOpenMode.Import);

                for (int i = 0; i < originalDocument.PageCount; i++)
                {
                    outputDocument.AddPage(originalDocument.Pages[i]);
                }
            }

            using var outputStream = new MemoryStream();
            outputDocument.Save(outputStream, false);
            return outputStream.ToArray();
        }

        private byte[] MergeAuditWithOriginalPdf_Watermark(byte[] auditPdfBytes, string originalFilePath, string requestCode)
        {
            using var outputDocument = new PdfDocument();

            // 1) Add audit report pages
            using (var auditStream = new MemoryStream(auditPdfBytes))
            {
                using var auditDocument = PdfReader.Open(auditStream, PdfDocumentOpenMode.Import);

                for (int i = 0; i < auditDocument.PageCount; i++)
                {
                    outputDocument.AddPage(auditDocument.Pages[i]);
                }
            }

            // 2) Add original PDF pages and watermark them
            if (!string.IsNullOrWhiteSpace(originalFilePath) && System.IO.File.Exists(originalFilePath))
            {
                using var originalDocument = PdfReader.Open(originalFilePath, PdfDocumentOpenMode.Import);

                for (int i = 0; i < originalDocument.PageCount; i++)
                {
                    var importedPage = outputDocument.AddPage(originalDocument.Pages[i]);

                    using var gfx = PdfSharpCore.Drawing.XGraphics.FromPdfPage(
                        importedPage,
                        PdfSharpCore.Drawing.XGraphicsPdfPageOptions.Append // ✅ draw above content
                    );

                    var font = new PdfSharpCore.Drawing.XFont(
                        "Arial",
                        42,
                        PdfSharpCore.Drawing.XFontStyle.Bold
                    );

                    var brush = new PdfSharpCore.Drawing.XSolidBrush(
                        PdfSharpCore.Drawing.XColor.FromArgb(230, 230, 230)
                    );

                    var state = gfx.Save();

                    gfx.TranslateTransform(importedPage.Width / 2, importedPage.Height / 2);
                    gfx.RotateTransform(-35);

                    gfx.DrawString(
                        requestCode,
                        font,
                        brush,
                        new PdfSharpCore.Drawing.XRect(-400, -50, 800, 100),
                        PdfSharpCore.Drawing.XStringFormats.Center
                    );

                    gfx.Restore(state);
                }
            }

            using var outputStream = new MemoryStream();
            outputDocument.Save(outputStream, false);
            return outputStream.ToArray();
        }



        private byte[] GenerateAuditReportPdf(
    AuditReportHeaderDto header,
    List<AuditReportSignerDto> signers,
    List<AuditReportEventDto> events,
    string fileName,
    string fileHash)
        {
            var generatedOn = DateTime.Now;

            var brandDark = "#1E458A";
            var brandPrimary = "#1D4ED8";
            var borderColor = "#D9E2EC";
            var softBg = "#F8FAFC";
            var mutedText = "#64748B";
            var headingText = "#0F172A";
            var successColor = "#15803D";
            var pendingColor = "#B45309";
            var neutralColor = "#475569";

            var document = QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(18);
                    page.PageColor(Colors.White);

                    page.DefaultTextStyle(x =>
                        x.FontFamily
                        ("Segoe UI")
                         .FontSize(9.5f)
                         .FontColor(headingText)
                         .LineHeight(1.25f));

                    page.Header().Element(c =>
                        ComposeHeader(c, header, generatedOn, brandDark, brandPrimary, mutedText));

                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(18);
                        page.PageColor(Colors.White);

                        page.DefaultTextStyle(x =>
                            x.FontFamily("Segoe UI")
                             .FontSize(9.5f)
                             .FontColor(headingText)
                             .LineHeight(1.25f));

                        page.Header().Element(c =>
                            ComposeHeader(c, header, generatedOn, brandDark, brandPrimary, mutedText));

                        // ✅ watermark on full page
                        //page.Foreground().AlignCenter().AlignMiddle().Rotate(-35).Text(header.RequestCode)
                        //    .FontSize(64)
                        //    .FontColor("#E5E7EB")
                        //    .Bold();

                        page.Content().PaddingTop(12).Column(column =>
                        {
                            column.Spacing(14);

                            column.Item().Element(c =>
                                ComposeSectionTitle(c, "Document Summary", "Request and document metadata"));

                            column.Item().Element(c =>
                                ComposeSummaryTable(c, header, fileName, fileHash, generatedOn, borderColor, softBg, mutedText));

                            column.Item().Element(c =>
                                ComposeSectionTitle(c, "Signer Workflow", "Configured signer levels and completion status"));

                            column.Item().Element(c =>
                                ComposeSignerTable(c, signers, borderColor, softBg, mutedText, successColor, pendingColor, neutralColor));

                            column.Item().Element(c =>
                                ComposeSectionTitle(c, "Audit Event Trail", "Complete timeline of actions captured by the system"));

                            column.Item().Element(c =>
                                ComposeEventTable(c, events, borderColor, softBg, mutedText));

                            column.Item().Element(c =>
                                ComposeSectionTitle(c, "", ""));

                            column.Item()
                                .Border(1)
                                .BorderColor(borderColor)
                                .Background("#FAFCFF")
                                .Padding(12)
                                .Column(col =>
                                {
                                    col.Spacing(4);

                                    col.Item().Text("This report is automatically generated by the eSignature Workflow System.")
                                        .FontSize(10)
                                        .SemiBold()
                                        .FontColor(headingText);

                                    col.Item().Text("It serves as an audit record of request creation, signer actions, notifications, and completion status for the referenced document.")
                                        .FontSize(9)
                                        .FontColor(mutedText);
                                });
                        });

                        page.Footer().PaddingTop(6).BorderTop(1).BorderColor(borderColor).Row(row =>
                        {
                            row.RelativeItem().AlignLeft().Text($"Request Code: {header.RequestCode}")
                                .FontSize(8.5f)
                                .FontColor(mutedText);

                            row.RelativeItem().AlignCenter().Text("System Generated Audit Report")
                                .FontSize(8.5f)
                                .FontColor(mutedText);

                            row.RelativeItem().AlignRight().Text(text =>
                            {
                                text.Span("Page ").FontSize(8.5f).FontColor(mutedText);
                                text.CurrentPageNumber().FontSize(8.5f).SemiBold().FontColor(headingText);
                                text.Span(" / ").FontSize(8.5f).FontColor(mutedText);
                                text.TotalPages().FontSize(8.5f).SemiBold().FontColor(headingText);
                            });
                        });
                    });
                    page.Footer().PaddingTop(6).BorderTop(1).BorderColor(borderColor).Row(row =>
                    {
                        row.RelativeItem().AlignLeft().Text($"Request Code: {header.RequestCode}")
                            .FontSize(8.5f)
                            .FontColor(mutedText);

                        row.RelativeItem().AlignCenter().Text("System Generated Audit Report")
                            .FontSize(8.5f)
                            .FontColor(mutedText);

                        row.RelativeItem().AlignRight().Text(text =>
                        {
                            text.Span("Page ").FontSize(8.5f).FontColor(mutedText);
                            text.CurrentPageNumber().FontSize(8.5f).SemiBold().FontColor(headingText);
                            text.Span(" / ").FontSize(8.5f).FontColor(mutedText);
                            text.TotalPages().FontSize(8.5f).SemiBold().FontColor(headingText);
                        });
                    });
                });
            });

            return document.GeneratePdf();
        }

        private void ComposeHeader(
    IContainer container,
    AuditReportHeaderDto header,
    DateTime generatedOn,
    string brandDark,
    string brandPrimary,
    string mutedText)
        {
            container.Column(column =>
            {
                column.Item()
                    .Background(brandDark)
                    .PaddingVertical(16)
                    .PaddingHorizontal(18)
                    .Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Spacing(3);

                            left.Item().Text("WETeSign Audit Report")
                                .FontSize(21)
                                .Bold()
                                .FontColor(Colors.White);

                            left.Item().Text("Audit evidence for digital signing workflow")
                                .FontSize(9.5f)
                                .FontColor("#CBD5E1");
                        });

                        row.ConstantItem(170).AlignRight().Column(right =>
                        {
                            right.Spacing(3);

                            right.Item().AlignRight().Text("Generated On")
                                .FontSize(8.5f)
                                .FontColor("#93C5FD");

                            right.Item().AlignRight().Text(generatedOn.ToString("dd-MMM-yyyy hh:mm tt"))
                                .FontSize(10)
                                .SemiBold()
                                .FontColor(Colors.White);

                            right.Item().AlignRight().Text($"Request: {header.RequestCode}")
                                .FontSize(8.5f)
                                .FontColor("#CBD5E1");
                        });
                    });

                column.Item()
                    .Background("#EFF6FF")
                    .Border(1)
                    .BorderColor("#DBEAFE")
                    .PaddingVertical(8)
                    .PaddingHorizontal(12)
                    .Row(row =>
                    {
                        row.RelativeItem().Text($"Document: {System.IO.Path.GetFileName(header.FilePath ?? "")}")
                            .FontSize(9)
                            .SemiBold()
                            .FontColor(brandPrimary);

                        row.ConstantItem(140).AlignRight().Text($"Status: {header.RequestStatus}")
                            .FontSize(9)
                            .SemiBold()
                            .FontColor(brandPrimary);
                    });
            });
        }


        private void ComposeSectionTitle(IContainer container, string title, string subtitle)
        {
            container.Column(column =>
            {
                column.Item().Text(title)
                    .FontSize(13.5f)
                    .Bold()
                    .FontColor("#0F172A");

                column.Item().PaddingTop(1).Text(subtitle)
                    .FontSize(8.5f)
                    .FontColor("#64748B");
            });
        }


        private void ComposeSummaryTable(
     IContainer container,
     AuditReportHeaderDto header,
     string fileName,
     string fileHash,
     DateTime generatedOn,
     string borderColor,
     string softBg,
     string mutedText)
        {
            var rows = new List<(string Label, string Value)>
    {
        ("Request Code", header.RequestCode ?? ""),
        ("Request Subject", header.RequestType ?? ""),
        ("Document Name", fileName ?? ""),
        ("Total Pages", header.TotalPages.ToString()),
        ("Created By", $"{header.CreatedByName} ({header.CreatedByEmail})"),
        ("Created On", header.CreatedOn.ToString("dd-MMM-yyyy hh:mm tt")),
        ("Current Status", header.RequestStatus ?? ""),
        ("Document SHA-256", fileHash ?? ""),
        ("Audit Generated On", generatedOn.ToString("dd-MMM-yyyy hh:mm tt"))
    };

            container
                .Border(1)
                .BorderColor(borderColor)
                .Background(Colors.White)
                .Padding(8)
                .Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(155);
                        columns.RelativeColumn();
                    });

                    foreach (var row in rows)
                    {
                        table.Cell().Element(CellLabel).Text(row.Label)
                            .FontSize(9)
                            .SemiBold()
                            .FontColor("#334155");

                        table.Cell().Element(CellValue).Text(row.Value)
                            .FontSize(9)
                            .FontColor("#0F172A");
                    }
                });

            IContainer CellLabel(IContainer c) => c
                .BorderBottom(1)
                .BorderColor(borderColor)
                .Background(softBg)
                .PaddingVertical(8)
                .PaddingHorizontal(10)
                .AlignMiddle();

            IContainer CellValue(IContainer c) => c
                .BorderBottom(1)
                .BorderColor(borderColor)
                .PaddingVertical(8)
                .PaddingHorizontal(10)
                .ShowEntire()
                .DefaultTextStyle(x => x.FontColor("#0F172A"));
        }

        private void ComposeSignerTable(
     IContainer container,
     List<AuditReportSignerDto> signers,
     string borderColor,
     string softBg,
     string mutedText,
     string successColor,
     string pendingColor,
     string neutralColor)
        {
            container
                .Border(1)
                .BorderColor(borderColor)
                .Padding(8)
                .Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(55);
                        columns.RelativeColumn(1.5f);
                        columns.RelativeColumn(2.2f);
                        columns.ConstantColumn(80);
                        columns.ConstantColumn(105);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(HeaderCellStyle).Text("Level").Bold();
                        header.Cell().Element(HeaderCellStyle).Text("Signer Name").Bold();
                        header.Cell().Element(HeaderCellStyle).Text("Email").Bold();
                        header.Cell().Element(HeaderCellStyle).Text("Status").Bold();
                        header.Cell().Element(HeaderCellStyle).Text("Signed On").Bold();
                    });

                    if (signers == null || signers.Count == 0)
                    {
                        table.Cell().ColumnSpan(5).Element(DataCellStyle).Padding(12)
                            .Text("No signer records found.")
                            .FontColor(mutedText);
                        return;
                    }

                    foreach (var s in signers)
                    {
                        var status = (s.Status ?? "").Trim();
                        var statusColor = neutralColor;

                        if (status.Equals("Approved", StringComparison.OrdinalIgnoreCase) ||
                            status.Equals("Signed", StringComparison.OrdinalIgnoreCase) ||
                            status.Equals("Completed", StringComparison.OrdinalIgnoreCase))
                            statusColor = successColor;
                        else if (status.Equals("Pending", StringComparison.OrdinalIgnoreCase) ||
                                 status.Equals("Awaiting", StringComparison.OrdinalIgnoreCase))
                            statusColor = pendingColor;

                        table.Cell().Element(DataCellStyle).Text($"L{s.LevelNo}")
                            .SemiBold();

                        table.Cell().Element(DataCellStyle).Text(s.SignerName ?? "");

                        table.Cell().Element(DataCellStyle).Text(s.SignerEmail ?? "")
                            .FontSize(8.8f)
                            .FontColor("#334155");

                        table.Cell().Element(DataCellStyle).AlignCenter().PaddingVertical(5).Text(status)
                            .FontSize(8.5f)
                            .SemiBold()
                            .FontColor(statusColor);

                        table.Cell().Element(DataCellStyle).Text(
                            s.SignedOn.HasValue ? s.SignedOn.Value.ToString("dd-MMM-yyyy hh:mm tt") : "-"
                        )
                        .FontColor(s.SignedOn.HasValue ? "#0F172A" : mutedText);
                    }
                });

            IContainer HeaderCellStyle(IContainer c) => c
                .Background("#EAF2FF")
                .BorderBottom(1)
                .BorderColor(borderColor)
                .PaddingVertical(8)
                .PaddingHorizontal(8)
                .DefaultTextStyle(x => x.FontSize(9).FontColor("#1E3A8A"));

            IContainer DataCellStyle(IContainer c) => c
                .BorderBottom(1)
                .BorderColor("#E2E8F0")
                .PaddingVertical(8)
                .PaddingHorizontal(8)
                .AlignMiddle();
        }

        private void ComposeEventTable(
     IContainer container,
     List<AuditReportEventDto> events,
     string borderColor,
     string softBg,
     string mutedText)
        {
            container
                .Border(1)
                .BorderColor(borderColor)
                .Padding(8)
                .Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(82);    // Date & Time
                        columns.RelativeColumn(1.25f); // User
                        columns.RelativeColumn(0.95f); // Action
                        columns.ConstantColumn(42);    // Level
                        columns.RelativeColumn(0.95f); // Status
                        columns.RelativeColumn(0.85f); // IP
                        columns.RelativeColumn(1.25f); // Device
                        columns.RelativeColumn(1.1f);  // Remarks
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(HeaderCellStyle).Text("Date & Time").Bold();
                        header.Cell().Element(HeaderCellStyle).Text("User").Bold();
                        header.Cell().Element(HeaderCellStyle).Text("Action").Bold();
                        header.Cell().Element(HeaderCellStyle).Text("Level").Bold();
                        header.Cell().Element(HeaderCellStyle).Text("Status").Bold();
                        header.Cell().Element(HeaderCellStyle).Text("IP Address").Bold();
                        header.Cell().Element(HeaderCellStyle).Text("Device").Bold();
                        header.Cell().Element(HeaderCellStyle).Text("Remarks").Bold();
                    });

                    if (events == null || events.Count == 0)
                    {
                        table.Cell().ColumnSpan(8).Element(DataCellStyle).Padding(12)
                            .Text("No audit events found.")
                            .FontColor(mutedText);
                        return;
                    }

                    foreach (var e in events)
                    {
                        table.Cell().Element(DataCellStyle).Column(col =>
                        {
                            col.Item().Text(e.EventDateTime.ToString("dd-MMM-yyyy"))
                                .FontSize(8.7f)
                                .SemiBold();

                            col.Item().Text(e.EventDateTime.ToString("hh:mm tt"))
                                .FontSize(8.2f)
                                .FontColor(mutedText);
                        });

                        table.Cell().Element(DataCellStyle).Column(col =>
                        {
                            col.Spacing(1);

                            col.Item().Text(e.UserName ?? "")
                                .FontSize(8.8f)
                                .SemiBold();

                            if (!string.IsNullOrWhiteSpace(e.UserEmail))
                            {
                                col.Item().Text(e.UserEmail)
                                    .FontSize(7.9f)
                                    .FontColor(mutedText);
                            }
                        });

                        table.Cell().Element(DataCellStyle).Text(e.EventType ?? "")
                            .FontSize(8.6f);

                        table.Cell().Element(DataCellStyle).AlignCenter().Text(e.LevelNo.HasValue ? $"L{e.LevelNo}" : "-")
                            .FontSize(8.4f)
                            .SemiBold();

                        table.Cell().Element(DataCellStyle).Text(e.ActionStatus ?? "")
                            .FontSize(8.5f)
                            .SemiBold()
                            .FontColor("#334155");

                        table.Cell().Element(DataCellStyle).Text(e.IPAddress ?? "")
                            .FontSize(8.1f)
                            .FontColor("#334155");

                        table.Cell().Element(DataCellStyle).Text(e.DeviceInfo ?? "")
                            .FontSize(8.1f)
                            .FontColor("#334155");

                        table.Cell().Element(DataCellStyle).Text(string.IsNullOrWhiteSpace(e.Remarks) ? "-" : e.Remarks)
                            .FontSize(8.1f)
                            .FontColor(string.IsNullOrWhiteSpace(e.Remarks) ? mutedText : "#334155");
                    }
                });

            IContainer HeaderCellStyle(IContainer c) => c
                .Background("#EAF2FF")
                .BorderBottom(1)
                .BorderColor(borderColor)
                .PaddingVertical(8)
                .PaddingHorizontal(6)
                .DefaultTextStyle(x => x.FontSize(8.4f).FontColor("#1E3A8A"));

            IContainer DataCellStyle(IContainer c) => c
                .BorderBottom(1)
                .BorderColor("#E2E8F0")
                .PaddingVertical(7)
                .PaddingHorizontal(6)
                .AlignTop();
        }
    }
}
