using LogViewer.DTO;
using LogViewer.Models;
using Microsoft.Web.Administration;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;

namespace LogViewer.Servises
{
    public static class IISService
    {
        public static List<Project> GetPaths()
        {
            var result = new List<Project>();

            using var serverManager = new ServerManager();

            foreach (var site in serverManager.Sites)
            {
                string physicalPath = string.Empty;
                string siteName = site.Name;
                string logPath = string.Empty;
                string configTargetPath = string.Empty;
                string configPath = string.Empty;

                var application = site.Applications.FirstOrDefault(app => !app.Path.Contains('0'));

                if(application == null) 
                    continue;

                var virtualDirectory = application.VirtualDirectories["/"];

                if (virtualDirectory == null)
                    continue;

                physicalPath = virtualDirectory.PhysicalPath;

                if (string.IsNullOrWhiteSpace(physicalPath))
                    continue;

                configTargetPath = TryGetConfigTargetPath(physicalPath, "nlog.targets.config");
                configPath = TryGetConfigTargetPath(physicalPath, "nlog.config");
                logPath =  TryGetLogPath(configTargetPath, physicalPath);
                var newLogPath = ChangeString(physicalPath, logPath, siteName);
                var logDirectories = GetLogDirectories(newLogPath);
                List<LogDirectorie> logDirectories_list = new List<LogDirectorie>();
                foreach (var dir in logDirectories)
                {
                    logDirectories_list.Add(new LogDirectorie { Name = dir, ShortName = Path.GetFileName(dir.TrimEnd('\\')) });
                }

                result.Add(new Project
                {
                    Name = siteName,
                    PhysicalPath = physicalPath,
                    ConfigTargetPath = configTargetPath,
                    ConfigPath = configPath,
                    LogsPath = newLogPath,
                    LogDirectories = logDirectories_list
                });
            }
            return result;
        }
        private static string TryGetConfigTargetPath(string physicalPath, string confugFile)
        {
            var nlogPath = Path.Combine(physicalPath, confugFile);

            if (File.Exists(nlogPath))
            {
                return nlogPath;
            }

            var subDirs = new[] { "BPMSoft.WebApp", "Terrasoft.WebApp" };

            foreach (var subDir in subDirs)
            {
                var fullPath = Path.Combine(physicalPath, subDir, confugFile);
                if (Directory.Exists(Path.Combine(physicalPath, subDir)) && File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            if (Directory.Exists(Path.Combine(physicalPath, "Terrasoft.WebApp")) && File.Exists(Path.Combine(physicalPath, "Terrasoft.WebApp\\log4net.config")))
            {
                return Path.Combine(physicalPath, "Terrasoft.WebApp\\log4net.config");
            }

            return string.Empty;
        }
        public static string TryGetLogPath(string configPath, string physicalPath)
        {
            if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            {
                return string.Empty;
            }

            try
            {
                if (configPath.EndsWith("nlog.targets.config", StringComparison.OrdinalIgnoreCase))
                {
                    return GetNLogPath(configPath, physicalPath);
                }

                if (configPath.EndsWith("log4net.config", StringComparison.OrdinalIgnoreCase))
                {
                    return GetLog4NetPath(configPath, physicalPath);
                }
            }
            catch(Exception ex)
            {
                return ex.Message;
            }

            return string.Empty;
        }
        private static string GetNLogPath(string configPath, string physicalPath)
        {
            var document = XDocument.Load(configPath);

            var variable = document.Descendants().FirstOrDefault(x => 
                x.Name.LocalName == "variable" && 
                (string.Equals((string?)x.Attribute("name"), "LogDir", StringComparison.OrdinalIgnoreCase) 
                    || string.Equals((string?)x.Attribute("name"), "TodayLogPath", StringComparison.OrdinalIgnoreCase)));

            if (variable == null)
            {
                return string.Empty;
            }

            var logDir = (string?)variable.Attribute("value");

            if (string.IsNullOrWhiteSpace(logDir))
            {
                return string.Empty;
            }

            return logDir;
        }
        private static string GetLog4NetPath(string configPath, string physicalPath)
        {
            var document = XDocument.Load(configPath);

            var conversionPattern = document
                .Descendants("conversionPattern")
                .Select(x => (string?)x.Attribute("value"))
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

            return conversionPattern ?? string.Empty;
        }
        private static string ChangeString(string physicalPath, string logPath, string siteName)
        {
            logPath = logPath.Replace("${tempdir}", "C:\\Windows\\Temp");
            logPath = logPath.Replace("${TEMP}", "C:\\Windows\\Temp");
            logPath = logPath.Replace("${iis-site-name}", siteName);
            logPath = logPath.Replace("Site_%AspNet{SiteId}", siteName);
            logPath = logPath.Replace("${iis-application-name}", "0");
            logPath = logPath.Replace("%AspNet{ApplicationPath}", "0");
            logPath = logPath.Replace("${basedir}", physicalPath);
            logPath = logPath.Replace("${shortdate:universalTime=true}/", "");
            logPath = logPath.Replace("Logs/", "Logs");
            logPath = logPath.Replace("/", "\\");
            return logPath;
        }
        private static List<string> GetLogDirectories(string logsPath)
        {
            if (string.IsNullOrWhiteSpace(logsPath) ||
                !Directory.Exists(logsPath))
            {
                return new List<string>();
            }

            return Directory
                .GetDirectories(logsPath)
                .OrderBy(x => x)
                .ToList();
        }
    }
}