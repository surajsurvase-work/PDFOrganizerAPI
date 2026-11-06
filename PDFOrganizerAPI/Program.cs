using PDFOrganizerAPI.Models;
using PDFOrganizerAPI.Services;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using System.Security.Cryptography;
using Microsoft.AspNetCore.StaticFiles;
using System.Data;
using System.Data.SqlClient;
using PDFOrganizerAPI.Controllers;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
  c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
  {
    Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
    Name = "Authorization",
    In = Microsoft.OpenApi.Models.ParameterLocation.Header,
    Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
    Scheme = "bearer",
    BearerFormat = "JWT"
  });

  c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});


// Register ESGContext with dependency injection
builder.Services.AddDbContext<ESGContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("appdbConnect"))); // Adjust connection string name as necessary


builder.Services.AddCors(opts =>
opts.AddPolicy("MyCors", options => options
                                        .WithOrigins("https://pdforguatwet.lntecc.com", "https://localhost:4200","https://pdforgdevwet.lntecc.com","https://pdforg.wet.lntecc.com")
                                         .WithMethods("GET", "POST")
                                        .AllowAnyHeader()
                                        .SetIsOriginAllowed(origin => true)));


// Register  class

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITokenService, GetEmailService>();
builder.Services.AddHttpClient();


builder.Services.AddSingleton<AiJsonChatbotDataService>();
builder.Services.AddScoped<ChatbotService>();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();


}



app.UseCors("MyCors");
app.UseHttpsRedirection();


//Proper MIME Types Start

var provider = new FileExtensionContentTypeProvider();
provider.Mappings[".pdf"] = "application/pdf";
provider.Mappings[".jpg"] = "image/jpeg";
provider.Mappings[".jpeg"] = "image/jpeg";
provider.Mappings[".png"] = "image/png";
provider.Mappings[".doc"] = "application/msword";
provider.Mappings[".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
provider.Mappings[".xls"] = "application/vnd.ms-excel";
provider.Mappings[".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
provider.Mappings[".ppt"] = "application/vnd.ms-powerpoint";
provider.Mappings[".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = provider
});

//Proper MIME Types End




//new header added suraj
app.Use(async (contex, next) =>
{

    var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)); // .NET 6+
    contex.Items["CSPNonce"] = nonce;

    //This line removes the Server header from the HTTP response, which helps in preventing the disclosure of server information.
    contex.Response.Headers.Remove("Server");


    //contex.Response.Headers["X-Content-Type-Options"] = "nosniff";

    if (contex.Request.Path == "/" || contex.Request.Path == "/home")
    {
        contex.Response.ContentType = "text/html; charset=utf-8";
    }


    //This header prevents clickjacking by not allowing the page to be displayed in a frame.
    // contex.Response.Headers["X-Frame-Options"] = "DENY";
    //This header enables the Cross-Site Scripti=ng (XSS) filter in the browser.
    //contex.Response.Headers["X-XSS-Protection"] = "1; mode=block";

    //contex.Response.Headers["Content-Security-Policy"] = "default-src 'self'; connect-src 'self'; script-src 'self'";

    //Old contex.Response.Headers["Content-Security-Policy"] = "default-src 'self'; connect-src 'self' https://login.microsoftonline.com https://graph.microsoft.com data:; font-src 'self' data: fonts.gstatic.com;  img-src 'self' blob: data:;  manifest-src 'self';  media-src 'self'; object-src 'none'; script-src 'self' https://login.microsoftonline.com https://cdnjs.cloudflare.com 'strict-dynamic';  worker-src 'self';  require-trusted-types-for 'script';";

    //contex.Response.Headers.Add("Content-Security-Policy",
    // $"default-src 'self'; connect-src 'self' https://login.microsoftonline.com https://graph.microsoft.com data:; font-src 'self' data: fonts.gstatic.com; img-src 'self' blob: data:; manifest-src 'self'; media-src 'self'; object-src 'none'; script-src 'self' https://login.microsoftonline.com https://cdnjs.cloudflare.com 'nonce-{nonce}'; style-src 'self' 'nonce-{nonce}'; worker-src blob:; frame-ancestors 'none'; form-action 'self'; base-uri 'self'; require-trusted-types-for 'script';");



    // Strict-Transport-Security

    //if (contex.Response.Headers.ContainsKey("Strict-Transport-Security"))
    //{
    //    contex.Response.Headers.Remove("Strict-Transport-Security");
    //}

    //contex.Response.Headers.Add("Strict-Transport-Security",
    //    "max-age=63072000; includeSubDomains; preload");


    //cookies

    contex.Response.Cookies.Append("testCookie", "1", new CookieOptions
    {
        Path = "/pdforganizer",
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        MaxAge = TimeSpan.FromMinutes(10)
    });

    await next();
});



app.UseAuthorization();

app.MapControllers();

app.Run();
