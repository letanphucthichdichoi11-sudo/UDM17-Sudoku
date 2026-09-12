using System.Net;

namespace Sudoku.Shared.Network
{
    public enum ApiErrorKind
    {
        None,
        Connection,
        Timeout,
        Unauthorized,
        Forbidden,
        Server,
        InvalidResponse,
        Unknown
    }

    public static class ApiErrorClassifier
    {
        public static ApiErrorKind FromStatusCode(HttpStatusCode statusCode)
        {
            if (statusCode == HttpStatusCode.Unauthorized)
                return ApiErrorKind.Unauthorized;
            if (statusCode == HttpStatusCode.Forbidden)
                return ApiErrorKind.Forbidden;
            if ((int)statusCode >= 500)
                return ApiErrorKind.Server;
            return ApiErrorKind.Unknown;
        }
    }
}
