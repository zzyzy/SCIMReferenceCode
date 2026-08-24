// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

#if NET48

namespace Anacle.ApiFramework.Authentication.ApiKey
{
    using System;
    using System.Collections.Generic;
    using System.Security.Claims;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Owin;
    using Owin;

    /// <summary>
    /// Authenticates a request by a key in a header, for an OWIN pipeline.
    /// </summary>
    /// <remarks>
    /// The net48 counterpart of <c>ApiKeyAuthenticationHandler</c>. OWIN has no equivalent of
    /// ASP.NET Core's scheme registry, so this is plain middleware that sets the request
    /// principal and lets whatever runs downstream authorise against it. Web API's
    /// <c>[Authorize]</c> reads that principal, so a 401 for a missing or wrong key comes from
    /// the same place on both legs.
    ///
    /// Passive by design: a missing or invalid key leaves the request anonymous rather than
    /// short-circuiting with a 401. That way it can sit in front of endpoints that permit
    /// anonymous access, and it can be combined with another authentication middleware.
    /// </remarks>
    public class ApiKeyAuthenticationMiddleware : OwinMiddleware
    {
        private readonly IApiKeyStore store;
        private readonly string headerName;

        public ApiKeyAuthenticationMiddleware(
            OwinMiddleware next,
            IApiKeyStore store,
            string headerName)
            : base(next)
        {
            if (null == store)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (string.IsNullOrWhiteSpace(headerName))
            {
                throw new ArgumentException("A header name is required.", nameof(headerName));
            }

            this.store = store;
            this.headerName = headerName;
        }

        public override async Task Invoke(IOwinContext context)
        {
            if (null == context)
            {
                throw new ArgumentNullException(nameof(context));
            }

            string presented = context.Request.Headers.Get(this.headerName);

            if (!string.IsNullOrWhiteSpace(presented))
            {
                ApiKeyIdentity identity =
                    await this.store
                        .ResolveAsync(presented, CancellationToken.None)
                        .ConfigureAwait(false);

                if (null != identity)
                {
                    List<Claim> claims =
                        new List<Claim> { new Claim(ClaimTypes.Name, identity.Name) };

                    claims.AddRange(identity.Claims);

                    context.Request.User =
                        new ClaimsPrincipal(
                            new ClaimsIdentity(
                                claims,
                                ApiKeyAuthenticationDefaults.AuthenticationScheme));
                }

                // An invalid key deliberately leaves the request anonymous rather than being
                // logged: an invalid key here is often a valid key elsewhere, and logs are read
                // more widely than secret stores.
            }

            await this.Next.Invoke(context).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Wires API key authentication into an OWIN pipeline.
    /// </summary>
    public static class ApiKeyAuthenticationExtensions
    {
        /// <summary>
        /// Adds API key authentication. Call before the protected endpoints run.
        /// </summary>
        public static IAppBuilder UseApiKeyAuthentication(
            this IAppBuilder app,
            IApiKeyStore store,
            string headerName = ApiKeyAuthenticationDefaults.HeaderName)
        {
            if (null == app)
            {
                throw new ArgumentNullException(nameof(app));
            }

            if (null == store)
            {
                throw new ArgumentNullException(nameof(store));
            }

            return app.Use<ApiKeyAuthenticationMiddleware>(store, headerName);
        }
    }
}

#endif
