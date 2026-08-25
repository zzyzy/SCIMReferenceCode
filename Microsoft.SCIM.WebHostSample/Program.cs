//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM.WebHostSample
{
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Authentication.JwtBearer;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.HttpLogging;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.IdentityModel.Tokens;
    using Microsoft.SCIM.WebHostSample.Provider;
    using Scim.EduPass;

    public class Program
    {
        public static void Main(string[] args)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            IWebHostEnvironment environment = builder.Environment;
            IConfiguration configuration = builder.Configuration;

            void ConfigureAuthenticationOptions(AuthenticationOptions options)
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            }

            void ConfigureJwtBearerOptons(JwtBearerOptions options)
            {
                // The development branch below disables every JWT validation
                // (issuer, audience, lifetime, signing-key) and is intended only
                // for local end-to-end testing of the sample. Guarding it with
                // #if DEBUG ensures that a Release build of this sample cannot
                // accidentally ship the bypass to a production environment - the
                // preprocessor strips the dev branch (and the surrounding 'if'),
                // leaving only the production branch which enforces real
                // Authority / Audience checks.
#if DEBUG
                if (environment.IsDevelopment())
                {
                    // SCIM_ENFORCE_JWT=1 turns the four checks back on while keeping the
                    // committed symmetric key, so that a test can watch an expired token,
                    // a wrong issuer or a wrong audience be rejected. The Release branch
                    // below resolves its keys over OIDC metadata and therefore cannot be
                    // reached without a live authority.
                    bool enforce = Program.Enabled(configuration["SCIM_ENFORCE_JWT"]);

                    options.TokenValidationParameters =
                       new TokenValidationParameters
                       {
                           ValidateIssuer = enforce,
                           ValidateAudience = enforce,
                           ValidateLifetime = enforce,
                           ValidateIssuerSigningKey = enforce,
                           ValidIssuer = configuration["Token:TokenIssuer"],
                           ValidAudience = configuration["Token:TokenAudience"],
                           IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Token:IssuerSigningKey"]))
                       };
                }
                else
#endif
                // Release: always enforce production JWT validation (dev-mode block is stripped by preprocessor).
                {
                    options.Authority = configuration["Token:TokenIssuer"];
                    options.Audience = configuration["Token:TokenAudience"];
                    options.Events = new JwtBearerEvents
                    {
                        OnTokenValidated = context =>
                        {
                            return Task.CompletedTask;
                        },
                        OnAuthenticationFailed = Program.AuthenticationFailed
                    };
                }
            }

            builder.Services
                .AddAuthentication(ConfigureAuthenticationOptions)
                .AddJwtBearer(ConfigureJwtBearerOptons);

            // Registers the provider, the monitor, the SCIM controllers (which live in
            // Microsoft.SCIM.AspNetCore, not in this assembly), the Newtonsoft settings and
            // the HttpResponseException filter.
            //
            // SCIM_PROVIDER=edupass serves the Edupass User resource type instead of the
            // plain core one: AddScim<EduPassUser> binds /Users to the extended type, which
            // is the only way its extension attributes survive model binding.
            Program.ConfigureScimLogging(configuration);

            string provider = configuration["SCIM_PROVIDER"];
            string pathPrefix = configuration["SCIM_PATH_PREFIX"];

            if (Program.Selected(provider, "edupass"))
            {
                bool requireUinFin = Program.Enabled(configuration["SCIM_EDUPASS_REQUIRE_UINFIN"]);
                builder.Services.AddScim<EduPassUser>(new InMemoryEduPassProvider(requireUinFin), pathPrefix);
            }
            else if (Program.Selected(provider, "unimplemented"))
            {
                builder.Services.AddScim(new UnimplementedProvider(), pathPrefix);
            }
            else if (Program.Selected(provider, "faulty"))
            {
                builder.Services.AddScim(new FaultyProvider(), pathPrefix);
            }
            else
            {
                builder.Services.AddScim(new InMemoryProvider(), pathPrefix);
            }

            // Request and response logging, which is the host's to configure - the SCIM
            // library logs only what a host cannot see, a failed operation. See
            // ConfigureHttpLogging.
            bool logRequests = Program.Enabled(configuration["SCIM_LOG_REQUESTS"]);
            if (logRequests)
            {
                builder.Services.AddHttpLogging(
                    (HttpLoggingOptions options) => Program.ConfigureHttpLogging(options, configuration));
            }

            WebApplication app = builder.Build();

            if (environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            if (logRequests)
            {
                app.UseHttpLogging();
            }

            // Deliberately no UseHsts() and no UseHttpsRedirection(): both samples are
            // HTTP-only dev harnesses so that the net48 and net10 legs can be compared
            // like-for-like. TLS is the host's responsibility - see docs/net48-hosting.md.
            // The startup banner below says so out loud.
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            // Attribute routes only - no MapDefaultControllerRoute() fallback (D14a).
            app.MapScim();

            app.Lifetime.ApplicationStarted.Register(
                () =>
                    SampleStartupBanner.Print(
                        "ASP.NET Core (net10.0), Kestrel",
                        string.Join(", ", app.Urls.DefaultIfEmpty("(see launchSettings.json)"))));

            app.Run();
        }

        private static bool Selected(string configured, string name)
        {
            return string.Equals(configured, name, System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The one SCIM logging setting: the ceiling on a logged request body.
        /// </summary>
        /// <remarks>
        /// A sample reads it from configuration so that it can be exercised without a rebuild;
        /// a real host would more likely set it outright. Either way it is set before AddScim,
        /// and so before the first request.
        ///
        /// There is no switch for whether requests are logged. That is this host's decision,
        /// and this host makes it with UseHttpLogging below - see ConfigureHttpLogging.
        /// </remarks>
        private static void ConfigureScimLogging(IConfiguration configuration)
        {
            string maximum = configuration["SCIM_LOG_MAXIMUM_BODY_LENGTH"];

            if (!string.IsNullOrWhiteSpace(maximum)
                && int.TryParse(maximum, out int parsed)
                && parsed > 0)
            {
                ScimLogging.MaximumBodyLength = parsed;
            }
        }

        /// <summary>
        /// Request and response logging, which belongs to the host rather than to the SCIM
        /// library.
        /// </summary>
        /// <remarks>
        /// Shown here because a reference implementation should show it: this is what a
        /// consumer wires up, and every switch a consumer might want - which fields, whether
        /// bodies, how much of them, which headers survive redaction - is already an option
        /// here rather than something the library reinvents.
        ///
        /// Off unless SCIM_LOG_REQUESTS says otherwise, because a body carries whatever the
        /// caller provisions and a sample should not write that by default.
        ///
        /// HttpLogging redacts every header it is not told to keep, which is the opposite
        /// default to the SCIM failure logging: that one names the few it replaces, because an
        /// entry about a failure is worth less with its headers blanked.
        /// </remarks>
        private static void ConfigureHttpLogging(HttpLoggingOptions options, IConfiguration configuration)
        {
            options.LoggingFields =
                HttpLoggingFields.RequestMethod
                | HttpLoggingFields.RequestPath
                | HttpLoggingFields.RequestQuery
                | HttpLoggingFields.RequestHeaders
                | HttpLoggingFields.ResponseStatusCode
                | HttpLoggingFields.ResponseHeaders;

            if (Program.Enabled(configuration["SCIM_LOG_BODIES"]))
            {
                options.LoggingFields |= HttpLoggingFields.RequestBody | HttpLoggingFields.ResponseBody;
            }

            options.RequestBodyLogLimit = ScimLogging.MaximumBodyLength;
            options.ResponseBodyLogLimit = ScimLogging.MaximumBodyLength;

            options.MediaTypeOptions.AddText(ProtocolConstants.ContentType);
        }

        private static bool Enabled(string value)
        {
            return string.Equals(value, "1", System.StringComparison.Ordinal)
                || string.Equals(value, "true", System.StringComparison.OrdinalIgnoreCase);
        }

        private static Task AuthenticationFailed(AuthenticationFailedContext arg)
        {
            // For debugging purposes only!
            string authenticationExceptionMessage = $"{{AuthenticationFailed: '{arg.Exception.Message}'}}";

            arg.Response.ContentLength = authenticationExceptionMessage.Length;
            arg.Response.Body.WriteAsync(
                Encoding.UTF8.GetBytes(authenticationExceptionMessage),
                0,
                authenticationExceptionMessage.Length);

            return Task.FromException(arg.Exception);
        }
    }
}
