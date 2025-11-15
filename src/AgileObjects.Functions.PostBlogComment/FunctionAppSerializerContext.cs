using System.Text.Json.Serialization;

namespace AgileObjects.Functions.PostBlogComment;

[JsonSerializable(typeof(string))]
internal sealed partial class FunctionAppSerializerContext : JsonSerializerContext
{
}