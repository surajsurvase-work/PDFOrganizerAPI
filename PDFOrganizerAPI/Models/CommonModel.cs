
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace PDFOrganizerAPI.Models
{

  public class PdfUserActionLog
  {
    [Key]
    [Required(ErrorMessage = "Tool is required")]
    public string PUA_Tool { get; set; }

    [Required(ErrorMessage = "File name is required")]
    public string PUA_File_Name { get; set; }

    [Required(ErrorMessage = "File size is required")]
    public string PUA_File_Size { get; set; }

  }

  public class Esign
  {
    [Key]
    public int Id { get; set; }  // Assuming 'Id' is the primary key (auto-increment)
    public string FileName { get; set; }
    public string Request_Type { get; set; }
    public string Created_by { get; set; }
    public string Status { get; set; }
  }

  public class EmailTable
  {
    [Key]
    public int Id { get; set; }
    public int EsignID { get; set; }  // Foreign key to main table[EsignID]
    public int Level { get; set; }
    public string? ApproverEmail { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
  }

  public class ToolUsageDto
  {
    public string PUA_Tool { get; set; }
    public int Tool_Usage_Count { get; set; }
  }

  [Keyless]
  public class EmployeeDropdownDto
  {
    public string EA_Email { get; set; } = string.Empty;
    public double? EA_PS_Number { get; set; }
  }


  public class EmployeeAccess
  {
    [Key]
    public int EA_Id { get; set; } 
    public string EA_Email { get; set; } = string.Empty; 
    public double EA_PS_Number { get; set; } 
    public string? EA_Name { get; set; }
    public string? EA_Gender { get; set; }
    public string? EA_Cadre { get; set; }
    public string? EA_Designation { get; set; }
    public string? EA_Job_Code { get; set; }
    public string? EA_Job_Desc { get; set; }
    public string? EA_SBG { get; set; }
    public string? EA_BU { get; set; }
    public string? EA_Base_Location { get; set; }
    public string? EA_Office_Site { get; set; }
    public string? EA_IC { get; set; }
    public int EA_IsActive { get; set; }
    public int EA_IsAdmin { get; set; }
    public int? EA_Inserted_By { get; set; }
  }

  public class Menus
  {
    [Key]
    public string PDF_Menu_Id { get; set; }           
    public string PDF_Menu_Name { get; set; }        
    public string? PDF_Menu_Parent_Id { get; set; }   
    public string? PDF_Menu_URL { get; set; }         
    public string? PDF_Menu_Icon { get; set; }        
    public int? PDF_Menu_Sort_Order { get; set; }     
    public string PDF_Menu_Status { get; set; }       
    public string? PDF_Menu_Description { get; set; }         
  }
  public class MenusDDN
  {
    [Key]
    public string PDF_Menu_Id { get; set; }          
    public string PDF_Menu_Name { get; set; }
  }

    public class RoleMasterModel
  {
    [Key]
    public string PDF_Role_Id { get; set; }             
    public string PDF_Role_Name { get; set; }
    public string PDF_Role_Description { get; set; }
    public string PDF_Role_Status { get; set; }
  }


  public class RoleMenu
  {
      [Key]
      public string PDFTRM_Id { get; set; }
      public string PDFTRM_Role_Id { get; set; }
      public string PDFTRM_Menu_Id { get; set; }
      public string PDFTRM_Menu_Name { get; set; }
      public string PDFTRM_Status { get; set; }
    }


    public class UserRoles
  {
    [Key]
    public string PUR_ID { get; set; }
    public string? Name{ get; set; }
    public string? EmailID { get; set; }
    public string? Role { get; set; }
    public string? Status { get; set; }
  }

  public class MenusByUid
  {
    [Key]
    public string? PDF_Menu_Name { get; set; }
    public string? PDF_Menu_Description { get; set; }
    public string? PDF_Menu_Icon { get; set; }
    public string? PDF_Menu_URL { get; set; }
    public int? PDF_Menu_Sort_Order { get; set; }
  }

    public class FilePathDto
    {
        public string RequestCode { get; set; }
        public string FilePath { get; set; }
    }


    public class SignedPdfRequestDto
    {
        public string RequestCode { get; set; }
        public string FilePath { get; set; }
        public string SignedFilePath { get; set; }
        public string RequestStatus { get; set; }
        public string Siganture_Status { get; set; }
    }

}
