namespace VmManager.Contracts.Models;

public record LocalImageMetadata(
    string Name,
    string? ParentImageId,
    string? ParentImageName,
    string? Version,
    string? FeedId,
    string? FeedUrl,
    string? FeedRepository
);
