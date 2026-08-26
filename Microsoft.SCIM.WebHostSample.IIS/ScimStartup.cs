//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

[assembly: Microsoft.Owin.OwinStartup(typeof(Microsoft.SCIM.WebHostSample.IIS.ScimStartup))]

namespace Microsoft.SCIM.WebHostSample.IIS
{
    using System;
    using System.IO;
    using System.Text;
    using System.Web;
    using System.Web.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.IdentityModel.Tokens;
    using Microsoft.Owin.Security.Jwt;
    using Microsoft.Owin.Security.OAuth;
    using Microsoft.SCIM.WebHostSample.Provider;
    using Microsoft.SCIM.WebHostSample.Provider.Database;
    // global:: because this file's namespace starts with Microsoft, so a plain 'using Owin'
    // binds to Microsoft.Owin rather than to the root Owin namespace that IAppBuilder lives in.
    using global::Owin;

    /// <summary>
    /// Adds the SCIM endpoints to an application that already has its own Web API surface.
    /// </summary>
    /// <remarks>
    /// Microsoft.Owin.Host.SystemWeb runs this pipeline inside IIS, ahead of the application's
    /// own System.Web handler. Nothing in Global.asax or App_Start knows this class exists.
    ///
    /// The SCIM endpoints get their own <see cref="HttpConfiguration"/> rather than sharing
    /// <c>GlobalConfiguration.Configuration</c>, because ScimHttpConfiguration.Configure
    /// replaces the dependency resolver, the controller activator and the controller selector,
    /// removes the XML formatter and changes the JSON null handling. On a shared configuration
    /// all of that would apply to the application's existing controllers too.
    /// </remarks>
    public class ScimStartup
    {
        private const string EnvironmentVariableName = "ASPNETCORE_ENVIRONMENT";
        private const string EnvironmentNameDevelopment = "Development";

        public void Configuration(IAppBuilder app)
        {
            if (null == app)
            {
                throw new ArgumentNullException(nameof(app));
            }

            string environmentName =
                Environment.GetEnvironmentVariable(ScimStartup.EnvironmentVariableName);

            // HttpRuntime.BinDirectory, not AppDomain.CurrentDomain.BaseDirectory.
            //
            // The other two samples use BaseDirectory because their configuration files sit
            // next to the executable. Under ASP.NET, BaseDirectory is the *application root* -
            // the folder holding Web.config - while the build drops appsettings.json into
            // bin\ alongside the assemblies. Using BaseDirectory here silently loads no
            // configuration at all, and the first symptom is an ArgumentNullException out of
            // the JWT middleware at startup.
            IConfiguration configuration =
                new ConfigurationBuilder()
                    .SetBasePath(HttpRuntime.BinDirectory ?? AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                    .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: false)
                    .AddEnvironmentVariables()
                    .Build();

            bool isDevelopment =
                string.Equals(environmentName, ScimStartup.EnvironmentNameDevelopment, StringComparison.OrdinalIgnoreCase);

            // SCIM_PROVIDER=database serves the same two resource types over SQLite instead
            // of a dictionary, which is the shape an application hosted under IIS is likelier
            // to want: the other two samples restart with an empty store, and a worker process
            // that recycles would too. SCIM_DATABASE overrides where the file goes; see
            // ScimDatabase.Resolve for what the application pool needs write access to.
            bool database =
                string.Equals(configuration["SCIM_PROVIDER"], "database", StringComparison.OrdinalIgnoreCase);

            IProvider provider =
                database
                    ? (IProvider)new DatabaseProvider(ScimDatabase.Resolve(configuration["SCIM_DATABASE"]))
                    : new InMemoryProvider();

            ServiceCollection services = new ServiceCollection();
            services.AddSingleton(typeof(IProvider), provider);
            // AddConfiguration, not just AddConsole: without it the Logging section of
            // appsettings is read into IConfiguration and then ignored, so a level set there -
            // or through Logging__LogLevel__* in the environment - has no effect on this leg
            // while having every effect on the net10.0 one. The two samples claim the same
            // appsettings layering; this is what makes that true of logging as well.
            services.AddLogging(
                builder =>
                {
                    builder.AddConfiguration(configuration.GetSection("Logging"));
                    builder.AddConsole();
                });
            services.AddSingleton(typeof(IConfiguration), configuration);
            IServiceProvider serviceProvider = services.BuildServiceProvider();

            // Logging each request and response is the host's job, not the SCIM library's.
            // On this leg that is IIS logging, Application Insights, or an OWIN middleware of
            // your own; on the net10.0 sample it is app.UseHttpLogging(). What the library
            // logs, because a host cannot, is a failed SCIM operation - with the request that
            // caused it. See ScimLogging.

            ScimStartup.ConfigureAuthentication(app, configuration, isDevelopment);

            HttpConfiguration scimConfiguration = new HttpConfiguration();
            ScimHttpConfiguration.Configure(
                scimConfiguration,
                serviceProvider,
                configuration["SCIM_PATH_PREFIX"]);

            // A request that matches no route on scimConfiguration falls through to the next
            // stage of the pipeline, which under Microsoft.Owin.Host.SystemWeb is the
            // application's own System.Web handler. That is what keeps api/inventory working.
            app.UseWebApi(scimConfiguration);
        }

