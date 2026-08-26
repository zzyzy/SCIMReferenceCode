// Copyright (c) Microsoft Corporation.// Licensed under the MIT license.

namespace Microsoft.SCIM
{
    using System;
    using System.Threading.Tasks;
    using System.Web.Http;

    /// <summary>
    /// The ASP.NET Web API half of the old <c>ControllerTemplate</c>:
    /// <see cref="ScimResult"/> translation and <see cref="HttpResponseException"/>
    /// containment. All orchestration lives in <see cref="ScimRequestHandler{T}"/> in
    /// Microsoft.SCIM, shared with the net10.0 leg.
    /// </summary>
    /// <remarks>
    /// <c>this.Request</c> is already an <c>HttpRequestMessage</c> here, so unlike the
    /// ASP.NET Core leg there is nothing to convert.
    /// </remarks>
    public abstract class ScimApiControllerBase : ApiController
    {
        protected IHttpActionResult ToActionResult(ScimResult result)
        {
            return new ScimActionResult(this.Request, result);
        }

        /// <summary>
        /// Runs a handler operation, mapping an <see cref="HttpResponseException"/> that
        /// escapes it to a <see cref="Core2Error"/> response.
        /// </summary>
        /// <remarks>
        /// This catch is why <see cref="ScimExceptionFilterAttribute"/> is not sufficient on
        /// its own: ASP.NET Web API special-cases <c>HttpResponseException</c> and returns the
        /// exception's own - body-less - response without ever consulting exception filters.
        /// Catching it here produces the same status AND the same <c>Core2Error</c> body that
        /// the ASP.NET Core leg's filter produces. The filter stays registered for throws that
        /// originate outside an action.
        /// </remarks>
        protected async Task<IHttpActionResult> ExecuteAsync(Func<Task<ScimResult>> operation)
        {
            if (null == operation)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            try
            {
                return this.ToActionResult(await operation().ConfigureAwait(false));
            }
            catch (HttpResponseException responseException)
            {
                return this.ToActionResult(ScimResult.FromException(responseException));
            }
        }

        /// <summary>The synchronous counterpart, for the discovery endpoints.</summary>
        protected IHttpActionResult Execute(Func<ScimResult> operation)
        {
            if (null == operation)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            try
            {
                return this.ToActionResult(operation());
            }
            catch (HttpResponseException responseException)
            {
                return this.ToActionResult(ScimResult.FromException(responseException));
            }
        }
    }

    /// <summary>
    /// The verb surface for a SCIM resource endpoint, translated from
    /// <c>ControllerTemplate&lt;T&gt;</c>. Web API verb attributes take no route template, so
    /// each action carries a separate <c>[Route]</c>; the controller carries a
    /// <c>[RoutePrefix]</c>.
    /// </summary>
    public abstract class ScimApiResourceControllerBase<T> : ScimApiControllerBase where T : Resource
    {
        protected const string AttributeValueIdentifier = "{identifier}";
        protected const string AttributeValueCollection = "";

        private readonly ScimRequestHandler<T> handler;

        protected ScimApiResourceControllerBase(ScimRequestHandler<T> handler)
        {
            this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        [HttpDelete]
        [Route(ScimApiResourceControllerBase<T>.AttributeValueIdentifier)]
        public virtual Task<IHttpActionResult> Delete(string identifier)
        {
            return this.ExecuteAsync(() => this.handler.DeleteAsync(this.Request, identifier));
        }

        [HttpGet]
        [Route(ScimApiResourceControllerBase<T>.AttributeValueCollection)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1716:IdentifiersShouldNotMatchKeywords", MessageId = "Get", Justification = "The names of the methods of a controller must correspond to the names of hypertext markup verbs")]
        public virtual Task<IHttpActionResult> Get()
        {
            return this.ExecuteAsync(() => this.handler.QueryAsync(this.Request));
        }

        [HttpGet]
        [Route(ScimApiResourceControllerBase<T>.AttributeValueIdentifier)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1716:IdentifiersShouldNotMatchKeywords", MessageId = "Get", Justification = "The names of the methods of a controller must correspond to the names of hypertext markup verbs")]
        public virtual Task<IHttpActionResult> Get([FromUri] string identifier)
        {
            return this.ExecuteAsync(() => this.handler.RetrieveAsync(this.Request, identifier));
        }

        [HttpPatch]
        [Route(ScimApiResourceControllerBase<T>.AttributeValueIdentifier)]
        public virtual Task<IHttpActionResult> Patch(string identifier, [FromBody] PatchRequest2 patchRequest)
        {
            return this.ExecuteAsync(() => this.handler.PatchAsync(this.Request, identifier, patchRequest));
        }

        [HttpPost]
        [Route(ScimApiResourceControllerBase<T>.AttributeValueCollection)]
        public virtual Task<IHttpActionResult> Post([FromBody] T resource)
        {
            return this.ExecuteAsync(() => this.handler.CreateAsync(this.Request, resource));
        }

        /// <summary>
        /// A query made with POST, per RFC 7644 section 3.4.3.
        /// </summary>
        /// <remarks>
        /// A separate route from <see cref="Post(T)"/> rather than a branch inside it: the two
        /// take different bodies, and letting the binder choose by shape would make a
        /// malformed creation read as a search of everything.
        /// </remarks>
        [HttpPost]
        [Route(ServiceConstants.PathSegmentSearch)]
        public virtual Task<IHttpActionResult> Search([FromBody] SearchRequest search)
        {
            return this.ExecuteAsync(() => this.handler.SearchAsync(this.Request, search));
        }

        [HttpPut]
        [Route(ScimApiResourceControllerBase<T>.AttributeValueIdentifier)]
        public virtual Task<IHttpActionResult> Put([FromBody] T resource, string identifier)
        {
            return this.ExecuteAsync(() => this.handler.ReplaceAsync(this.Request, resource, identifier));
        }
    }
}
