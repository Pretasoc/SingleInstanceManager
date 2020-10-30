using System;

namespace SingleInstanceManager
{
    public class SecondInstanceStartupEventArgs : EventArgs
    {
        public SecondInstanceStartupEventArgs(string[]? commandLineParameters)
        {
            CommandLineParameters = commandLineParameters ?? Array.Empty<string>();
        }

        public string[] CommandLineParameters { get; }
    }
}
