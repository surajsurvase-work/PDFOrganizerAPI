using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PDFOrganizerAPI.Models;
using PDFOrganizerAPI.Services;
using PdfiumViewer;

using PdfSharp.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;

using PdfiumDoc = PdfiumViewer.PdfDocument;
using DrawingImage = System.Drawing.Image;
using DrawingColor = System.Drawing.Color;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Signatures;

namespace PDFOrganizerAPI.Controllers
{
    [ApiController]
    public class ESignaturePDFSharpController : ControllerBase
    {
        private readonly ITokenService _userinfoservice;
        private readonly IConfiguration _config;
        private readonly ESGContext context = new ESGContext();

        public ESignaturePDFSharpController(
            ITokenService userInfoService,
            IConfiguration configuration)
        {
            _config = configuration;
            _userinfoservice = userInfoService;
        }

        [HttpGet]
        [Route("api/ESign/GetSignedPdf4/{requestCode}")]
        public async Task<IActionResult> GetSignedPdf(string requestCode)
        {
            try
            {
                requestCode = Uri.UnescapeDataString(requestCode);

                var visualPdfBytes = await GenerateSignedPdf(requestCode);
                var finalSignedPdfBytes = ApplyDigitalSignature(visualPdfBytes);

                return File(finalSignedPdfBytes, "application/pdf", "SignedDocument.pdf");
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        private async Task<byte[]> GenerateSignedPdf(string requestCode)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var requests = await context.SubmittedRequests
                .FromSqlRaw("EXEC SP_ESign_GetSubmittedRequests")
                .ToListAsync();

            var requestData = requests.FirstOrDefault(r => r.RequestCode == requestCode);
            if (requestData == null)
                throw new Exception("Request not found");

            string filePath = requestData.FilePath;
            if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
                throw new Exception("File not found");

            var signatures = await context.RequestDetails
                .FromSqlRaw(
                    "EXEC SP_ESign_GetRequestDetails @RequestCode",
                    new SqlParameter("@RequestCode", requestCode))
                .ToListAsync();

            var signedUsers = signatures
                .Where(x =>
                    x.ES_Siganture_Status != null &&
                    x.ES_Siganture_Status.Trim().Equals("Approved", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(x.ES_Signer_Siganture))
                .ToList();

            var renderedPages = new List<(byte[] ImageBytes, float WidthPt, float HeightPt)>();

            using var pdfiumDoc = PdfiumDoc.Load(filePath);

            int totalPages = pdfiumDoc.PageCount;

            for (int pageIndex = 0; pageIndex < totalPages; pageIndex++)
            {
                int pageNumber = pageIndex + 1;
                const float renderScale = 3f;

                var pageSizePt = pdfiumDoc.PageSizes[pageIndex];

                int renderWidth = Math.Max(1, (int)Math.Round(pageSizePt.Width * renderScale));
                int renderHeight = Math.Max(1, (int)Math.Round(pageSizePt.Height * renderScale));

                using var bitmap = new Bitmap(renderWidth, renderHeight, PixelFormat.Format32bppArgb);

                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(DrawingColor.White);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
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
                    g.DrawImage(rendered, 0, 0, renderWidth, renderHeight);
                }

                var pageUsers = signedUsers
                    .Where(s => s.PageNumber == pageNumber)
                    .ToList();

                if (pageUsers.Any())
                {
                    using var g = Graphics.FromImage(bitmap);

                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                    foreach (var sig in pageUsers)
                    {
                        if (!sig.PositionX.HasValue || !sig.PositionY.HasValue ||
                            !sig.BoxWidth.HasValue || !sig.BoxHeight.HasValue)
                            continue;

                        float x = (float)sig.PositionX.Value * renderWidth;
                        float y = (float)sig.PositionY.Value * renderHeight;
                        float w = (float)sig.BoxWidth.Value * renderWidth;
                        float h = (float)sig.BoxHeight.Value * renderHeight;

                        float signHeight = h * 0.70f;
                        float nameHeight = h * 0.15f;
                        float timeHeight = h * 0.15f;

                        float nameY = y + signHeight;
                        float timeY = y + signHeight + nameHeight;

                        float padding = Math.Min(w, h) * 0.04f;

                        float sigX = x + padding;
                        float sigY = y + padding;
                        float sigW = w - (padding * 2f);
                        float sigH = signHeight - (padding * 2f);

                        float realSigX = sigX;
                        float realSigW = sigW;

                        byte[] imgBytes = ExtractBase64ImageBytes(sig.ES_Signer_Siganture);

                        if (imgBytes.Length > 0)
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
                            g.DrawLine(pen, realSigX, nameY, realSigX + realSigW, nameY);
                        }

                        string signerName = sig.SignerName ?? string.Empty;
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
                }

                using var msImg = new MemoryStream();
                bitmap.Save(msImg, System.Drawing.Imaging.ImageFormat.Png);

                renderedPages.Add((
                    msImg.ToArray(),
                    (float)pageSizePt.Width,
                    (float)pageSizePt.Height
                ));
            }

            var document = QuestPDF.Fluent.Document.Create(container =>
            {
                foreach (var page in renderedPages)
                {
                    container.Page(pdfPage =>
                    {
                        pdfPage.Size(page.WidthPt, page.HeightPt, Unit.Point);
                        pdfPage.Margin(0);
                        pdfPage.PageColor(Colors.White);

                        pdfPage.Content()
                            .Image(page.ImageBytes)
                            .FitArea();
                    });
                }
            });

            return document.GeneratePdf();
        }

        private byte[] ApplyDigitalSignature(byte[] pdfBytes)
        {
            if (pdfBytes == null || pdfBytes.Length == 0)
                throw new Exception("Generated PDF is empty.");

            string pfxPath = Path.Combine(
                AppContext.BaseDirectory,
                "Certificates",
                "esign-cert.pfx"
            );

            string pfxPassword = "Test@123";

            if (!System.IO.File.Exists(pfxPath))
                throw new Exception("Signing certificate not found.");

            var certificate = new X509Certificate2(
                pfxPath,
                pfxPassword,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet
            );

            using var inputStream = new MemoryStream(pdfBytes);
            using var outputStream = new MemoryStream();

            var document = PdfReader.Open(inputStream, PdfDocumentOpenMode.Modify);

            var options = new DigitalSignatureOptions
            {
                ContactInfo = "WETIC eSign",
                Location = "LTC-WETIC",
                Reason = "Final approved document"
                // No Rectangle
                // No AppearanceHandler
            };

            var signer = new PdfSharpDefaultSigner(
                certificate,
                PdfMessageDigestType.SHA256
            );

            DigitalSignatureHandler.ForDocument(document, signer, options);

            document.Save(outputStream, false);

            return outputStream.ToArray();
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
                base64 = base64[(commaIndex + 1)..];

            return Convert.FromBase64String(base64);
        }

        private static string FormatSignatureDate(DateTime? signedOn)
        {
            if (!signedOn.HasValue)
                return string.Empty;

            return signedOn.Value.ToString("dd-MMM-yyyy HH:mm", CultureInfo.InvariantCulture);
        }
    }
}