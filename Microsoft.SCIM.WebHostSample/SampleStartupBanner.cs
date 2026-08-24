//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM.WebHostSample
{
    using System;

    /// <summary>
    /// The DEV-ONLY startup warning printed by both samples.
    /// </summary>
    /// <remarks>
    /// Single-sourced deliberately: Microsoft.SCIM.WebHostSample.Net48 links this file rather
    /// than copying it, so the two samples cannot end up warning about different things. See
    /// MULTI-TARGET-PLAN.md D20 and D21.
    /// </remarks>
    public static class SampleStartupBanner
    {
        public static void Print(string hostDescription, string listeningAddress)
        {
            ConsoleColor original = Console.ForegroundColor;
            try
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine();
                Console.WriteLine("==============================================================================");
                Console.WriteLine(" SCIM reference sample - DEVELOPMENT USE ONLY. DO NOT DEPLOY AS-IS.");
                Console.WriteLine("==============================================================================");
                Console.WriteLine($" Host      : {hostDescription}");
                Console.WriteLine($" Listening : {listeningAddress}");
                Console.WriteLine();
                Console.WriteLine(" This sample is an HTTP-only test harness:");
                Console.WriteLine();
                Console.WriteLine("  * NO TLS. Neither sample enables HTTPS, HSTS or an HTTPS redirect.");
                Console.WriteLine("    Terminating TLS is the host's responsibility - see docs/net48-hosting.md.");
                Console.WriteLine("  * Bearer tokens are signed with a symmetric key committed to this");
                Console.WriteLine("    repository, so anyone reading it can mint one. Wire a real OAuth");
                Console.WriteLine("    issuer before deploying anything - see README.md.");
                Console.WriteLine("  * In a DEBUG build with ASPNETCORE_ENVIRONMENT=Development, JWT validation");
                Console.WriteLine("    (issuer, audience, lifetime, signing key) is disabled outright.");
                Console.WriteLine("  * The provider stores everything in memory. Nothing survives a restart.");
                Console.WriteLine();
                Console.WriteLine("==============================================================================");
                Console.WriteLine();
            }
            finally
            {
                Console.ForegroundColor = original;
            }
        }
    }
}
