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

                configPath = TryGetConfigPath(physicalPath);
                logPath =  TryGetLogPath(configPath, physicalPath);
                var newLogPath = ChangeString(physicalPath, logPath, siteName);

                result.Add(new Project
                {
                    Name = siteName,
                    IisSite = siteName,
                    PhysicalPath = physicalPath,
                    ConfigPath = configPath,
                    LogsPath = logPath,
                    NewLogsPath = newLogPath,
                });
            }
            return result;
        }

        public static string TryGetConfigPath(string physicalPath)
        {
            var nlogPath = Path.Combine(physicalPath, "nlog.targets.config");

            if(File.Exists(nlogPath))
            {
                return nlogPath;
            }
            else if (Directory.Exists(Path.Combine(physicalPath, "BPMSoft.WebApp")) && File.Exists(Path.Combine(physicalPath, "BPMSoft.WebApp\\nlog.targets.config"))  )
            {
                return Path.Combine(physicalPath, "BPMSoft.WebApp\\nlog.targets.config");
            }
            else if (Directory.Exists(Path.Combine(physicalPath, "Terrasoft.WebApp")) && File.Exists(Path.Combine(physicalPath, "Terrasoft.WebApp\\nlog.targets.config")))
            {
                return Path.Combine(physicalPath, "Terrasoft.WebApp\\nlog.targets.config");
            }
            else if (Directory.Exists(Path.Combine(physicalPath, "Terrasoft.WebApp")) && File.Exists(Path.Combine(physicalPath, "Terrasoft.WebApp\\log4net.config")))
            {
                return Path.Combine(physicalPath, "Terrasoft.WebApp\\log4net.config");
            }
            else
            {
                return string.Empty;
            }
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

            //"${TEMP}\\BPMonline\\Site_%AspNet{SiteId}\\%AspNet{ApplicationPath}\\Log\\"
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
    }
}