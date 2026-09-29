using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace Filo.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProcessingTestController : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var scriptPath = Path.GetFullPath(
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "..",
                    "Filo.Processing",
                    "processor_test.py"));

            var startInfo = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{scriptPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process
            {
                StartInfo = startInfo
            };

            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                return StatusCode(500, new
                {
                    error
                });
            }

            return Content(output, "application/json");
        }
    }
}