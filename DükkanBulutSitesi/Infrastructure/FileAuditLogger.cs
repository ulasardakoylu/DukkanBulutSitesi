using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Security.Claims;
using System.Text;

namespace DükkanBulutSitesi.Infrastructure
{
    public interface IAuditLogger
    {
        Task LogAsync(string message, HttpContext? httpContext = null, int? explicitUserId = null, string? explicitUserName = null);
    }

    public class FileAuditLogger : IAuditLogger
    {
        private readonly IWebHostEnvironment _env;
        private static readonly SemaphoreSlim _lock = new(1, 1);

        public FileAuditLogger(IWebHostEnvironment env)
        {
            _env = env;
        }

        public async Task LogAsync(
            string message,
            HttpContext? httpContext = null,
            int? explicitUserId = null,
            string? explicitUserName = null)
        {
            string? userId = explicitUserId?.ToString();
            string? userName = explicitUserName;

            if (httpContext?.User?.Identity?.IsAuthenticated == true)
            {
                userName ??= httpContext.User.Identity?.Name;

                userId ??= httpContext.User
                    .FindFirst(ClaimTypes.NameIdentifier)?
                    .Value;
            }

            userName ??= "Anonymous";
            userId ??= "-";

            string ip = "-";
            if (httpContext != null)
            {
                var remoteIp = httpContext.Connection?.RemoteIpAddress;

                if (remoteIp != null)
                {
                    if (IPAddress.IsLoopback(remoteIp))
                    {
                        ip = "127.0.0.1";
                    }
                    else if (remoteIp.IsIPv4MappedToIPv6)
                    {
                        ip = remoteIp.MapToIPv4().ToString();
                    }
                    else
                    {
                        ip = remoteIp.ToString();
                    }
                }
                else
                {
                    ip = "-";
                }
            }

            var timestamp = DateTime.Now.ToString("dd-MM-yyyy HH:mm:ss");
            var line = $"{timestamp}\tUser={userName}\tUserId={userId}\tIP={ip}\t{message}{Environment.NewLine}";

            var root = _env.WebRootPath;
            var logsDir = Path.Combine(root, "logs");
            Directory.CreateDirectory(logsDir);

            var fileName = $"log-{DateTime.UtcNow:dd-MM-yyyy}.txt";
            var fullPath = Path.Combine(logsDir, fileName);

            await _lock.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(fullPath, line);
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}