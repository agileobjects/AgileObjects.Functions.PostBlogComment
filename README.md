# AgileObjects.Functions.PostBlogComment

An Azure Function to post a comment to a [GitHub Pages](https://pages.github.com) 
[Jekyll](https://jekyllrb.com) blog by automatically creating a pull request in the blog repository.

Phil Haack [has described](https://haacked.com/archive/2018/06/24/comments-for-jekyll-blogs) using
JavaScript and an Azure Function to support comments on his blog. 
[The Azure Function](https://github.com/Haacked/jekyll-blog-comments-azure) is forked from [an 
Azure function](https://github.com/Azure-Functions/jekyll-blog-comments) by [Damien 
Guard](https://damieng.com) - _this_ Azure Function updates it to .NET Core 3.1, tidies up a bit and 
adds [dependency injection](https://docs.microsoft.com/en-us/azure/azure-functions/functions-dotnet-dependency-injection).

## Setup

Borrowed from [Phil Haack's repository](https://github.com/Haacked/jekyll-blog-comments-azure):

1. Create an [Azure Portal account](https://portal.azure.com).
2. Fork this repository.
3. [Create an Azure Function](https://docs.microsoft.com/en-us/azure/azure-functions/functions-create-first-azure-function)
4. [optional] [Set up your function to deploy from GitHub](https://docs.microsoft.com/en-us/azure/azure-functions/scripts/functions-cli-create-function-app-github-continuous). 
   Point it to your fork of this repository.
5. Set up the following [App Settings for your Azure Function App](https://docs.microsoft.com/en-us/azure/azure-functions/functions-how-to-use-azure-function-app-settings).

| Setting                    | Value |
|----------------------------|-------|
| PullRequestRepository      | `owner/name` of the repository that houses your Jekyll site for pull requests to be created against. For example, agileobjects/blog will post to https://github.com/agileobjects/blog |
| GitHubToken                | A [GitHub personal access token](https://help.github.com/articles/creating-a-personal-access-token-for-the-command-line) with access to edit your target repository. |
| CommentFallbackCommitEmail | The email address to use for GitHub commits and PR's if the form does not supply one. |

The `CommentWebsiteUrl` setting has been removed from this version of the function - you can use 
[CORS](https://docs.microsoft.com/en-us/azure/azure-functions/functions-how-to-use-azure-function-app-settings#cors)
to restrict which website(s) can post comments to your function.

## Use

Use an HTML form or AJAX call to post the following data to the function URL:

| Name    | Value |
|---------|-------|
| postId  | A unique identifier for the post on which the comment is being made. |
| name    | The commenter's name. |
| message | The comment. |
| email   | [optional] The commenter's email address. |
| avatar  | [optional] The URL of the commenter's avatar image. |
| url     | [optional] A URL to which the commenter's name and avatar will link on the posted comment. |

## Responses

The function will respond with one of the following:

| Status | Reason |
|--------|--------|
| 200    | Comment posted successfully. |
| 500    | Something unexpected went wrong. |
| 400    | A piece of required information was either missing, or invalid. A collection of new-line-separated error messages is returned to say what. |

A successful posting creates a pull request in the configured GitHub repository with the comment in 
[a YAML file](https://en.wikipedia.org/wiki/YAML). Completing the pull request commits the comment's 
file to the repository's `_data` folder, adding it to the collection of comments belonging to the 
relevant blog, to be rendered by Jekyll.
