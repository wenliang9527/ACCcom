namespace ACCcom.Core.Models;

public class ApiResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public object? Data { get; set; }

    public static ApiResponse Ok(object? data = null) => new() { Success = true, Data = data };
    public static ApiResponse Fail(string error) => new() { Success = false, Error = error };
}
