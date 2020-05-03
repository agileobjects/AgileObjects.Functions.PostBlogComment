namespace AgileObjects.Functions.PostBlogComment
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Linq;
    using System.Reflection;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using System.Web.Http;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Azure.WebJobs;
    using Microsoft.Azure.WebJobs.Extensions.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using Octokit;
    using Octokit.Internal;
    using YamlDotNet.Serialization;
    using static System.Environment;
    using static System.StringComparison;
    using static System.Text.RegularExpressions.RegexOptions;
    using static System.UriKind;

    public class PostBlogComment
    {
        private readonly string _commentSiteHost;
        private readonly GitHubClient _githubClient;
        private readonly IRepositoriesClient _githubRepoClient;
        private readonly string _repoOwnerName;
        private readonly string _repoName;
        private readonly string _committerFallbackEmail;

        public PostBlogComment(IConfiguration configuration)
        {
            _commentSiteHost = new Uri(configuration["CommentWebsiteUrl"], Absolute).Host;

            var githubCredentials = new Credentials(configuration["GitHubToken"]);

            _githubClient = new GitHubClient(
                new ProductHeaderValue("PostCommentToPullRequest"),
                new InMemoryCredentialStore(githubCredentials));

            _githubRepoClient = _githubClient.Repository;

            // Get a reference to our GitHub repository
            var repoOwnerParts = configuration["PullRequestRepository"].Split('/');
            _repoOwnerName = repoOwnerParts[0];
            _repoName = repoOwnerParts[1];

            _committerFallbackEmail = configuration["CommentFallbackCommitEmail"];
        }

        [FunctionName("PostBlogComment")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest request,
            ILogger log)
        {
            log.LogTrace("AgileObjects.Functions.Email.PostBlogComment triggered");

            var form = await request.ReadFormAsync();

            if (IsNotCommentFromCorrectSite(form))
            {
                return new BadRequestErrorMessageResult(
                    "Please make sure you post this to your own Jekyll comments receiever.");
            }

            if (Comment.TryCreate(form, out var comment, out var errors))
            {
                await CreatePullRequestFor(comment);
            }

            if (errors.Any())
            {
                return new BadRequestErrorMessageResult(string.Join(NewLine, errors));
            }

            if (Uri.TryCreate(form["redirect"], Absolute, out var redirectUri))
            {
                return new RedirectResult(redirectUri.ToString());
            }

            return new OkResult();
        }

        private bool IsNotCommentFromCorrectSite(IFormCollection form)
        {
            if (!Uri.TryCreate(form["comment-site"], Absolute, out var postedCommentSite))
            {
                return true;
            }

            return !_commentSiteHost.Equals(postedCommentSite.Host, OrdinalIgnoreCase);
        }

        private async Task CreatePullRequestFor(Comment comment)
        {
            var repo = await GetGithubRepo();
            var defaultBranch = await GetDefaultBranch(repo);
            var prBranch = await CreatePullRequestBranch(repo, defaultBranch, comment);
            var fileRequest = await CreateCommentFile(repo, prBranch, comment);

            await CreatePullRequest(repo, defaultBranch, prBranch, comment, fileRequest);
        }

        #region GitHub Helpers

        private Task<Repository> GetGithubRepo()
            => _githubRepoClient.Get(_repoOwnerName, _repoName);

        private Task<Branch> GetDefaultBranch(Repository repo)
            => _githubRepoClient.Branch.Get(repo.Id, repo.DefaultBranch);

        private async Task<Reference> CreatePullRequestBranch(
            Repository repo,
            Branch defaultBranch,
            Comment comment)
        {
            var reference = new NewReference($"refs/heads/comment-{comment.id}", defaultBranch.Commit.Sha);
            var pullRequestBranch = await _githubClient.Git.Reference.Create(repo.Id, reference);

            return pullRequestBranch;
        }

        private async Task<CreateFileRequest> CreateCommentFile(
            Repository repo,
            Reference prBranch,
            Comment comment)
        {
            var commitMessage = $"Comment by {comment.name} on {comment.post_id}";
            var commentContent = new SerializerBuilder().Build().Serialize(comment);
            var commenterEmail = comment.email ?? _committerFallbackEmail;

            var fileRequest = new CreateFileRequest(commitMessage, commentContent, prBranch.Ref)
            {
                Committer = new Committer(comment.name, commenterEmail, comment.date)
            };

            var commentFilePath = $"_data/comments/{comment.post_id}/{comment.id}.yml";

            await _githubRepoClient.Content.CreateFile(repo.Id, commentFilePath, fileRequest);

            return fileRequest;
        }

        private Task CreatePullRequest(
            Repository repo,
            Branch defaultBranch,
            Reference prBranch,
            Comment comment,
            ContentRequest fileRequest)
        {
            var pullRequest = new NewPullRequest(fileRequest.Message, prBranch.Ref, defaultBranch.Name)
            {
                Body = $"avatar: <img src=\"{comment.avatar}\" />{NewLine}{NewLine}{comment.message}"
            };

            return _githubRepoClient.PullRequest.Create(repo.Id, pullRequest);
        }

        #endregion

        /// <summary>
        /// Represents a Comment to be written to the repository in YML format.
        /// </summary>
        private class Comment
        {
            private static readonly ConstructorInfo _ctor = typeof(Comment).GetConstructors()[0];
            private static readonly ParameterInfo[] _ctorParameters = _ctor.GetParameters();

            // Valid characters when mapping from the blog post slug to a file path
            private static readonly Regex _validPathChars = new Regex(@"[^a-zA-Z0-9-]", Compiled);
            private static readonly Regex _emailMatcher = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", Compiled);

            public Comment(
                string post_id,
                string message,
                string name,
                string email = null,
                Uri url = null,
                string avatar = null)
            {
                this.post_id = _validPathChars.Replace(post_id, "-");
                this.message = message;
                this.name = name;
                this.email = email;
                this.url = url;

                date = DateTime.UtcNow;
                id = new { this.post_id, this.name, this.message, this.date }.GetHashCode().ToString("x8");

                if (Uri.TryCreate(avatar, Absolute, out Uri avatarUrl))
                {
                    this.avatar = avatarUrl;
                }
            }

            #region Factory Method

            /// <summary>
            /// Try to create a Comment from the form.  Each Comment constructor argument will be name-matched
            /// against values in the form. Each non-optional arguments (those that don't have a default value)
            /// not supplied will cause an error in the list of errors and prevent the Comment from being created.
            /// </summary>
            /// <param name="form">Incoming form submission as a <see cref="NameValueCollection"/>.</param>
            /// <param name="comment">Created <see cref="Comment"/> if no errors occurred.</param>
            /// <param name="errors">A list containing any potential validation errors.</param>
            /// <returns>True if the Comment was able to be created, false if validation errors occurred.</returns>
            public static bool TryCreate(
                IFormCollection form,
                out Comment comment,
                out List<string> errors)
            {
                var values = _ctorParameters
                    .ToDictionary(p => p.Name, p => GetParameterValue(form, p));

                errors = values
                    .Where(p => p.Value is MissingRequiredValue)
                    .Select(p => $"Form value missing for {p.Key}")
                    .ToList();

                if (values["email"] is string email && !_emailMatcher.IsMatch(email))
                {
                    errors.Add($"'{email}' is not a valid email address");
                }

                if (errors.Any())
                {
                    comment = null;
                    return false;
                }

                comment = (Comment)_ctor.Invoke(values.Values.ToArray());
                return true;
            }

            private static object GetParameterValue(IFormCollection form, ParameterInfo parameter)
            {
                var value = form[parameter.Name];

                if (string.IsNullOrWhiteSpace(value))
                {
                    return GetParameterFallbackValue(parameter);
                }

                return TypeDescriptor.GetConverter(parameter.ParameterType).ConvertFrom(value)
                    ?? GetParameterFallbackValue(parameter);
            }

            private static object GetParameterFallbackValue(ParameterInfo parameter)
                => parameter.HasDefaultValue ? parameter.DefaultValue : default(MissingRequiredValue);

            #endregion

            [YamlIgnore]
            public string post_id { get; }

            public string id { get; }

            public DateTime date { get; }

            public string name { get; }

            public string email { get; }

            [YamlMember(typeof(string))]
            public Uri avatar { get; }

            [YamlMember(typeof(string))]
            public Uri url { get; }

            public string message { get; }
        }

        private struct MissingRequiredValue { }
    }
}
