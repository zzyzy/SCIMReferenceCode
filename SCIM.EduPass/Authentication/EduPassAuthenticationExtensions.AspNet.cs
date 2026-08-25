// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

#if NET48

namespace Scim.EduPass
{
    using System;
    using Anacle.ApiFramework.Authentication.ApiKey;
    using Anacle.ApiFramework.Authentication.Jwt;
    using Owin;

    /// <summary>
    /// Wires the configured Edupass authentication modes into an OWIN pipeline.
    /// </summary>
    public static class EduPassAuthenticationExtensions
    {
        /// <summary>
        /// Adds the modes named by <paramref name="settings"/>. Call before the protected
        /// endpoints run.
        /// </summary>
        /// <remarks>
        /// OWIN has no scheme registry, so with both modes enabled the two middlewares simply
        /// run in turn and whichever recognises the request sets the principal. Both are
        /// passive: neither short-circuits, and a request that satisfies neither reaches the
        /// endpoint anonymous for <c>[Authorize]</c> to reject. That also means the modes
        /// cannot be selected per endpoint on this leg the way they can on ASP.NET Core.
        /// </remarks>
        public static IAppBuilder UseEduPassAuthentication(
            this IAppBuilder app,
            EduPassAuthenticationSettings settings)
        {
            if (null == app)
            {
                throw new ArgumentNullException(nameof(app));
            }

            if (null == settings)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            settings.Validate();

            if (settings.IsJwtEnabled)
            {
                app.UseJsonWebKeySetAuthentication(settings.CreateJsonWebKeySetOptions());
            }

            if (settings.IsApiKeyEnabled)
            {
                app.UseApiKeyAuthentication(settings.ApiKeyStore, settings.ApiKeyHeaderName);
            }

            return app;
        }
    }
}

#endif
