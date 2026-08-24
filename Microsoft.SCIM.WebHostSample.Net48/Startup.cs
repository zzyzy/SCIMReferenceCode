//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.SCIM.WebHostSample
{
    using System;
    using System.IO;
    using System.Text;
    using System.Web.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.IdentityModel.Tokens;
    using Microsoft.Owin.Security.Jwt;
    using Microsoft.Owin.Security.OAuth;
    using Microsoft.SCIM.WebHostSample.Provider;
    // global:: because this file's namespace starts with Microsoft, so a plain 'using Owin'
    // binds to Microsoft.Owin rather than to the root Owin namespace that IAppBuilder lives in.
    using global::Owin;

    /// <summary>
    /// OWIN pipeline for the .NET Framework 4.8 SCIM sample. The counterpart of
    /// <c>Program.Main</c> in the net10.0 sample; the two are kept deliberately parallel.
    /// </summary>
    public class Startup
    {
        private const string EnvironmentVariableName = "ASPNETCORE_ENVIRONMENT";
        private const string EnvironmentNameDevelopment = "Development";

        public void Configuration(IAppBuilder app)
        {
            if (null == app)
            {
                throw new ArgumentNullException(nameof(app));
            }

            // Same variable name and the same appsettings layering as the net10.0 sample, so
            // one set of docs and CI scripts covers both legs. See MULTI-TARGET-PLAN.md D8/D17.
            string environmentName =
                Environment.GetEnvironmentVariable(Startup.EnvironmentVariableName);

            IConfiguration configuration =
                new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory ?? Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                    .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: false)
                    .AddEnvironmentVariables()
                    .Build();

            bool isDevelopment =
                string.Equals(environmentName, Startup.EnvironmentNameDevelopment, StringComparison.OrdinalIgnoreCase);

            IProvider provider = new InMemoryProvider();

            // Identical registration lines to the net10.0 sample - that is the point of
            // bridging MEDI onto Web API's IDependencyResolver. See MULTI-TARGET-PLAN.md D9.
            ServiceCollection services = new ServiceCollection();
            services.AddSingleton(typeof(IProvider), provider);
            services.AddLogging(builder => builder.AddConsole());
            services.AddSingleton(typeof(IConfiguration), configuration);
            IServiceProvider serviceProvider = services.BuildServiceProvider();

            app.Use<RequestLoggingMiddleware>(
                serviceProvider.GetRequiredService<ILoggerFactory>()
                    .CreateLogger<RequestLoggingMiddleware>());

            Startup.ConfigureAuthentication(app, configuration, isDevelopment);

            HttpConfiguration httpConfiguration = new HttpConfiguration();
            ScimHttpConfiguration.Configure(httpConfiguration, serviceProvider);
            app.UseWebApi(httpConfiguration);
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

            // The development branch below disables every JWT validation
            // (issuer, audience, lifetime, signing-key) and is intended only
            // for local end-to-end testing of the sample. Guarding it with
            // #if DEBUG ensures that a Release build of this sample cannot
            // accidentally ship the bypass to a production environment - the
            // preprocessor strips the dev branch (and the surrounding 'if'),
            // leaving only the production branch which enforces real
            // issuer / audience / lifetime / signing-key checks.
            //
            // Unlike Microsoft.AspNetCore.Authentication.JwtBearer, OWIN's JWT middleware
            // exposes no per-check toggles of its own, so both branches are expressed as
            // explicit TokenValidationParameters carried by a JwtFormat rather than
            // approximated through middleware options.
#if DEBUG
            if (isDevelopment)
            {
                validationParameters =
                    new TokenValidationParameters
                    {
                        ValidateIssuer = false,
                        ValidateAudience = false,
                        ValidateLifetime = false,
                        ValidateIssuerSigningKey = false,
                        ValidIssuer = issuer,
                        ValidAudience = audience,
                        IssuerSigningKey = securityKey
                    };
            }
            else
#endif
            // Release: always enforce production JWT validation (dev-mode block is stripped by preprocessor).
            {
                // The net10.0 sample uses JwtBearerOptions.Authority here, which discovers the
                // issuer's signing keys over OIDC metadata. OWIN's JWT middleware has no
                // discovery, so a production deployment of this leg must either supply the
                // issuer's keys itself or front the service with a gateway that validates the
                // token. Documented in docs/net48-hosting.md.
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

            // UseOAuthBearerAuthentication rather than UseJwtBearerAuthentication: only the
            // former accepts an AccessTokenFormat, which is how the explicit
            // TokenValidationParameters above get used. UseJwtBearerAuthentication would build
            // its own format from AllowedAudiences plus the key providers and silently discard
            // the dev/prod distinction.
            app.UseOAuthBearerAuthentication(
                new OAuthBearerAuthenticationOptions
                {
                    AccessTokenFormat = new JwtFormat(validationParameters, keyProvider)
                });
        }
    }
}
