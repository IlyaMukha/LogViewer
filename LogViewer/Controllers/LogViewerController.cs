using ConsoleApp1;
using LogViewer.DTO;
using LogViewer.Models;
using LogViewer.Servises;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Web.Administration;
using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;

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
        public async Task<ActionResult<ResultDto>> GetEventViewerLogs([FromBody] GetEventViewerLogsRequest request)
        {
            try
            {
                var res = EventViewerService.GetEvetViewerLog(request.StartDate, request.EndDate);
                var result = new ResultDto { logs = res };
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
        public async Task<ActionResult<ResultDto>> ReadLogFile([FromBody] LogRequestDto request)
        {
            try
            {                
                var records = await LogFileService.ReadLogFileNewAsync(request.Url);
                var result = new ResultDto { logs = records };
                return Ok(result);
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

            var concurrentLogs = new ConcurrentBag<LogEntity>();

            var logFiles = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories);

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 4
            };

            await Parallel.ForEachAsync(logFiles, parallelOptions, async (file, cancellationToken) =>
            {
                try
                {
                    var fileLogs = await LogFileService.ReadLogFileNewAsync(file);

                    foreach (var log in fileLogs)
                    {
                        concurrentLogs.Add(log);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка параллельного парсинга файла {file}: {ex.Message}");
                }
            });

            var allLogs = concurrentLogs.ToList();

            var groupedLogs = allLogs
                .GroupBy(log => LogFileService.NormalizeText(log.Title))
                .Select(group =>
                {
                    var bestLog = group.OrderByDescending(g => g.FullText.Length).First();

                    string cleanFullText = bestLog.FullText
                        .Replace("\\u002B", "+")
                        .Replace("\\n", "\n")
                        .Replace("\\r", "\r");

                    try
                    {
                        cleanFullText = Uri.UnescapeDataString(cleanFullText);
                    }
                    catch { }

                    var mergedImpacts = group
                        .SelectMany(g => g.LogOfImpactList)
                        .GroupBy(impact => impact.File)
                        .Select(fileGroup => new LogOfImpact
                        {
                            File = fileGroup.Key,
                            Time = fileGroup.Select(i => i.Time).OrderBy(t => t).First(),
                        })
                        .ToList();

                    return new LogEntity
                    {
                        Id = bestLog.Id,
                        Type = bestLog.Type,
                        Title = group.Key,
                        FullText = cleanFullText.Trim(),

                        StartDate = group.Min(g => g.StartDate),
                        EndDate = group.Max(g => g.EndDate),

                        LogOfImpactList = group.SelectMany(g => g.LogOfImpactList)
                            .Select(s => new LogOfImpact
                            {
                                File = s.File,
                                Time = s.Time,
                                Text = s.Text
                            })
                            .ToList()
                    };
                })
                .ToList();

            return new ResultDto { logs = groupedLogs };
        }
    }
}

