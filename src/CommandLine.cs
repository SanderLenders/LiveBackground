using System;

namespace LiveBackground
{
    // Command-line switches are matched without regard to case, and a missing
    // argument list simply matches nothing. The app and the setup program used
    // to carry identical private copies; they now share this one.
    public static class CommandLine
    {
        public static bool HasArg(string[] args, string value)
        {
            if (args == null) return false;
            foreach (string arg in args)
            {
                if (string.Equals(arg, value, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
