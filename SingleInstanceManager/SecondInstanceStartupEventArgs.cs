using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SingleInstanceManager
{
    public class SecondInstanceStartupEventArgs : EventArgs
    {
        public SecondInstanceStartupEventArgs(string[] commandLineParameters)
        {
            CommandLineParameters = commandLineParameters;
        }

        public string[] CommandLineParameters { get; }
    }
}
