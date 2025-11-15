using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Octokit;
using YamlDotNet.Serialization;
using static System.Environment;

namespace AgileObjects.Functions.PostBlogComment;

public sealed class PostBlogComment
{
    private readonly ILogger<PostBlogComment> _logger;
    private readonly GitHubClient _githubClient;
    private readonly IRepositoriesClient _githubRepoClient;
    private readonly CommentInfo _info;
    private readonly ISerializer _yamlSerializer;

    public PostBlogComment(
        ILogger<PostBlogComment> logger,
        GitHubClient githubClient,
        CommentInfo info,
        ISerializer yamlSerializer)
    {
        _logger = logger;
        _githubClient = githubClient;
        _githubRepoClient = _githubClient.Repository;
        _info = info;
        _yamlSerializer = yamlSerializer;
    }

    [Function(nameof(PostBlogComment))]
    public async Task<IActionResult> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest request)
    {
        _logger.LogTrace("Function triggered");

        var form = await request.ReadFormAsync().ConfigureAwait(false);

        try
        {
            if (!Comment.TryCreate(form, out var comment, out var errors))
            {
                return new BadRequestObjectResult(string.Join(NewLine, errors));
            }

            var (prUri, prId) = await
                CreatePullRequestForAsync(comment)
               .ConfigureAwait(false);

            _logger.LogInformation("Blog comment posted.");

            return new CreatedResult(prUri, prId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Blog comment posting failed. An unspecified error occurred.");

            throw;
        }
    }

    private async Task<(Uri, long)> CreatePullRequestForAsync(Comment comment)
    {
        var repo = await
            GetGithubRepoAsync()
           .ConfigureAwait(false);

        var defaultBranch = await
            GetDefaultBranchAsync(repo)
           .ConfigureAwait(false);

        var prBranch = await
            CreatePullRequestBranchAsync(repo, defaultBranch, comment)
           .ConfigureAwait(false);

        var fileRequest = await
            CreateCommentFileAsync(repo, prBranch, comment)
           .ConfigureAwait(false);

        var pr = await
            CreatePullRequestAsync(repo, defaultBranch, prBranch, comment, fileRequest)
           .ConfigureAwait(false);

        return (new(pr.HtmlUrl), pr.Id);
    }

    private Task<Repository> GetGithubRepoAsync() =>
        _githubRepoClient.Get(_info.Repo.OwnerName, _info.Repo.Name);

    private Task<Branch> GetDefaultBranchAsync(Repository repo) =>
        _githubRepoClient.Branch.Get(repo.Id, repo.DefaultBranch);

    private async Task<Reference> CreatePullRequestBranchAsync(
        Repository repo,
        Branch defaultBranch,
        Comment comment)
    {
        var reference = new NewReference($"refs/heads/comment-{comment.Id}", defaultBranch.Commit.Sha);

        var pullRequestBranch = await _githubClient
            .Git.Reference.Create(repo.Id, reference)
            .ConfigureAwait(false);

        return pullRequestBranch;
    }

    private async Task<CreateFileRequest> CreateCommentFileAsync(
        Repository repo,
        Reference prBranch,
        Comment comment)
    {
        var commitMessage = $"Comment by {comment.Name} on {comment.PostId}";
        var commentContent = _yamlSerializer.Serialize(comment);
        var commenterEmail = comment.Email ?? _info.CommitterFallbackEmail;

        var fileRequest = new CreateFileRequest(commitMessage, commentContent, prBranch.Ref)
        {
            Committer = new(comment.Name, commenterEmail, comment.Date)
        };

        var commentFilePath = $"_data/comments/{comment.PostId}/{comment.Id}.yml";

        await _githubRepoClient.Content
            .CreateFile(repo.Id, commentFilePath, fileRequest)
            .ConfigureAwait(false);

        return fileRequest;
    }

    private Task<PullRequest> CreatePullRequestAsync(
        Repository repo,
        Branch defaultBranch,
        Reference prBranch,
        Comment comment,
        ContentRequest fileRequest)
    {
        var pullRequest = new NewPullRequest(fileRequest.Message, prBranch.Ref, defaultBranch.Name)
        {
            Body = $"avatar: <img src=\"{comment.AvatarUri}\" />{NewLine}{NewLine}{comment.Message}"
        };

        return _githubRepoClient.PullRequest.Create(repo.Id, pullRequest);
    }
}