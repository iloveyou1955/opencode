using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

using OpenCode.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace OpenCode.Core.Services;

[ServiceRegistration(ServiceLifetime.Singleton)]
public class GitHubService
{
    private readonly AuthService _authService;
    private readonly ILogger<GitHubService> _logger;
    private readonly HttpClient _httpClient;

    public GitHubService(AuthService authService, ILogger<GitHubService> logger)
    {
        _authService = authService;
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("opencode", "1.0.0"));
    }

    public async Task<string?> GetUserAsync()
    {
        var auth = await _authService.GetAsync("github");
        if (auth is not ApiAuthInfo apiAuth || string.IsNullOrEmpty(apiAuth.Key))
        {
            _logger.LogWarning("GitHub token not found in AuthService. Please login using 'auth login github'.");
            return null;
        }

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", apiAuth.Key);
        try
        {
            var response = await _httpClient.GetAsync("https://api.github.com/user");
            if (response.IsSuccessStatusCode)
            {
                var user = await response.Content.ReadFromJsonAsync<GitHubUser>();
                return user?.Login;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get GitHub user");
        }
        return null;
    }

    public async Task<List<GitHubPullRequest>> ListPullRequestsAsync(string owner, string repo)
    {
        var auth = await _authService.GetAsync("github");
        if (auth is not ApiAuthInfo apiAuth) return new List<GitHubPullRequest>();

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", apiAuth.Key);
        try
        {
            var response = await _httpClient.GetAsync($"https://api.github.com/repos/{owner}/{repo}/pulls");
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<List<GitHubPullRequest>>() ?? new List<GitHubPullRequest>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list pull requests for {Owner}/{Repo}", owner, repo);
        }
        return new List<GitHubPullRequest>();
    }

    public async Task<GitHubPullRequest?> CreatePullRequestAsync(string owner, string repo, string title, string head, string baseBranch, string body)
    {
        var auth = await _authService.GetAsync("github");
        if (auth is not ApiAuthInfo apiAuth) return null;

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", apiAuth.Key);
        try
        {
            var payload = new { title, head, @base = baseBranch, body };
            var response = await _httpClient.PostAsJsonAsync($"https://api.github.com/repos/{owner}/{repo}/pulls", payload);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<GitHubPullRequest>();
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to create PR: {Error}", error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create pull request for {Owner}/{Repo}", owner, repo);
        }
        return null;
    }

    public async Task<GitHubPullRequest?> GetPullRequestAsync(string owner, string repo, int prNumber)
    {
        var auth = await _authService.GetAsync("github");
        if (auth is not ApiAuthInfo apiAuth) return null;

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", apiAuth.Key);
        try
        {
            var response = await _httpClient.GetAsync($"https://api.github.com/repos/{owner}/{repo}/pulls/{prNumber}");
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<GitHubPullRequest>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get PR info for {Owner}/{Repo} #{Number}", owner, repo, prNumber);
        }
        return null;
    }

    public async Task<string?> DetectSessionInPrAsync(string owner, string repo, int prNumber)
    {
        // 1. 检查 PR 描述
        var pr = await GetPullRequestAsync(owner, repo, prNumber);
        if (pr?.Body != null)
        {
            var match = MatchSessionId(pr.Body);
            if (match != null) return match;
        }

        // 2. 检查 PR 评论
        var comments = await GetPullRequestCommentsAsync(owner, repo, prNumber);
        foreach (var comment in comments)
        {
            if (comment.Body != null)
            {
                var match = MatchSessionId(comment.Body);
                if (match != null) return match;
            }
        }

        // 3. 检查关联的 Issue 描述
        // 注意：GitHub PR 关联 Issue 的关系通常在 PR 描述中使用 "fixes #123" 或通过 API 链接
        // 这里简化实现：尝试扫描 PR 描述中的 Issue 引用并检查它们
        if (pr?.Body != null)
        {
            var issueMatches = System.Text.RegularExpressions.Regex.Matches(pr.Body, @"#(\d+)");
            foreach (System.Text.RegularExpressions.Match issueMatch in issueMatches)
            {
                if (int.TryParse(issueMatch.Groups[1].Value, out var issueNumber))
                {
                    var issueBody = await GetIssueBodyAsync(owner, repo, issueNumber);
                    if (issueBody != null)
                    {
                        var match = MatchSessionId(issueBody);
                        if (match != null) return match;
                    }
                }
            }
        }

        return null;
    }

    private string? MatchSessionId(string text)
    {
        // 匹配 opencode.ai/opncd.ai 的分享链接
        var match = System.Text.RegularExpressions.Regex.Match(text, @"(?:opencode\.ai|opncd\.ai)/(?:s|share)/([a-zA-Z0-9]+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private async Task<List<GitHubComment>> GetPullRequestCommentsAsync(string owner, string repo, int prNumber)
    {
        var auth = await _authService.GetAsync("github");
        if (auth is not ApiAuthInfo apiAuth) return new List<GitHubComment>();

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", apiAuth.Key);
        try
        {
            var response = await _httpClient.GetAsync($"https://api.github.com/repos/{owner}/{repo}/issues/{prNumber}/comments");
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<List<GitHubComment>>() ?? new List<GitHubComment>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get comments for PR #{Number}", prNumber);
        }
        return new List<GitHubComment>();
    }

    private async Task<string?> GetIssueBodyAsync(string owner, string repo, int issueNumber)
    {
        var auth = await _authService.GetAsync("github");
        if (auth is not ApiAuthInfo apiAuth) return null;

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", apiAuth.Key);
        try
        {
            var response = await _httpClient.GetAsync($"https://api.github.com/repos/{owner}/{repo}/issues/{issueNumber}");
            if (response.IsSuccessStatusCode)
            {
                var issue = await response.Content.ReadFromJsonAsync<GitHubIssue>();
                return issue?.Body;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get issue #{Number} body", issueNumber);
        }
        return null;
    }

    public class GitHubComment
    {
        public string? Body { get; set; }
    }

    public class GitHubIssue
    {
        public string? Body { get; set; }
    }

    public class GitHubUser
    {
        public string Login { get; set; } = "";
    }

    public class GitHubPullRequest
    {
        public int Number { get; set; }
        public string Title { get; set; } = "";
        public string State { get; set; } = "";
        public string? Body { get; set; }
        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";
        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }
        public GitHubUser User { get; set; } = new();
        public GitHubBranchInfo Head { get; set; } = new();
        public GitHubBranchInfo Base { get; set; } = new();
    }

    public class GitHubBranchInfo
    {
        public string Ref { get; set; } = "";
        [JsonPropertyName("repo")]
        public GitHubRepository? Repository { get; set; }
    }

    public class GitHubRepository
    {
        public string Name { get; set; } = "";
        [JsonPropertyName("full_name")]
        public string FullName { get; set; } = "";
        public GitHubRepoOwner Owner { get; set; } = new();
    }

    public class GitHubRepoOwner
    {
        public string Login { get; set; } = "";
    }
}
