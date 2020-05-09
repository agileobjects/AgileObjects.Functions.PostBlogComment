using Microsoft.Azure.Functions.Extensions.DependencyInjection;

[assembly: FunctionsStartup(typeof(AgileObjects.Functions.PostBlogComment.Startup))]

namespace AgileObjects.Functions.PostBlogComment
{
    using Microsoft.Azure.Functions.Extensions.DependencyInjection;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Octokit;
    using Octokit.Internal;
    using YamlDotNet.Serialization;

    public class Startup : FunctionsStartup
    {
        private readonly IConfiguration _configuration;

        public Startup()
        {
            _configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddUserSecrets(typeof(Startup).Assembly, optional: true)
                .Build();
        }

        public override void Configure(IFunctionsHostBuilder builder)
        {
            builder.Services
                .AddSingleton(BuildGitHubClient())
                .AddSingleton(BuildCommentInfo())
                .AddSingleton(new SerializerBuilder().Build());
        }

        private GitHubClient BuildGitHubClient()
        {
            var githubCredentials = new Credentials(_configuration["GitHubToken"]);

            var githubClient = new GitHubClient(
                new ProductHeaderValue("PostCommentToPullRequest"),
                new InMemoryCredentialStore(githubCredentials));

            return githubClient;
        }

        private CommentInfo BuildCommentInfo()
        {
            var repoOwnerParts = _configuration["PullRequestRepository"].Split('/');

            var info = new CommentInfo
            {
                Repo = new CommentRepo
                {
                    OwnerName = repoOwnerParts[0],
                    Name = repoOwnerParts[1]
                },
                CommitterFallbackEmail = _configuration["CommentFallbackCommitEmail"]
            };
            return info;
        }
    }
}