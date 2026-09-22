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
        [HttpGet]
        public ActionResult<List<Project>> GetProjects()
        {
            var result = IISService.GetPaths();
            return Ok(result);
        }

        [HttpPost("Logs", Name = "GetExseptionFromLogs")]
        public async Task<ResultDto> GetExseptionFromLogs([FromBody] LogRequestDto requestDto)
        {
            List<LogsDTO> result = new List<LogsDTO>
            {
                new LogsDTO { Title = "[Information]:Test info 1", Description = "Test description_Information", LogType = LogType.Information },
                new LogsDTO { Title = "[Information]:Test info 2", Description = "Test description_Information", LogType = LogType.Information },
                new LogsDTO { Title = "[Warning]:Test Warning 1", Description = "Test description_Warning", LogType = LogType.Warning },
                new LogsDTO { Title = "[Warning]:Test Warning 2", Description = "Test description_Warning", LogType = LogType.Warning },
                new LogsDTO { Title = "[Exseption]:Test Exseption 1", Description = "Test description_Exseption", LogType = LogType.Exseption },
                new LogsDTO { Title = "[Exseption]:Test Exseption 2", Description = "Test description_Exseption", LogType = LogType.Exseption },
                new LogsDTO { Title = "[Exseption]:Test Exseption 3", Description = "Test description_Exseption", LogType = LogType.Exseption },
                new LogsDTO { Title = "[Exseption]:Test Exseption 4", Description = "Test description_Exseption", LogType = LogType.Exseption },
                new LogsDTO { Title = "[Exseption]:Test Exseption 5", Description = "Test description_Exseption", LogType = LogType.Exseption },
                new LogsDTO { Title = "[Exseption]:Test Exseption 6", Description = "Test description_Exseption", LogType = LogType.Exseption },
            };



            Console.Write("Путь к папке с логами: ");
            var root = requestDto.Url.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(root) || !System.IO.Directory.Exists(root))
            {
                return null;
            }

            Console.Write("Путь к nlog.config (Enter — пропустить): ");
            var nlogPath = requestDto.UrlConfig.Trim().Trim('"');

            var analyzer = new LogAnalyzer();
            var all = await analyzer.AnalyzeAsync(root, nlogPath);

            Console.WriteLine();
            Console.WriteLine($"Уникальных сообщений: {all.Count}");
            Console.WriteLine($"  из них ERROR/FATAL : {all.Count(e => e.IsError)}");
            Console.WriteLine($"  из них WARN        : {all.Count(e => !e.IsError)}");
            Console.WriteLine($"Всего вхождений     : {all.Sum(e => e.Count)}");
            Console.WriteLine();

            List<LogsDTO> result2 = new List<LogsDTO>
            {
               
                new LogsDTO { Title = "[Exseption]:Test Exseption 6", Description = "Test description_Exseption", LogType = LogType.Exseption },
            };


            foreach (var e in all)
            {
                result2.Add(new LogsDTO { Title = e.Message });
            }



            return new ResultDto { logsDTOs = result2};
        }

        [HttpPost("Exseptions", Name = "GetExseptionFromLogsExseptions")]
        public ResultDto GetExseptionFromLogsExseptions([FromBody] LogRequestDto requestDto)
        {
            return new ResultDto { IsExseption = true, Exseptions = new EexseptionDto { ErrorMessage = "Запрос говно, ОШИБКА!" } };
        }
    }
}
