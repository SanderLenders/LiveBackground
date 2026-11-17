using System;
using System.IO;
using System.Runtime.InteropServices;
using LiveBackground;
using Xunit;

namespace LiveBackground.Tests
{
    public class UtilTests
    {
        [Theory]
        [InlineData(null, "\"\"")]
        [InlineData("", "\"\"")]
        [InlineData("abc", "\"abc\"")]
        [InlineData("C:\\a b\\c.mp4", "\"C:\\a b\\c.mp4\"")]
        [InlineData("C:\\dir\\", "\"C:\\dir\\\\\"")]
        public void Quote_produces_the_expected_text(string value, string expected)
        {
            Assert.Equal(expected, Util.Quote(value));
        }

        // The real contract of Quote is "CommandLineToArgvW hands the value
        // back unchanged". Checking that directly is stronger than comparing
        // hand-written expected strings, and it covers the tricky cases
        // (embedded quotes, runs of backslashes) without arithmetic by hand.
        [Theory]
        [InlineData("")]
        [InlineData("abc")]
        [InlineData("a b")]
        [InlineData("C:\\Program Files\\LiveBackground\\mpv\\mpv.exe")]
        [InlineData("he said \"hi\"")]
        [InlineData("back\\slash\\")]
        [InlineData("quote\\\"inside")]
        [InlineData("two\\\\backslashes")]
        [InlineData("\"leading quote")]
        [InlineData("trailing quote\"")]
        [InlineData("\\\"\\\\\"")]
        [InlineData("--vf=scale=1920:1080,format=yuv420p")]
        public void Quote_round_trips_through_CommandLineToArgvW(string value)
        {
            Assert.Equal(value, Argv1("prog.exe " + Util.Quote(value)));
        }

        [Fact]
        public void ResolvePath_returns_the_root_for_an_empty_path()
        {
            Assert.Equal(Path.GetFullPath(@"C:\root"), Util.ResolvePath(@"C:\root", ""));
        }

        [Fact]
        public void ResolvePath_combines_a_relative_path_with_the_root()
        {
            Assert.Equal(Path.GetFullPath(@"C:\root\sub\file.mp4"),
                Util.ResolvePath(@"C:\root", @"sub\file.mp4"));
        }

        [Fact]
        public void ResolvePath_leaves_an_already_rooted_path_alone()
        {
            Assert.Equal(Path.GetFullPath(@"C:\other\x.mp4"),
                Util.ResolvePath(@"C:\root", @"C:\other\x.mp4"));
        }

        [Fact]
        public void ResolvePath_expands_environment_variables()
        {
            string expected = Path.GetFullPath(Environment.ExpandEnvironmentVariables("%TEMP%"));
            Assert.Equal(expected, Util.ResolvePath(@"C:\root", "%TEMP%"));
        }

        [Fact]
        public void ResolvePath_falls_back_to_the_program_folder_without_a_root()
        {
            string expected = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "x.mp4"));
            Assert.Equal(expected, Util.ResolvePath(null, "x.mp4"));
        }

        [Fact]
        public void DetectImageExtension_reads_the_file_signature()
        {
            using (TempFolder temp = new TempFolder())
            {
                string png = temp.Write("a.bin", "");
                File.WriteAllBytes(png, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A });
                Assert.Equal(".png", Util.DetectImageExtension(png));

                string bmp = temp.Write("b.bin", "");
                File.WriteAllBytes(bmp, new byte[] { 0x42, 0x4D, 0x00, 0x00 });
                Assert.Equal(".bmp", Util.DetectImageExtension(bmp));

                string jpg = temp.Write("c.bin", "");
                File.WriteAllBytes(jpg, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });
                Assert.Equal(".jpg", Util.DetectImageExtension(jpg));

                string gif = temp.Write("d.bin", "");
                File.WriteAllBytes(gif, new byte[] { 0x47, 0x49, 0x46, 0x38 });
                Assert.Equal(".gif", Util.DetectImageExtension(gif));

                string unknown = temp.Write("e.bin", "not an image");
                Assert.Equal(".img", Util.DetectImageExtension(unknown));
            }
        }

        private static string Argv1(string commandLine)
        {
            int argc;
            IntPtr argv = CommandLineToArgvW(commandLine, out argc);
            if (argv == IntPtr.Zero)
                throw new InvalidOperationException("CommandLineToArgvW failed");
            try
            {
                if (argc < 2) return null;
                IntPtr p = Marshal.ReadIntPtr(argv, IntPtr.Size);
                return Marshal.PtrToStringUni(p);
            }
            finally
            {
                LocalFree(argv);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);
    }
}
