using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sudoku.Shared.Network;

namespace Sudoku.Server.Tests
{
    [TestClass]
    public class ApiErrorClassifierTests
    {
        [DataTestMethod]
        [DataRow(HttpStatusCode.Unauthorized, ApiErrorKind.Unauthorized)]
        [DataRow(HttpStatusCode.Forbidden, ApiErrorKind.Forbidden)]
        [DataRow(HttpStatusCode.InternalServerError, ApiErrorKind.Server)]
        [DataRow(HttpStatusCode.ServiceUnavailable, ApiErrorKind.Server)]
        [DataRow(HttpStatusCode.BadRequest, ApiErrorKind.Unknown)]
        public void HttpFailure_PreservesItsRealCategory(
            HttpStatusCode statusCode,
            ApiErrorKind expected)
        {
            Assert.AreEqual(expected, ApiErrorClassifier.FromStatusCode(statusCode));
        }
    }
}
