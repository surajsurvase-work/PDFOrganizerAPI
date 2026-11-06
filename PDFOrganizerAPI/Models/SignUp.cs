using System.ComponentModel.DataAnnotations;

namespace PDFOrganizerAPI.Models
{
    public class UserlistsGrid
    {
        [Key]
        public string EA_Email { get; set; }
        public int EA_Id { get; set; }
        public double? EA_PS_Number { get; set; }
        public string EA_Name { get; set; }
        public string EA_Gender { get; set; }
        public string EA_Cadre { get; set; }
        public string EA_Designation { get; set; }
        public string EA_Job_Code { get; set; }
        public string EA_Job_Desc { get; set; }
        public string EA_SBG { get; set; }
        public string EA_BU { get; set; }
        public string EA_Base_Location { get; set; }
        public string EA_Office_Site { get; set; }
        public int EA_IsActive { get; set; }
        public int EA_IsAdmin { get; set; }

    }
    public class Userlist
    {
        [Key]
        public string EA_Email { get; set; }
        public int EA_IsActive { get; set; }
    }

    public class Json
    {
        public string JsonInput { get; set; }
    }
}
