namespace Microsoft.SCIM
{
    public class HttpResponse
    {
        public string ContentType { get; set; } = "text/html";

        public int StatusCode { get; set; } = -1;

        public HttpHeaders Headers { get; set; } = new HttpHeaders();
    }
}