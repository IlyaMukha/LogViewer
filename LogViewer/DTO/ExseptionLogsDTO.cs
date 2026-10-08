namespace LogViewer.DTO
{
    public class ResultDto
    {
        public List<LogsDTO> logsDTOs { get; set; } = new();
        public bool IsExseption { get; set; }
        public EexseptionDto? Exseptions { get; set; }
    }
    public class LogsDTO
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public LogType LogType { get; set; }
        public int LogCount { get; set; }
    }
    public class EexseptionDto
    {
        public string ErrorMessage { get; set; } = string.Empty;
    }
    public enum LogType
    {
        Information = 3,
        Warning = 1,
        Exseption = 2,

    }
}
