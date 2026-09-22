namespace LogViewer.Models
{
    public class Project
    {
        public string Name { get; set; } = string.Empty;
        public string IisSite { get; set; } = string.Empty;
        public string PhysicalPath { get; set; } = string.Empty;
        public string ConfigPath { get; set; } = string.Empty;
        public string LogsPath { get; set; } = string.Empty;
        public string NewLogsPath { get; set; } = string.Empty;
        public ProjectsType ProjectType { get; set; }
    }
    public enum ProjectsType
    {
        BpmSoft,
        TerraSoft, 
        None
    }
}
