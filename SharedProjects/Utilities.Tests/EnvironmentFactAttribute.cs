using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>
    /// A test that reads inputs kept out of the repo, named by environment variables (the real models' parity runs, too large
    /// for the repo): skipped, and reported as skipped, when any of its variables is not set, so a run without them never
    /// counts it as passed. A variable set to a file that is not there fails the test (<see cref="InputFile"/>).
    /// </summary>
    public sealed class EnvironmentFactAttribute : FactAttribute
    {
        public EnvironmentFactAttribute(params string[] variables)
        {
            var unset = variables.Where(v => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(v))).ToArray();
            if (unset.Length > 0)
            {
                Skip = string.Join(", ", unset) + (unset.Length == 1 ? " is" : " are") + " not set: this test reads inputs kept out of the repo.";
            }
        }

        /// <summary>The file an environment variable names; the test fails when it is not there.</summary>
        public static string InputFile(string variable)
        {
            var path = Environment.GetEnvironmentVariable(variable);
            Assert.True(!string.IsNullOrEmpty(path) && File.Exists(path), variable + " names " + (path ?? "nothing") + ", which is not there.");
            return path;
        }
    }
}
