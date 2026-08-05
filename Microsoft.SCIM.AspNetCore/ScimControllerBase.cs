// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// The ASP.NET Core half of the old <c>ControllerTemplate</c>: request conversion and
    /// <see cref="ScimResult"/> translation. All orchestration lives in
    /// <see cref="ScimRequestHandler{T}"/> in Microsoft.SCIM, shared with the net48 leg.
    /// </summary>
    public abstract class ScimControllerBase : ControllerBase
    {
        protected const string AttributeValueIdentifier = "{identifier}";
        private const string HeaderKeyLocation = "Location";

        protected HttpRequestMessage ConvertRequest()
        {
            return HttpContextRequestConverter.Convert(this.HttpContext);
        }

        /// <summary>
        /// Translates a <see cref="ScimResult"/> into an MVC action result.
        /// </summary>
        /// <remarks>
        /// Restricting <c>ContentTypes</c> to <c>application/scim+json</c> both satisfies
        /// RFC 7644 section 3.1 and short-circuits content negotiation, so the two hosting
        /// legs cannot disagree about the response media type. The Newtonsoft output
        /// formatter accepts it because it matches <c>application/*+json</c>.
        /// </remarks>
        protected IActionResult ToActionResult(ScimResult result)
        {
            if (null == result)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (null != result.Location)
            {
                this.Response.Headers[ScimControllerBase.HeaderKeyLocation] = result.Location.AbsoluteUri;
            }

            if (null == result.Payload)
            {
                return this.StatusCode((int)result.StatusCode);
            }

            ObjectResult objectResult =
                new ObjectResult(result.Payload)
                {
                    StatusCode = (int)result.StatusCode
                };
            objectResult.ContentTypes.Add(ProtocolConstants.ContentType);
            return objectResult;
        }
    }

    /// <summary>
    /// The verb surface for a SCIM resource endpoint. Routes, verbs and binding sources are
    /// carried over from <c>ControllerTemplate&lt;T&gt;</c> unchanged, except that
    /// <c>[FromUri]</c> becomes <c>[FromRoute]</c> - the net48 leg keeps the native
    /// <c>[FromUri]</c>. See MULTI-TARGET-PLAN.md D24.
    /// </summary>
    public abstract class ScimResourceControllerBase<T> : ScimControllerBase where T : Resource
    {
        private readonly ScimRequestHandler<T> handler;

        protected ScimResourceControllerBase(ScimRequestHandler<T> handler)
        {
            this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        [HttpDelete(ScimControllerBase.AttributeValueIdentifier)]
        public virtual async Task<IActionResult> Delete(string identifier)
        {
            ScimResult result =
                await this.handler.DeleteAsync(this.ConvertRequest(), identifier).ConfigureAwait(false);
            return this.ToActionResult(result);
        }

        [HttpGet]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1716:IdentifiersShouldNotMatchKeywords", MessageId = "Get", Justification = "The names of the methods of a controller must correspond to the names of hypertext markup verbs")]
        public virtual async Task<IActionResult> Get()
        {
            ScimResult result =
                await this.handler.QueryAsync(this.ConvertRequest()).ConfigureAwait(false);
            return this.ToActionResult(result);
        }

        [HttpGet(ScimControllerBase.AttributeValueIdentifier)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1716:IdentifiersShouldNotMatchKeywords", MessageId = "Get", Justification = "The names of the methods of a controller must correspond to the names of hypertext markup verbs")]
        public virtual async Task<IActionResult> Get([FromRoute] string identifier)
        {
            ScimResult result =
                await this.handler.RetrieveAsync(this.ConvertRequest(), identifier).ConfigureAwait(false);
            return this.ToActionResult(result);
        }

        [HttpPatch(ScimControllerBase.AttributeValueIdentifier)]
        public virtual async Task<IActionResult> Patch(string identifier, [FromBody] PatchRequest2 patchRequest)
        {
            ScimResult result =
                await this.handler
                    .PatchAsync(this.ConvertRequest(), identifier, patchRequest)
                    .ConfigureAwait(false);
            return this.ToActionResult(result);
        }

        [HttpPost]
        public virtual async Task<IActionResult> Post([FromBody] T resource)
        {
            ScimResult result =
                await this.handler.CreateAsync(this.ConvertRequest(), resource).ConfigureAwait(false);
            return this.ToActionResult(result);
        }

        [HttpPut(ScimControllerBase.AttributeValueIdentifier)]
        public virtual async Task<IActionResult> Put([FromBody] T resource, string identifier)
        {
            ScimResult result =
                await this.handler
                    .ReplaceAsync(this.ConvertRequest(), resource, identifier)
                    .ConfigureAwait(false);
            return this.ToActionResult(result);
        }
    }
}
