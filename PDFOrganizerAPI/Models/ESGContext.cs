    using System.Collections.Generic;
    using Microsoft.EntityFrameworkCore;
    using static PDFOrganizerAPI.Controllers.ESignatureController;


    namespace PDFOrganizerAPI.Models
    {
        public partial class ESGContext : DbContext
        {
            public ESGContext()
            {

            }

            public ESGContext(DbContextOptions<ESGContext> options)
               : base(options)
            {
            }

            public virtual DbSet<Userlist> EmployeeAccess { get; set; }
            public virtual DbSet<EmployeeAccess> EmployeeGridAccess { get; set; }
            public virtual DbSet<EmployeeDropdownDto> EmployeeDropdowns { get; set; }
            public virtual DbSet<Menus> Menus { get; set; }
            public virtual DbSet<MenusByUid> MenusByUid { get; set; }
            public virtual DbSet<MenusDDN> MenusDDN { get; set; }
            public virtual DbSet<RoleMasterModel> Roles { get; set; }
            public virtual DbSet<RoleMenu> RoleMenu { get; set; }
            public virtual DbSet<UserRoles> UserRoles { get; set; }
            public virtual DbSet<ESignatureResult> ESignatureResults { get; set; }
            public DbSet<SubmittedRequestDto> SubmittedRequests { get; set; }
            public DbSet<RequestDetailsDto> RequestDetails { get; set; }
            public DbSet<EmployeeSignerDto> EmployeeSignerDtos { get; set; }
            public DbSet<ESignMailRequestDto> ESignMailRequestDtos { get; set; }
            public DbSet<UserSignature> UserSignature { get; set; }
            public DbSet<AuditReportHeaderDto> AuditReportHeaderDtos { get; set; }
            public DbSet<AuditReportSignerDto> AuditReportSignerDtos { get; set; }
            public DbSet<AuditReportEventDto> AuditReportEventDtos { get; set; }

            public DbSet<RequestStatusInfoDto> RequestStatusInfos { get; set; }
            public DbSet<FilePathDto> FilePathDtos { get; set; }
        public DbSet<SignedPdfRequestDto> SignedPdfRequestDtos { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                base.OnModelCreating(modelBuilder);

                modelBuilder.Entity<ESignatureResult>().HasNoKey();
                modelBuilder.Entity<SubmittedRequestDto>().HasNoKey();
                modelBuilder.Entity<RequestDetailsDto>().HasNoKey();
                modelBuilder.Entity<UserSignature>().HasNoKey();
                modelBuilder.Entity<EmployeeSignerDto>().HasNoKey();
                modelBuilder.Entity<EmployeeAccess>().ToTable("EmployeeAccess", "dbo");
                modelBuilder.Entity<ESignMailRequestDto>().HasNoKey();
                modelBuilder.Entity<AuditReportHeaderDto>().HasNoKey();
                modelBuilder.Entity<AuditReportSignerDto>().HasNoKey();
                modelBuilder.Entity<AuditReportEventDto>().HasNoKey();
                modelBuilder.Entity<RequestStatusInfoDto>().HasNoKey();
                modelBuilder.Entity<FilePathDto>().HasNoKey();
                modelBuilder.Entity<SignedPdfRequestDto>().HasNoKey();
        }




            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                => optionsBuilder.UseSqlServer(new ConfigurationBuilder().AddJsonFile("appsettings.json").Build().GetConnectionString("appdbConnect"));

        }
    }
