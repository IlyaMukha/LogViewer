namespace LogViewer.Models
{
    public class Project
    {
        public string Name { get; set; } = string.Empty;
        public string PhysicalPath { get; set; } = string.Empty;
        public string ConfigTargetPath { get; set; } = string.Empty;
        public string ConfigPath { get; set; } = string.Empty;
        public string LogsPath { get; set; } = string.Empty;
        public List<LogDirectorie> LogDirectories { get; set; } = new();
    }
    public class LogDirectorie
    {
        public string Name { get; set; } = string.Empty;
        public string ShortName { get; set; } = string.Empty;
    }
}
