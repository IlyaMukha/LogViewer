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

            List<LogsDTO> result = new List<LogsDTO>();

            foreach (var e in all)
            {
                result.Add(new LogsDTO { Title = e.Title, LogType = LogType.Warning, LogCount = e.Exceptions.Count });
            }

            return new ResultDto { logsDTOs = result,  };
        }

        [HttpPost("Exseptions", Name = "GetExseptionFromLogsExseptions")]
        public ResultDto GetExseptionFromLogsExseptions([FromBody] LogRequestDto requestDto)
        {
            return new ResultDto { IsExseption = true, Exseptions = new EexseptionDto { ErrorMessage = "Запрос говно, ОШИБКА!" } };
        }
    }
}

