// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Net.Http;
    using System.Net.Http.Formatting;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web.Http;

    /// <summary>
    /// Renders a <see cref="ScimResult"/> as an ASP.NET Web API action result.
    /// </summary>
    /// <remarks>
    /// Written by hand rather than composed from <c>ApiController.Ok</c> /
    /// <c>Content</c> / <c>Created</c> because those go through content negotiation, and the
    /// media type and the <c>Location</c> header both have to come out byte-identical to the
    /// ASP.NET Core leg. Serializing through <c>Formatters.JsonFormatter</c> keeps the
    /// Newtonsoft settings configured in <see cref="ScimHttpConfiguration"/> - notably
    /// <c>NullValueHandling.Ignore</c>.
    /// </remarks>
    internal sealed class ScimActionResult : IHttpActionResult
    {
        private readonly HttpRequestMessage request;
        private readonly ScimResult result;

        public ScimActionResult(HttpRequestMessage request, ScimResult result)
        {
            this.request = request ?? throw new ArgumentNullException(nameof(request));
            this.result = result ?? throw new ArgumentNullException(nameof(result));
        }

        public Task<HttpResponseMessage> ExecuteAsync(CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new HttpResponseMessage(this.result.StatusCode);
            response.RequestMessage = this.request;

            if (null != this.result.Location)
            {
                response.Headers.Location = this.result.Location;
            }

            if (null != this.result.Payload)
            {
                MediaTypeFormatter formatter = this.request.GetConfiguration().Formatters.JsonFormatter;
                response.Content =
                    new ObjectContent(
                        this.result.Payload.GetType(),
                        this.result.Payload,
                        formatter,
                        ProtocolConstants.ContentType);
            }

            return Task.FromResult(response);
        }
    }
}
