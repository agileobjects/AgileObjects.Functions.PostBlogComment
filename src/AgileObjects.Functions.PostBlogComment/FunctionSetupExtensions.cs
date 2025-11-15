using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Octokit;
using Octokit.Internal;
using YamlDotNet.Serialization;

namespace AgileObjects.Functions.PostBlogComment;

internal static class FunctionSetupExtensions
{
    public static void AddPostBlogCommentFunction(
        this IServiceCollection services)
    {
        services
            .ConfigureJsonSerializer()
            .AddGitHubClient()
            .AddCommentInfo()
            .AddSingleton(new SerializerBuilder().Build());
    }

    private static IServiceCollection ConfigureJsonSerializer(
        this IServiceCollection services)
    {
        return services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain
                .Insert(0, FunctionAppSerializerContext.Default);
        });
    }

    private static IServiceCollection AddGitHubClient(
        this IServiceCollection services)
    {
        return services.AddSingleton(sp =>
        {
            var configuration = sp
                .GetRequiredService<IConfiguration>();

            var githubCredentials =
                new Credentials(configuration["GitHubToken"]);

            var githubClient = new GitHubClient(
                new("PostCommentToPullRequest"),
                new InMemoryCredentialStore(githubCredentials));

            return githubClient;
        });
    }

    private static IServiceCollection AddCommentInfo(
        this IServiceCollection services)
    {
        return services.AddSingleton(sp =>
        {
            var configuration = sp
                .GetRequiredService<IConfiguration>();

            var repoOwnerParts =
                configuration["PullRequestRepository"]!.Split('/');

            return new CommentInfo
            {
                Repo =
                {
                    OwnerName = repoOwnerParts[0],
                    Name = repoOwnerParts[1]
                },
                CommitterFallbackEmail = configuration["CommentFallbackCommitEmail"]!
            };
        });
    }
}