using System;
using System.Collections.Generic;

namespace SingleInstanceManager
{
    public class SecondInstanceStartupEventArgs : EventArgs
    {
        public SecondInstanceStartupEventArgs(string[]? commandLineParameters, IReadOnlyDictionary<string, string>? environmentalProperties, string? workingDirectory)
        {
            EnvironmentalProperties = environmentalProperties ?? new Dictionary<string, string>();
            WorkingDirectory = workingDirectory ?? string.Empty;
            CommandLineParameters = commandLineParameters ?? Array.Empty<string>();
        }

        public string[] CommandLineParameters { get; }

        public IReadOnlyDictionary<string, string> EnvironmentalProperties { get; }

        public string WorkingDirectory { get; }
    }
}
