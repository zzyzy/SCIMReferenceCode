// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

#if !NET48

namespace Anacle.ApiFramework.Authentication.ApiKey
{
    using System;
    using System.Collections.Generic;
    using System.Security.Claims;
    using System.Text.Encodings.Web;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Options for the ASP.NET Core API key scheme.
    /// </summary>
    public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
    {
        /// <summary>The header the key is read from.</summary>
        public string HeaderName
        {
            get;
            set;
        } = ApiKeyAuthenticationDefaults.HeaderName;

        /// <summary>
        /// The RFC 7235 auth-scheme <see cref="HeaderName"/> carries, for a key presented as
        /// <c>Authorization: Bearer &lt;key&gt;</c>.
        /// </summary>
        /// <remarks>
        /// Null - the default - means the whole header value is the key, which is the shape a
        /// header of the API key's own takes.
        /// </remarks>
        public string HeaderScheme
        {
            get;
            set;
        }

        /// <summary>
        /// Resolves a presented key to a caller. Required.
        /// </summary>
        /// <remarks>
        /// Set directly, or leave null to have the handler resolve an
        /// <see cref="IApiKeyStore"/> from the container.
        /// </remarks>
        public IApiKeyStore Store
        {
            get;
            set;
        }
    }

    /// <summary>
    /// Authenticates a request by a key in a header.
    /// </summary>
    /// <remarks>
    /// An <see cref="AuthenticationHandler{TOptions}"/> rather than middleware, so that the key
    /// scheme takes part in the standard scheme selection: an endpoint can name it with
    /// <c>[Authorize(AuthenticationSchemes = ...)]</c>, and it can sit alongside a JWT scheme
    /// with each protecting different endpoints.
    ///
    /// A missing header is <c>NoResult</c>, not a failure, so that another registered scheme
    /// still gets its turn. Only a header that is present and wrong fails.
    /// </remarks>
    public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
    {
        private readonly IServiceProvider services;

        public ApiKeyAuthenticationHandler(
            IOptionsMonitor<ApiKeyAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IServiceProvider services)
            : base(options, logger, encoder)
        {
            this.services = services;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!this.Request.Headers.TryGetValue(this.Options.HeaderName, out var values))
            {
                return AuthenticateResult.NoResult();
            }

            if (!ApiKeyCredential.TryRead(values.ToString(), this.Options.HeaderScheme, out string presented))
            {
                // Including a value that does not carry the expected scheme: a bearer token
                // arriving where an API key is expected is not this scheme's to reject.
                return AuthenticateResult.NoResult();
            }

            IApiKeyStore store =
                this.Options.Store ?? this.services?.GetService<IApiKeyStore>();

            if (null == store)
            {
                throw new InvalidOperationException(
                    "No IApiKeyStore is configured. Set ApiKeyAuthenticationOptions.Store or "
                    + "register an IApiKeyStore in the container.");
            }

            ApiKeyIdentity identity =
                await store
                    .ResolveAsync(presented, this.Context.RequestAborted)
                    .ConfigureAwait(false);

            if (null == identity)
            {
                // Deliberately not logged with the presented value: an invalid key is often a
                // valid key for a different environment, and logs are read more widely than
                // secret stores.
                return AuthenticateResult.Fail("The API key is not valid.");
            }

            List<Claim> claims = new List<Claim> { new Claim(ClaimTypes.Name, identity.Name) };
            claims.AddRange(identity.Claims);

            ClaimsPrincipal principal =
                new ClaimsPrincipal(new ClaimsIdentity(claims, this.Scheme.Name));

            return AuthenticateResult.Success(new AuthenticationTicket(principal, this.Scheme.Name));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            // The scheme, never the header name: RFC 7235 section 4.1 defines a challenge as an
            // auth-scheme token, and "WWW-Authenticate: X-Api-Key" - which this sent - names a
            // header, which no client can act on.
            this.Response.Headers["WWW-Authenticate"] =
                ApiKeyCredential.ChallengeScheme(this.Options.HeaderScheme);
            return base.HandleChallengeAsync(properties);
        }
    }

    /// <summary>
    /// Wires API key authentication into ASP.NET Core.
    /// </summary>
    public static class ApiKeyAuthenticationExtensions
    {
        /// <param name="authenticationScheme">
        /// The scheme name to register under. Name it explicitly when more than one
        /// authentication mode is registered, so that endpoints can select between them.
        /// </param>
        public static AuthenticationBuilder AddApiKey(
            this AuthenticationBuilder builder,
            Action<ApiKeyAuthenticationOptions> configure = null,
            string authenticationScheme = ApiKeyAuthenticationDefaults.AuthenticationScheme)
        {
            if (null == builder)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (string.IsNullOrWhiteSpace(authenticationScheme))
            {
                throw new ArgumentException(
                    "An authentication scheme name is required.",
                    nameof(authenticationScheme));
            }

            return
                builder.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                    authenticationScheme,
                    configure ?? (options => { }));
        }

        /// <summary>
        /// Adds API key authentication as the default scheme, backed by
        /// <paramref name="store"/>.
        /// </summary>
        /// <param name="headerName">The header the key is read from.</param>
        /// <param name="headerScheme">
        /// The RFC 7235 auth-scheme the header value carries, for a key presented as
        /// <c>Authorization: Bearer &lt;key&gt;</c>. Null - the default - means the whole
        /// header value is the key.
        /// </param>
        public static IServiceCollection AddApiKeyAuthentication(
            this IServiceCollection services,
            IApiKeyStore store,
            string headerName = ApiKeyAuthenticationDefaults.HeaderName,
            string authenticationScheme = ApiKeyAuthenticationDefaults.AuthenticationScheme,
            string headerScheme = null)
        {
            if (null == services)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (null == store)
            {
                throw new ArgumentNullException(nameof(store));
            }

            void Configure(ApiKeyAuthenticationOptions options)
            {
                options.Store = store;
                options.HeaderName = headerName;
                options.HeaderScheme = headerScheme;
            }

            services
                .AddAuthentication(authenticationScheme)
                .AddApiKey(Configure, authenticationScheme);

            return services;
        }
    }
}

#endif
