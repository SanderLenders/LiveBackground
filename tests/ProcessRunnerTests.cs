using System;
using System.IO;
using LiveBackground;
using Xunit;

namespace LiveBackground.Tests
{
    public class ProcessRunnerTests
    {
        [Fact]
        public void Run_flushes_async_output_on_timeout()
        {
            string cmd = Environment.GetEnvironmentVariable("COMSPEC");
            if (string.IsNullOrEmpty(cmd) || !File.Exists(cmd))
                cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");

            // The child keeps running long enough for the timeout/kill path.
            // The echoed line must still appear in the captured output.
            ProcessResult result = ProcessRunner.Run(cmd,
                "/c echo timeout-flush-marker & ping -n 5 127.0.0.1 >nul", null, 1000);

            Assert.True(result.TimedOut);
            Assert.Contains("timeout-flush-marker", result.StandardOutput);
        }
    }
}