        private static void ConfigureAuthentication(
            IAppBuilder app,
            IConfiguration configuration,
            bool isDevelopment)
        {
            string issuer = configuration["Token:TokenIssuer"];
            string audience = configuration["Token:TokenAudience"];
            string signingKey = configuration["Token:IssuerSigningKey"];

            SecurityKey securityKey =
                string.IsNullOrWhiteSpace(signingKey)
                    ? null
                    : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));

            TokenValidationParameters validationParameters;

            // Identical treatment to Microsoft.SCIM.WebHostSample.Net48: the development
            // branch disables every JWT check and is stripped from a Release build by the
            // preprocessor, so the bypass cannot ship.
#if DEBUG
            if (isDevelopment)
            {
                // SCIM_ENFORCE_JWT=1 turns the four checks back on while keeping the
                // committed symmetric key, so that a test can watch an expired token, a
                // wrong issuer or a wrong audience be rejected. Kept identical to the
                // net10.0 sample and to Microsoft.SCIM.WebHostSample.Net48.
                bool enforce =
                    string.Equals(configuration["SCIM_ENFORCE_JWT"], "1", StringComparison.Ordinal)
                    || string.Equals(configuration["SCIM_ENFORCE_JWT"], "true", StringComparison.OrdinalIgnoreCase);

                validationParameters =
                    new TokenValidationParameters
                    {
                        ValidateIssuer = enforce,
                        ValidateAudience = enforce,
                        ValidateLifetime = enforce,
                        ValidateIssuerSigningKey = enforce,
                        ValidIssuer = issuer,
                        ValidAudience = audience,
                        IssuerSigningKey = securityKey
                    };
            }
            else
#endif
            // Release: always enforce production JWT validation.
            {
                // OWIN's JWT middleware has no OIDC discovery. A real deployment must supply
                // the issuer's keys itself or front the service with a gateway that validates
                // the token. See docs/net48-hosting.md.
                validationParameters =
                    new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = issuer,
                        ValidAudience = audience,
                        IssuerSigningKey = securityKey
                    };
            }

            IIssuerSecurityKeyProvider keyProvider =
                new SymmetricKeyIssuerSecurityKeyProvider(issuer, Encoding.UTF8.GetBytes(signingKey ?? string.Empty));

            app.UseOAuthBearerAuthentication(
                new OAuthBearerAuthenticationOptions
                {
                    AccessTokenFormat = new JwtFormat(validationParameters, keyProvider)
                });
        }
    }
}
