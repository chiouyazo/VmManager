namespace VmManager.Agent.Services;

public class FeedResolutionService
{
    private readonly ILogger<FeedResolutionService> _logger;

    public FeedResolutionService(ILogger<FeedResolutionService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public FeedConfiguration? ResolvePushFeed(VmOrigin? origin, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (origin == null || string.IsNullOrEmpty(origin.FeedId))
        {
            _logger.LogDebug("ResolvePushFeed: no origin or empty FeedId");
            return null;
        }

        // Direct match by deterministic ID
        FeedConfiguration? originFeed = settings.Feeds.FirstOrDefault(f => f.Id == origin.FeedId);
        if (originFeed != null)
        {
            _logger.LogInformation(
                "ResolvePushFeed: direct match on feed {FeedName} (ID {FeedId})",
                originFeed.Name,
                originFeed.Id
            );
            return originFeed;
        }

        // Feed ID doesn't match any configured feed -> the origin might point to an
        // auto-discovered Nexus repo. Try to find the parent feed by URL and reconstruct.
        if (!string.IsNullOrEmpty(origin.FeedUrl))
        {
            FeedConfiguration? parentFeed = settings.Feeds.FirstOrDefault(f =>
                string.Equals(
                    f.Url?.TrimEnd('/'),
                    origin.FeedUrl.TrimEnd('/'),
                    StringComparison.OrdinalIgnoreCase
                )
            );
            if (parentFeed != null)
            {
                _logger.LogInformation(
                    "ResolvePushFeed: ID {FeedId} not found directly, resolved parent feed {ParentName} by URL match (repo={Repo})",
                    origin.FeedId,
                    parentFeed.Name,
                    origin.Repository ?? "(empty)"
                );
                return new FeedConfiguration
                {
                    Id = origin.FeedId,
                    Name = parentFeed.Name,
                    Type = parentFeed.Type,
                    Url = parentFeed.Url,
                    Repository = origin.Repository ?? parentFeed.Repository,
                    Username = parentFeed.Username,
                    Password = parentFeed.Password,
                };
            }
        }

        _logger.LogWarning(
            "ResolvePushFeed: no match for FeedId={FeedId}, FeedUrl={FeedUrl}",
            origin.FeedId,
            origin.FeedUrl
        );
        return null;
    }
}
