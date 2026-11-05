using HomeShowing.BuildingBlocks.Results;
using Microsoft.AspNetCore.Http;

namespace HomeShowing.BuildingBlocks.Web;

public static class ResultExtensions
{
    public static IResult ToProblem(this Error error)
    {
        var status = error.Kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorKind.Unprocessable => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status500InternalServerError,
        };
        return TypedResults.Problem(title: error.Title, detail: error.Detail, statusCode: status,
            extensions: error.Extensions?.ToDictionary(k => k.Key, v => v.Value));
    }

    public static IResult ToHttp<T>(this Result<T> result, Func<T, IResult>? onSuccess = null) =>
        result.IsSuccess ? (onSuccess?.Invoke(result.Value) ?? TypedResults.Ok(result.Value)) : result.Error!.ToProblem();
}
