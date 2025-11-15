namespace AgileObjects.Functions.PostBlogComment;

public sealed class CommentInfo
{
    public CommentRepo Repo { get; } = new();

    public string CommitterFallbackEmail { get; set; } = string.Empty;
}