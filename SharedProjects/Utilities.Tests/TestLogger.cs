using System.Collections.Generic;
using System.Drawing;
using Utilities;

namespace Utilities.Tests
{
    /// <summary>Collects everything the code under test logs so assertions can look at it.</summary>
    internal sealed class TestLogger : ILoggingProvider
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Messages = new List<string>();

        public void ReportProgressChanged(int progress) { }

        public void RaiseError(string error, int rank = 0) { Errors.Add(error); }

        public void RaiseWarning(string warning, int rank = 0) { Warnings.Add(warning); }

        public void RaiseMessage(string message, int rank = 0, bool emphasis = false) { Messages.Add(message); }

        public void RaiseMessage(string message, Color color, int rank = 0, bool emphasis = false) { Messages.Add(message); }

        public void RaiseVerbose(string message, int rank = 0, bool emphasis = false) { }

        public void CheckCancelled() { }
    }
}
