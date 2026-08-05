//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM.WebHostSample
{
    using System;
    using System.Threading;
    using Microsoft.Owin.Hosting;

    /// <summary>
    /// OWIN self-host entry point for the .NET Framework 4.8 SCIM sample.
    /// </summary>
    /// <remarks>
    /// Self-hosting rather than IIS so that the sample runs with F5 and no machine setup.
    /// For hosting Microsoft.SCIM.AspNet under IIS, see docs/net48-hosting.md.
    /// </remarks>
    public static class Program
    {
        private const string DefaultUrl = "http://localhost:5000";
        private const string UrlEnvironmentVariable = "SCIM_SAMPLE_URL";

        public static void Main(string[] args)
        {
            string url = Program.ResolveUrl(args);

            using (IDisposable host = WebApp.Start<Startup>(url))
            {
                SampleStartupBanner.Print("ASP.NET Web API 2 (net48), OWIN self-host", url);

                ManualResetEventSlim stopped = new ManualResetEventSlim(false);

                Console.CancelKeyPress +=
                    (object sender, ConsoleCancelEventArgs eventArguments) =>
                    {
                        eventArguments.Cancel = true;
                        stopped.Set();
                    };

                Console.WriteLine("Press Ctrl+C to stop.");
                stopped.Wait();

                Console.WriteLine("Stopping.");
            }
        }

        private static string ResolveUrl(string[] args)
        {
            if (args != null && args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
            {
                return args[0];
            }

            string configured = Environment.GetEnvironmentVariable(Program.UrlEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            return Program.DefaultUrl;
        }
    }
}
