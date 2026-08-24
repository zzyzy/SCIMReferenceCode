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
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.IdentityModel.Tokens;
    using Microsoft.SCIM.WebHostSample.Provider;

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
                    options.TokenValidationParameters =
                       new TokenValidationParameters
                       {
                           ValidateIssuer = false,
                           ValidateAudience = false,
                           ValidateLifetime = false,
                           ValidateIssuerSigningKey = false,
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
            builder.Services.AddScim(new InMemoryProvider());

            WebApplication app = builder.Build();

            if (environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            // Deliberately no UseHsts() and no UseHttpsRedirection(): both samples are
            // HTTP-only dev harnesses so that the net48 and net10 legs can be compared
            // like-for-like. TLS is the host's responsibility - see MULTI-TARGET-PLAN.md D20
            // and docs/net48-hosting.md. The startup banner below says so out loud.
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
