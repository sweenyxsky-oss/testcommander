namespace TestCommander.Models;

public sealed record CapturedResponse(string UrlPath, string ContentType, int StatusCode, int BodyBytes, DateTimeOffset CapturedAt, string Summary);