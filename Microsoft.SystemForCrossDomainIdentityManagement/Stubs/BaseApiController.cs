using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Results;

namespace Microsoft.SCIM
{
    public abstract class BaseApiController : ApiController
    {
        private HttpResponse _response;

        protected HttpResponse Response
        {
            get
            {
                if (_response == null)
                {
                    _response = new HttpResponse();
                }

                return _response;
            }
        }

        protected IHttpActionResult StatusCode<T>(int statusCode, T content)
        {
            var result = new NegotiatedContentResult<T>((HttpStatusCode)statusCode, content, this);
            return result;
        }

        protected IHttpActionResult CreatedAtAction<T>(string actionName, T content)
        {
            Response.Headers.Location = Request.RequestUri.OriginalString;
            var result = new NegotiatedContentResult<T>(HttpStatusCode.Created, content, this);
            return result;
        }

        protected IHttpActionResult NoContent()
        {
            var response = Request.CreateResponse();
            response.StatusCode = HttpStatusCode.NoContent;
            var result = new ResponseMessageResult(response);
            return result;
        }

        public override async Task<HttpResponseMessage> ExecuteAsync(HttpControllerContext controllerContext,
            CancellationToken cancellationToken)
        {
            var response = await base.ExecuteAsync(controllerContext, cancellationToken);

            if (Response != null)
            {
                if (Response.ContentType != null && response.Content != null)
                {
                    response.Content.Headers.ContentType = new MediaTypeHeaderValue(Response.ContentType);
                }

                if (Response.StatusCode != -1)
                {
                    response.StatusCode = (HttpStatusCode)Response.StatusCode;    
                }

                foreach (var h in Response.Headers)
                {
                    response.Headers.TryAddWithoutValidation(h.Key, h.Value);
                }
            }

            return response;
        }
    }
}