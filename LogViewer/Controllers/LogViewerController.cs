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
            var result = IISService.GetLogFiles(dir);
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

            return new ResultDto { logsDTOs = all,  };
        }

        [HttpPost("Exseptions", Name = "GetExseptionFromLogsExseptions")]
        public ResultDto GetExseptionFromLogsExseptions([FromBody] LogRequestDto requestDto)
        {
            return new ResultDto { IsExseption = true, Exseptions = new EexseptionDto { ErrorMessage = "Запрос говно, ОШИБКА!" } };
        }
    }
}

