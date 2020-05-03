using Microsoft.Azure.Functions.Extensions.DependencyInjection;

[assembly: FunctionsStartup(typeof(AgileObjects.Functions.PostBlogComment.Startup))]

namespace AgileObjects.Functions.PostBlogComment
{
    using Microsoft.Azure.Functions.Extensions.DependencyInjection;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;

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
            => builder.Services.AddSingleton(_configuration);
    }
}