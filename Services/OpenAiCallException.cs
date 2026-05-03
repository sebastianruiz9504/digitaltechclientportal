using System.Net;

namespace DigitalTechApp.Services
{
    public sealed class OpenAiCallException : Exception
    {
        public OpenAiCallException(
            string message,
            HttpStatusCode? statusCode = null,
            string? errorCode = null,
            string? responseBody = null,
            Exception? innerException = null)
            : base(message, innerException)
        {
            StatusCode = statusCode;
            ErrorCode = errorCode;
            ResponseBody = responseBody;
        }

        public HttpStatusCode? StatusCode { get; }
        public string? ErrorCode { get; }
        public string? ResponseBody { get; }

        public string ToUserMessage()
        {
            var parts = new List<string>();

            if (StatusCode.HasValue)
            {
                parts.Add($"HTTP {(int)StatusCode.Value}");
            }

            if (!string.IsNullOrWhiteSpace(ErrorCode))
            {
                parts.Add(ErrorCode);
            }

            parts.Add(Message);

            return string.Join(" - ", parts);
        }
    }
}
