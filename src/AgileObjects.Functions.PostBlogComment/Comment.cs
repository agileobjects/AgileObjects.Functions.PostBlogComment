using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using YamlDotNet.Serialization;

namespace AgileObjects.Functions.PostBlogComment;

/// <summary>
/// Represents a Comment to be written to the repository in YML format.
/// </summary>
internal sealed partial class Comment
{
    private static readonly ConstructorInfo _ctor = typeof(Comment).GetConstructors()[0];
    private static readonly ParameterInfo[] _ctorParameters = _ctor.GetParameters();

    // Valid characters when mapping from the blog post slug to a file path
    [GeneratedRegex("[^a-zA-Z0-9-]")]
    private static partial Regex InvalidPathChars();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailMatcher();

    public Comment(
        string postId,
        string message,
        string name,
        string? email = null,
        Uri? url = null,
        string? avatar = null)
    {
        PostId = InvalidPathChars().Replace(postId, "-");
        Message = message;
        Name = name;
        Email = email;
        Uri = url;

        Date = DateTime.UtcNow;
        Id = new { post_id = PostId, name, message, date = Date }.GetHashCode().ToString("x8");

        if (Uri.TryCreate(avatar, UriKind.Absolute, out var avatarUrl))
        {
            AvatarUri = avatarUrl;
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
        [NotNullWhen(true)] out Comment? comment,
        out List<string> errors)
    {
        var values = _ctorParameters
            .ToDictionary(p => p.Name!, p => GetParameterValueOrNull(form, p));

        errors = values
            .Where(p => p.Value is MissingRequiredValue)
            .Select(p => $"Form value missing for {p.Key}")
            .ToList();

        if (values["email"] is string email && !EmailMatcher().IsMatch(email))
        {
            errors.Add($"'{email}' is not a valid email address");
        }

        if (errors.Count != 0)
        {
            comment = null;
            return false;
        }

        comment = (Comment)_ctor.Invoke(values.Values.ToArray());
        return true;
    }

    private static object? GetParameterValueOrNull(
        IFormCollection form,
        ParameterInfo parameter)
    {
        var value = form[parameter.Name!].ToString();

        if (string.IsNullOrWhiteSpace(value))
        {
            return GetParameterFallbackValue(parameter);
        }

        return TypeDescriptor.GetConverter(parameter.ParameterType).ConvertFrom(value) ??
               GetParameterFallbackValue(parameter);
    }

    private static object? GetParameterFallbackValue(ParameterInfo parameter) =>
        parameter.HasDefaultValue ? parameter.DefaultValue : default(MissingRequiredValue);

    #endregion

    [YamlIgnore]
    public string PostId { get; }

    [YamlMember(Alias = "id")]
    public string Id { get; }

    [YamlMember(Alias = "date")]
    public DateTime Date { get; }

    [YamlMember(Alias = "name")]
    public string Name { get; }

    [YamlMember(Alias = "email")]
    public string? Email { get; }

    [YamlMember(typeof(string), Alias = "avatar")]
    public Uri? AvatarUri { get; }

    [YamlMember(typeof(string), Alias = "url")]
    public Uri? Uri { get; }

    [YamlMember(Alias = "message")]
    public string Message { get; }

    private struct MissingRequiredValue
    {
    }
}