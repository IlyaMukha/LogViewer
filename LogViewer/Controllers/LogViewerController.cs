using ConsoleApp1;
using LogViewer.DTO;
using LogViewer.Models;
using LogViewer.Servises;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Web.Administration;

namespace LogViewer.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class LogViewerController : ControllerBase
    {
        [HttpGet("Projects")]
        public ActionResult<List<Project>> GetProjects()
        {
            var result = IISService.GetPaths();
            return Ok(result);
        }
        [HttpPost("SubDir")]
        public ActionResult<List<LogDirectorie>> GetSubDir([FromBody] string dir)
        {
            var result = LogFileService.GetLogFiles(dir);
            return Ok(result);
        }
        [HttpPost("EventViewer")]
        public async Task<ActionResult<List<LogEntry>>> GetEventViewerLogs([FromBody] GetEventViewerLogsRequest request)
        {
            try
            {
                var result = EventViewerService.GetEvetViewerLog(request.StartDate, request.EndDate);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = ex.Message
                });
            }
        }
        [HttpPost("ReadLogFile")]
        public async Task<ActionResult<List<LogRecord>>> ReadLogFile([FromBody] ReadLogFileRequest request)
        {
            try
            {
                var records = await LogFileService.ReadLogFileAsync(request.Path);
                return Ok(records);
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return StatusCode(403, new
                {
                    message = "Нет доступа к указанному файлу."
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidDataException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (IOException)
            {
                return StatusCode(500, new
                {
                    message = "Ошибка при чтении файла."
                });
            }
        }

        [HttpPost("Logs", Name = "GetExseptionFromLogs")]
        public async Task<ResultDto> GetExseptionFromLogs([FromBody] LogRequestDto requestDto)
        {
            var root = requestDto.Url.Trim().Trim('"');

            if (string.IsNullOrWhiteSpace(root) || !System.IO.Directory.Exists(root))
            {
                return null;
            }

            var nlogPath = requestDto.UrlConfig.Trim().Trim('"');

            var analyzer = new LogAnalyzer();
            var all = await analyzer.AnalyzeAsync(root, nlogPath);

            return new ResultDto { logsDTOs = all,  };
        }
    }
}

