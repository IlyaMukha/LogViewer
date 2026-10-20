namespace LogViewer.DTO
{
    public class LogRequestDto
    {
        public string Url { get; set; } = string.Empty;
        public string UrlConfig { get; set; } = string.Empty;
    }

    public sealed class ReadLogFileRequest
    {
        public string Path { get; set; } = "";
    }


    public class GetEventViewerLogsRequest
    {
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }
}
